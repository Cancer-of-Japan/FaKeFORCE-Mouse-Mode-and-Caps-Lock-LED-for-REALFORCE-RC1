using System.Text.Json;

namespace RealforceApcMouse.App;

/// <summary>
/// User-tunable mouse-mode settings, persisted as JSON so the GUI, the tray app,
/// and autostart all share one source of truth.
/// </summary>
public sealed class MouseSettings
{
    public double MaxSpeed { get; set; } = 18.0;
    public double CurveExponent { get; set; } = 2.0;
    public bool UseSCurve { get; set; }
    public double Deadzone { get; set; } = 0.08;
    public double ScrollSpeed { get; set; } = 18.0;

    public int Up { get; set; } = 17;
    public int Down { get; set; } = 31;
    public int Left { get; set; } = 30;
    public int Right { get; set; } = 32;
    public int? Fn { get; set; } = 69;

    public int UpKey { get; set; } = 0x57;
    public int DownKey { get; set; } = 0x53;
    public int LeftKey { get; set; } = 0x41;
    public int RightKey { get; set; } = 0x44;
    public int LeftClickKey { get; set; } = 0x51;
    public int RightClickKey { get; set; } = 0x45;
    public int ScrollUpKey { get; set; } = 0x4B;
    public int ScrollDownKey { get; set; } = 0x4A;

    public int ActivationKey1 { get; set; } = 0xA1;
    public int ActivationKey2 { get; set; }
    public bool UseFnActivation { get; set; } = true;
    public bool HoldToActivate { get; set; }
    public bool DisableLedEffects { get; set; }

    public byte CapsLockLedR { get; set; } = 100;
    public byte CapsLockLedG { get; set; } = 0;
    public byte CapsLockLedB { get; set; } = 0;
    public int CapsLockBlinkDurationMs { get; set; } = 250;

    public string MouseModeLedPattern { get; set; } = "Cycle";
    public byte MouseModeLedR { get; set; } = 0;
    public byte MouseModeLedG { get; set; } = 100;
    public byte MouseModeLedB { get; set; } = 100;
    public int MouseModeLedDurationMs { get; set; } = 6000;

    public MouseSettings Clone() => (MouseSettings)MemberwiseClone();
}

/// <summary>Loads and saves <see cref="MouseSettings"/> under %APPDATA%\RealforceApcMouse.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RealforceApcMouse",
        "settings.json");

    public static MouseSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<MouseSettings>(json);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults rather than crashing.
        }

        return new MouseSettings();
    }

    public static void Save(MouseSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
