using RealforceApcMouse.Hid;

namespace RealforceApcMouse.Protocol;

/// <summary>
/// R3-generation memory-mapped command client: read/write 32-byte ROM/RAM
/// blocks, and fire numbered general requests. Command numbers and block
/// numbers are documented in docs/PROTOCOL.md.
/// </summary>
public sealed class R3MemoryMapClient : IDisposable
{
    public const int BlockSize = 32;
    public const int RomBlockCount = 192;
    public const int RamBlockCount = 161;
    public const int KeyCount = 128;

    private const byte CmdGeneralRequest = 0xC0;
    private const byte CmdRomRead = 0xC1;
    private const byte CmdRomWrite = 0xC2;
    private const byte CmdRamRead = 0xC3;
    private const byte CmdRamWrite = 0xC4;
    private const byte CmdLiveLed = 0xA1; // inferred from firmware 0x0801B4EA; see docs/LED_TRACE.md
    private const byte CmdLiveLedPercent = 0xA2; // inferred from firmware 0x0801B654

    public const int RamBlockSupportedFunctions = 157;
    public const int RamBlockBattery = 159;

    private readonly HidTransport _transport;
    private readonly object _transactLock = new();

    private R3MemoryMapClient(HidTransport transport) => _transport = transport;

    public static R3MemoryMapClient Open(string devicePath)
        => new(HidTransport.Open(devicePath, Frame.ReportLength));

    public byte[] ReadRomBlock(int block) => Transact(CmdRomRead, new[] { (byte)block });

    public void WriteRomBlock(int block, byte[] data)
    {
        if (data.Length != BlockSize)
            throw new ArgumentException($"ROM block writes must be {BlockSize} bytes.", nameof(data));
        var payload = new byte[1 + BlockSize];
        payload[0] = (byte)block;
        data.CopyTo(payload, 1);
        Transact(CmdRomWrite, payload);
    }

    public byte[] ReadRamBlock(int block) => Transact(CmdRamRead, new[] { (byte)block });

    public void WriteRamBlock(int block, byte[] data)
    {
        if (data.Length != BlockSize)
            throw new ArgumentException($"RAM block writes must be {BlockSize} bytes.", nameof(data));
        var payload = new byte[1 + BlockSize];
        payload[0] = (byte)block;
        data.CopyTo(payload, 1);
        Transact(CmdRamWrite, payload);
    }

    public void GeneralRequest(byte subRequest) => Transact(CmdGeneralRequest, new[] { subRequest });

    /// <summary>Volatile (RAM-only) indicator effect. Unverified command; layout from firmware decode.</summary>
    public void LiveLed(byte subCommand, byte channelMask, byte level, ushort duration)
    {
        var payload = new byte[11];
        payload[0] = subCommand;
        payload[1] = channelMask;
        payload[2] = level;
        payload[9] = (byte)(duration >> 8);
        payload[10] = (byte)duration;
        Transact(CmdLiveLed, payload);
    }

    /// <summary>Sets the persistent idle color from 0-100 percent per channel (unverified command).</summary>
    public void LiveLedPercent(byte red, byte green, byte blue)
        => Transact(CmdLiveLedPercent, new[] { Math.Min(red, (byte)100), Math.Min(green, (byte)100), Math.Min(blue, (byte)100) });

    private byte[] Transact(byte command, byte[] payload)
    {
        lock (_transactLock)
        {
            _transport.Write(Frame.BuildRequest(command, payload));
            var response = _transport.Read();
            return Frame.ParseResponse(response, command);
        }
    }

    public void Dispose() => _transport.Dispose();
}
