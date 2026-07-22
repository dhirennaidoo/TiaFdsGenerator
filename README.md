# TiaFdsGenerator

Milestone 0.3.0 provides an x64 command-line inventory reader for TIA Portal V15.1 Update 4 projects and archives. It discovers PLC software and inventories program-block metadata, PLC tag tables, and user-defined PLC data types without exposing Siemens objects outside `TiaFds.Openness`. Block source, networks, interfaces, tags, DB members, and UDT members are intentionally not analysed yet.

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

Run the Siemens-independent tests with:

```powershell
dotnet test .\tests\TiaFds.Core.Tests\TiaFds.Core.Tests.csproj --configuration Debug -p:Platform=x64
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

Print the selected PLC's detailed metadata inventory:

```powershell
.\src\TiaFds.Cli\bin\x64\Release\TiaFds.Cli.exe --input "C:\TIA\Example.ap15_1" --plc "BP_PLC" --inventory
```

Selecting a PLC always prints an inventory summary:

```text
Selected PLC: BP_PLC
PLC inventory:
  Program blocks: 315
    Organization blocks: 8
    Function blocks: 62
    Functions: 41
    Global data blocks: 150
    Instance data blocks: 54
  Tag tables: 12
  PLC data types: 27
  Diagnostics: 0
```

`--inventory` adds aligned metadata tables:

```text
Program blocks:
Type                 Number  Language     Consistent Group                                Name
OrganizationBlock         1  LAD          Yes        Program blocks                       Main
FunctionBlock            20  SCL          Yes        Program blocks/Drives                Drv
GlobalDataBlock          50  DB           Yes        Program blocks/Data                  Config

Tag tables:
  Tags  Group                                Name
   120  PLC tag tables                       Inputs
    45  PLC tag tables/Process               ProcessTags

PLC data types:
Group                                Name
PLC data types/Control Modules       Drv_Type
```

`--inventory` requires `--plc`. `--verbose` still controls only hardware hierarchy output. When both flags are supplied, the hardware hierarchy is printed before the PLC inventory.

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

Replace only the project path in this command and run it to verify recursive discovery and inventory:

```powershell
.\src\TiaFds.Cli\bin\x64\Release\TiaFds.Cli.exe --input "C:\Path\To\BP.ap15_1" --plc "BP_PLC" --inventory --verbose
```

For a `.zap15_1` archive from Command Prompt, the equivalent command is:

```bat
TiaFds.Cli.exe ^
  --input "C:\Projects\BP_Project.zap15_1" ^
  --retrieve-to "C:\Temp\BP_Project" ^
  --plc "BP_PLC" ^
  --inventory ^
  --verbose
```

Confirm that the output contains:

- `S71500/ET200MP station_1` under `Top-level devices` and `Hardware hierarchy`;
- `BP_PLC` under `PLCs`;
- the actual CPU device-item name beneath `BP_PLC`;
- `Software: BP_PLC [PlcSoftware]` at the correct hierarchy depth; and
- the PLC inventory summary and detailed program-block, tag-table, and data-type sections;
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

The final command should list every discovered PLC name and report exit code `2`. Review any inventory diagnostics, particularly unreadable block metadata or unclassified block types. Runtime verification of milestone 0.3.0 against a real TIA Portal project remains pending until these commands are run externally; Codex did not run against the BP project.

`--retrieve-to` is required for `.zap15_1` input. Run the executable on a machine with the matching TIA Portal V15.1 Update 4 Openness runtime installed. Retrieved projects and generated build output must remain uncommitted.
