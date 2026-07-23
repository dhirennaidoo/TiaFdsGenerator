# TiaFdsGenerator

Application version 0.5.1 is a corrective update to milestone 0.5.0. It discovers Advansys control-module declarations from global PLC data blocks and includes declaration comments as module descriptions in detailed CLI output, while preserving the Siemens-independent snapshot boundary introduced in 0.4.0.

At this plant, a module family is stored in one global DB and each declared DB member represents a plant module:

```text
db.cm.Drv
    M16006 : Udt.cm.Drv
    M16007 : Udt.cm.Drv
    M16008 : Udt.cm.Drv
        |
        v
TiaFds.Analysis
        |
        v
DriveModule collection
```

The declared datatype is the primary classification evidence. Member names and DB numbers are not used to infer module types.

```text
Milestone 0.5.0:
    What modules exist?

Milestone 0.6.0:
    Which processing FC variant is connected to each module through InOut?
```

No FC call, InOut connection, network, subtype, alarm, interlock, or other UDT semantic analysis is performed in 0.5.0.

## Architecture

```text
TIA Portal V15.1 machine

TiaFds.Extract.Cli (x64, .NET Framework 4.8)
    -> TiaFds.Openness
    -> TiaFds.Openness.Xml
    -> TiaFds.Core
    -> EngineeringSnapshot JSON

Development/reporting machine

EngineeringSnapshot JSON
    -> TiaFds.Cli (Any CPU, .NET Framework 4.8)
    -> TiaFds.Analysis
    -> Advansys control-module discovery
    -> TiaFds.Reporting
```

`TiaFds.Openness.Xml` is an Any CPU parser library with no Siemens assembly reference. It isolates secure parsing of exported declaration XML so the parser can be tested without TIA Portal. Only `TiaFds.Openness` references `Siemens.Engineering`.

`TiaFds.Cli` references Core, Analysis, and Reporting only. It does not initialize Openness, inspect Siemens registry entries, or load `Siemens.Engineering`.

## Prerequisites and Siemens reference

Building the complete solution requires:

- Visual Studio 2022 with the .NET desktop development workload
- .NET Framework 4.8 Developer Pack
- TIA Portal V15.1 Update 4 with Openness installed
- A 64-bit Windows environment for extraction

The extraction account must belong to the **Siemens TIA Openness** local user group. Sign out and back in after adding the account.

Only `TiaFds.Openness` references:

```text
Openness API\V15.1\Siemens.Engineering.dll
```

Copy Local/`Private` is `false`. The proprietary DLL must not be committed or redistributed. At runtime, `TiaFds.Openness` resolves and validates `Siemens.Engineering` version `15.1.0.0` from the 64-bit registration beneath `HKLM\SOFTWARE\Siemens\Automation\Openness`.

## Build and test

The solution `x64` configuration maps Siemens-facing projects to x64 and Siemens-independent projects to Any CPU.

```powershell
msbuild .\TiaFdsGenerator.sln /restore /p:Configuration=Debug /p:Platform=x64
msbuild .\TiaFdsGenerator.sln /t:Build /p:Configuration=Release /p:Platform=x64
dotnet test .\tests\TiaFds.Core.Tests\TiaFds.Core.Tests.csproj --configuration Debug -p:Platform=AnyCPU
```

All automated tests use synthetic XML and snapshots. They require no TIA Portal installation, Siemens DLL, registry entry, or real project.

## Extraction

`TiaFds.Extract.Cli` supports:

- `--input <path>`
- `--retrieve-to <folder>`
- `--plc <name>`
- `--inventory`
- `--verbose`
- `--include-db-structures`
- `--export-json <path>`
- `--overwrite`
- `--include-source-path`

DB declaration extraction is deliberately opt-in because exporting every global DB can be expensive:

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_V15.1.ap15_1" ^
  --plc "BP_PLC" ^
  --include-db-structures ^
  --export-json "C:\Exports\BP_PLC.0.5.0.json" ^
  --overwrite
```

When `--include-db-structures` is absent, normal 0.4.0 inventory extraction continues and the snapshot records that DB structures were not included. Module discovery then returns exit code `6` with instructions to re-export.

The Openness layer enumerates all eligible global DBs; it does not hard-code DB50, DB60, DB80, DB90, or DB95. Each `GlobalDB` is exported through the documented V15.1 `PlcBlock.Export(..., ExportOptions.WithDefaults)` API. The temporary XML is parsed with DTD processing disabled and no external entity resolver, converted to generic Core models, and deleted where practical.

If one DB cannot be exported or parsed, its basic program-block inventory remains, a per-DB extraction diagnostic is recorded, and other DBs continue.

Extractor exit codes:

- `0`: success
- `1`: general or Openness failure
- `2`: PLC not found
- `3`: snapshot destination already exists
- `4`: invalid arguments
- `5`: snapshot serialization/write failure

## Offline module discovery

`TiaFds.Cli` supports:

- `--import-json <path>`
- `--inventory`
- `--verbose`
- `--discover-modules`
- `--module-family <name>`

Run all-family discovery:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.5.0.json" ^
  --discover-modules
```

Filter detailed rows to one known family:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.5.0.json" ^
  --discover-modules ^
  --module-family Drive
```

Recognised exact datatype mappings are:

| Declared datatype | Family | Expected container |
|---|---|---|
| `Udt.cm.Drv` | Drive | `db.cm.Drv` |
| `Udt.cm.Vlv` | Valve | `db.cm.Vlv` |
| `Udt.cm.Spd` | Speed | `db.cm.Spd` |
| `Udt.cm.DI` | DigitalInput | `db.cm.DI` |
| `Udt.cm.AI` | AnalogueInput | `db.cm.AI` |
| `Udt.cm.AO` | AnalogueOutput | `db.cm.AO` |
| `Udt.cm.DO` | DigitalOutput | `db.cm.DO` |

Comparison is case-insensitive to match the existing TIA symbol-selection workflow; original spelling is retained in output. A matching datatype in another DB is still a module, but produces `CM004_MODULE_IN_UNEXPECTED_CONTAINER`. A familiar member name without a recognised datatype is not classified.

Import CLI exit code `6` means the snapshot did not include DB structures. Existing general and invalid-argument exit codes remain `1` and `4`.

## Snapshot schema 1.1

Application version 0.5.1 writes the unchanged snapshot schema `1.1`. Schema and application versions remain independent.

Schema 1.1 adds:

- `inventory.dataBlockStructuresIncluded`
- `inventory.dataBlockStructures`
- DB name, number, and group path
- hierarchical members with full member paths
- declared element datatypes
- optional comments
- nesting levels
- array declarations and bounds
- per-DB extraction diagnostics

The reader also accepts schema 1.0 and normalizes missing DB structures to an empty, not-included collection. Unknown schema versions are rejected.

Absolute source paths remain omitted unless `--include-source-path` is supplied. Raw Siemens XML, temporary paths, and Siemens runtime objects are never serialized.

## Arrays and hierarchy

An array such as:

```text
Drives : Array[1..20] of Udt.cm.Drv
```

is represented as one module-collection candidate with `isArray`, bounds, path, and element datatype. Milestone 0.5.0 does not invent symbolic modules for individual indexes and emits `CM006_ARRAY_NOT_EXPANDED`.

The hierarchy and nesting level distinguish a module declaration from its nested fields. Once a member directly matches a recognised module datatype, its children are preserved in the snapshot but are not independently classified as modules.

TIA V15.1 global-DB exports preserve declaration nodes emitted for the DB. Inline nested structures can therefore be retained. Referenced UDT definitions may not be expanded inside the DB export; 0.5.0 does not separately export and merge UDT internals.

Know-how-protected, inconsistent, or otherwise non-exportable blocks may be rejected by the V15.1 Openness runtime. These cases are retained as per-DB extraction diagnostics rather than terminating the complete snapshot. Comment availability and language depend on what V15.1 includes in the exported XML.

## Manual BP verification

Codex does not have the BP project and must not open it. On the TIA V15.1 machine run:

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_V15.1.ap15_1" ^
  --plc "BP_PLC" ^
  --include-db-structures ^
  --export-json "C:\Exports\BP_PLC.0.5.0.json" ^
  --overwrite
```

Verify that:

1. `db.cm.Drv` and its top-level members are present.
2. Drive declarations retain `Udt.cm.Drv`.
3. Full paths, emitted nested declarations, comments, arrays, and bounds are preserved.
4. No Siemens object, raw Siemens XML, or temporary export path appears.
5. A single failed DB creates diagnostics without preventing other DBs from exporting.

Transfer the JSON to a machine without TIA Portal and run:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.5.0.json" ^
  --discover-modules
```

Verify module totals against declarations visible in TIA, ensure nested primitive fields are absent from module rows, and confirm DB numbers are displayed but not used as identifiers. No module should have an FC variant assigned. FC/InOut correlation begins in milestone 0.6.0.

Generated binaries, `bin`/`obj`, retrieved TIA projects, customer snapshots, temporary exports, and Siemens DLLs must remain uncommitted.
