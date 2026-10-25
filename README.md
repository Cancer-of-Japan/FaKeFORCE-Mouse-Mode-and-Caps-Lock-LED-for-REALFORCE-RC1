# FaKeFORCE Software, Mouse Mode and Caps Lock Indicator for REALFORCE RC1

Use the analog keys on a Topre REALFORCE RC1 keyboard to control the mouse and adds Caps Lock LED feature. The app reads key press depth and turns it into pointer movement, clicks, and scrolling.

> **Windows only:** This version requires Windows and the .NET 8 SDK. The REALFORCE keyboard must stay connected while the app is running. Mouse control and LED effects stop if you quit the app or disconnect the keyboard.(I myself is a Linux/Mac user and Microsoft product aint my favorite but I must follow democracy. I will add both Mac and Linux support in future.)

> **Project note:** This project was created through vibe coding. REA was used to reverse-engineer the original software and firmware to understand their behavior. The functionality, performance, and UI have not been fully verified; this is experimental software and may be incomplete or unreliable. Do not rely on it for critical use.

## Why this project exists

Topre makes amazing keyboards. My first experience with its switches was on a Japanese 7-Eleven ATM keypad, which apparently uses the same switches as REALFORCE. That experience eventually led me to buy the RC1, and I loved it. Typing on it makes everyday tasks feel theatrical: asking an AI “Who is JSON?” can feel like Socrates questioning Plato; writing a client email can feel like Cicero addressing the Senate; and complaining to my PM about a Teams deadline can feel like Mishima. To me, the keyboard feels like a creation by Da Vinci and Michelangelo.

But the RC1 has a frustrating omission. Topre’s setup software offers mouse mode and a Caps Lock LED indicator for some other models, such as the R4 and GX1, but not the RC1. My reverse-engineering suggests the RC1 can drive these status-LED effects, even though the official software does not expose them. I felt let down, like Michael realizing Fredo had betrayed him in Godfather.

With an “if there’s a will, there’s a way” attitude, and help from GPT-5.6 Luna, I used REA to study the original software and firmware’s LED-control paths. That work led to FaKeFORCE: an experimental companion app that brings mouse mode and Caps Lock LED effects to the RC1.

## What it does

- W/A/S/D move the pointer according to analog key depth.
- Q/E perform left and right clicks.
- J/K scroll down and up.
- Fn + Right-Shift turns mouse mode on or off.
- The status LED cycles through RGB while mouse mode is on. Caps Lock takes priority and blinks red. These effects are temporary; they do not modify keyboard firmware or saved lighting settings.
- The Caps Lock effect follows the Windows Caps Lock state, not just the key press: while Caps Lock is enabled, the LED blinks using the selected Caps Lock color and timing instead of the mouse-mode LED effect. Configure both under **LED Effects**. Turning LED effects off disables both effects; the feature works only while this app is running and the keyboard is connected.
- Settings let you change speed, deadzone, Power or S-curve response, key assignments, the activation shortcut, and LED effects.

## Install and start

1. Install the **[.NET 8 SDK for Windows](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)** from Microsoft. Open a new PowerShell window after installing it.
2. Connect the REALFORCE RC1 keyboard.
3. Open PowerShell in the repository folder. Run:

   ```powershell
   cd .\src
   dotnet run
   ```

On the first run, the app builds, starts in the background, and registers itself to start when you sign in to Windows. You will see its icon in the notification area near the clock. Once the command returns, you can close PowerShell; the app keeps running.

Press **Fn + Right-Shift** to turn mouse mode on. Press it again to turn mouse mode off. While mouse mode is on, its assigned movement, click, and scroll keys are intercepted and will not type into other apps.

## Open settings

While the tray app is running, open PowerShell in the repository's `src` folder and run:

```powershell
dotnet run --no-build
```

This asks the running app to show its Settings window; it does not start a second mouse controller. You can also right-click the tray icon and choose **Mouse sensitivity...**.

- **Mouse Mode:** overview of the activation shortcut.
- **Sensitivity:** movement speed, deadzone, curve shape, and curve strength. Power gives a slow-to-fast response; S-curve eases in and out with a stronger middle response.
- **Key Assignment:** analog slots and keyboard keys for movement, clicks, scrolling, and activation.
- **LED Effects:** enable or disable effects, choose colors and timing, and preview them.

Changes apply to the running app. Press **Save** to keep them for future launches. Settings are stored in `%APPDATA%\RealforceApcMouse\settings.json`.

## Stop or disable startup

Right-click the tray icon and select **Quit** to stop the app. This does not remove its Windows login startup entry.

To disable automatic startup, while the app is running open PowerShell in `src` and run:

```powershell
dotnet run --no-build -- uninstall
```

The app continues running until you choose **Quit**. Running `dotnet run` again starts it and re-enables automatic startup.

## Build and troubleshooting

To build without starting the app:

```powershell
cd .\src
dotnet build
```

If a build reports that `realforce-apc-mouse.exe` is locked, the background app is still running. Choose **Quit** from the tray menu, then run `dotnet run` again. Keep the repository in the same location after first launch; Windows login startup points to the executable inside this folder.

If the keyboard is not found, check that it is connected, then run `dotnet run -- list` from `src`. `dotnet run -- scan` can help identify analog slot numbers; `dotnet run -- status 17 --watch` watches a slot (replace `17` with the slot to inspect). Press Ctrl+C to stop a diagnostic command.

`dotnet run -- led-test` is an optional hardware diagnostic that temporarily writes LED configuration blocks and restores them afterward. It asks for confirmation before writing. Skip it if you do not want to test saved keyboard lighting.

For the protocol reference and technical details, see [docs/PROTOCOL.md](docs/PROTOCOL.md) and [docs/LED_TRACE.md](docs/LED_TRACE.md).
