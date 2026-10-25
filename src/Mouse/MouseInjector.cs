using System.Runtime.InteropServices;

namespace RealforceApcMouse.Mouse;

/// <summary>
/// Injects relative mouse movement using the standard Win32 SendInput API.
/// This is ordinary, publicly documented Win32 usage — not derived from any
/// Topre code.
/// </summary>
public static class MouseInjector
{
    /// <summary>One wheel notch, as defined by Win32.</summary>
    public const int WheelDelta = 120;

    public static void MoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return;

        Send(NativeMethods.MOUSEEVENTF_MOVE, dx, dy, 0);
    }

    public static void LeftDown() => Send(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0);
    public static void LeftUp() => Send(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0);
    public static void RightDown() => Send(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0);
    public static void RightUp() => Send(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0);

    /// <summary>Vertical wheel. Positive scrolls up, negative scrolls down.</summary>
    public static void ScrollWheel(int amount) => Send(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, amount);

    private static void Send(uint flags, int dx, int dy, int mouseData)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = unchecked((uint)mouseData),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static class NativeMethods
    {
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }
    }
}
