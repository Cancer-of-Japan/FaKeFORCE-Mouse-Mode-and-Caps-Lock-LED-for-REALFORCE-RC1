namespace RealforceApcMouse.Protocol;

/// <summary>
/// Reads a single key's live analog press state. Reading RAM block N for
/// 0 &lt;= N &lt; 128 returns this key's current status record (see
/// docs/PROTOCOL.md, "Per-key live status block"). Field widths and offsets
/// are a hypothesis derived from the device's own configuration channel;
/// <see cref="RawBytes"/> is kept on the result so callers/tests can sanity
/// check it against a live keyboard.
/// </summary>
public readonly struct KeyStatus
{
    public byte Flags { get; }
    public byte LockFlag { get; }
    public ushort RawData { get; }
    public ushort Minimum { get; }
    public ushort Maximum { get; }
    public IReadOnlyList<ushort> Thresholds { get; }
    public byte[] RawBytes { get; }

    public KeyStatus(byte flags, byte lockFlag, ushort rawData, ushort minimum, ushort maximum,
        IReadOnlyList<ushort> thresholds, byte[] rawBytes)
    {
        Flags = flags;
        LockFlag = lockFlag;
        RawData = rawData;
        Minimum = minimum;
        Maximum = maximum;
        Thresholds = thresholds;
        RawBytes = rawBytes;
    }

    /// <summary>
    /// Normalized press depth in [0, 1], 0 = fully released, 1 = fully bottomed out,
    /// derived from RawData scaled against this key's own observed Minimum/Maximum.
    /// Returns 0 if the key hasn't reported a usable calibration range yet.
    /// </summary>
    public double NormalizedDepth()
    {
        int range = Maximum - Minimum;
        if (range <= 0)
            return 0;

        double depth = (RawData - Minimum) / (double)range;
        return Math.Clamp(depth, 0.0, 1.0);
    }
}

public static class KeyStatusReader
{
    public static KeyStatus Read(R3MemoryMapClient client, int keyIndex)
    {
        if (keyIndex is < 0 or >= R3MemoryMapClient.KeyCount)
            throw new ArgumentOutOfRangeException(nameof(keyIndex), $"Key index must be 0-{R3MemoryMapClient.KeyCount - 1}.");

        byte[] raw = client.ReadRamBlock(keyIndex);
        return Parse(raw);
    }

    internal static KeyStatus Parse(byte[] raw)
    {
        byte flags = raw.Length > 0 ? raw[0] : (byte)0;
        byte lockFlag = raw.Length > 1 ? raw[1] : (byte)0;
        ushort rawData = ReadWordOrZero(raw, 2);
        ushort minimum = ReadWordOrZero(raw, 4);
        ushort maximum = ReadWordOrZero(raw, 6);

        var thresholds = new List<ushort>(8);
        for (int i = 0; i < 8; i++)
            thresholds.Add(ReadWordOrZero(raw, 8 + i * 2));

        return new KeyStatus(flags, lockFlag, rawData, minimum, maximum, thresholds, raw);
    }

    private static ushort ReadWordOrZero(byte[] data, int offset)
        => offset + 1 < data.Length ? Frame.ReadBigEndianU16(data, offset) : (ushort)0;
}
