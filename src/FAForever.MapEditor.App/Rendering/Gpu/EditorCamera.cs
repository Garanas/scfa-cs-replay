using System.Numerics;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// The 3D view's camera, after the FAF map editor's (<c>Assets/Scripts/Camera/CameraControler*.cs</c>):
/// it looks at a point of the ground (the pivot) from a distance, straight down to start with.
/// The wheel moves it along the ray under the mouse, towards what the mouse points at; panning
/// moves it over the ground, faster the higher it is; rotating turns it around the pivot and tilts
/// it from straight down to 10 degrees above the horizon. Each change sets a target that the
/// camera glides to (<see cref="Update"/>), and after every move the pivot is again the ground
/// the camera looks at.
/// </summary>
/// <remarks>
/// The ground the camera rests on is the terrain blurred (<see cref="SmoothGround"/>), over about
/// 15 % of the camera's distance: from far away a spike or a cliff does not move it, from close
/// by it still follows hills. Only the eye keeps clear of the real terrain.
/// </remarks>
/// <param name="smoothHeight">The height of the blurred terrain at a place of the map (x, z) over a radius, in ogrids.</param>
/// <param name="terrainHeight">The height of the terrain itself at a place of the map.</param>
public sealed class EditorCamera(Func<float, float, float, float> smoothHeight, Func<float, float, float> terrainHeight)
{
    // the eye stays this far above the terrain itself
    private const float Clearance = 2;

    public const float FieldOfView = 60 * MathF.PI / 180;

    // in the FAF map editor's units of ten ogrids: MinDistance 0.45, MaxDistance 1100, ScrollStep 0.033, three steps a notch
    private const float MinDistance = 4.5f;
    private const float MaxDistance = 11000;
    private const float ScrollStep = 0.033f;
    private const int StepsPerNotch = 3;
    private const float MinPitch = 10;
    private const float MaxPitch = 90;

    // how fast the camera follows its target, per second: zooming (SmoothDamp of 0.043 s), panning and turning
    private const float ZoomRate = 23;
    private const float PanRate = 16;
    private const float TurnRate = 14;

    private State _current = new State(Vector2.Zero, 100, 0, MaxPitch);
    private State _target = new State(Vector2.Zero, 100, 0, MaxPitch);

    private readonly record struct State(Vector2 Pivot, float Distance, float Yaw, float Pitch);

    /// <summary>
    /// The point of the ground the camera looks at (x and z in ogrids), how far it is, and its
    /// yaw and pitch in degrees (pitch 90: straight down); as it is now, gliding to its target.
    /// </summary>
    public (float X, float Z, float Distance, float Yaw, float Pitch) Now =>
        (_current.Pivot.X, _current.Pivot.Y, _current.Distance, _current.Yaw, _current.Pitch);

    /// <summary>
    /// Where the camera is: above and behind its pivot.
    /// </summary>
    public Vector3 Eye => EyeOf(_current);

    /// <summary>
    /// Over the middle of a map, straight down, high enough to see all of it; at once.
    /// </summary>
    public void Reset(float width, float height)
    {
        Vector2 middle = new Vector2(width / 2, height / 2);
        _target = new State(middle, Math.Clamp(Math.Max(width, height) * 1.05f, MinDistance, MaxDistance), 0, MaxPitch);
        _current = _target;
    }

    /// <summary>
    /// Points the camera; null keeps a value as it is. It moves there at once.
    /// </summary>
    public void Look(float? x = null, float? z = null, float? distance = null, float? yaw = null, float? pitch = null)
    {
        _target = new State(
            new Vector2(x ?? _target.Pivot.X, z ?? _target.Pivot.Y),
            Math.Clamp(distance ?? _target.Distance, MinDistance, MaxDistance),
            yaw ?? _target.Yaw,
            Math.Clamp(pitch ?? _target.Pitch, MinPitch, MaxPitch));
        _current = _target;
    }

    /// <summary>
    /// The wheel: towards the point under the mouse (<paramref name="mouse"/> in normalized device
    /// coordinates, -1 to 1, y up) when <paramref name="notches"/> is positive, back along the
    /// view when negative. Each step goes 3.3 % of 2.6 times the distance to the ground ahead.
    /// </summary>
    public void Zoom(Vector2 mouse, float aspect, int notches)
    {
        Vector3 eye = EyeOf(_target);
        for (int step = 0; step < Math.Abs(notches) * StepsPerNotch; step++)
        {
            Vector3 forward = Forward(_target);
            float ahead = GroundAlong(eye, forward, _target.Distance) ?? _target.Distance;
            float length = ScrollStep * Math.Max(50, ahead * 2.6f);
            if (notches > 0)
            {
                Vector3 ray = Ray(_target, eye, mouse, aspect);
                if (GroundAlong(eye, ray, _target.Distance) is not float hit || hit <= MinDistance)
                {
                    break;
                }
                eye += ray * Math.Min(length, hit - MinDistance);
            }
            else
            {
                if (eye.Y >= MaxDistance)
                {
                    break;
                }
                eye -= forward * length;
            }
            _target = Aim(_target, eye);
        }
    }

    /// <summary>
    /// The middle mouse button: the ground moves with the mouse, by <paramref name="delta"/>
    /// pixels of a view <paramref name="viewHeight"/> pixels high.
    /// </summary>
    public void Pan(Vector2 delta, float viewHeight)
    {
        // a pixel covers this much of the ground at the pivot
        float scale = 2 * _target.Distance * MathF.Tan(FieldOfView / 2) / Math.Max(1, viewHeight);
        (float sin, float cos) = MathF.SinCos(_target.Yaw * MathF.PI / 180);
        Vector2 right = new Vector2(cos, -sin);
        Vector2 up = new Vector2(-sin, -cos);
        // straight down, the screen's up is the ground's; tilted, a pixel at the pivot spans more ground along the view
        float along = scale / Math.Max(0.2f, MathF.Sin(_target.Pitch * MathF.PI / 180));
        Vector2 pivot = _target.Pivot - right * delta.X * scale + up * delta.Y * along;
        Vector3 eye = EyeOf(_target) + new Vector3(pivot.X - _target.Pivot.X, 0, pivot.Y - _target.Pivot.Y);
        _target = Aim(_target with { Pivot = pivot }, eye);
    }

    /// <summary>
    /// Space and the mouse: turns around the pivot (the mouse to the right turns the view
    /// clockwise) and tilts (the mouse up tilts towards the horizon), a quarter degree a pixel.
    /// </summary>
    public void Rotate(Vector2 delta) =>
        _target = _target with
        {
            Yaw = _target.Yaw - delta.X * 0.25f,
            Pitch = Math.Clamp(_target.Pitch + delta.Y * 0.25f, MinPitch, MaxPitch),
        };

    /// <summary>
    /// Moves the camera towards its target.
    /// </summary>
    /// <returns>Whether it is still on its way: draw another frame.</returns>
    public bool Update(float seconds)
    {
        float Follow(float rate) => 1 - MathF.Exp(-rate * Math.Max(0, seconds));
        State next = new State(
            Vector2.Lerp(_current.Pivot, _target.Pivot, Follow(PanRate)),
            _current.Distance + (_target.Distance - _current.Distance) * Follow(ZoomRate),
            _current.Yaw + (_target.Yaw - _current.Yaw) * Follow(TurnRate),
            _current.Pitch + (_target.Pitch - _current.Pitch) * Follow(TurnRate));

        bool moving = Vector2.Distance(next.Pivot, _target.Pivot) > 0.01f
            || Math.Abs(next.Distance - _target.Distance) > 0.01f
            || Math.Abs(next.Yaw - _target.Yaw) > 0.01f
            || Math.Abs(next.Pitch - _target.Pitch) > 0.01f;
        _current = moving ? next : _target;
        return moving;
    }

    public Matrix4x4 ViewProjection(float aspect) => ViewProjectionOf(_current, aspect);

    private Matrix4x4 ViewProjectionOf(State state, float aspect)
    {
        Vector3 eye = EyeOf(state);
        Vector3 target = Ground(state.Pivot, state.Distance);
        // straight down, the screen's up is the direction the yaw faces
        (float sin, float cos) = MathF.SinCos(state.Yaw * MathF.PI / 180);
        Vector3 up = state.Pitch > 89.9f ? new Vector3(-sin, 0, -cos) : Vector3.UnitY;
        float near = Math.Max(0.5f, state.Distance * 0.01f);
        float far = state.Distance * 4 + 20000;
        return Matrix4x4.CreateLookAt(eye, target, up) * Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, near, far);
    }

    private Vector3 EyeOf(State state)
    {
        Vector3 eye = Ground(state.Pivot, state.Distance) - Forward(state) * state.Distance;
        return eye with { Y = Math.Max(eye.Y, terrainHeight(eye.X, eye.Z) + Clearance) };
    }

    /// <summary>
    /// How far around a place the ground is blurred for a camera this far away.
    /// </summary>
    private static float Radius(float distance) => Math.Clamp(distance * 0.15f, 8, 2048);

    private Vector3 Ground(Vector2 place, float distance) => new Vector3(place.X, smoothHeight(place.X, place.Y, Radius(distance)), place.Y);

    /// <summary>
    /// The direction the camera looks: down by the pitch, and across the ground against the yaw
    /// (yaw 0 looks north, towards smaller z).
    /// </summary>
    private static Vector3 Forward(State state)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(state.Yaw * MathF.PI / 180);
        (float sinPitch, float cosPitch) = MathF.SinCos(state.Pitch * MathF.PI / 180);
        return Vector3.Normalize(new Vector3(-sinYaw * cosPitch, -sinPitch, -cosYaw * cosPitch));
    }

    /// <summary>
    /// The ray through a point of the view.
    /// </summary>
    private Vector3 Ray(State state, Vector3 eye, Vector2 mouse, float aspect)
    {
        if (!Matrix4x4.Invert(ViewProjectionOf(state, aspect), out Matrix4x4 inverse))
        {
            return Forward(state);
        }
        Vector4 far = Vector4.Transform(new Vector4(mouse.X, mouse.Y, 1, 1), inverse);
        return Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) / far.W - eye);
    }

    /// <summary>
    /// The camera at <paramref name="eye"/> with the state's angles, its pivot the ground it looks at.
    /// </summary>
    private State Aim(State state, Vector3 eye)
    {
        Vector3 forward = Forward(state);
        float distance = Math.Clamp(GroundAlong(eye, forward, state.Distance) ?? state.Distance, MinDistance, MaxDistance);
        Vector3 pivot = eye + forward * distance;
        return state with { Pivot = new Vector2(pivot.X, pivot.Z), Distance = distance };
    }

    /// <summary>
    /// How far along the ray the blurred ground is, as a camera <paramref name="distance"/> away
    /// sees it: in steps, then halving the last one; null when the ray points up or misses within
    /// 30 000 ogrids.
    /// </summary>
    private float? GroundAlong(Vector3 origin, Vector3 direction, float distance)
    {
        float radius = Radius(distance);
        bool Below(float t)
        {
            Vector3 p = origin + direction * t;
            return p.Y <= smoothHeight(p.X, p.Z, radius);
        }

        if (Below(0))
        {
            return 0;
        }
        float step = Math.Max(1, origin.Y / 64);
        for (float t = step; t < 30000; t += step)
        {
            if (Below(t))
            {
                float low = t - step;
                float high = t;
                for (int i = 0; i < 20; i++)
                {
                    float middle = (low + high) / 2;
                    if (Below(middle))
                    {
                        high = middle;
                    }
                    else
                    {
                        low = middle;
                    }
                }
                return high;
            }
            if (direction.Y >= 0 && origin.Y + direction.Y * t > 1000)
            {
                return null;
            }
        }
        return null;
    }
}
