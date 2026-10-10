using System.Numerics;
using Avalonia;
using Avalonia.Threading;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The sky cube map's faces drawn by the 3D view's renderer (user story L-1, <see cref="ISkyCubeFaces"/>):
/// after a frame, six views of 90 degrees from the eye into a framebuffer of the face's size, each
/// with the generated sky as the sky's cube map (behind everything and in the water's reflection),
/// the terrain with its textures, the decals, props and units; read back as a strip of six faces.
/// While textures and models are still being read it draws the faces again (for at most
/// <see cref="SkyCubeWait"/>, holding the frame), so the faces show what the 3D view shows.
/// </summary>
public sealed partial class TerrainViewGpu : ISkyCubeFaces
{
    /// <summary>
    /// How long the faces are drawn again while textures and models load.
    /// </summary>
    private static readonly TimeSpan SkyCubeWait = TimeSpan.FromSeconds(20);

    private sealed class SkyCubeJob(MapDocument document, Vector3 eye, DecodedTexture sky, CancellationToken cancel)
    {
        public MapDocument Document { get; } = document;
        public Vector3 Eye { get; } = eye;
        public DecodedTexture Sky { get; } = sky;
        public CancellationToken Cancel { get; } = cancel;
        public TaskCompletionSource<DecodedTexture> Done { get; } = new TaskCompletionSource<DecodedTexture>(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Frames { get; set; }
    }

    private SkyCubeJob? _skyCubeJob;
    private volatile bool _skyCubeReady;
    private uint _skyCubeFramebuffer;
    private uint _skyCubeColor;
    private uint _skyCubeDepth;
    private int _skyCubeSize;

    /// <summary>
    /// Whether the view can draw the faces: it is shown and its renderer runs. Read from any thread.
    /// </summary>
    public bool CanRender => _skyCubeReady;

    /// <summary>
    /// Gives the editor's sky cube generator this view's renderer.
    /// </summary>
    private void AttachSkyCube()
    {
        if (_model is not null)
        {
            _model.SkyCubeGenerator.Faces = this;
        }
    }

    public Task<DecodedTexture> RenderAsync(MapDocument document, Vector3 eye, DecodedTexture sky, CancellationToken cancel)
    {
        SkyCubeJob job = new SkyCubeJob(document, eye, sky, cancel);
        Dispatcher.UIThread.Post(() =>
        {
            _skyCubeJob?.Done.TrySetCanceled();
            _skyCubeJob = job;
            RequestNextFrameRendering();
        });
        return job.Done.Task;
    }

    /// <summary>
    /// Draws the faces asked for, after the frame; the frame's own framebuffer is bound again after.
    /// </summary>
    private unsafe void RenderSkyCube(int frameBuffer, int width, int height)
    {
        _skyCubeReady = IsVisible && _renderer is not null && _gl is not null;
        if (_skyCubeJob is not SkyCubeJob job || _gl is null || _renderer is null || _model is null)
        {
            return;
        }
        if (job.Cancel.IsCancellationRequested)
        {
            _skyCubeJob = null;
            job.Done.TrySetCanceled();
            return;
        }
        using Performance.Span measure = Performance.Measure("3d.sky-cube");
        int size = job.Sky.Height;
        try
        {
            if (!SkyCubeTarget(size))
            {
                throw new InvalidOperationException("The graphics card offers no framebuffer of the sky cube map's size");
            }
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _skyCubeFramebuffer);
            _gl.Viewport(0, 0, (uint)size, (uint)size);
            TextureSource sky = new TextureSource("cube:editor:sky-cube-generator", job.Sky, () => job.Sky);
            Workspace? workspace = _model.Setup.Workspace;
            MapDocument drawn = _model.WideDecals.Shown(job.Document);
            ObjectFrame frame = new ObjectFrame(_model.Assets.Catalog, _model.ShowProps, _model.ShowUnits, false, false, null, job.Eye,
                ThemeColor("SelectedStrokeBrush"), ThemeColor("WarningBrush"), EffectColor: ThemeColor("MapEffectPropBrush"));
            float far = 4 * Math.Max(job.Document.Width, job.Document.Height) + 2000;
            byte[] rgba = new byte[6 * size * size * 4];
            byte[] face = new byte[size * size * 4];
            _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                for (int side = 0; side < 6; side++)
                {
                    // the sky covers every pixel: the colour cleared is the frame's
                    _gl.ClearDepth(1);
                    _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    _renderer.Render(drawn, FaceViewProjection(side, job.Eye, 0.5f, far), path => _model.WideDecals.TextureOf(path) ?? FindTexture(path, job.Document, workspace),
                        objects: _objects is null ? null : (_objects, frame), findCube: _ => sky, eye: job.Eye, showDecals: true, sweep: false);
                    fixed (byte* data = face)
                    {
                        _gl.ReadPixels(0, 0, (uint)size, (uint)size, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    }
                    // OpenGL's first row is the bottom one, the face's first row its top
                    for (int row = 0; row < size; row++)
                    {
                        Array.Copy(face, (size - 1 - row) * size * 4, rgba, (row * 6 * size + side * size) * 4, size * 4);
                    }
                }
                job.Frames++;
                // the faces share their textures (none is let go between them); while some are still being
                // read, draw the faces again with them, here: a frame of the view would let go of those it does not show
                bool loading = _renderer.TexturesLoading + (_objects?.TexturesLoading ?? 0) + ObjectState.ModelsLoading > 0 || !_renderer.SkyDrawn;
                if (!loading || waited.Elapsed > SkyCubeWait)
                {
                    break;
                }
                Thread.Sleep(30);
            }
            for (int i = 3; i < rgba.Length; i += 4)
            {
                rgba[i] = 255;
            }
            _skyCubeJob = null;
            Log.LogInformation("The 3D view drew the sky cube map's faces of {Size} pixels in {Passes} passes", size, job.Frames);
            job.Done.TrySetResult(new DecodedTexture(6 * size, size, rgba));
        }
        catch (Exception exception) when (exception is InvalidOperationException or OutOfMemoryException)
        {
            _skyCubeJob = null;
            job.Done.TrySetException(exception);
        }
        finally
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frameBuffer);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
        }
    }

    /// <summary>
    /// The framebuffer the faces are drawn into: colour and depth of the face's size.
    /// </summary>
    private bool SkyCubeTarget(int size)
    {
        GL gl = _gl!;
        if (_skyCubeFramebuffer != 0 && _skyCubeSize == size)
        {
            return true;
        }
        ReleaseSkyCube();
        _skyCubeFramebuffer = gl.GenFramebuffer();
        _skyCubeColor = gl.GenRenderbuffer();
        _skyCubeDepth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _skyCubeColor);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)size, (uint)size);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _skyCubeDepth);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)size, (uint)size);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _skyCubeFramebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _skyCubeColor);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _skyCubeDepth);
        bool complete = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == GLEnum.FramebufferComplete;
        _skyCubeSize = size;
        if (!complete)
        {
            ReleaseSkyCube();
        }
        return complete;
    }

    private void ReleaseSkyCube()
    {
        if (_gl is null)
        {
            return;
        }
        if (_skyCubeFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_skyCubeFramebuffer);
            _gl.DeleteRenderbuffer(_skyCubeColor);
            _gl.DeleteRenderbuffer(_skyCubeDepth);
        }
        (_skyCubeFramebuffer, _skyCubeColor, _skyCubeDepth, _skyCubeSize) = (0, 0, 0, 0);
    }

    /// <summary>
    /// The camera of one face of a cube map: 90 degrees across, from <paramref name="eye"/> along the
    /// face's axis, so the pixel (column, row) of the face sees what <see cref="CubeFaces.PixelDirection"/>
    /// says (OpenGL's rows from the bottom: the face's first row is the framebuffer's last).
    /// </summary>
    public static Matrix4x4 FaceViewProjection(int face, Vector3 eye, float near, float far)
    {
        Vector3 forward = CubeFaces.Direction(face, 0, 0);
        Vector3 right = CubeFaces.Direction(face, 1, 0) - forward;
        Vector3 down = CubeFaces.Direction(face, 0, 1) - forward;
        float a = (far + near) / (far - near);
        float b = -2 * far * near / (far - near);
        // clip = ((p - eye) . right, -(p - eye) . down, a (p - eye) . forward + b, (p - eye) . forward), as rows times the matrix
        return new Matrix4x4(
            right.X, -down.X, a * forward.X, forward.X,
            right.Y, -down.Y, a * forward.Y, forward.Y,
            right.Z, -down.Z, a * forward.Z, forward.Z,
            -Vector3.Dot(eye, right), Vector3.Dot(eye, down), -a * Vector3.Dot(eye, forward) + b, -Vector3.Dot(eye, forward));
    }
}
