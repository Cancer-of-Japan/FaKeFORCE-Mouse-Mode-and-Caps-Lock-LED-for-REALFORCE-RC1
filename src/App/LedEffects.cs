using System.Diagnostics;
using System.Runtime.InteropServices;
using RealforceApcMouse.Protocol;

namespace RealforceApcMouse.App;

/// <summary>
/// Drives the RC1 status LED from the host while the app runs: red blink while Caps Lock
/// is on, RGB color cycling while mouse mode is active, otherwise off. Uses the volatile
/// firmware command 0xA2 (RAM only), so nothing is written to ROM.
/// </summary>
public sealed class LedEffects : IDisposable
{
    private const int FrameMs = 25;
    private const int VkCapital = 0x14;

    private readonly R3MemoryMapClient _client;
    private readonly Func<bool> _isEnabled;
    private readonly Func<MouseSettings> _getSettings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;
    private volatile bool _mouseModeActive;
    private volatile bool _everWritten;

    public LedEffects(R3MemoryMapClient client, Func<bool> isEnabled, Func<MouseSettings> getSettings)
    {
        _client = client;
        _isEnabled = isEnabled;
        _getSettings = getSettings;
        _thread = new Thread(Run) { IsBackground = true, Name = "led-effects" };
    }

    public void SetMouseModeActive(bool active) => _mouseModeActive = active;

    public void Start() => _thread.Start();

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        (byte R, byte G, byte B)? sent = null;
        bool lit = false;

        while (!_cts.IsCancellationRequested)
        {
            double t = clock.Elapsed.TotalSeconds;
            bool caps = (GetKeyState(VkCapital) & 1) != 0;
            bool enabled = _isEnabled();
            bool effect = enabled && (caps || _mouseModeActive);
            (byte R, byte G, byte B)? wanted = null;
            var s = _getSettings();

            if (effect && caps)
            {
                int periodMs = s.CapsLockBlinkDurationMs <= 0 ? 250 : s.CapsLockBlinkDurationMs;
                double phase = (t * 1000.0) / periodMs % 2.0;
                wanted = phase < 1.0 ? (s.CapsLockLedR, s.CapsLockLedG, s.CapsLockLedB) : ((byte)0, (byte)0, (byte)0);
            }
            else if (effect && _mouseModeActive)
            {
                if (string.Equals(s.MouseModeLedPattern, "Static", StringComparison.OrdinalIgnoreCase))
                    wanted = (s.MouseModeLedR, s.MouseModeLedG, s.MouseModeLedB);
                else
                {
                    double speed = s.MouseModeLedDurationMs <= 0 ? 6.0 : (6.0 * 6000.0 / s.MouseModeLedDurationMs);
                    double hue = t / speed % 1.0 * 6.0;
                    double x = 1 - Math.Abs(hue % 2 - 1);
                    (double r, double g, double b) = (int)hue switch
                    {
                        0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x),
                        3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
                    };
                    wanted = ((byte)Math.Round(r * 100), (byte)Math.Round(g * 100), (byte)Math.Round(b * 100));
                }
            }
            else if (lit)
                wanted = (0, 0, 0); // release the LED once after an effect ends

            if (wanted is { } color && color != sent)
            {
                try
                {
                    _client.LiveLedPercent(color.R, color.G, color.B);
                    sent = color;
                    _everWritten = true;
                    lit = effect;
                }
                catch (Exception) when (!_cts.IsCancellationRequested)
                {
                    Thread.Sleep(1000); // device busy or unplugged; retry next frame
                }
            }

            Thread.Sleep(FrameMs);
        }
    }

    private static (byte R, byte G, byte B) RgbCycle(double t)
    {
        double hue = t / 6.0 % 1.0 * 6.0;
        double x = 1 - Math.Abs(hue % 2 - 1);
        (double r, double g, double b) = (int)hue switch
        {
            0 => (1.0, x, 0.0), 1 => (x, 1.0, 0.0), 2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0), 4 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
        };

        return ((byte)Math.Round(r * 100), (byte)Math.Round(g * 100), (byte)Math.Round(b * 100));
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive)
            _thread.Join(1000);

        if (_everWritten)
        {
            try { _client.LiveLedPercent(0, 0, 0); }
            catch (Exception) { /* device already gone */ }
        }

        _cts.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
