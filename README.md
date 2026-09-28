# WarthogLedControl

Standalone Windows Forms application for controlling the LEDs of a
Thrustmaster HOTAS Warthog Throttle via USB HID.

## Project structure

There is intentionally exactly one `MainForm` class:

- `Program.cs` - application entry point
- `MainForm.cs` - Windows Forms UI
- `WarthogThrottle.cs` - HID communication
- `WarthogLedControl.csproj` - project definition

Do not copy these files into an existing project. Use this directory as
a clean standalone project.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio Code
- Thrustmaster HOTAS Warthog Throttle

## Run

Open this folder in VS Code and execute:

```powershell
dotnet restore
dotnet build
dotnet run
```

## HID command

The application uses the same 4-byte command as the supplied Helios code:

```text
01 06 <LED mask> <brightness>
```

LED mapping:

```text
bit 0 = status LED 4
bit 1 = status LED 2
bit 2 = status LED 1
bit 3 = backlight
bit 4 = status LED 3
bit 6 = status LED 5
```

Brightness is currently limited to 1..3.

The connection status is deliberately kept separate from the LED status,
so changing an LED cannot overwrite the connection information.
