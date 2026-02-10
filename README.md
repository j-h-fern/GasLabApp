```markdown
# GasLabApp

GasLabApp is a small WPF utility for communicating with CPC6050 pressure controller devices (real or emulated), reading measurements, and assisting with calibration of multiple reference devices and DUTs (SUTs).

## Features

- Connect to serial/virtual COM ports and exchange CPC6050 protocol messages
- Support for running an internal emulator (`Cpc6050Emulator.cs`) for testing without hardware
- Simple GUI for live monitoring (`MainWindow.xaml`) and device-specific display (`PressureControllerDisplay.xaml`)

## Prerequisites

- .NET Framework / .NET SDK compatible with the project (open the solution in Visual Studio recommended)
- A serial/virtual COM port for hardware; use the provided `VirtualCom` helper for loopback testing

## Build and Run

Open the solution `GasLabApp.sln` in Visual Studio and run the `GasLabApp` project, or build from the command line:

```powershell
dotnet build GasLabApp.sln
dotnet run --project GasLabApp.csproj
```

If you plan to use the emulator for testing instead of real hardware, start the emulator first (see `Cpc6050Emulator.cs`) or enable the emulator mode in the app

## Important Files

- `Cpc6050Client.cs` — client-side communication and protocol handling
- `Cpc6050Emulator.cs` — device emulator for testing without physical hardware
- `SerialPortClient.cs` — low-level serial port wrapper
- `MainWindow.xaml` / `MainWindow.xaml.cs` — main UI and application entry
- `PressureControllerDisplay.xaml` — UI for pressure controller visualization
- `UnitTest.cs` — basic unit test harness (if applicable)

## Notes

- Use `ListComPorts.cs` to discover available serial ports on the machine.
- For debugging serial issues, confirm the COM port settings and that no other program is holding the port open.

## Next steps

If you want, I can add usage examples, screenshots, or a short troubleshooting section. Tell me which sections you'd like expanded.

---
Updated: February 2026
