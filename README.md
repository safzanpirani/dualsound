# DualSound

DualSound mirrors all audio playing through your Windows default output to as many as seven additional playback devices, for eight outputs total.

## Use it

1. Connect both playback devices in Windows.
2. Make Device 01 your Windows default output.
3. Open `DualSound.exe` and select up to seven additional outputs.
4. Select **Start mirroring**.

The app uses WASAPI shared-mode loopback. It needs no administrator rights, audio driver, account, or internet connection. Keep DualSound open while mirroring.

Bluetooth devices introduce their own buffering, so a Bluetooth speaker may sound behind wired speakers. Two independent Windows audio devices cannot be perfectly clock-synchronized without a virtual/multi-output audio driver.

## Agent setup prompt

Copy this prompt into a coding or computer-use agent running on the Windows PC:

```text
Set up DualSound on this Windows PC from https://github.com/safzanpirani/dualsound.

Goal: make the PC's current Windows default output play normally, then mirror the same system audio to the additional playback devices the user chooses. DualSound supports the default output plus up to seven additional outputs.

Do the following:

1. Confirm this is 64-bit Windows 10 or 11. List the active playback devices and identify the current Windows multimedia default. Do not change the default device without asking the user.
2. Ask which additional playback devices should receive audio. Do not guess based on device names.
3. Get the DualSound source. Check for a .NET 9 SDK with `dotnet --list-sdks`. If it is missing, ask before installing it. Use `winget search Microsoft.DotNet.SDK.9` to confirm the current package, then install the matching .NET 9 SDK.
4. From the repository root, run `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.
5. Confirm that `artifacts\win-x64\DualSound.exe` exists. Copy it to `%LOCALAPPDATA%\Programs\DualSound\DualSound.exe`, then launch that copy. The published executable is self-contained and should not need the SDK after this step.
6. In DualSound, confirm Device 01 matches the Windows default. Select only the additional outputs approved by the user, then start mirroring.
7. Play a short test sound through the normal Windows output. Ask the user to confirm that every selected device is audible. Keep the app running for at least 30 seconds and check that it still says `MIRRORING` with no error. Do not claim the speakers worked unless the user confirms hearing them.
8. Stop and start mirroring once to test cleanup. Create a desktop shortcut only if the user wants one. Do not add startup behavior unless asked.
9. Report the installed executable path, default device, selected additional devices, build result, and what the user confirmed hearing. Mention that Bluetooth outputs can lag and independent hardware devices cannot be perfectly synchronized without a virtual audio driver.
```

## Build on Windows

Requirements: Windows 10/11 and the .NET 9 SDK.

```powershell
.\scripts\build.ps1
```

The self-contained executable is written to `artifacts\win-x64\DualSound.exe`.

## Architecture

- WPF desktop interface targeting `net9.0-windows`
- NAudio 3 `WasapiRecorder` loopback capture from the default render endpoint
- One NAudio 3 `WasapiPlayer` shared-mode session per selected endpoint
- An independent bounded buffer per output, so one slow device cannot stall the others
- Local settings at `%LOCALAPPDATA%\DualSound\settings.json`
