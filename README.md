# TiaFdsGenerator

Milestone 0.4.0 adds a portable, versioned JSON boundary around the generic PLC inventory. A TIA Portal V15.1 machine extracts engineering facts; development, analysis, and reporting can then proceed on a machine with no TIA installation, Siemens registry registration, or `Siemens.Engineering.dll`.

The snapshot contains inventory metadata only: PLC identity, program-block metadata, tag tables, PLC data types, and extraction diagnostics. It does not interpret names such as `Drv`, `Vlv`, or `Lim`. Advansys-standard semantic analysis begins in milestone 0.5.0.

## Architecture

```text
TIA Portal V15.1 machine

TiaFds.Extract.Cli (x64, .NET Framework 4.8)
    -> TiaFds.Openness
    -> TiaFds.Core
    -> EngineeringSnapshot JSON

Development/reporting machine

EngineeringSnapshot JSON
    -> TiaFds.Cli (Any CPU, .NET Framework 4.8)
    -> TiaFds.Analysis
    -> future Advansys engineering model
    -> TiaFds.Reporting
```

`TiaFds.Extract.Cli` is the only executable that references `TiaFds.Openness`. `TiaFds.Cli` references Core, Analysis, and Reporting only; it never initializes Openness or performs a Siemens registry lookup.

## Prerequisites

Building the complete solution requires Visual Studio 2022, the .NET desktop development workload, the .NET Framework 4.8 Developer Pack, and TIA Portal V15.1 Update 4 Openness. Extractor execution requires 64-bit Windows and membership in the **Siemens TIA Openness** local user group. Sign out and back in after group membership changes.

Only `TiaFds.Openness` references:

```text
Openness API\V15.1\Siemens.Engineering.dll
```

Copy Local/`Private` is `false`. Do not commit or redistribute this proprietary DLL. At runtime, `TiaFds.Openness` resolves the exact `Siemens.Engineering` version `15.1.0.0` from the 64-bit registry beneath `HKLM\SOFTWARE\Siemens\Automation\Openness`, validates its identity, and loads it from the registered installation. This release does not support other TIA versions.

## Build and test

Open `TiaFdsGenerator.sln` in Visual Studio 2022 and build `Debug | x64` or `Release | x64`. That solution platform maps Siemens-independent projects to Any CPU and the Openness/extractor projects to x64.

```powershell
msbuild .\TiaFdsGenerator.sln /restore /p:Configuration=Debug /p:Platform=x64
msbuild .\TiaFdsGenerator.sln /t:Build /p:Configuration=Release /p:Platform=x64
dotnet test .\tests\TiaFds.Core.Tests\TiaFds.Core.Tests.csproj --configuration Debug -p:Platform=AnyCPU
```

No automated test requires TIA Portal, a Siemens DLL or registry entry, or a real TIA project.

## Extract on the TIA machine

Supported options are `--input <path>`, `--retrieve-to <folder>`, `--plc <name>`, `--inventory`, `--verbose`, `--export-json <path>`, `--overwrite`, and `--include-source-path`. Archive input (`.zap15_1`) requires `--retrieve-to`; project input uses `.ap15_1`. PLC matching is case-insensitive.

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_Project.zap15_1" ^
  --retrieve-to "C:\Temp\BP_Project" ^
  --plc "BP_PLC" ^
  --inventory ^
  --export-json "C:\Exports\BP_PLC.inventory.json"
```

The extractor prints the live project and inventory information, then:

```text
Selected PLC: BP_PLC

Snapshot exported:
C:\Exports\BP_PLC.inventory.json
```

An existing destination is left untouched unless `--overwrite` is supplied. Exports use a temporary file and publish it only after serialization succeeds.

Extractor exit codes are stable:

- `0`: success
- `1`: general or Openness failure
- `2`: requested PLC not found
- `3`: snapshot destination already exists
- `4`: invalid command arguments
- `5`: snapshot serialization/write failure

## Import without TIA Portal

Copy the JSON plus the `TiaFds.Cli` application files to the development/reporting machine. Supported options are `--import-json <path>`, `--inventory`, and `--verbose`.

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.inventory.json" ^
  --inventory
```

The CLI prints the same shared inventory summary and detailed rows used by the extractor. Live options such as `--input`, `--retrieve-to`, and `--plc` are rejected with guidance to use `TiaFds.Extract.Cli`.

## JSON contract and privacy

Snapshots use indented UTF-8 JSON with camel-case property names. Schema version `1.0` is declared independently from generator version `0.4.0`; unsupported schema versions are rejected. Readers tolerate unknown future properties and normalize null inventory collections to empty collections.

The default JSON stores the source filename but omits the absolute source path. `--include-source-path` explicitly opts into writing the absolute path as `sourcePath`. Retrieved temporary directories and Siemens runtime objects are never serialized.

Typical JSON shape:

```json
{
  "schemaVersion": "1.0",
  "generatorVersion": "0.4.0",
  "exportedAtUtc": "2026-07-23T20:00:00+00:00",
  "project": {
    "name": "BP_Project",
    "sourceFileName": "BP_Project.zap15_1",
    "selectedPlc": {
      "name": "BP_PLC",
      "deviceName": "S71500/ET200MP station_1",
      "deviceItemName": "PLC_1"
    },
    "inventory": {
      "plcName": "BP_PLC",
      "programBlocks": [],
      "tagTables": [],
      "dataTypes": [],
      "diagnostics": []
    }
  }
}
```

## Manual BP verification

Codex cannot access the BP project and has not performed real-project runtime validation. Run the following on the TIA V15.1 Update 4 machine:

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_Project.zap15_1" ^
  --retrieve-to "C:\Temp\BP_Project" ^
  --plc "BP_PLC" ^
  --inventory ^
  --verbose ^
  --export-json "C:\Exports\BP_PLC.inventory.json"
```

Confirm the live summary and detailed rows, JSON creation, Unicode/nested paths, and exit code `0`. Search the JSON for `C:\Projects\`; it must be absent. Repeat with `--include-source-path --overwrite` and confirm the absolute input path is then present.

On a machine without TIA Portal, run:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.inventory.json" ^
  --inventory ^
  --verbose
```

Confirm startup without `Siemens.Engineering`, no Openness registry lookup, matching summary totals and rows, and correct Unicode/nested group paths. Runtime verification against the real BP project remains pending until these commands are run externally.

Generated binaries, `bin`/`obj`, retrieved TIA projects, snapshots containing customer data, and Siemens DLLs must remain uncommitted.
