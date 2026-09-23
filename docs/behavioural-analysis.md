# Behavioural logic extraction and analysis

## Observed V15.1 pattern

The available real `FC501 cm.Drv.System` export contains four start-command
networks. Each network writes `db.cm.Drv.<module>.SA` through a LAD `Coil`.
The coil input is formed from:

- serial `Contact` parts (AND);
- parallel branches feeding an `O` part (OR);
- `<Negated Name="operand" />` on a contact (NOT);
- a power rail as the logical source;
- symbolic `Access` nodes connected through `IdentCon`;
- `NameCon` links for `in`, `out`, and `operand`;
- a network title describing the module and purpose.

The processing-block call is in the following compile unit, rather than in the
same network as the SA coil. Behaviour ownership therefore comes from the
resolved coil destination, never from the title or call proximity.

No real CR or ILK network export is present in the available fixture set. The
first implementation applies the same exact terminal-member and graph rules to
`CR`, `CRn`, `CR[n]`, `ILK`, `ILKn`, and `ILK[n]`. These rules are covered by
sanitized and in-memory fixtures; external validation against real CR/ILK
exports remains required.

## Stage boundaries

- Extraction preserves neutral coil assignments, expression trees, operands,
  resolved paths, source order, block/language, network title/comment, and
  incomplete status.
- Analysis recognizes SA, CR, and ILK members, correlates by resolved owner
  path, traces a single prior temporary assignment, and emits behavioural
  diagnostics.
- Reporting maps the analysed result into `AnalysisReport`; it does not parse
  PLC instructions.
- Later FDS generation will consume `AnalysisReport`. This milestone does not
  generate prose or an FDS.

## Supported boundary

Supported LAD/FBD graph nodes are:

- `Coil`, `Assign`, `Assignment`, or `=` destinations;
- symbolic operands and TRUE/FALSE constants;
- contacts, including negated contacts;
- AND (`A`) and OR (`O`) parts;
- NOT parts;
- nested supported AND/OR/NOT expressions;
- numeric indexed or suffixed SA/CR/ILK members;
- one unambiguous prior assignment to a local `#temporary` in the same block;
- deterministic statement and source-expression order.

Unsupported or partial evidence remains visible. This includes unknown parts,
unresolved operands, dynamic/indirect indices, multiple assignments, ambiguous
temporary definitions, unsupported block languages, indirect addressing,
arbitrary STL/AWL control flow, jumps, loops, and unrestricted data-flow
analysis. Expression traversal is bounded and circular/incomplete temporary
traces are not guessed.

## Resolution statuses

- `Complete`: destination ownership and the supported expression are resolved.
- `Partial`: useful structure exists but at least one operand is unresolved.
- `Unsupported`: a recognized destination uses an unsupported expression form.
- `Unresolved`: the destination or owning module cannot be resolved.
- `Ambiguous`: multiple assignments or other competing evidence exists.

Behavioural diagnostics use the `BEH` prefix. Current codes include
`BEH100`, `BEH101`, `BEH102`, `BEH103`, `BEH104`, `BEH105`, `BEH106`,
`BEH107`, `BEH109`, `BEH111`, `BEH112`, `BEH115`, `BEH118`, and `BEH119`.
Incomplete conditions are retained in the report and manual-review output.

## Boolean constants and effective conditions

Confirmed representations are:

- LAD/FBD literal `TRUE` and `FALSE`;
- quoted or unquoted exact project symbols `glb.One` and `glb.Zero`;
- constants propagated through a supported expression or one bounded,
  unambiguous local `#temporary`.

The default immutable catalogue maps `glb.One` to TRUE through rule
`BP_GLB_ONE` and `glb.Zero` to FALSE through `BP_GLB_ZERO`. It accepts exact,
case-insensitive forms after removing TIA identifier quotes. It deliberately
does not infer constants from suffixes or fragments: `DB1.OneAlarm`,
`DB1.ZeroSpeed`, `Motor.TrueStatus`, `Valve.FalseFeedback`,
`SomeTagNamedOne`, and `SomeTagNamedZero` remain ordinary operands.

Numeric `0` and `1` are not resolved globally. Until extraction provides
unambiguous boolean datatype context, they remain partial and produce
`BEH118_BOOLEAN_LITERAL_DATATYPE_AMBIGUOUS`. Conflicting exact catalogue
mappings produce `BEH119_CONSTANT_CATALOGUE_CONFLICT`.

Analysis annotates the original expression tree with constant provenance and
creates a separate simplified tree. It applies nested NOT, AND, and OR rules,
including safe absorbing rules where an unresolved branch cannot affect the
result. The source expression is never overwritten.

Effects are `Dynamic`, `PermanentlyTrue`, `PermanentlyFalse`,
`PartiallySimplified`, or `Unknown`. Engineering interpretation is separate:

- SA/CR TRUE: `PermanentlyEnabled`, using an active-high request rule;
- SA/CR FALSE: `PermanentlyDisabled`, using an active-high request rule;
- dynamic complete expression: `NormalDynamicCondition`;
- Drive/DrvType* ILK TRUE: `BridgedOrBypassed`;
- Drive/DrvType* ILK FALSE: `PermanentlyAsserted`;
- permanent ILK values for non-Drive or missing variants:
  `RequiresManualInterpretation`.

The real `cm.DrvType1` export demonstrates the Drive rule. In its
`Control Relay, Restart Timeout, to Motor Output` network, the motor-output
path contains `(Drv.CR OR Drv.OVR)` in series with
`(Drv.ILK OR Drv.OVR)`. `Drv.ILK` is therefore an active-high permissive:
TRUE permits the output path, while FALSE inhibits it unless override is
active. The semantic rule identifier is
`DRV_ILK_ACTIVE_HIGH_PERMISSIVE`. The library owner confirmed that every Drive
processing type uses the same ILK and CR polarity, so the rule applies to
recognized `DrvType*` variants and to modules using more than one such variant.

In that exported compile unit, CR/OVR are Access UIDs 26/27 feeding Contact
UIDs 39/40 and OR UID 41. ILK/OVR are Access UIDs 28/29 feeding Contact UIDs
42/43 and OR UID 44. Wires 61-65 form the first branch; wires 66-70 form the
second branch and connect it to the MO coils. This UID evidence, rather than a
network title, is the basis of the polarity rule.

The same network confirms Drive CR is an active-high enable through rule
`DRV_CR_ACTIVE_HIGH_ENABLE`. Non-Drive families and Drive modules without
processing-variant evidence continue to produce
`BEH115_CONSTANT_SEMANTICS_UNKNOWN` and manual review for permanent ILK values.

The real `cm.Drv.Feed` export confirms CR and ILK destinations are standard LAD
coil assignments. CR examples include serial conditions such as a bin enable,
downstream drive state, and valve state. ILK examples include direct upstream
drive-state connections and nested fault/permissive logic. Sanitized fixtures
preserve both real UID/wire shapes.

Generic SA/CR/ILK-looking destinations are ignored unless their resolved path
belongs to a discovered control-module owner or a known control-module
container. This prevents generic sequence/block interface members such as
`Seq_Header.ILK` from becoming module behaviour while retaining diagnostics
for unresolved members inside known module containers.

Report schema 1.2 adds original/simplified expressions, constant provenance,
effective value, effect, review classification, semantic rule, and findings.
The six-sheet workbook keeps findings in filterable columns on `Behavioural
Conditions`; it does not add a duplicate findings worksheet.

A constant result describes the statically extracted program logic. It is not
proof of the current online PLC value, force state, HMI override, or plant
safety condition.
