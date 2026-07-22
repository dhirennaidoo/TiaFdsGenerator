# TiaFdsGenerator

Milestone 0.2.0 provides an x64 command-line reader for TIA Portal V15.1 Update 4 projects and archives. It opens `.ap15_1` projects, retrieves `.zap15_1` archives, recursively traverses the hardware hierarchy, and discovers PLC software through each device item's `SoftwareContainer`. Program-block enumeration is intentionally outside this milestone.

## Prerequisites

- Visual Studio 2022 with the **.NET desktop development** workload
- .NET Framework 4.8 Developer Pack
- TIA Portal V15.1 Update 4 with Openness installed
- A 64-bit Windows environment

The Windows account running the tool must be a member of the **Siemens TIA Openness** local user group. Sign out and back in after the account is added so the group membership takes effect.

## Siemens reference

Only `TiaFds.Openness` references `Siemens.Engineering.dll`. The project expects it at:

```text
Openness API\V15.1\Siemens.Engineering.dll
```

The reference has `Private`/Copy Local set to `false`. The Siemens DLL is proprietary and deliberately ignored by Git; obtain it from the installed TIA Portal V15.1 PublicAPI/Openness installation or place a local development copy at the path above. Do not commit it.

At runtime, `TiaFds.Openness` initializes the Siemens runtime through `TiaOpennessRuntime.Initialize()`. It resolves the exact `Siemens.Engineering` version `15.1.0.0` from the 64-bit Siemens Openness registry entries below `HKLM\SOFTWARE\Siemens\Automation\Openness`. The registered assembly path, version, and public key token are validated before the DLL is loaded. This allows the TIA Portal installation directory to differ between the development and execution systems without packaging the proprietary DLL. This milestone intentionally supports only TIA Portal V15.1.

To inspect the target machine's Openness registration:

```powershell
reg query "HKLM\SOFTWARE\Siemens\Automation\Openness" /s /reg:64
```

## Build

Open `TiaFdsGenerator.sln` in Visual Studio 2022, select `Debug | x64` or `Release | x64`, and build the solution.

From a Visual Studio Developer PowerShell prompt:

```powershell
msbuild .\TiaFdsGenerator.sln /restore /p:Configuration=Debug /p:Platform=x64
```

## Run

Open an existing project:

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.ap15_1"
```

Retrieve and inspect an archive:

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.zap15_1" --retrieve-to "C:\TIA\Retrieved"
```

Select a discovered PLC by name (case-insensitive):

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.ap15_1" --plc "BP_PLC"
```

Print the complete recursive hardware hierarchy and attached software:

```powershell
.\src\TiaFds.Cli\bin\x64\Debug\TiaFds.Cli.exe --input "C:\TIA\Example.ap15_1" --verbose
```

Expected PLC discovery output resembles:

```text
PLCs:
- BP_PLC
  Device: S71500/ET200MP station_1
  Device item: PLC_1
```

If `--plc` does not match a discovered PLC, the CLI lists the available PLC names and returns exit code `2`. `--verbose` uses the actual device, device-item, and software names from the project.

## Manual BP project test

Runtime verification must be performed on the Windows machine that contains the BP project and has TIA Portal V15.1 Update 4 with Openness installed. Copy the built application files to that machine if the repository is built elsewhere, then open PowerShell in the repository or deployment root.

Replace only the project path in this command and run it to verify recursive discovery:

```powershell
.\src\TiaFds.Cli\bin\x64\Release\TiaFds.Cli.exe --input "C:\Path\To\BP.ap15_1" --verbose
```

Confirm that the output contains:

- `S71500/ET200MP station_1` under `Top-level devices` and `Hardware hierarchy`;
- `BP_PLC` under `PLCs`;
- the actual CPU device-item name beneath `BP_PLC`;
- `Software: BP_PLC [PlcSoftware]` at the correct hierarchy depth; and
- process exit code `0` (`$LASTEXITCODE` in PowerShell).

Then verify case-insensitive PLC selection:

```powershell
.\src\TiaFds.Cli\bin\x64\Release\TiaFds.Cli.exe --input "C:\Path\To\BP.ap15_1" --plc "bp_plc"
```

This command should print `Selected PLC: BP_PLC` and return exit code `0`. Finally, verify the distinct not-found result:

```powershell
.\src\TiaFds.Cli\bin\x64\Release\TiaFds.Cli.exe --input "C:\Path\To\BP.ap15_1" --plc "DOES_NOT_EXIST"
$LASTEXITCODE
```

The final command should list every discovered PLC name and report exit code `2`. Runtime verification against a real TIA Portal project remains pending until these commands are run externally.

`--retrieve-to` is required for `.zap15_1` input. Run the executable on a machine with the matching TIA Portal V15.1 Update 4 Openness runtime installed. Retrieved projects and generated build output must remain uncommitted.
