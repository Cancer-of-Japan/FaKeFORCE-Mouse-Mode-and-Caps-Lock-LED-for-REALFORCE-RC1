using System.Runtime.InteropServices;

namespace RealforceApcMouse.Input;

/// <summary>
/// A global low-level keyboard hook (WH_KEYBOARD_LL). It tracks which keys are
/// currently held and, while <see cref="SuppressActive"/> is set, swallows a
/// configured set of virtual-key codes so they never reach the focused app.
/// This is how mouse mode stops W/A/S/D/Q/E/J/K from typing letters while they
/// drive the cursor. The hook runs its own message loop on a dedicated thread,
/// as Win32 requires.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    private readonly NativeMethods.LowLevelKeyboardProc _proc; // kept alive to prevent GC of the callback
    private int[] _suppressKeys;
    private readonly bool[] _down = new bool[256];
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _threadId;

    /// <summary>When true, keys in the suppress set are blocked from the OS.</summary>
    public volatile bool SuppressActive;

    public KeyboardHook(IEnumerable<int> suppressKeys)
    {
        _suppressKeys = suppressKeys.Distinct().ToArray();
        _proc = HookCallback;
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "realforce-kbd-hook" };
        _thread.Start();
        _ready.Wait(2000);
    }

    public bool IsDown(int virtualKey) => virtualKey is >= 0 and < 256 && _down[virtualKey];

    public void SetSuppressedKeys(IEnumerable<int> virtualKeys)
        => Volatile.Write(ref _suppressKeys, virtualKeys.Where(key => key is > 0 and < 256).Distinct().ToArray());

    private void ThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hookHandle = NativeMethods.SetWindowsHookEx(
            WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        _ready.Set();

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = (int)wParam;
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field

            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (vk is >= 0 and < 256) _down[vk] = true;
            }
            else if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                if (vk is >= 0 and < 256) _down[vk] = false;
            }

            if (SuppressActive && Volatile.Read(ref _suppressKeys).Contains(vk))
                return (IntPtr)1; // swallow: don't pass to the next hook or the app
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0)
            NativeMethods.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }

    private static class NativeMethods
    {
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }
    }
}
