# WarthogLedControl over DCS-BIOS

Standalone Windows application for controlling the background LEDs of a
Thrustmaster HOTAS Warthog Throttle via DCS-BIOS. 


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

## Setup 


Configure the following settings:

- **DCS-BIOS Address:** Set the DCS-BIOS address used to control the cockpit console backlighting.
- **Brightness Thresholds:** Define the threshold values for the four brightness levels (0–3).
