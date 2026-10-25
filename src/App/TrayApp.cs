using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RealforceApcMouse.Mouse;

namespace RealforceApcMouse.App;

/// <summary>
/// Resident background mode: hides the console, shows a system-tray icon, and
/// runs the mouse-mode controller on a worker thread. This is what lets mouse
/// mode keep working after the launching terminal is closed - the process stays
/// alive in the tray until you choose Quit. Pairs with <see cref="Autostart"/>
/// so it can relaunch at every Windows login.
/// </summary>
public sealed class TrayApp : IDisposable
{
    public const string InstanceMutexName = "Local\\RealforceApcMouse.Instance";
    private const string SettingsEventName = "Local\\RealforceApcMouse.OpenSettings";
    private const string ChildReadyEventName = "Local\\RealforceApcMouse.BackgroundReady";

    private readonly MouseModeController _controller;
    private readonly IDisposable _hook;
    private readonly IDisposable _client;
    private readonly IDisposable? _led;
    private readonly MouseSettings _settings;
    private readonly AccelerationCurve _curve;
    private readonly MouseModeOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private SynchronizationContext? _ui;
    private Thread? _worker;
    private SettingsForm? _settingsForm;
    private EventWaitHandle? _settingsEvent;
    private System.Windows.Forms.Timer? _settingsEventTimer;

    public TrayApp(
        MouseModeController controller,
        IDisposable hook,
        IDisposable client,
        MouseSettings settings,
        AccelerationCurve curve,
        MouseModeOptions options,
        IDisposable? led = null)
    {
        _controller = controller;
        _hook = hook;
        _client = client;
        _led = led;
        _settings = settings;
        _curve = curve;
        _options = options;

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Mouse mode: OFF") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mouse sensitivity...", null, (_, _) => OpenSettings());

        var ledEffectsItem = new ToolStripMenuItem("LED effects")
        {
            CheckOnClick = true,
            Checked = !_settings.DisableLedEffects,
        };
        ledEffectsItem.Click += (_, _) => _settings.DisableLedEffects = !ledEffectsItem.Checked;
        menu.Items.Add(ledEffectsItem);

        menu.Opening += (_, _) => ledEffectsItem.Checked = !_settings.DisableLedEffects;

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "FAKEFORCE mouse mode (off)",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenSettings();

        _controller.ModeChanged += active => _ui?.Post(_ => UpdateUi(active), null);
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        // Bind the form to the live curve/options so changes apply immediately.
        _settingsForm = new SettingsForm(_settings, _curve, _options, onSaved: null, client: _client as RealforceApcMouse.Protocol.R3MemoryMapClient);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void UpdateUi(bool active)
    {
        _statusItem.Text = active ? "Mouse mode: ON" : "Mouse mode: OFF";
        _icon.Text = active ? "FAKEFORCE mouse mode (ON)" : "FAKEFORCE mouse mode (off)";
    }

    public void Run()
    {
        HideConsoleWindow();

        _ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        _settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsEventName);
        _settingsEventTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _settingsEventTimer.Tick += (_, _) =>
        {
            if (_settingsEvent.WaitOne(0))
                OpenSettings();
        };
        _settingsEventTimer.Start();

        _worker = new Thread(() => _controller.Run(_cts.Token))
        {
            IsBackground = true,
            Name = "mouse-mode-worker",
        };
        _worker.Start();

        Application.Run(); // pumps the tray message loop until Application.Exit
    }

    public static EventWaitHandle CreateSettingsSignal()
        => new(false, EventResetMode.AutoReset, SettingsEventName);

    public static EventWaitHandle CreateChildReadySignal()
        => new(false, EventResetMode.AutoReset, ChildReadyEventName);

    public static void SignalChildReady()
    {
        try
        {
            using var ready = EventWaitHandle.OpenExisting(ChildReadyEventName);
            ready.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public static void RequestSettings()
    {
        try
        {
            using var settingsEvent = EventWaitHandle.OpenExisting(SettingsEventName);
            settingsEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void Quit()
    {
        _cts.Cancel();
        _worker?.Join(1000);
        _icon.Visible = false;
        Application.Exit();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _worker?.Join(1000);
        _icon.Dispose();
        _settingsEventTimer?.Stop();
        _settingsEventTimer?.Dispose();
        _settingsEvent?.Dispose();
        _hook.Dispose();
        _led?.Dispose();
        _client.Dispose();
        _cts.Dispose();
    }

    private static void HideConsoleWindow()
    {
        IntPtr handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
            ShowWindow(handle, SW_HIDE);
    }

    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
