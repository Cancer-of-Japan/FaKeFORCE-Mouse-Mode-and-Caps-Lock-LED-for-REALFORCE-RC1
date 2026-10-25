using RealforceApcMouse.Protocol;

namespace RealforceApcMouse.Mouse;

/// <summary>
/// One physical key mapped to a 2D movement direction, driven by that key's
/// live analog press depth.
/// </summary>
public sealed class KeyAxisBinding
{
    public required int KeyIndex { get; init; }
    public required double DirectionX { get; init; }
    public required double DirectionY { get; init; }
}

public sealed class AccelerationCurve
{
    /// <summary>Press depth below this fraction produces no movement (filters noise/resting touch).</summary>
    public double Deadzone { get; set; } = 0.08;

    /// <summary>Cursor speed at full press depth, in pixels per poll tick.</summary>
    public double MaxSpeed { get; set; } = 18.0;

    /// <summary>
    /// Exponent applied to normalized depth beyond the deadzone: 1.0 = linear,
    /// &gt;1.0 = slow start / fast finish ("soft" acceleration), &lt;1.0 = fast
    /// start / slow finish.
    /// </summary>
    public double CurveExponent { get; set; } = 2.0;
    public bool UseSCurve { get; set; }

    public double Evaluate(double normalizedDepth)
    {
        if (normalizedDepth <= Deadzone)
            return 0.0;

        double scaled = (normalizedDepth - Deadzone) / (1.0 - Deadzone);
        scaled = Math.Clamp(scaled, 0.0, 1.0);
        if (!UseSCurve)
            return Math.Pow(scaled, CurveExponent) * MaxSpeed;

        double rising = Math.Pow(scaled, CurveExponent);
        double falling = Math.Pow(1.0 - scaled, CurveExponent);
        return rising / (rising + falling) * MaxSpeed;
    }
}

/// <summary>
/// Polls a set of keys' live analog depth and turns them into continuous
/// mouse movement: this is the feature the whole project exists for.
/// </summary>
public sealed class MouseAccelerationEngine
{
    private readonly R3MemoryMapClient _client;
    private IReadOnlyList<KeyAxisBinding> _bindings;
    private readonly AccelerationCurve _curve;
    private readonly Dictionary<int, (ushort Min, ushort Max)> _calibration;
    private double _fractionalX;
    private double _fractionalY;

    public MouseAccelerationEngine(
        R3MemoryMapClient client,
        IReadOnlyList<KeyAxisBinding> bindings,
        AccelerationCurve curve,
        IReadOnlyDictionary<int, (ushort Min, ushort Max)>? calibrationOverride = null)
    {
        _client = client;
        _bindings = bindings;
        _curve = curve;
        _calibration = calibrationOverride is null
            ? new Dictionary<int, (ushort, ushort)>()
            : new Dictionary<int, (ushort, ushort)>(calibrationOverride);
    }

    /// <summary>Reads all bound keys once and injects the resulting relative mouse movement.</summary>
    public void SetBindings(IReadOnlyList<KeyAxisBinding> bindings)
        => Volatile.Write(ref _bindings, bindings);

    /// <summary>Reads all bound keys once and injects the resulting relative mouse movement.</summary>
    public void Tick()
    {
        double totalX = 0, totalY = 0;

        foreach (var binding in Volatile.Read(ref _bindings))
        {
            var status = KeyStatusReader.Read(_client, binding.KeyIndex);
            double depth = ResolveDepth(binding.KeyIndex, status);
            double speed = _curve.Evaluate(depth);

            totalX += binding.DirectionX * speed;
            totalY += binding.DirectionY * speed;
        }

        int dx = AccumulatePixels(totalX, ref _fractionalX);
        int dy = AccumulatePixels(totalY, ref _fractionalY);
        MouseInjector.MoveRelative(dx, dy);
    }

    private static int AccumulatePixels(double movement, ref double remainder)
    {
        if (movement == 0)
        {
            remainder = 0;
            return 0;
        }

        double accumulated = movement + remainder;
        int pixels = (int)Math.Truncate(accumulated);
        remainder = accumulated - pixels;
        return pixels;
    }

    private double ResolveDepth(int keyIndex, KeyStatus status)
    {
        if (_calibration.TryGetValue(keyIndex, out var range))
        {
            int span = range.Max - range.Min;
            if (span <= 0)
                return 0;
            return Math.Clamp((status.RawData - range.Min) / (double)span, 0.0, 1.0);
        }

        return status.NormalizedDepth();
    }
}
