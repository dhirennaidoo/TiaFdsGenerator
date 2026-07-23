# TiaFdsGenerator

Application version 0.6.1 corrects milestone 0.6.0 block-call graph reconstruction and correlates Advansys control-module declarations with the processing-function calls that receive those declarations through an `InOut` parameter.

```text
db.cm.Drv.BP_M16006 : Udt.cm.Drv
              |
              | InOut
              v
FC52 cm.DrvType2
              |
              v
ControlModuleImplementation
```

```text
Milestone 0.5.x:
    What modules exist?

Milestone 0.6.0:
    Which processing FC variant is connected to each module?

Future milestone:
    What commands, feedbacks, alarms, interlocks and I/O are connected?
```

The DB declaration remains the source of truth for a module's name, datatype, family, path, and description. A structurally observed block call supplies separate implementation evidence: processing FC, variant, caller, network, and actual `InOut` expression. Version 0.6.0 does not interpret the semantic meaning of any other call parameter.

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
    -> control-module declaration and call correlation
    -> TiaFds.Reporting
    -> immutable AnalysisReport
```

Only `TiaFds.Openness` references `Siemens.Engineering`. `TiaFds.Openness.Xml` securely parses exported XML without Siemens dependencies. `TiaFds.Cli` references Core, Analysis, and Reporting and runs without TIA Portal, Siemens registry entries, `Siemens.Engineering`, or `TiaFds.Openness`.

## Reporting model

`TiaFds.Reporting` converts the completed discovery and implementation results into an immutable, deterministic `AnalysisReport`. The report owns copies of module declarations, implementation statuses, call sites, family and processing-variant summaries, individual and grouped diagnostics, and manual-review items. It does not perform extraction or correlation.

The module-call CLI path builds this report and uses `AnalysisReportConsoleRenderer` for summary and detail output. This stable reporting boundary is intended for later JSON, Excel, FDS, and project-comparison consumers; those exporters are not implemented yet.

## Prerequisites and Siemens reference

- Visual Studio 2022 with .NET desktop development
- .NET Framework 4.8 Developer Pack
- TIA Portal V15.1 Update 4 with Openness
- x64 Windows for extraction
- extraction user in the **Siemens TIA Openness** local user group

`TiaFds.Openness` references `Openness API\V15.1\Siemens.Engineering.dll` with Copy Local/`Private` set to false. Do not redistribute or commit this DLL. Runtime discovery validates version `15.1.0.0` through the 64-bit registration under `HKLM\SOFTWARE\Siemens\Automation\Openness`.

## Build and test

```powershell
msbuild .\TiaFdsGenerator.sln /restore /p:Configuration=Debug /p:Platform=x64
msbuild .\TiaFdsGenerator.sln /t:Build /p:Configuration=Release /p:Platform=x64
dotnet test .\tests\TiaFds.Core.Tests\TiaFds.Core.Tests.csproj --configuration Debug -p:Platform=AnyCPU
```

All automated tests use synthetic snapshots and XML. They require no TIA installation or customer project.

## Snapshot schema 1.2

Application version and schema version are independent: application 0.6.1 writes schema `1.2`.

Schema 1.2 retains the 1.1 DB-declaration contract and adds:

- `inventory.blockCallsIncluded`
- `inventory.blockCalls`
- caller and called-block identities
- caller group path, network number/title, and call ordinal
- generic formal parameter name, direction, datatype, actual expression, and resolved member path
- per-call extraction/parsing diagnostics

Readers remain compatible with schema 1.1 and normalize missing calls to an empty, not-included collection. Schema 1.0 also remains readable. Unsupported schema versions are rejected clearly. Raw XML, temporary paths, Siemens objects, handles, and runtime references are never serialized.

## TIA extraction

DB structure and block-call extraction are separate opt-in operations:

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_V15.1.ap15_1" ^
  --plc "BP_PLC" ^
  --include-db-structures ^
  --include-block-calls ^
  --export-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --overwrite
```

`--include-block-calls` does not imply `--include-db-structures`. When absent, block calls remain empty and normal extraction continues. Offline correlation requires both flags and reports `CM100_BLOCK_CALLS_NOT_EXTRACTED` or `CM101_DB_STRUCTURES_NOT_EXTRACTED` when either dataset is absent.

Executable OBs, FCs, and FBs are exported generically through the V15.1 `PlcBlock.Export(..., ExportOptions.WithDefaults)` API. Each export is parsed and deleted where practical. A failure on one block produces `CM111_BLOCK_CALL_EXTRACTION_FAILED`; extraction continues for other blocks.

Version 0.6.1 reconstructs each LAD/FBD network as a UID-based connectivity graph. A call formal may be connected using its own parameter UID or the enclosing call-part UID plus port name. The parser follows matching wire endpoints to `Access` or constant nodes without depending on XML element order. Input, Output, and InOut directions use the same graph evidence; multiple connected operands are diagnosed and never selected arbitrarily.

The verified V15.1 FC501 export uses a dedicated `Call` element rather than a `Part` element. Its first `cm.DrvType1` call is connected as follows:

```text
Call UId=29
  CallInfo cm.DrvType1
    Parameter Name=Drv Section=InOut

Wire UId=36
  NameCon UId=29 Name=Drv
  IdentCon UId=26

Access UId=26
  Component db.cm.Drv
  Component BP_M16001
```

The resulting expression is `"db.cm.Drv".BP_M16001`, normalized and DB-structure-validated as `db.cm.Drv.BP_M16001`.

Access rendering preserves quoted symbolic components, nested members, array indexes, block-interface/local variables, simple literals, and supported absolute DB addresses. When DB structures were extracted, a normalized symbolic path is written to `resolvedMemberPath` only if that exact member exists in the snapshot. `actualExpression` is retained even when validation or normalization fails.

### Supported languages and limitations

The current parser supports the namespace-qualified FlgNet call representation exported for:

- LAD
- FBD

It reads all `CallInfo` instructions, multiple calls per network, formal metadata, wired symbolic accesses, caller identity, and network metadata. SCL and STL textual network formats are not parsed in 0.6.0 and produce `CM110_UNSUPPORTED_BLOCK_LANGUAGE`. Unsupported instructions, protected/inconsistent blocks, missing parameter assignments, and V15.1 export variations are diagnosed rather than guessed.

Corrective graph diagnostics include:

- `CM117_PARAMETER_CONNECTION_AMBIGUOUS`
- `CM118_CONNECTED_OPERAND_NOT_SUPPORTED`
- `CM119_ACCESS_EXPRESSION_RENDER_FAILED`
- `CM120_CONNECTION_REFERENCE_NOT_FOUND`
- `CM121_RESOLVED_PATH_NOT_IN_DB_STRUCTURES`
- `CM122_INOUT_CONNECTION_INCOMPLETE`

Optional unconnected outputs do not generate warnings merely for being unconnected.

## Symbol normalization

Both the original actual expression and its normalized path are retained:

```text
Actual:   "db.cm.Drv".BP_M16006
Resolved: db.cm.Drv.BP_M16006
```

Normalization handles quoted/unquoted symbolic DB names, whitespace, nested members, escaped quotes, and array indexes without changing case. Ordinal comparisons are used. Local (`#Local`), pointer (`P##Drive`), and absolute (`DB50.DBX0.0`) expressions are not claimed as resolved because the snapshot does not currently contain enough address metadata for an unambiguous symbolic mapping.

## Processing-function catalogue

Function name is the primary identity. Expected FC numbers validate known drive definitions but never classify a function by number alone:

| Family | Function | Expected number | Variant | Module datatype |
|---|---|---:|---|---|
| Drive | `cm.DrvType0` | FC50 | DrvType0 | `Udt.cm.Drv` |
| Drive | `cm.DrvType1` | FC51 | DrvType1 | `Udt.cm.Drv` |
| Drive | `cm.DrvType2` | FC52 | DrvType2 | `Udt.cm.Drv` |
| Drive | `cm.DrvType3` | FC53 | DrvType3 | `Udt.cm.Drv` |
| Valve | `cm.VlvType0` | — | VlvType0 | `Udt.cm.Vlv` |
| Valve | `cm.VlvType1` | — | VlvType1 | `Udt.cm.Vlv` |
| DigitalInput | `cm.LimType0..2` | — | LimType0..2 | `Udt.cm.DI` |
| AnalogueInput | `cm.AI` | — | AI | `Udt.cm.AI` |
| AnalogueOutput | `cm.AO` | — | AO | `Udt.cm.AO` |
| DigitalOutput | `cm.DOType0` | — | DOType0 | `Udt.cm.DO` |
| DigitalOutput | `cm.DOType1` | — | DOType1 | `Udt.cm.DO` |
| Speed | `cm.SpdType0` | — | SpdType0 | `Udt.cm.Spd` |
| Speed | `cm.SpdType1` | — | SpdType1 | `Udt.cm.Spd` |

A changed drive FC number produces `CM113_FUNCTION_NUMBER_MISMATCH` but the name-matched call evidence is retained.

## InOut selection and correlation

The analyser prefers evidence in this order:

1. `InOut` direction
2. formal datatype matching the catalogue's module UDT
3. catalogue parameter-name hint
4. actual path matching a discovered module

Multiple equally strong candidates produce `CM103_AMBIGUOUS_INOUT_PARAMETER`; no arbitrary choice is made. The normalized actual path must exactly match a top-level module declaration path. Nested UDT fields are not modules. Names, descriptions, DB offsets, nearby calls, and FC numbers alone never create a correlation.

Statuses are:

- `Correlated`: exactly one recognized call resolves to the module
- `Unreferenced`: no recognized call resolves to the module
- `MultipleCalls`: more than one distinct call resolves to it; all sites are retained
- `UnresolvedParameter`: reserved for unresolved per-module evidence
- `UnsupportedCall`: reserved for unsupported parsed call forms
- `FamilyMismatch`: processing-function family differs from declaration datatype family

Exact duplicate parser records for one call site are deduplicated and diagnosed. No call is selected as primary when multiple calls remain.

## Offline commands

Declaration discovery remains available:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --discover-modules
```

Run implementation correlation:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --analyze-module-calls
```

Optional filters:

```bat
--module-family Drive
--implementation-status Correlated
--implementation-status Unreferenced
--module BP_M16006
```

Filter values are case-insensitive. Invalid family or status values are rejected. Output preserves descriptions, all multiple-call sites, processing FC and variant, caller, network, original `InOut` expression, and canonical member path.

## Manual BP verification

Codex must not open the BP project. On the TIA V15.1 machine run:

```bat
TiaFds.Extract.Cli.exe ^
  --input "C:\Projects\BP_V15.1.ap15_1" ^
  --plc "BP_PLC" ^
  --include-db-structures ^
  --include-block-calls ^
  --export-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --overwrite
```

Verify `blockCalls` contains `cm.DrvType0` through `cm.DrvType3`, caller/network facts, parameters, original actual expressions, and resolved paths where possible. Confirm no raw XML or temporary path appears and per-block failures are diagnostics.

Transfer the JSON to the development machine:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --analyze-module-calls
```

Verify `BP_M16006` against the FC/caller/network visible in TIA, descriptions remain visible, spare declarations are unreferenced, multiple calls retain every site, unresolved parameters are not correlated, and the CLI runs without Siemens software.

Generated binaries, `bin`/`obj`, retrieved projects, customer snapshots, temporary XML, and Siemens DLLs must remain uncommitted.
