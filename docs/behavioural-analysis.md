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
`BEH107`, and `BEH109`. Incomplete conditions are retained in the report and
manual-review output.
