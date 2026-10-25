namespace RealforceApcMouse.Protocol;

/// <summary>
/// Wire-format framing for the Topre REALFORCE "R3-generation" vendor HID
/// configuration channel. See docs/PROTOCOL.md for how these constants were
/// derived and corroborated. All field offsets are into the 65-byte HID
/// report, including the leading report-ID byte at offset 0.
/// </summary>
public static class Frame
{
    public const int ReportLength = 65;
    public const int HeaderLength = 6;
    public const int MaxPayloadLength = ReportLength - HeaderLength;

    public const byte SignalSend = 0xAA;
    public const byte SignalReceive = 0x55;

    public const byte ReportIdUsb = 0x00;
    public const byte ReportIdBluetooth = 0x12;

    // Offsets within an outgoing (request) report.
    public const int OffsetReportId = 0;
    public const int OffsetSignalLow = 1;
    public const int OffsetSignalHigh = 2;
    public const int OffsetCommand = 3;
    public const int OffsetFlag = 4;
    public const int OffsetLength = 5;
    public const int OffsetData = 6;

    // Offsets within an incoming (response) report: one byte wider header
    // than a request. Status and flag are both present (status at 4, flag
    // at 5), pushing length to 6 and the payload to 7.
    public const int ResponseOffsetStatus = 4;
    public const int ResponseOffsetFlag = 5;
    public const int ResponseOffsetLength = 6;
    public const int ResponseOffsetData = 7;

    public const byte StatusOk = 0x00;

    /// <summary>Builds a request report: signature + command + payload, zero-padded to 65 bytes.</summary>
    public static byte[] BuildRequest(byte command, ReadOnlySpan<byte> payload, byte reportId = ReportIdUsb)
    {
        if (payload.Length > MaxPayloadLength)
            throw new ArgumentException($"Payload too long ({payload.Length} > {MaxPayloadLength}).", nameof(payload));

        var report = new byte[ReportLength];
        report[OffsetReportId] = reportId;
        report[OffsetSignalLow] = SignalSend;
        report[OffsetSignalHigh] = SignalSend;
        report[OffsetCommand] = command;
        report[OffsetFlag] = 0x00;
        report[OffsetLength] = (byte)payload.Length;
        payload.CopyTo(report.AsSpan(OffsetData));
        return report;
    }

    /// <summary>Validates a response report's signature/command/status and returns its payload.</summary>
    public static byte[] ParseResponse(byte[] report, byte expectedCommand)
    {
        if (report.Length != ReportLength)
            throw new ArgumentException($"Response must be {ReportLength} bytes, got {report.Length}.");

        if (report[OffsetSignalLow] != SignalReceive || report[OffsetSignalHigh] != SignalReceive)
            throw new InvalidDataException("Response signature mismatch (expected 0x55 0x55).");

        if (report[OffsetCommand] != expectedCommand)
            throw new InvalidDataException(
                $"Response command mismatch (expected 0x{expectedCommand:X2}, got 0x{report[OffsetCommand]:X2}).");

        if (report[ResponseOffsetStatus] != StatusOk)
            throw new RealforceProtocolException(report[ResponseOffsetStatus]);

        int length = report[ResponseOffsetLength];
        var payload = new byte[length];
        Array.Copy(report, ResponseOffsetData, payload, 0, Math.Min(length, report.Length - ResponseOffsetData));
        return payload;
    }

    public static ushort ReadBigEndianU16(ReadOnlySpan<byte> data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);
}

public sealed class RealforceProtocolException : Exception
{
    public byte StatusCode { get; }

    public RealforceProtocolException(byte statusCode)
        : base($"Device returned non-OK status 0x{statusCode:X2}.")
    {
        StatusCode = statusCode;
    }
}
