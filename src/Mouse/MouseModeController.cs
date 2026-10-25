using RealforceApcMouse.Input;
using RealforceApcMouse.Mouse;
using RealforceApcMouse.Protocol;

namespace RealforceApcMouse.Mouse;

/// <summary>
/// Configuration for mouse mode: which analog key slots drive movement and the
/// optional Fn slot used in the enter/leave combo.
/// </summary>
public sealed class MouseModeOptions
{
    public required int UpIndex { get; set; }
    public required int DownIndex { get; set; }
    public required int LeftIndex { get; set; }
    public required int RightIndex { get; set; }

    public int? FnIndex { get; set; }
    public int UpKey { get; set; } = 0x57;
    public int DownKey { get; set; } = 0x53;
    public int LeftKey { get; set; } = 0x41;
    public int RightKey { get; set; } = 0x44;
    public int LeftClickKey { get; set; } = 0x51;
    public int RightClickKey { get; set; } = 0x45;
    public int ScrollUpKey { get; set; } = 0x4B;
    public int ScrollDownKey { get; set; } = 0x4A;
    public int ActivationKey1 { get; set; } = 0xA1;
    public int ActivationKey2 { get; set; }
    public bool UseFnActivation { get; set; } = true;
    public bool HoldToActivate { get; set; }

    /// <summary>Scroll notches emitted per second while J/K is held.</summary>
    public double ScrollNotchesPerSecond { get; set; } = 18.0;
}

/// <summary>
/// Drives mouse mode using the configured activation chord and action bindings.
/// The movement keys use analog press depth; click and scroll use keyboard state.
/// Action keys are suppressed from normal typing via
/// <see cref="KeyboardHook"/> while the mode is active.
/// </summary>
public sealed class MouseModeController
{
    private readonly R3MemoryMapClient _client;
    private readonly MouseAccelerationEngine _engine;
    private readonly KeyboardHook _hook;
    private readonly MouseModeOptions _options;

    private bool _active;
    private bool _comboLatched;
    private bool _leftDown;
    private bool _rightDown;
    private double _scrollAccumulator;
    private (int Up, int Down, int Left, int Right)? _lastMovementSlots;
    private int[] _lastSuppressedKeys = Array.Empty<int>();

    /// <summary>Raised when mouse mode toggles on (true) or off (false).</summary>
    public event Action<bool>? ModeChanged;

    public MouseModeController(
        R3MemoryMapClient client,
        MouseAccelerationEngine engine,
        KeyboardHook hook,
        MouseModeOptions options)
    {
        _client = client;
        _engine = engine;
        _hook = hook;
        _options = options;
    }

    public void Run(CancellationToken token)
    {
        const int tickMs = 8;
        var lastTick = DateTime.UtcNow;

        while (!token.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            double dtSeconds = (now - lastTick).TotalSeconds;
            lastTick = now;

            UpdateToggle();

            if (_active)
            {
                RefreshRuntimeBindings();
                _engine.Tick();
                HandleButtons();
                HandleScroll(dtSeconds);
            }

            Thread.Sleep(tickMs);
        }

        // Leave the system in a clean state on exit.
        ReleaseHeldButtons();
        _hook.SuppressActive = false;
    }

    private void UpdateToggle()
    {
        bool combo = ComboPressed();
        if (_options.HoldToActivate)
        {
            SetActive(combo);
        }
        else if (combo && !_comboLatched)
        {
            SetActive(!_active);
        }
        _comboLatched = combo;
    }

    private bool ComboPressed()
    {
        if (_options.ActivationKey1 <= 0 || !_hook.IsDown(_options.ActivationKey1))
            return false;

        if (_options.ActivationKey2 > 0 && !_hook.IsDown(_options.ActivationKey2))
            return false;

        if (!_options.UseFnActivation)
            return true;

        if (_options.FnIndex is not int fn)
            return false;

        double depth = KeyStatusReader.Read(_client, fn).NormalizedDepth();
        return depth > 0.3;
    }

    private void SetActive(bool active)
    {
        if (_active == active)
            return;

        _active = active;
        _hook.SuppressActive = active;
        if (active)
            RefreshSuppressedKeys();
        if (!active)
            ReleaseHeldButtons();

        Console.WriteLine(active ? "Mouse mode: ON" : "Mouse mode: OFF");
        ModeChanged?.Invoke(active);
    }

    private void RefreshRuntimeBindings()
    {
        var slots = (_options.UpIndex, _options.DownIndex, _options.LeftIndex, _options.RightIndex);
        if (_lastMovementSlots != slots)
        {
            _engine.SetBindings(new[]
            {
                new KeyAxisBinding { KeyIndex = slots.Item1, DirectionX = 0, DirectionY = -1 },
                new KeyAxisBinding { KeyIndex = slots.Item2, DirectionX = 0, DirectionY = 1 },
                new KeyAxisBinding { KeyIndex = slots.Item3, DirectionX = -1, DirectionY = 0 },
                new KeyAxisBinding { KeyIndex = slots.Item4, DirectionX = 1, DirectionY = 0 },
            });
            _lastMovementSlots = slots;
        }

        RefreshSuppressedKeys();
    }

    private void RefreshSuppressedKeys()
    {
        int[] keys = GetSuppressedKeys();
        if (_lastSuppressedKeys.SequenceEqual(keys))
            return;

        _hook.SetSuppressedKeys(keys);
        _lastSuppressedKeys = keys;
    }

    private int[] GetSuppressedKeys() => new[]
    {
        _options.UpKey, _options.DownKey, _options.LeftKey, _options.RightKey,
        _options.LeftClickKey, _options.RightClickKey,
        _options.ScrollUpKey, _options.ScrollDownKey,
    }.Where(key => key > 0).Distinct().ToArray();

    private void HandleButtons()
    {
        bool q = _hook.IsDown(_options.LeftClickKey);
        if (q && !_leftDown) MouseInjector.LeftDown();
        else if (!q && _leftDown) MouseInjector.LeftUp();
        _leftDown = q;

        bool e = _hook.IsDown(_options.RightClickKey);
        if (e && !_rightDown) MouseInjector.RightDown();
        else if (!e && _rightDown) MouseInjector.RightUp();
        _rightDown = e;
    }

    private void HandleScroll(double dtSeconds)
    {
        int direction = 0;
        if (_hook.IsDown(_options.ScrollUpKey)) direction += 1;
        if (_hook.IsDown(_options.ScrollDownKey)) direction -= 1;

        if (direction == 0)
        {
            _scrollAccumulator = 0;
            return;
        }

        _scrollAccumulator += dtSeconds * _options.ScrollNotchesPerSecond;
        while (_scrollAccumulator >= 1.0)
        {
            MouseInjector.ScrollWheel(direction * MouseInjector.WheelDelta);
            _scrollAccumulator -= 1.0;
        }
    }

    private void ReleaseHeldButtons()
    {
        if (_leftDown) { MouseInjector.LeftUp(); _leftDown = false; }
        if (_rightDown) { MouseInjector.RightUp(); _rightDown = false; }
        _scrollAccumulator = 0;
    }
}
