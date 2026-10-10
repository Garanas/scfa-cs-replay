using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Input for a window as its platform gives it: raw mouse, keyboard, text and drag events handed
/// to the window's own input (<c>PlatformImpl.Input</c>), so they go through hit testing, pointer
/// capture, focus, key bindings and drag and drop as a person's do. The operating system's mouse
/// and keyboard are never touched: the user's cursor stays where it is. Automation's <c>mouse</c>,
/// <c>key</c>, <c>type</c> and <c>drag</c> use it; it works the same in the headless tests, where
/// Avalonia.Headless's own input takes the same path.
/// </summary>
/// <remarks>
/// Places are in the window's device-independent pixels (what <c>ui</c> and <c>hit</c> report).
/// The mouse is a device of its own, so its capture and the pointer over a control are apart from
/// the real mouse's; the keyboard is the platform's, which sends keys to the focused control.
/// </remarks>
public sealed class WindowInput
{
    private static readonly PropertyInfo? InputRootProperty =
        typeof(TopLevel).GetProperty("InputRoot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private readonly TopLevel _top;
    private IInputDevice? _mouse;
    private RawInputModifiers _buttons;
    // the modifier keys held down, and every key held
    private RawInputModifiers _keys;
    private readonly HashSet<Key> _held = [];

    public WindowInput(TopLevel top)
    {
        _top = top;
    }

    /// <summary>
    /// The window's own mouse when its platform has one (Windows and the headless platform do),
    /// else one of ours.
    /// </summary>
    private IInputDevice Mouse => _mouse ??= (_top.PlatformImpl is { } platform ? Raw.MouseOf(platform) : null) ?? Raw.NewMouse();

    /// <summary>
    /// Where the mouse is, in the window.
    /// </summary>
    public Point Position { get; private set; }

    /// <summary>
    /// The keys held down (<see cref="KeyDown"/>) and not yet let go.
    /// </summary>
    public IReadOnlyCollection<Key> Held => _held;

    public void Move(Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Position = at;
        Send(Raw.Pointer(Mouse, Timestamp, Root, RawPointerEventType.Move, at, modifiers | _buttons | _keys));
    }

    public void Down(Point at, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Position = at;
        (RawPointerEventType type, RawInputModifiers held) = button switch
        {
            MouseButton.Left => (RawPointerEventType.LeftButtonDown, RawInputModifiers.LeftMouseButton),
            MouseButton.Right => (RawPointerEventType.RightButtonDown, RawInputModifiers.RightMouseButton),
            MouseButton.Middle => (RawPointerEventType.MiddleButtonDown, RawInputModifiers.MiddleMouseButton),
            _ => throw new ArgumentException($"The mouse has no button {button} here: left, right or middle"),
        };
        _buttons |= held;
        Send(Raw.Pointer(Mouse, Timestamp, Root, type, at, modifiers | _buttons | _keys));
    }

    public void Up(Point at, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Position = at;
        (RawPointerEventType type, RawInputModifiers held) = button switch
        {
            MouseButton.Left => (RawPointerEventType.LeftButtonUp, RawInputModifiers.LeftMouseButton),
            MouseButton.Right => (RawPointerEventType.RightButtonUp, RawInputModifiers.RightMouseButton),
            MouseButton.Middle => (RawPointerEventType.MiddleButtonUp, RawInputModifiers.MiddleMouseButton),
            _ => throw new ArgumentException($"The mouse has no button {button} here: left, right or middle"),
        };
        _buttons &= ~held;
        Send(Raw.Pointer(Mouse, Timestamp, Root, type, at, modifiers | _buttons | _keys));
    }

    /// <summary>
    /// Turns the wheel: <paramref name="notches"/> up (away from the person) when positive.
    /// </summary>
    public void Wheel(Point at, double notches, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Position = at;
        Send(Raw.Wheel(Mouse, Timestamp, Root, at, new Vector(0, notches), modifiers | _buttons | _keys));
    }

    /// <summary>
    /// Presses a button where the mouse is and drags it to another place in <paramref name="steps"/>
    /// moves, then lets go.
    /// </summary>
    public void Drag(Point from, Point to, MouseButton button = MouseButton.Left, int steps = 10, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Move(from, modifiers);
        Down(from, button, modifiers);
        steps = Math.Max(1, steps);
        for (int i = 1; i <= steps; i++)
        {
            Move(from + (to - from) * ((double)i / steps), modifiers);
        }
        Up(to, button, modifiers);
    }

    /// <summary>
    /// Presses a key and lets it go, as a keyboard does: down, the text it types (a printable key
    /// without control or alt), up.
    /// </summary>
    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        KeyDown(key, modifiers);
        KeyUp(key, modifiers);
    }

    /// <summary>
    /// Presses a key and holds it (with the text a printable key types) until <see cref="KeyUp"/>:
    /// a modifier key held goes with every mouse event and key meanwhile, as shift held while
    /// dragging does.
    /// </summary>
    public void KeyDown(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _held.Add(key);
        _keys |= ModifierOf(key);
        modifiers |= _keys;
        PhysicalKey physical = PhysicalOf(key);
        string? symbol = SymbolOf(key, modifiers);
        Send(Raw.Key(Raw.Keyboard, Timestamp, Root, RawKeyEventType.KeyDown, key, modifiers, physical, symbol));
        if (symbol is not null && (modifiers & (RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Meta)) == 0)
        {
            Send(Raw.Text(Raw.Keyboard, Timestamp, Root, symbol));
        }
    }

    /// <summary>
    /// Lets go of a key (held or not).
    /// </summary>
    public void KeyUp(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        modifiers |= _keys;
        _held.Remove(key);
        _keys = _held.Aggregate(RawInputModifiers.None, (all, held) => all | ModifierOf(held));
        Send(Raw.Key(Raw.Keyboard, Timestamp, Root, RawKeyEventType.KeyUp, key, modifiers & ~ModifierOf(key), PhysicalOf(key), SymbolOf(key, modifiers)));
    }

    /// <summary>
    /// The modifier a key is (shift, control, alt, the Windows key); none for other keys.
    /// </summary>
    private static RawInputModifiers ModifierOf(Key key) => key switch
    {
        Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift => RawInputModifiers.Shift,
        Avalonia.Input.Key.LeftCtrl or Avalonia.Input.Key.RightCtrl => RawInputModifiers.Control,
        Avalonia.Input.Key.LeftAlt or Avalonia.Input.Key.RightAlt => RawInputModifiers.Alt,
        Avalonia.Input.Key.LWin or Avalonia.Input.Key.RWin => RawInputModifiers.Meta,
        _ => RawInputModifiers.None,
    };

    /// <summary>
    /// Types text into the focused control, as an input method hands it over (no key presses).
    /// </summary>
    public void Type(string text)
    {
        foreach (string character in Characters(text))
        {
            Send(Raw.Text(Raw.Keyboard, Timestamp, Root, character));
        }
    }

    /// <summary>
    /// Drags data from outside onto a place of the window, as drag and drop from another control
    /// does: it enters at <paramref name="from"/>, moves to <paramref name="to"/> and is dropped there.
    /// </summary>
    /// <returns>What the target said it does with the drop: <see cref="DragDropEffects.None"/> when nothing took it.</returns>
    public DragDropEffects DropData(Point from, Point to, IDataTransfer data, int steps = 5, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        RawInputEventArgs Drag(RawDragEventType type, Point at) =>
            Raw.Drag(type, Root, at, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, modifiers);
        Send(Drag(RawDragEventType.DragEnter, from));
        steps = Math.Max(1, steps);
        for (int i = 1; i <= steps; i++)
        {
            Send(Drag(RawDragEventType.DragOver, from + (to - from) * ((double)i / steps)));
        }
        RawInputEventArgs drop = Drag(RawDragEventType.Drop, to);
        Send(drop);
        Position = to;
        return Raw.EffectsOf(drop);
    }

    /// <summary>
    /// A key by its name (<c>E</c>, <c>Delete</c>, <c>Escape</c>, <c>F5</c>, <c>Up</c>) or a gesture
    /// with its modifiers (<c>Ctrl+K</c>, <c>Shift+E</c>), as menus write shortcuts.
    /// </summary>
    public static (Key Key, RawInputModifiers Modifiers) ParseKey(string text)
    {
        string[] parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[^1].Length == 0)
        {
            throw new ArgumentException($"'{text}' is no key: give its name (E, Delete, Escape, F5, Up, 5) or a gesture (Ctrl+K, Ctrl+2)");
        }
        // a modifier alone (Shift) is a key; with others it is the last part's modifier
        RawInputModifiers modifiers = parts.Length > 1 ? ParseModifiers(parts[..^1]) : RawInputModifiers.None;
        string name = parts[^1];
        name = name.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "del" => "Delete",
            "return" => "Enter",
            "backspace" => "Back",
            "arrowup" => "Up",
            "arrowdown" => "Down",
            "arrowleft" => "Left",
            "arrowright" => "Right",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "shift" => "LeftShift",
            "ctrl" or "control" => "LeftCtrl",
            "alt" => "LeftAlt",
            "[" => "OemOpenBrackets",
            "]" => "OemCloseBrackets",
            "+" or "plus" => "OemPlus",
            "-" or "minus" => "OemMinus",
            _ => name,
        };
        // a digit is the key above the letters (D2), as a menu writes Ctrl+2
        if (name.Length == 1 && char.IsDigit(name[0]))
        {
            name = "D" + name;
        }
        if (int.TryParse(name, out _) || !Enum.TryParse(name, ignoreCase: true, out Key key) || !Enum.IsDefined(key))
        {
            throw new ArgumentException($"'{text}' is no key: '{name}' is not a key's name (E, Delete, Escape, F5, Up, 5, OemPlus); a gesture joins modifiers with + (Ctrl+K, Ctrl+2)");
        }
        return (key, modifiers);
    }

    /// <summary>
    /// The modifiers by their names (<c>Shift</c>, <c>Control</c> or <c>Ctrl</c>, <c>Alt</c>, <c>Meta</c>),
    /// as a list or one text joined with <c>+</c>.
    /// </summary>
    public static RawInputModifiers ParseModifiers(IEnumerable<string> names)
    {
        RawInputModifiers modifiers = RawInputModifiers.None;
        foreach (string name in names.SelectMany(name => name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            modifiers |= name.ToLowerInvariant() switch
            {
                "shift" => RawInputModifiers.Shift,
                "control" or "ctrl" => RawInputModifiers.Control,
                "alt" => RawInputModifiers.Alt,
                "meta" or "win" or "cmd" => RawInputModifiers.Meta,
                _ => throw new ArgumentException($"'{name}' is no modifier: Shift, Control, Alt or Meta"),
            };
        }
        return modifiers;
    }

    public static RawInputModifiers Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : 0)
        | (modifiers.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : 0)
        | (modifiers.HasFlag(KeyModifiers.Meta) ? RawInputModifiers.Meta : 0);

    public static MouseButton ParseButton(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "left" => MouseButton.Left,
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        _ => throw new ArgumentException($"'{name}' is no mouse button: left, right or middle"),
    };

    /// <summary>
    /// The input root the platform hands its own events to (what <c>SetInputRoot</c> gave it),
    /// else the window's.
    /// </summary>
    private IInputRoot Root => (_top.PlatformImpl is { } platform ? Raw.RootOf(platform) : null)
        ?? InputRootProperty?.GetValue(_top) as IInputRoot
        ?? throw new InvalidOperationException("The window takes no input yet (it has no input root)");

    private static ulong Timestamp => (ulong)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);

    private void Send(RawInputEventArgs input)
    {
        Action<RawInputEventArgs> handler = (_top.PlatformImpl is { } platform ? Raw.InputOf(platform) : null)
            ?? throw new InvalidOperationException("The window takes no input (it is closed)");
        handler(input);
    }

    /// <summary>
    /// The raw events and devices, which Avalonia's reference assemblies leave out (they belong to
    /// its platforms, not to an application): made by reflection from the implementation, where
    /// they are public. A newer Avalonia that changes them fails here, saying what is missing.
    /// </summary>
    private static class Raw
    {
        private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static IInputRoot? RootOf(object platform)
        {
            for (Type? type = platform.GetType(); type is not null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (typeof(IInputRoot).IsAssignableFrom(field.FieldType) && field.GetValue(platform) is IInputRoot root)
                    {
                        return root;
                    }
                }
            }
            return null;
        }

        public static IInputDevice? MouseOf(object platform) =>
            platform.GetType().GetProperty("MouseDevice", Everything)?.GetValue(platform) as IInputDevice;

        public static IInputDevice NewMouse() =>
            (IInputDevice)Construct(typeof(MouseDevice), [typeof(Avalonia.Input.Pointer)], [null]);

        public static IKeyboardDevice Keyboard
        {
            get
            {
                object locator = Property(typeof(AvaloniaLocator), "Current").GetValue(null)
                    ?? throw new InvalidOperationException("Avalonia is not started");
                MethodInfo getService = locator.GetType().GetMethod("GetService", [typeof(Type)])
                    ?? throw new MissingMethodException("AvaloniaLocator.Current.GetService(Type) is missing");
                return getService.Invoke(locator, [typeof(IKeyboardDevice)]) as IKeyboardDevice
                    ?? throw new InvalidOperationException("The platform has no keyboard device");
            }
        }

        public static Action<RawInputEventArgs>? InputOf(object platform) =>
            Property(typeof(Avalonia.Platform.ITopLevelImpl), "Input").GetValue(platform) as Action<RawInputEventArgs>;

        public static RawInputEventArgs Pointer(IInputDevice mouse, ulong timestamp, IInputRoot root, RawPointerEventType type, Point at, RawInputModifiers modifiers) =>
            (RawInputEventArgs)Construct(typeof(RawPointerEventArgs),
                [typeof(IInputDevice), typeof(ulong), typeof(IInputRoot), typeof(RawPointerEventType), typeof(Point), typeof(RawInputModifiers)],
                [mouse, timestamp, root, type, at, modifiers]);

        public static RawInputEventArgs Wheel(IInputDevice mouse, ulong timestamp, IInputRoot root, Point at, Vector delta, RawInputModifiers modifiers) =>
            (RawInputEventArgs)Construct(typeof(RawMouseWheelEventArgs),
                [typeof(IInputDevice), typeof(ulong), typeof(IInputRoot), typeof(Point), typeof(Vector), typeof(RawInputModifiers)],
                [mouse, timestamp, root, at, delta, modifiers]);

        public static RawInputEventArgs Key(IKeyboardDevice keyboard, ulong timestamp, IInputRoot root, RawKeyEventType type, Key key, RawInputModifiers modifiers, PhysicalKey physical, string? symbol) =>
            (RawInputEventArgs)Construct(typeof(RawKeyEventArgs),
                [typeof(IInputDevice), typeof(ulong), typeof(IInputRoot), typeof(RawKeyEventType), typeof(Key), typeof(RawInputModifiers), typeof(PhysicalKey), typeof(string), typeof(KeyDeviceType)],
                [keyboard, timestamp, root, type, key, modifiers, physical, symbol, KeyDeviceType.Keyboard]);

        public static RawInputEventArgs Text(IKeyboardDevice keyboard, ulong timestamp, IInputRoot root, string text) =>
            (RawInputEventArgs)Construct(typeof(RawTextInputEventArgs),
                [typeof(IKeyboardDevice), typeof(ulong), typeof(IInputRoot), typeof(string)],
                [keyboard, timestamp, root, text]);

        public static RawInputEventArgs Drag(RawDragEventType type, IInputRoot root, Point at, IDataTransfer data, DragDropEffects effects, RawInputModifiers modifiers) =>
            (RawInputEventArgs)Construct(typeof(RawDragEvent),
                [typeof(IDragDropDevice), typeof(RawDragEventType), typeof(IInputRoot), typeof(Point), typeof(IDataTransfer), typeof(DragDropEffects), typeof(RawInputModifiers)],
                [StaticValue(typeof(DragDropDevice), "Instance"), type, root, at, data, effects, modifiers]);

        public static DragDropEffects EffectsOf(RawInputEventArgs drag) =>
            (DragDropEffects)(Property(typeof(RawDragEvent), "Effects").GetValue(drag) ?? DragDropEffects.None);

        private static object Construct(Type type, Type[] parameters, object?[] arguments)
        {
            ConstructorInfo constructor = type.GetConstructor(Everything, parameters)
                ?? throw new MissingMethodException($"{type.Name}({string.Join(", ", parameters.Select(parameter => parameter.Name))}) is missing in this Avalonia");
            return constructor.Invoke(arguments);
        }

        private static object? StaticValue(Type type, string name) =>
            type.GetProperty(name, Everything) is { } property ? property.GetValue(null)
            : type.GetField(name, Everything) is { } field ? field.GetValue(null)
            : throw new MissingMemberException($"{type.Name}.{name} is missing in this Avalonia");

        private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Everything)
            ?? throw new MissingMemberException($"{type.Name}.{name} is missing in this Avalonia");
    }

    private static IEnumerable<string> Characters(string text)
    {
        System.Globalization.TextElementEnumerator elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            yield return elements.GetTextElement();
        }
    }

    private static string? SymbolOf(Key key, RawInputModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(RawInputModifiers.Shift);
        return key switch
        {
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => shift ? key.ToString() : key.ToString().ToLowerInvariant(),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => ((char)('0' + (key - Avalonia.Input.Key.D0))).ToString(),
            >= Avalonia.Input.Key.NumPad0 and <= Avalonia.Input.Key.NumPad9 => ((char)('0' + (key - Avalonia.Input.Key.NumPad0))).ToString(),
            Avalonia.Input.Key.Space => " ",
            _ => null,
        };
    }

    private static PhysicalKey PhysicalOf(Key key) => key switch
    {
        >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => PhysicalKey.A + (key - Avalonia.Input.Key.A),
        >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => PhysicalKey.Digit0 + (key - Avalonia.Input.Key.D0),
        >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F12 => PhysicalKey.F1 + (key - Avalonia.Input.Key.F1),
        Avalonia.Input.Key.Escape => PhysicalKey.Escape,
        Avalonia.Input.Key.Delete => PhysicalKey.Delete,
        Avalonia.Input.Key.Enter => PhysicalKey.Enter,
        Avalonia.Input.Key.Space => PhysicalKey.Space,
        Avalonia.Input.Key.Tab => PhysicalKey.Tab,
        Avalonia.Input.Key.Back => PhysicalKey.Backspace,
        Avalonia.Input.Key.Up => PhysicalKey.ArrowUp,
        Avalonia.Input.Key.Down => PhysicalKey.ArrowDown,
        Avalonia.Input.Key.Left => PhysicalKey.ArrowLeft,
        Avalonia.Input.Key.Right => PhysicalKey.ArrowRight,
        Avalonia.Input.Key.Home => PhysicalKey.Home,
        Avalonia.Input.Key.End => PhysicalKey.End,
        Avalonia.Input.Key.PageUp => PhysicalKey.PageUp,
        Avalonia.Input.Key.PageDown => PhysicalKey.PageDown,
        Avalonia.Input.Key.Insert => PhysicalKey.Insert,
        _ => PhysicalKey.None,
    };
}
