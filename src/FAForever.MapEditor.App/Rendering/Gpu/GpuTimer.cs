using System.Diagnostics;
using FAForever.MapEditor.App.Diagnostics;
using Silk.NET.OpenGL;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// How long the graphics card takes for a frame of the 3D view, which the spans (time on the
/// processor while the commands are sent) do not show. Off unless asked for (automation's
/// <c>perf {gpu: true}</c>): with timer queries (<c>EXT_disjoint_timer_query</c> on OpenGL ES, core in
/// OpenGL 3.3) it records the span <c>3d.gpu-frame</c> a few frames late without waiting; without
/// them, <c>glFinish</c> before and after the frame measures it (<c>3d.gpu-frame-finish</c>), which
/// stalls the processor and is only for finding faults.
/// </summary>
internal sealed class GpuTimer(GL gl, bool queries, bool es) : IDisposable
{
    private const GLEnum GpuDisjoint = (GLEnum)0x8FBB;

    private readonly Queue<uint> _pending = new Queue<uint>();
    private readonly Stack<uint> _free = new Stack<uint>();
    private long _start;
    private bool _running;

    /// <summary>
    /// Whether timing is on, for every 3D view; set by automation.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Whether the card offers timer queries; without them timing waits for the card.
    /// </summary>
    public bool HasQueries => queries;

    /// <summary>
    /// What the last 3D view that drew measures with, for automation: <c>queries</c>, <c>finish</c> or null before it drew.
    /// </summary>
    public static string? Method { get; private set; }

    /// <summary>
    /// Whether this OpenGL offers timer queries: OpenGL 3.3, or OpenGL ES with <c>GL_EXT_disjoint_timer_query</c>.
    /// </summary>
    public static bool Offered(GL gl, bool es, int major, int minor)
    {
        if (!es)
        {
            return major > 3 || major == 3 && minor >= 3;
        }
        gl.GetInteger(GetPName.NumExtensions, out int count);
        for (uint i = 0; i < count; i++)
        {
            if (gl.GetStringS(StringName.Extensions, i) == "GL_EXT_disjoint_timer_query")
            {
                return true;
            }
        }
        return false;
    }

    public void Begin()
    {
        if (!Enabled)
        {
            return;
        }
        Method = queries ? "queries" : "finish";
        if (queries)
        {
            Collect();
            uint query = _free.Count > 0 ? _free.Pop() : gl.GenQuery();
            gl.BeginQuery(QueryTarget.TimeElapsed, query);
            _pending.Enqueue(query);
        }
        else
        {
            gl.Finish();
            _start = Stopwatch.GetTimestamp();
        }
        _running = true;
    }

    public void End()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        if (queries)
        {
            gl.EndQuery(QueryTarget.TimeElapsed);
        }
        else
        {
            gl.Finish();
            Performance.Record("3d.gpu-frame-finish", Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Records the queries the card has answered, oldest first; a disjoint period (the card's clock
    /// jumped) throws them away.
    /// </summary>
    private void Collect()
    {
        // the disjoint flag is the extension's (OpenGL ES); OpenGL's clock does not jump
        bool disjoint = es && gl.GetInteger(GpuDisjoint) != 0;
        while (_pending.Count > 0)
        {
            uint query = _pending.Peek();
            gl.GetQueryObject(query, QueryObjectParameterName.ResultAvailable, out uint available);
            if (available == 0)
            {
                break;
            }
            _pending.Dequeue();
            gl.GetQueryObject(query, QueryObjectParameterName.Result, out uint nanoseconds);
            if (!disjoint)
            {
                Performance.Record("3d.gpu-frame", nanoseconds / 1e6);
            }
            _free.Push(query);
        }
    }

    public void Dispose()
    {
        foreach (uint query in _pending.Concat(_free))
        {
            gl.DeleteQuery(query);
        }
        _pending.Clear();
        _free.Clear();
    }
}
