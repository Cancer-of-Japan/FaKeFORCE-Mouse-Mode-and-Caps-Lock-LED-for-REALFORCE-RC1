using Microsoft.Win32;

namespace RealforceApcMouse.App;

/// <summary>
/// Registers this app to launch at Windows login via the per-user Run key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). This is a user-scoped,
/// fully reversible change - no admin rights, no services, no system files.
/// </summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RealforceApcMouse";

    /// <summary>Registers autostart to run "<exe> tray [extraArgs]" at login, and returns the command stored.</summary>
    public static string Install(string exePath, string extraArgs)
    {
        string command = string.IsNullOrWhiteSpace(extraArgs)
            ? $"\"{exePath}\" tray"
            : $"\"{exePath}\" tray {extraArgs}";

        SetCommand(command);
        return command;
    }

    public static string InstallBackground(string exePath)
    {
        string command = $"\"{exePath}\" --background-child";
        SetCommand(command);
        return command;
    }

    private static void SetCommand(string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the per-user Run registry key.");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    /// <summary>Removes the autostart entry. Returns true if one existed.</summary>
    public static bool Uninstall()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is null)
            return false;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    }

    public static string? CurrentCommand()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }
}
