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

Any Windows machine (STEP 7 Classic V5.x, no Siemens software)

TiaFds.Step7.Extract.Cli (Any CPU, .NET Framework 4.8)
    -> TiaFds.Step7Classic (reads .s7p project files directly)
    -> TiaFds.Core
    -> EngineeringSnapshot JSON (sourceKind = Step7Classic)

Development/reporting machine

EngineeringSnapshot JSON
    -> TiaFds.Cli (Any CPU, .NET Framework 4.8)
    -> TiaFds.Analysis
    -> control-module declaration and call correlation
    -> TiaFds.Reporting
    -> immutable AnalysisReport
```

Only `TiaFds.Openness` references `Siemens.Engineering`, and only `TiaFds.Step7Classic` references `DotNetSiemensPLCToolBoxLibrary`. `TiaFds.Openness.Xml` securely parses exported XML without Siemens dependencies. `TiaFds.Cli` references Core, Analysis, and Reporting and runs without TIA Portal, Siemens registry entries, `Siemens.Engineering`, or `TiaFds.Openness`.

## Reporting model

`TiaFds.Reporting` converts the completed discovery and implementation results into an immutable, deterministic `AnalysisReport`. The report owns copies of module declarations, implementation statuses, call sites, family and processing-variant summaries, individual and grouped diagnostics, and manual-review items. It does not perform extraction or correlation.

The module-call CLI path builds this report once and passes the same instance to the console, JSON, and Excel renderers. Reporting does not repeat extraction, discovery, correlation, or summary calculations.

## JSON and Excel reports

Reporting runs offline from an extracted `EngineeringSnapshot` and does not require TIA Portal or Siemens assemblies. Request either or both output formats while running module-call analysis:

```bat
TiaFds.Cli.exe ^
  --import-json "C:\Exports\BP_PLC.0.6.0.json" ^
  --analyze-module-calls ^
  --report-json "C:\Reports\AnalysisReport.json" ^
  --report-excel "C:\Reports\AnalysisReport.xlsx"
```

Output paths are explicit; parent directories are created and existing files are overwritten. The CLI prints each successfully generated absolute path. A write failure names the affected path and returns exit code `7`.

The JSON reporting schema is `1.4`. The workbook contains these worksheets in order: `Summary`, `Modules`, `Processing Calls`, `Behavioural Conditions`, `Diagnostics`, `Manual Review`, and `Call Parameters`.

Report schema 1.4 adds every actual parameter of each processing call: `parameters` on each processing call in JSON, and one row per parameter on `Call Parameters`. The columns are Module Family, Module Name, Module Path, Processing FC, Caller Block Name/Number, Network Number/Title, Formal Name, Direction, Formal Datatype, Actual Expression, Raw Actual, Resolved Member Path, Constant Value, Not Connected and Trace Status. Constant Value is set for inputs driven by a catalogued constant (`glb.One`, `glb.Zero`, `glb.Bridge`). Not Connected marks the library's placeholder `glb.DontCare`. It uses filterable tables and preserves project text as text so formula-like engineering values are not evaluated by Excel.

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

Build with MSBuild before running `dotnet test`. `TiaFds.Reporting` is a legacy-format project whose NuGet references do not resolve under a `dotnet`-driven build; after the MSBuild step, `dotnet test ... --no-build` is reliable.

All automated tests use synthetic snapshots, XML, and in-memory STEP 7 models. They require no TIA installation, STEP 7 installation, or customer project. The `LocalSample` test category runs against a real STEP 7 V5.x project only when `TIAFDS_STEP7_SAMPLE_PROJECT` points at its `.s7p` file, and is otherwise inconclusive:

```powershell
$env:TIAFDS_STEP7_SAMPLE_PROJECT = "D:\Samples\A-Mill\A-Mill.s7p"
dotnet test .\tests\TiaFds.Core.Tests\TiaFds.Core.Tests.csproj --no-build -p:Platform=AnyCPU --filter TestCategory=LocalSample
```

## Snapshot schema 1.4

Application version and schema version are independent. Schema `1.4` adds
`project.sourceKind` (`TiaPortal` or `Step7Classic`) and an optional
byte.bit `address` on data-block members (STEP 7 Classic only), plus an
`operation` on logic assignments (`Assign` for `=`, `Set` for `S`, `Reset`
for `R`). Snapshots without `sourceKind` are TIA Portal exports; assignments
without `operation` are coil assignments. Schema 1.4 also adds member
`actualValue` and `inventory.startupValues` (library startup initialisation).
Readers remain compatible with
schemas `1.0` through `1.3`.

Schema `1.3` extends the `1.2` block-call contract with neutral PLC logic
assignments and boolean expression trees.

Schema 1.2 retained the 1.1 DB-declaration contract and added:

- `inventory.blockCallsIncluded`
- `inventory.blockCalls`
- caller and called-block identities
- caller group path, network number/title, and call ordinal
- generic formal parameter name, direction, datatype, actual expression, and resolved member path
- per-call extraction/parsing diagnostics

Schema 1.3 additionally writes `inventory.logicAssignmentsIncluded` and
`inventory.logicAssignments`. Assignments preserve destination, source
expression tree, operands, resolved paths, statement order, block language,
and network metadata. Missing logic in older snapshots normalizes to an empty,
not-included collection. Raw XML, temporary paths, Siemens objects, handles,
and runtime references are never serialized.

## Behavioural conditions

When `--include-block-calls` is used, the same exported LAD/FBD XML is also
examined for neutral boolean assignments. Offline analysis recognizes exact
SA (start command), CR (control request), and ILK (interlock) members and
correlates them to modules by resolved member-path ownership.

Supported expressions preserve operands, TRUE/FALSE constants, contact
negation, AND/OR/NOT grouping, and one unambiguous prior local-temporary
assignment. Partial, unsupported, unresolved, and ambiguous conditions are
retained with `BEH...` diagnostics and manual-review entries. No condition is
inferred from a network title.

`AnalysisReport.json` schema `1.3` includes `behaviourSummary`,
`behaviouralConditions`, module-level `startCommands`, `controlRequests`, and
`interlocks`. Schema 1.2 preserves both original and simplified expression
trees and adds constant provenance, effective value, condition effect, semantic
classification, and structured findings. Schema 1.3 adds each condition's
`operation` (`Assign`, `Set`, `Reset`); Excel appends it as the last
`Behavioural Conditions` column so existing column positions are unchanged.

Start commands are `SA` coils (TIA library) or `CmdSA` set/reset latches
(STEP 7 Classic library, written by the sequence FCs). Several `S`/`R` writes
to one latch are expected and each becomes its own condition; several coil
(`=`) writes, or coil writes mixed with latch writes, remain
`BEH104_MULTIPLE_ASSIGNMENTS`. A constant set or reset condition does not make
the latch constant, so it is classified `RequiresManualInterpretation` with
rule `LATCH_CONSTANT_REQUIRES_REVIEW`.

The immutable default boolean catalogue recognizes literal `TRUE`/`FALSE` and
the exact project symbols `glb.One`/`glb.Zero` (including quoted qualified
forms). Matching is case-insensitive and exact after removing TIA identifier
quotes. Names that merely contain `One`, `Zero`, `True`, or `False` are not
constants. Numeric `0`/`1` remain unresolved without unambiguous boolean
datatype evidence.

`glb.Bridge` is also TRUE (rule `BP_GLB_BRIDGE`) but is flagged as a
**bypass flag**: it is used deliberately to bridge logic, so it is not a
neutral constant. When it short-circuits other operands (`(NOT HL1038.FLT OR
glb.Bridge)`), or makes the whole condition TRUE, the condition is classified
`BridgedOrBypassed` with rule `BYPASS_FLAG_APPLIED`, and a finding lists the
bypassed operands. In series (`glb.Bridge AND x`) it removes nothing and is
not counted.

Analysis simplifies supported NOT/AND/OR trees and bounded local-temporary
traces. Absorbing rules such as `FALSE AND Unknown = FALSE` and
`TRUE OR Unknown = TRUE` are safe; writable markers, DB bits, and globals are
never inferred constant from a single write.

SA and CR are treated as active-high request channels: constant TRUE is
permanently enabled and constant FALSE is permanently disabled. The exported
`cm.DrvType1` implementation demonstrates that `Drv.ILK` is an active-high
permissive in the motor-output path `(CR OR OVR) AND (ILK OR OVR)`. The library
owner confirmed that all Drive processing variants share this ILK and CR
polarity. For modules implemented by recognized `DrvType*` variants, ILK TRUE
is therefore reported as bridged/bypassed and ILK FALSE as permanently
asserted. Non-Drive families and missing variant evidence still require manual
interpretation.

Excel retains six worksheets rather than adding a separate findings sheet.
The normalized `Behavioural Conditions` table contains the constant,
simplification, effect, review, and finding columns, keeping every noteworthy
row filterable without duplicating condition data. Summary and Modules include
constant/bypass counts, and console output prints a bounded noteworthy list.
FDS prose generation is not implemented.

A constant result is a static code-analysis finding, not evidence of the live
PLC state.

The supported boundary and the real FC501 pattern are documented in
[`docs/behavioural-analysis.md`](docs/behavioural-analysis.md).

## STEP 7 Classic (V5.x) extraction

STEP 7 V5.x has no Openness API. `TiaFds.Step7.Extract.Cli` reads the project
files (`.s7p`, or a zipped project) directly through
[DotNetSiemensPLCToolBoxLibrary](https://github.com/dotnetprojects/DotNetSiemensPLCToolBoxLibrary)
(LGPL-2.1, referenced unmodified; see `THIRD-PARTY-NOTICES.md`). It needs no
Siemens software and opens the project read-only. The project's `Sources`
folder is ignored: compiled blocks are the source of truth.

```bat
TiaFds.Step7.Extract.Cli.exe ^
  --input "D:\Projects\A-Mill\A-Mill.s7p" ^
  --plc "S7 Program" ^
  --include-db-structures ^
  --include-block-calls ^
  --export-json "C:\Exports\A-Mill.json" ^
  --overwrite

TiaFds.Cli.exe --import-json "C:\Exports\A-Mill.json" --analyze-module-calls ^
  --report-json "C:\Reports\A-Mill.report.json" --report-excel "C:\Reports\A-Mill.report.xlsx"
```

`--plc` accepts the S7 program name, its CPU name, or
`Station/CPU/Program`; a name matching several programs is rejected with
the candidates listed (exit code `4`). Exit codes otherwise match
`TiaFds.Extract.Cli`.

Mapping onto the shared snapshot:

- blocks keep their symbolic names (absolute names such as `DB1006` when unnamed); German language mnemonics map to TIA names (`AWL`→`STL`, `KOP`→`LAD`, `FUP`→`FBD`)
- the symbol table becomes one tag table named `Symbols`; UDTs become PLC data types
- only global DB structures are kept; instance DBs are classified from the decoded block header, because the project's block list does not flag them reliably
- UDT-typed members are named by their UDT symbol (`Udt.cm.Drv`) and expanded, and every member carries its byte.bit `address` for later pointer resolution
- variable tables are not inventoried

Classic diagnostics use the `S7C` prefix:

- `S7C001_BLOCK_LIST_READ_FAILED`
- `S7C002_DB_STRUCTURE_DECODE_FAILED` (also surfaces as `CM007` during discovery)
- `S7C003_BLOCK_KNOW_HOW_PROTECTED`
- `S7C004_UDT_SYMBOL_NOT_FOUND`
- `S7C005_SYMBOL_TABLE_NOT_FOUND`
- `S7C006_POINTER_NOT_RESOLVED`
- `S7C007_CALLED_BLOCK_NOT_IN_PROJECT`
- `S7C008_SETUP_ROUTINE_NOT_RECOGNISED`
- `S7C009_SETUP_ENTRY_NAME_MISMATCH`
- `S7C010_DRIVE_NOT_INITIALISED_BY_SETUP`
- `S7C011_CALL_INPUT_NOT_TRACED`
- `S7C012_CALL_OUTPUT_NOT_TRACED`

Data-block members also carry their **actual value** from the offline DB
(`actualValue`), not just the declared start value.

### Classic drive startup (`FnBlk.Setup.Drv`)

`Global Setup` calls `FnBlk.Setup.Drv` on the first scan only (`A "glb.One";
JC M001` skips it once `glb.One` is set). Its loops use indirect addressing,
so the routine is recognised by name and applied as rule
`CLASSIC_SETUP_DRV_STARTUP`, verified against the A-Mill FC51 code:

```text
zero db.cm.Drv
for i in 0 .. NoDrives-1                       (NoDrives read from "L n; T #NoDrives")
    db.cm.Drv[i].ILK := TRUE, .CR := TRUE     (only when the "L W#16#50; OW" mask is present)
    db.cm.Drv[i].DrvType := db.Setup.Drv[i].DrvType   (actual value, paired by index)
```

The results are written to `inventory.startupValues`. Pairing is by index, so
a setup entry with a different name raises `S7C009`, and drives at or beyond
`NoDrives` raise `S7C010`. A member that no cyclic logic writes keeps its
startup value, and the analyser reports it as a condition sourced from
`FnBlk.Setup.Drv`. Any cyclic write supersedes the startup value.

### Classic block calls

`--include-block-calls` decompiles every OB, FC and FB (MC7 → STL, which
covers LAD, FBD and STL blocks alike) and reads `CALL`/`UC`/`CC`
statements. It always decodes the DB layouts as well, because STEP 7 passes
UDT and ANY arguments as absolute pointers:

```text
CALL "FnBlk.cm.Drv" (... Drv := P#DB50.DBX 400.0)
                                  |
      DB50 layout: 400.0  AM_M1035 : Udt.cm.Drv   <- formal type of Drv
                   400.0  AM_M1035.MO : BOOL
                                  v
resolvedMemberPath = db.cm.Drv.AM_M1035
```

A pointer resolves only when exactly one member starts at that address with
the formal parameter's datatype, taken from the called block's interface.
Otherwise the call keeps its actual expression and reports
`S7C006_POINTER_NOT_RESOLVED` with the candidates, and correlation reports
`CM104`. A resolved pointer is shown by symbol (`"db.cm.Drv".AM_M1035`), and
the pointer itself is kept as `rawActualExpression`.

### Classic call actuals through local data

LAD passes actuals through anonymous local data, so the decompiled call shows
`RN := L 0.0`, not what the engineer wired. Inputs are assigned before the
call, and outputs are copied after it inside the CALL row. The decompiler
folds those copies into the row, and the loader keeps them as
`PostCallStatements`:

```text
A  DB11.DBX 24.4        ("ioDI".AM_M1020RN)    =  L0.0        RN := L 0.0
A  M0.1                 ("glb.One")            =  L0.1        TR := L 0.1
CALL FC50  ...  UC FC50;  A L 1.1;  OPN DB10;  = DBX16.1       MO := L 1.1  -> "ioDO".AM_M1020MO
```

The logic parser records the network's local bits and words at every CALL.
The call mapper then replaces the local actual with the operand or
expression wired to it. Outputs come from the post-call copies, matching
only the plain patterns `A L x; [OPN DBn;] = dest` and
`L LWn|LDn; [OPN DBn;] T dest`. Absolute global actuals are shown by symbol
(`M 0.4` → `"glb.DontCare"`). Each parameter keeps `rawActualExpression`, a
`traceStatus` (`Traced` / `NotTraced`), and for boolean inputs the
`actualExpressionTree`.

The trace never guesses. An input with no earlier writer, a network made
conditional by a jump, or an output copy with extra logic keeps the raw
address and raises `S7C011_CALL_INPUT_NOT_TRACED` or
`S7C012_CALL_OUTPUT_NOT_TRACED`. The post-call copies are also emitted as logic
assignments (`"ioDO".AM_M1020MO := "FnBlk.cm.Drv".MO (call output)`).

Call records follow the TIA conventions (directions `Input`/`Output`/`InOut`,
1-based ordinal per calling block, caller path `Blocks/<name>`). `CM122` is
raised only for FC/SFC calls: unassigned FB/SFB parameters are optional
because they live in the instance DB.

### Classic logic (STL evaluation)

The same decompiled networks provide the boolean logic. The STL is evaluated
as the CPU evaluates the logic string (RLO, first check, standalone `O`, the
bracket stack), so LAD, FBD and STL blocks are handled alike:

- `A`/`AN`/`O`/`ON`, `A(`…`)`, standalone `O` (AND before OR), `NOT`, `SET`, `CLR`
- `=`, `S` and `R` are extracted with their operation; timer and counter `S`/`R` are not boolean writes
- comparisons become operands such as `"db.seq".SeqFeed.StepNo == 110`; a comparison of one resolved member with a literal resolves to that member
- LAD's anonymous local bits (`= L 0.0` … `A L 0.0`) are substituted within the network; they are not emitted
- InOut/UDT parameter access through the compiler's pointer preamble (`L P##Seq` … `LAR1` … `A [AR1,P#0.0]`) is rendered as `#Seq.SG`
- a timer, counter or call inside a bracket starts a new logic string at that bracket level only
- forward jumps that skip only non-boolean code (the EN/ENO pattern of MOVE boxes) are followed

Interface references are bound to the caller when a block has exactly one call
site: `#Seq.SG` in `Seq RollerMills` becomes `"db.seq".SeqRollerMills.SG`
because its only call passes `Seq := P#DB200.DBX 180.0`. With several callers
the references stay unbound (Partial), because the actual depends on the
caller.

Jumps over boolean writes, backward or cross-network jumps, `BEC`, MCR, XOR,
edge instructions (`FP`/`FN`) and status-word operands (`A BR`) produce an
`Unknown` source with the reason, never a guessed expression.

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

STEP 7 Classic projects use the older library generation, with one processing FC per family. The drive type there is a runtime INT (`Drv.DrvType`), not a separate FC:

| Family | Function | Expected number | Variant | Module datatype | Extra InOut hint |
|---|---|---:|---|---|---|
| Drive | `FnBlk.cm.Drv` | — | FnBlk.cm.Drv | `Udt.cm.Drv` | |
| Valve | `FnBlk.cm.Vlv` | — | FnBlk.cm.Vlv | `Udt.cm.Vlv` | `Vlv` |
| DigitalInput | `FnBlk.cm.DI` | — | FnBlk.cm.DI | `Udt.cm.DI` | `Lim` |
| AnalogueInput | `FnBlk.cm.AI` | — | FnBlk.cm.AI | `Udt.cm.AI` | |
| AnalogueOutput | `FnBlk.cm.AO` | — | FnBlk.cm.AO | `Udt.cm.AO` | `AOut` |
| DigitalOutput | `FnBlk.cm.DO` | — | FnBlk.cm.DO | `Udt.cm.DO` | `Ctrl` |
| Speed | `FnBlk.cm.Spd` | — | FnBlk.cm.Spd | `Udt.cm.Spd` | `Spd` |
| Analogue | `FnBlkRAW.cm.AO` | — | FnBlkRAW.cm.AO | `Udt.cm.An` | `AOut` |
| Analogue | `FnBlk.RAWScaling` | — | FnBlk.RAWScaling | `Udt.cm.An` | `AIn` |

`Analogue` (`Udt.cm.An` in `db.cm.An`) is the Classic library's generic analogue module, used for both inputs and outputs.

A changed drive FC number produces `CM113_FUNCTION_NUMBER_MISMATCH` but the name-matched call evidence is retained. Classic entries have no expected number (A-Mill's `FnBlk.cm.Drv` is FC50, the number TIA expects for `cm.DrvType0`). Unknown names that look like processing FCs (`cm.*Type*`, `FnBlk.cm.*`) raise `CM109`.

The TIA Drive ILK/CR polarity rules (`DRV_ILK_ACTIVE_HIGH_PERMISSIVE`, `DRV_CR_ACTIVE_HIGH_ENABLE`) apply only to catalogue entries flagged `DriveSemanticsConfirmed`, which means the four `cm.DrvType*` FCs.

Drives processed only by `FnBlk.cm.Drv` use the Classic rules. These were verified from the A-Mill FC50 code: ILK is read only in networks 10–11, CR only in network 13 (gated by DrvType 1, bypassed by `CmdMAN`), and DrvType only in networks 1–4.

| Condition | DrvType | Classification | Rule |
|---|---|---|---|
| ILK constant TRUE / FALSE | any | `BridgedOrBypassed` / `PermanentlyAsserted` | `CLASSIC_DRV_ILK_ACTIVE_HIGH_PERMISSIVE` |
| CR constant TRUE / FALSE | 1 | `PermanentlyEnabled` / `PermanentlyDisabled` | `CLASSIC_DRV_CR_ENABLE_DRVTYPE1` |
| CR (dynamic or constant) | 0, 2, 3 | `NotUsedByProcessing` | `CLASSIC_DRV_CR_NOT_USED_FOR_DRVTYPE` |
| CR constant | unknown | `RequiresManualInterpretation` | `CLASSIC_DRV_CR_DRVTYPE_UNKNOWN` |

DrvType comes from the `FnBlk.Setup.Drv` startup model. The report shows it per module (`configuredDriveType`, and the last `Modules` column `Drive Type`), and counts `NotUsedByProcessing` conditions in the summary (`notUsedByProcessingCount`). Cyclic CR logic on a drive whose type ignores CR is reported, because it has no effect on the motor.

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
