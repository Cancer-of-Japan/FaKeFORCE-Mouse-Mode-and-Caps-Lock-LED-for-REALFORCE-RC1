using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RealforceApcMouse.Hid;

/// <summary>
/// Minimal Win32 SetupAPI + hid.dll device enumeration: finds the vendor-defined
/// HID configuration interface (USB interface "MI_02") exposed by Topre
/// REALFORCE keyboards (USB vendor ID 0x0853). This mirrors the device-selection
/// logic independently re-derived while studying the vendor's own configuration
/// tool's public enumeration behavior (match on VID_0853, PID_03xx, MI_02), not
/// any copied code.
/// </summary>
public static class HidDeviceFinder
{
    private const ushort TopreVendorId = 0x0853;

    /// <summary>
    /// Enumerates all HID device paths on the system and returns those that
    /// belong to a Topre REALFORCE keyboard's vendor-defined configuration
    /// collection (device path containing "MI_02").
    /// </summary>
    public static IEnumerable<string> FindConfigInterfaces()
    {
        foreach (var path in EnumerateHidPaths())
        {
            if (!path.Contains("mi_02", StringComparison.OrdinalIgnoreCase))
                continue;

            using var handle = NativeMethods.CreateFile(
                path,
                0, // query-only: no GENERIC_READ/WRITE needed to read attributes
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
                continue;

            if (!NativeMethods.HidD_GetAttributes(handle, out var attributes))
                continue;

            if (attributes.VendorID == TopreVendorId)
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateHidPaths()
    {
        var hidGuid = NativeMethods.HidGuid;
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            yield break;

        try
        {
            var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf(interfaceData);

            uint index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(
                       deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
            {
                index++;

                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

                if (requiredSize == 0)
                    continue;

                var detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 4 + 1);

                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                        continue;

                    var pathPtr = detailDataBuffer + 4;
                    var path = Marshal.PtrToStringAuto(pathPtr);
                    if (path != null)
                        yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detailDataBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }
}

internal static class NativeMethods
{
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    public static Guid HidGuid
    {
        get
        {
            HidD_GetHidGuid(out var guid);
            return guid;
        }
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, out HIDD_ATTRIBUTES attributes);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }
}
