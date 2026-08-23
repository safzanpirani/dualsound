# DualSound

DualSound mirrors all audio playing through your Windows default output to as many as seven additional playback devices — eight outputs total.

## Use it

1. Connect both playback devices in Windows.
2. Make Device 01 your Windows default output.
3. Open `DualSound.exe` and select up to seven additional outputs.
4. Select **Start mirroring**.

The app uses WASAPI shared-mode loopback. It needs no administrator rights, audio driver, account, or internet connection. Keep DualSound open while mirroring.

Bluetooth devices introduce their own buffering, so a Bluetooth speaker may sound behind wired speakers. Two independent Windows audio devices cannot be perfectly clock-synchronized without a virtual/multi-output audio driver.

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
