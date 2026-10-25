using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RealforceApcMouse.Hid;

/// <summary>
/// Raw synchronous HID report read/write over a Win32 file handle. One fixed-size
/// report per call, matching how Windows exposes HID input/output reports.
/// </summary>
public sealed class HidTransport : IDisposable
{
    // One FileStream owns the handle for the transport's whole lifetime: a
    // FileStream constructed over a SafeFileHandle takes ownership of it, so
    // wrapping the handle in a new FileStream per call would close the
    // handle the first time that per-call stream was disposed.
    private readonly FileStream _stream;
    public int ReportLength { get; }

    private HidTransport(FileStream stream, int reportLength)
    {
        _stream = stream;
        ReportLength = reportLength;
    }

    public static HidTransport Open(string devicePath, int reportLength)
    {
        const uint genericRead = 0x80000000;
        const uint genericWrite = 0x40000000;

        var handle = NativeMethods.CreateFile(
            devicePath,
            genericRead | genericWrite,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"Could not open HID device '{devicePath}' (error {Marshal.GetLastWin32Error()}).");

        var stream = new FileStream(handle, FileAccess.ReadWrite, reportLength, isAsync: false);
        return new HidTransport(stream, reportLength);
    }

    /// <summary>Sends a report. <paramref name="report"/> must be exactly ReportLength bytes, report ID in [0].</summary>
    public void Write(byte[] report)
    {
        if (report.Length != ReportLength)
            throw new ArgumentException($"Report must be {ReportLength} bytes.", nameof(report));

        _stream.Write(report, 0, report.Length);
        _stream.Flush();
    }

    /// <summary>Blocks until one report arrives; returns exactly ReportLength bytes, report ID in [0].</summary>
    public byte[] Read()
    {
        var buffer = new byte[ReportLength];
        int total = 0;
        while (total < buffer.Length)
        {
            int n = _stream.Read(buffer, total, buffer.Length - total);
            if (n <= 0)
                throw new IOException("HID device closed the stream.");
            total += n;
        }
        return buffer;
    }

    public void Dispose() => _stream.Dispose();
}
