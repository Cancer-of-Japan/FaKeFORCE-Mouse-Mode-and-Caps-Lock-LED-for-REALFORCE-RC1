using RealforceApcMouse.App;
using RealforceApcMouse.Hid;
using RealforceApcMouse.Input;
using RealforceApcMouse.Mouse;
using RealforceApcMouse.Protocol;
using System.Reflection;

namespace RealforceApcMouse;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return LaunchBackgroundTray();

        if (args[0] == "--background-child")
            return RunBackgroundTray();

        if (args[0] == "tray")
        {
            using var instanceMutex = new Mutex(false, TrayApp.InstanceMutexName);
            bool ownsMutex;
            try
            {
                ownsMutex = instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                TrayApp.RequestSettings();
                return 0;
            }

            using var settingsSignal = TrayApp.CreateSettingsSignal();
            try
            {
                return RunTray(args);
            }
            finally
            {
                instanceMutex.ReleaseMutex();
            }
        }

        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "list" => RunList(),
            "status" => RunStatus(args),
            "scan" => RunScan(),
            "led-dump" => RunLedDump(),
            "led-test" => RunLedTest(),
            "led-restore" => RunLedRestore(),
            "led-live" => RunLedLive(args),
            "run" => RunMouse(args),
            "mouse-mode" => RunMouseMode(args),
            "tray" => RunTray(args),
            "settings" => RunSettings(),
            "install" => RunInstall(args),
            "uninstall" => RunUninstall(),
            _ => Unknown(args[0]),
        };
    }

    private static int LaunchBackgroundTray()
    {
        using var instanceMutex = new Mutex(false, TrayApp.InstanceMutexName);
        if (!TryAcquireMutex(instanceMutex))
        {
            TrayApp.RequestSettings();
            return 0;
        }

        using var settingsSignal = TrayApp.CreateSettingsSignal();
        using var childReady = TrayApp.CreateChildReadySignal();
        try
        {
            string exePath = GetAppExecutablePath();

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--background-child",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (System.Diagnostics.Process.Start(startInfo) is null)
                throw new InvalidOperationException("Could not start the background tray process.");

            if (!childReady.WaitOne(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("The background tray process did not start in time.");

            Console.WriteLine("REALFORCE mouse mode is running in the system tray.");
            return 0;
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private static int RunBackgroundTray()
    {
        TrayApp.SignalChildReady();
        using var instanceMutex = new Mutex(false, TrayApp.InstanceMutexName);
        if (!TryAcquireMutex(instanceMutex, TimeSpan.FromSeconds(5)))
        {
            TrayApp.RequestSettings();
            return 0;
        }

        using var settingsSignal = TrayApp.CreateSettingsSignal();
        try
        {
            return RunTray(Array.Empty<string>());
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private static bool TryAcquireMutex(Mutex mutex, TimeSpan? timeout = null)
    {
        try
        {
            return mutex.WaitOne(timeout ?? TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            realforce-apc-mouse - use a REALFORCE APC key's analog press depth as a mouse axis

            Usage:
              realforce-apc-mouse list
                  List connected REALFORCE vendor HID configuration interfaces.

              realforce-apc-mouse scan
                  Read-only: samples all 128 key slots twice, 2 seconds apart,
                  and reports which indices changed - press and hold one key
                  during the wait to find its index. Helps you build --up/
                  --down/--left/--right without needing the keymap decoded.

              realforce-apc-mouse led-dump
                  Read-only: print LED-related ROM blocks 50-51 and 96-133 as
                  hex for comparing lighting settings before and after using
                  REALFORCE CONNECT. This command never writes to the keyboard.

              realforce-apc-mouse led-test
                  Temporarily sets the RC1 indicator LED to red at full brightness
                  for 15 seconds, then restores its original blocks. Prompts
                  before writing; press Caps Lock during the test to verify.

              realforce-apc-mouse led-restore
                  Restore the saved LED block if an LED test was interrupted.

              realforce-apc-mouse status <keyIndex> [--watch]
                  Read (and optionally continuously watch) one key's live analog
                  status (raw depth, calibration bounds, thresholds). Read-only;
                  useful to find a key's index and verify the protocol on your
                  hardware before using 'run'.

              realforce-apc-mouse run --up <k> --down <k> --left <k> --right <k>
                                       [--max-speed N] [--deadzone F] [--curve F]
                  Continuously poll the given keys' analog depth and inject
                  relative mouse movement. Read-only against the keyboard -
                  never writes ROM/RAM, never commits to flash.

              realforce-apc-mouse mouse-mode [--fn <k>] [--up <k>] [--down <k>]
                                             [--left <k>] [--right <k>]
                                             [--max-speed N] [--deadzone F]
                                             [--curve F] [--scroll-speed N]
                  Full mouse mode. Tap Fn+Right-Shift to toggle it on/off.
                  While on: W/A/S/D move the cursor by analog press depth,
                  Q/E are left/right click, J/K scroll down/up. Those keys are
                  blocked from typing while mouse mode is active (via a Windows
                  keyboard hook). Defaults: --up 17 --down 31 --left 30 --right
                  32 (QWERTY WASD on a REALFORCE). Pass --fn <k> with Fn's
                  analog slot; omit it to toggle with Right-Ctrl+Right-Shift.
                  The status LED cycles through RGB colors while mouse mode is on and
                  blinks red while Caps Lock is on (volatile, nothing is saved to
                  the keyboard). Pass --no-led to leave the LED alone.

              realforce-apc-mouse tray [same options as mouse-mode]
                  Run mouse mode hidden in the background with a system-tray
                  icon (right-click the icon to Quit). Keeps working after the
                  launching terminal is closed.

              realforce-apc-mouse install [same options as mouse-mode]
                  Register the tray app to auto-start at every Windows login
                  (per-user, no admin needed) and start it now. Options are
                  remembered in the autostart command.

              realforce-apc-mouse uninstall
                  Remove the auto-start entry.

                realforce-apc-mouse settings
                  Start the tray if needed, then open its live mouse-settings
                  window: sliders for speed, acceleration curve, deadzone and
                  scroll speed, plus a checkbox to disable LED effects, with a
                  live chart of press depth (X) vs mouse speed (Y). Closing the
                  window leaves mouse mode and LED effects running in the tray.
                  The tray icon also has Mouse sensitivity... and LED effects
                  entries (and double-click) for quick access.

            Mouse/status/scan/LED-dump operations only read from the keyboard.
            LED-test and LED-restore write ROM blocks 50-51 and reload LED settings;
            they do not commit RAM to ROM. Back up your profile before using them.
            See docs/PROTOCOL.md for the reverse-engineered protocol.
            """);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run with --help for usage.");
        return 1;
    }

    private static int RunList()
    {
        var paths = HidDeviceFinder.FindConfigInterfaces().ToList();
        if (paths.Count == 0)
        {
            Console.WriteLine("No Topre REALFORCE configuration interface found.");
            return 1;
        }

        foreach (var path in paths)
            Console.WriteLine(path);
        return 0;
    }

    private static int RunStatus(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int keyIndex))
        {
            Console.Error.WriteLine("Usage: realforce-apc-mouse status <keyIndex> [--watch]");
            return 1;
        }

        bool watch = args.Contains("--watch");

        using var client = OpenFirstDeviceOrThrow();

        do
        {
            var status = KeyStatusReader.Read(client, keyIndex);
            Console.WriteLine(
                $"key {keyIndex,3}: raw={status.RawData,5} min={status.Minimum,5} max={status.Maximum,5} " +
                $"depth={status.NormalizedDepth():P1} flags=0x{status.Flags:X2} lock=0x{status.LockFlag:X2} " +
                $"thresh=[{string.Join(",", status.Thresholds)}] bytes=[{Convert.ToHexString(status.RawBytes)}]");

            if (watch)
                Thread.Sleep(50);
        } while (watch && !Console.KeyAvailable);

        return 0;
    }

    private static int RunScan()
    {
        using var client = OpenFirstDeviceOrThrow();

        Console.WriteLine("Sampling all 128 keys... press and hold the key you want to identify now.");
        var before = new ushort[R3MemoryMapClient.KeyCount];
        for (int i = 0; i < before.Length; i++)
            before[i] = KeyStatusReader.Read(client, i).RawData;

        Thread.Sleep(2000);

        Console.WriteLine("Sampling again...");
        bool any = false;
        for (int i = 0; i < before.Length; i++)
        {
            var status = KeyStatusReader.Read(client, i);
            int delta = status.RawData - before[i];
            if (Math.Abs(delta) > 10) // small analog jitter is normal; only report real movement
            {
                any = true;
                Console.WriteLine($"key {i,3}: raw {before[i]} -> {status.RawData} (delta {delta:+0;-0})");
            }
        }

        if (!any)
            Console.WriteLine("No key changed. Re-run and hold a key down throughout the 2-second wait.");

        return 0;
    }

    private static int RunLedDump()
    {
        using var client = OpenFirstDeviceOrThrow();
        int[] blocks = new[] { 50, 51 }.Concat(Enumerable.Range(96, 38)).ToArray();

        foreach (int block in blocks)
        {
            byte[] data = client.ReadRomBlock(block);
            Console.WriteLine($"{block:D3}:{Convert.ToHexString(data)}");
        }

        return 0;
    }

    private static int RunLedTest()
    {
        string backupPath = GetLedIndicatorBackupPath();
        if (File.Exists(backupPath) || File.Exists(GetLegacyLedBackupPath()))
        {
            Console.Error.WriteLine("An LED backup already exists. Run 'led-restore' before starting another test.");
            return 1;
        }

        Console.WriteLine(
            "This test temporarily writes RC1 indicator LED ROM blocks 50-51 " +
            "(100% brightness + red), reloads LED settings, then restores both blocks after 15 seconds. " +
            "Two persistent ROM writes will occur.");
        Console.Write("Proceed? [y/N] ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Cancelled; keyboard was not changed.");
            return 0;
        }

        using var client = OpenFirstDeviceOrThrow();
    byte[] originalBrightness = client.ReadRomBlock(50);
    byte[] originalColor = client.ReadRomBlock(51);
    byte[] backup = new byte[R3MemoryMapClient.BlockSize * 2];
    originalBrightness.CopyTo(backup, 0);
    originalColor.CopyTo(backup, R3MemoryMapClient.BlockSize);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
    File.WriteAllBytes(backupPath, backup);
    byte[] testBrightness = (byte[])originalBrightness.Clone();
    byte[] testColor = (byte[])originalColor.Clone();
    testBrightness[0] = 4; // 100% brightness
    testColor[0] = 4; // red indicator

        bool testWriteAttempted = false;
        var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            testWriteAttempted = true;
            client.WriteRomBlock(50, testBrightness);
            client.WriteRomBlock(51, testColor);
            client.GeneralRequest(0x09); // reload LED settings
            Console.WriteLine("Test active for 15 seconds. Toggle Caps Lock now and check its indicator.");
            for (int remaining = 15; remaining > 0 && !cancellation.IsCancellationRequested; remaining--)
            {
                Console.Write($"Restoring in {remaining} seconds...\r");
                Thread.Sleep(1000);
            }
            Console.WriteLine();
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            cancellation.Dispose();
            if (testWriteAttempted)
            {
                RestoreLedBlock(client, backupPath);
            }
        }

        return 0;
    }

    private static int RunLedLive(string[] args)
    {
        // led-live [sub] [mask] [level] [durationTicks]   or   led-live cycle
        using var client = OpenFirstDeviceOrThrow();

        if (args.Length > 1 && args[1] == "rgb")
        {
            client.LiveLedPercent(byte.Parse(args[2]), byte.Parse(args[3]), byte.Parse(args[4]));
            return 0;
        }

        if (args.Length > 1 && args[1] == "breathe")
        {
            Console.WriteLine("RGB breathing for 15 seconds...");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 15)
            {
                double t = clock.Elapsed.TotalSeconds;
                double hue = (t / 6.0 % 1.0) * 6.0;
                double glow = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 3.0);
                double x = 1 - Math.Abs(hue % 2 - 1);
                (double r, double g, double b) = (int)hue switch
                {
                    0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x),
                    3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
                };
                client.LiveLedPercent((byte)(r * glow * 100), (byte)(g * glow * 100), (byte)(b * glow * 100));
                Thread.Sleep(50);
            }
            client.LiveLedPercent(0, 0, 0);
            return 0;
        }

        if (args.Length > 1 && args[1] == "cycle")
        {
            Console.WriteLine("Starting in 3 seconds - watch the indicator LED...");
            Thread.Sleep(3000);
            foreach (byte mask in new byte[] { 1, 2, 4, 3, 5, 6, 7 })
            {
                Console.WriteLine($"mask {mask} (bit0={mask & 1} bit1={(mask >> 1) & 1} bit2={(mask >> 2) & 1}) - now showing");
                client.LiveLed(0, mask, 4, 6000);
                Thread.Sleep(5000);
            }
            return 0;
        }

        byte sub = args.Length > 1 ? byte.Parse(args[1]) : (byte)0;
        byte maskArg = args.Length > 2 ? byte.Parse(args[2]) : (byte)7;
        byte level = args.Length > 3 ? byte.Parse(args[3]) : (byte)4;
        ushort duration = args.Length > 4 ? ushort.Parse(args[4]) : (ushort)3000;
        client.LiveLed(sub, maskArg, level, duration);
        Console.WriteLine($"Sent sub={sub} mask={maskArg} level={level} duration={duration}. Replug the keyboard if the LED sticks.");
        return 0;
    }

    private static int RunLedRestore()
    {
        string backupPath = File.Exists(GetLedIndicatorBackupPath())
            ? GetLedIndicatorBackupPath()
            : GetLegacyLedBackupPath();
        if (!File.Exists(backupPath))
        {
            Console.WriteLine("No interrupted LED test backup exists.");
            return 0;
        }

        using var client = OpenFirstDeviceOrThrow();
        RestoreLedBlock(client, backupPath);
        return 0;
    }

    private static void RestoreLedBlock(R3MemoryMapClient client, string backupPath)
    {
        byte[] original = File.ReadAllBytes(backupPath);
        if (original.Length == R3MemoryMapClient.BlockSize)
        {
            client.WriteRomBlock(96, original);
            client.GeneralRequest(0x09);
            File.Delete(backupPath);
            Console.WriteLine("Original LED block 96 restored.");
            return;
        }
        if (original.Length != R3MemoryMapClient.BlockSize * 2)
            throw new InvalidDataException("LED backup must contain 32 legacy bytes or 64 indicator bytes.");

        byte[] brightness = original[..R3MemoryMapClient.BlockSize];
        byte[] color = original[R3MemoryMapClient.BlockSize..];
        client.WriteRomBlock(50, brightness);
        client.WriteRomBlock(51, color);
        client.GeneralRequest(0x09);
        File.Delete(backupPath);
        Console.WriteLine("Original indicator LED blocks restored.");
    }

    private static string GetLedIndicatorBackupPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RealforceApcMouse",
            "led-indicator-blocks-50-51.backup");

    private static string GetLegacyLedBackupPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RealforceApcMouse",
            "led-block-96.backup");

    private static int RunMouse(string[] args)
    {
        int? up = ParseIntOption(args, "--up");
        int? down = ParseIntOption(args, "--down");
        int? left = ParseIntOption(args, "--left");
        int? right = ParseIntOption(args, "--right");

        if (up is null && down is null && left is null && right is null)
        {
            Console.Error.WriteLine(
                "Specify at least one of --up/--down/--left/--right <keyIndex>. Use 'status' to find key indices.");
            return 1;
        }

        double maxSpeed = ParseDoubleOption(args, "--max-speed") ?? 18.0;
        double deadzone = ParseDoubleOption(args, "--deadzone") ?? 0.08;
        double curveExponent = ParseDoubleOption(args, "--curve") ?? 2.0;

        var bindings = new List<KeyAxisBinding>();
        if (up is int u) bindings.Add(new KeyAxisBinding { KeyIndex = u, DirectionX = 0, DirectionY = -1 });
        if (down is int d) bindings.Add(new KeyAxisBinding { KeyIndex = d, DirectionX = 0, DirectionY = 1 });
        if (left is int l) bindings.Add(new KeyAxisBinding { KeyIndex = l, DirectionX = -1, DirectionY = 0 });
        if (right is int r) bindings.Add(new KeyAxisBinding { KeyIndex = r, DirectionX = 1, DirectionY = 0 });

        var curve = new AccelerationCurve
        {
            MaxSpeed = maxSpeed,
            Deadzone = deadzone,
            CurveExponent = curveExponent,
        };

        using var client = OpenFirstDeviceOrThrow();
        var engine = new MouseAccelerationEngine(client, bindings, curve);

        Console.WriteLine("Running. Press Ctrl+C to stop.");
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (!cts.IsCancellationRequested)
        {
            engine.Tick();
            Thread.Sleep(8); // ~125 Hz poll, matches typical HID polling intervals
        }

        return 0;
    }

    private static int RunMouseMode(string[] args)
    {
        var setup = BuildMouseMode(args);
        using var client = setup.Client;
        using var hook = setup.Hook;
        using var led = setup.Led;

        string toggle = setup.FnIndex is int fn ? $"Fn(slot {fn})+Right-Shift" : "Right-Ctrl+Right-Shift";
        Console.WriteLine($"Mouse mode ready. Toggle with {toggle}. Press Ctrl+C to quit.");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        setup.Controller.Run(cts.Token);
        return 0;
    }

    private static int RunTray(string[] args)
    {
        var setup = BuildMouseMode(args);
        Autostart.InstallBackground(GetAppExecutablePath());

        using var app = new TrayApp(
            setup.Controller, setup.Hook, setup.Client,
            setup.Settings, setup.Curve, setup.Options, setup.Led);
        app.Run();
        return 0;
    }

    private static int RunSettings()
    {
        LaunchBackgroundTray();
        TrayApp.RequestSettings();
        return 0;
    }

    private static int RunInstall(string[] args)
    {
        string exe = GetAppExecutablePath();

        // Forward any tuning args (everything after "install") to the autostart command.
        string extra = string.Join(' ', args.Skip(1));
        string command = Autostart.Install(exe, extra);
        Console.WriteLine($"Autostart installed. At login Windows will run:\n  {command}");
        Console.WriteLine("Starting it now in the background (system tray).");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = string.IsNullOrWhiteSpace(extra) ? "tray" : $"tray {extra}",
            UseShellExecute = false,
        });
        return 0;
    }

    private static int RunUninstall()
    {
        bool removed = Autostart.Uninstall();
        Console.WriteLine(removed
            ? "Autostart removed. A running tray instance keeps going until you Quit it from the tray."
            : "No autostart entry was set.");
        return 0;
    }

    private static string GetAppExecutablePath()
    {
        string? location = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(location))
        {
            string candidate = Path.ChangeExtension(location, ".exe");
            if (File.Exists(candidate))
                return candidate;
        }

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return processPath;

        throw new InvalidOperationException(
            "Could not locate the app executable. Build/publish the app and run it from the generated .exe.");
    }

    private readonly struct MouseModeSetup
    {
        public required R3MemoryMapClient Client { get; init; }
        public required KeyboardHook Hook { get; init; }
        public required MouseModeController Controller { get; init; }
        public required AccelerationCurve Curve { get; init; }
        public required MouseModeOptions Options { get; init; }
        public required MouseSettings Settings { get; init; }
        public required int? FnIndex { get; init; }
        public required LedEffects? Led { get; init; }
    }

    private static MouseModeSetup BuildMouseMode(string[] args)
    {
        // Saved settings are the defaults; any CLI flag overrides them.
        var settings = SettingsStore.Load();
        bool noLed = args.Contains("--no-led");

        int up = ParseIntOption(args, "--up") ?? settings.Up;
        int down = ParseIntOption(args, "--down") ?? settings.Down;
        int left = ParseIntOption(args, "--left") ?? settings.Left;
        int right = ParseIntOption(args, "--right") ?? settings.Right;
        int? fn = ParseIntOption(args, "--fn") ?? settings.Fn;

        double maxSpeed = ParseDoubleOption(args, "--max-speed") ?? settings.MaxSpeed;
        double deadzone = ParseDoubleOption(args, "--deadzone") ?? settings.Deadzone;
        double curveExponent = ParseDoubleOption(args, "--curve") ?? settings.CurveExponent;
        double scrollSpeed = ParseDoubleOption(args, "--scroll-speed") ?? settings.ScrollSpeed;
        settings.DisableLedEffects |= noLed;

        // Keep the settings object in sync so the GUI opens with the effective values.
        settings.Up = up; settings.Down = down; settings.Left = left; settings.Right = right; settings.Fn = fn;
        settings.MaxSpeed = maxSpeed; settings.Deadzone = deadzone;
        settings.CurveExponent = curveExponent; settings.ScrollSpeed = scrollSpeed;

        var bindings = new List<KeyAxisBinding>
        {
            new() { KeyIndex = up, DirectionX = 0, DirectionY = -1 },
            new() { KeyIndex = down, DirectionX = 0, DirectionY = 1 },
            new() { KeyIndex = left, DirectionX = -1, DirectionY = 0 },
            new() { KeyIndex = right, DirectionX = 1, DirectionY = 0 },
        };

        var curve = new AccelerationCurve
        {
            MaxSpeed = maxSpeed,
            Deadzone = deadzone,
            CurveExponent = curveExponent,
        };

        var options = new MouseModeOptions
        {
            UpIndex = up,
            DownIndex = down,
            LeftIndex = left,
            RightIndex = right,
            FnIndex = fn,
            ScrollNotchesPerSecond = scrollSpeed,
            UpKey = settings.UpKey,
            DownKey = settings.DownKey,
            LeftKey = settings.LeftKey,
            RightKey = settings.RightKey,
            LeftClickKey = settings.LeftClickKey,
            RightClickKey = settings.RightClickKey,
            ScrollUpKey = settings.ScrollUpKey,
            ScrollDownKey = settings.ScrollDownKey,
            ActivationKey1 = settings.ActivationKey1,
            ActivationKey2 = settings.ActivationKey2,
            UseFnActivation = settings.UseFnActivation,
            HoldToActivate = settings.HoldToActivate,
        };

        var client = OpenFirstDeviceOrThrow();
        var engine = new MouseAccelerationEngine(client, bindings, curve);
        var hook = new KeyboardHook(Array.Empty<int>());
        var controller = new MouseModeController(client, engine, hook, options);

        LedEffects? led = null;
        if (!noLed)
        {
            led = new LedEffects(client, () => !settings.DisableLedEffects, () => settings);
            controller.ModeChanged += led.SetMouseModeActive;
            led.Start();
        }

        System.Diagnostics.Debug.Assert(noLed == (led is null));

        return new MouseModeSetup
        {
            Client = client,
            Hook = hook,
            Controller = controller,
            Curve = curve,
            Options = options,
            Settings = settings,
            FnIndex = fn,
            Led = led,
        };
    }

    private static R3MemoryMapClient OpenFirstDeviceOrThrow()
    {
        var path = HidDeviceFinder.FindConfigInterfaces().FirstOrDefault()
            ?? throw new InvalidOperationException("No Topre REALFORCE configuration interface found.");
        return R3MemoryMapClient.Open(path);
    }

    private static int? ParseIntOption(string[] args, string name)
    {
        int idx = Array.IndexOf(args, name);
        if (idx < 0 || idx + 1 >= args.Length)
            return null;
        return int.TryParse(args[idx + 1], out var value) ? value : null;
    }

    private static double? ParseDoubleOption(string[] args, string name)
    {
        int idx = Array.IndexOf(args, name);
        if (idx < 0 || idx + 1 >= args.Length)
            return null;
        return double.TryParse(args[idx + 1], System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
