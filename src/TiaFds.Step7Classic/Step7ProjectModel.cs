using System.Collections.Generic;

namespace TiaFds.Step7Classic
{
    // Neutral, library-independent view of a STEP 7 V5.x project. The loader fills it from
    // the project files; mappers and tests only ever see these types.

    public sealed class Step7ProjectModel
    {
        public Step7ProjectModel(string name, string path, IReadOnlyList<Step7StationModel> stations,
            IReadOnlyList<Step7ProgramIdentity> programs)
        {
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            Stations = stations ?? new Step7StationModel[0];
            Programs = programs ?? new Step7ProgramIdentity[0];
        }

        public string Name { get; }
        public string Path { get; }
        public IReadOnlyList<Step7StationModel> Stations { get; }
        public IReadOnlyList<Step7ProgramIdentity> Programs { get; }
    }

    public sealed class Step7ProgramIdentity
    {
        public Step7ProgramIdentity(string name, string stationName, string cpuName)
        {
            Name = name ?? string.Empty;
            StationName = string.IsNullOrWhiteSpace(stationName) ? null : stationName;
            CpuName = string.IsNullOrWhiteSpace(cpuName) ? null : cpuName;
        }

        // S7 program folder name, e.g. "S7 Program".
        public string Name { get; }
        // Null when the program is not assigned to a CPU (a program directly under the project).
        public string StationName { get; }
        public string CpuName { get; }

        // "Station/CPU/Program", or just the program name when it has no CPU.
        public string Path
        {
            get
            {
                return CpuName == null
                    ? Name
                    : (StationName ?? string.Empty) + "/" + CpuName + "/" + Name;
            }
        }
    }

    public sealed class Step7StationModel
    {
        public Step7StationModel(string name, IReadOnlyList<string> cpuNames)
        {
            Name = name ?? string.Empty;
            CpuNames = cpuNames ?? new string[0];
        }

        public string Name { get; }
        public IReadOnlyList<string> CpuNames { get; }
    }

    public sealed class Step7ProgramModel
    {
        public Step7ProgramModel(
            Step7ProgramIdentity identity,
            IReadOnlyList<Step7SymbolModel> symbols,
            IReadOnlyList<Step7BlockModel> blocks,
            IReadOnlyList<Step7DataBlockModel> dataBlocks,
            bool dataBlocksIncluded,
            IReadOnlyList<Step7LoadDiagnostic> diagnostics)
            : this(identity, symbols, blocks, dataBlocks, dataBlocksIncluded, null, false, diagnostics)
        {
        }

        public Step7ProgramModel(
            Step7ProgramIdentity identity,
            IReadOnlyList<Step7SymbolModel> symbols,
            IReadOnlyList<Step7BlockModel> blocks,
            IReadOnlyList<Step7DataBlockModel> dataBlocks,
            bool dataBlocksIncluded,
            IReadOnlyList<Step7CodeBlockModel> codeBlocks,
            bool codeBlocksIncluded,
            IReadOnlyList<Step7LoadDiagnostic> diagnostics)
        {
            Identity = identity ?? new Step7ProgramIdentity(null, null, null);
            Symbols = symbols ?? new Step7SymbolModel[0];
            Blocks = blocks ?? new Step7BlockModel[0];
            DataBlocks = dataBlocks ?? new Step7DataBlockModel[0];
            DataBlocksIncluded = dataBlocksIncluded;
            CodeBlocks = codeBlocks ?? new Step7CodeBlockModel[0];
            CodeBlocksIncluded = codeBlocksIncluded;
            Diagnostics = diagnostics ?? new Step7LoadDiagnostic[0];
        }

        // Decoded OB/FC/FB (with networks) and SFC/SFB (interface only); empty unless block
        // calls were requested.
        public IReadOnlyList<Step7CodeBlockModel> CodeBlocks { get; }
        public bool CodeBlocksIncluded { get; }

        public Step7ProgramIdentity Identity { get; }
        public IReadOnlyList<Step7SymbolModel> Symbols { get; }
        public IReadOnlyList<Step7BlockModel> Blocks { get; }
        // Decoded global data blocks; empty unless structures were requested.
        public IReadOnlyList<Step7DataBlockModel> DataBlocks { get; }
        public bool DataBlocksIncluded { get; }
        public IReadOnlyList<Step7LoadDiagnostic> Diagnostics { get; }
    }

    public sealed class Step7SymbolModel
    {
        public Step7SymbolModel(string symbol, string operand, string dataType, string comment)
        {
            Symbol = symbol ?? string.Empty;
            Operand = operand ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Comment = comment ?? string.Empty;
        }

        public string Symbol { get; }
        // Absolute operand without padding, e.g. "DB50", "UDT50", "M10.0".
        public string Operand { get; }
        public string DataType { get; }
        public string Comment { get; }
    }

    public enum Step7BlockKind
    {
        OrganizationBlock,
        Function,
        FunctionBlock,
        DataBlock,
        UserDataType,
        SystemFunction,
        SystemFunctionBlock,
        VariableTable,
        Other
    }

    public sealed class Step7BlockModel
    {
        public Step7BlockModel(
            Step7BlockKind kind,
            int number,
            string symbolName,
            string language,
            bool isInstanceDataBlock,
            bool knowHowProtected)
        {
            Kind = kind;
            Number = number;
            SymbolName = string.IsNullOrWhiteSpace(symbolName) ? null : symbolName;
            Language = language ?? string.Empty;
            IsInstanceDataBlock = isInstanceDataBlock;
            KnowHowProtected = knowHowProtected;
        }

        public Step7BlockKind Kind { get; }
        public int Number { get; }
        public string SymbolName { get; }
        // STEP 7 V5 language name as stored in the project (AWL, KOP, FUP, SCL, DB, ...).
        public string Language { get; }
        public bool IsInstanceDataBlock { get; }
        public bool KnowHowProtected { get; }
    }

    public sealed class Step7DataBlockModel
    {
        public Step7DataBlockModel(int number, IReadOnlyList<Step7DataRowModel> rows, string decodeError)
        {
            Number = number;
            Rows = rows ?? new Step7DataRowModel[0];
            DecodeError = string.IsNullOrWhiteSpace(decodeError) ? null : decodeError;
        }

        public int Number { get; }
        // Top-level declaration rows of the DB.
        public IReadOnlyList<Step7DataRowModel> Rows { get; }
        // Set when the library could not decode the block; Rows is then empty.
        public string DecodeError { get; }
    }

    public sealed class Step7DataRowModel
    {
        public Step7DataRowModel(
            string name,
            string elementaryType,
            int? userDataTypeNumber,
            string comment,
            int byteAddress,
            int bitAddress,
            bool isArray,
            string arrayBounds,
            IReadOnlyList<Step7DataRowModel> children)
            : this(name, elementaryType, userDataTypeNumber, comment, byteAddress, bitAddress, isArray, arrayBounds, null, children)
        {
        }

        public Step7DataRowModel(
            string name,
            string elementaryType,
            int? userDataTypeNumber,
            string comment,
            int byteAddress,
            int bitAddress,
            bool isArray,
            string arrayBounds,
            string actualValue,
            IReadOnlyList<Step7DataRowModel> children)
        {
            ActualValue = string.IsNullOrWhiteSpace(actualValue) ? null : actualValue;
            Name = name ?? string.Empty;
            ElementaryType = elementaryType ?? string.Empty;
            UserDataTypeNumber = userDataTypeNumber;
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            ByteAddress = byteAddress;
            BitAddress = bitAddress;
            IsArray = isArray;
            ArrayBounds = string.IsNullOrWhiteSpace(arrayBounds) ? null : arrayBounds;
            Children = children ?? new Step7DataRowModel[0];
        }

        public string Name { get; }
        // S7 type keyword (BOOL, INT, STRUCT, UDT, ...). For arrays, the element type.
        public string ElementaryType { get; }
        // UDT number when ElementaryType is UDT.
        public int? UserDataTypeNumber { get; }
        public string Comment { get; }
        public int ByteAddress { get; }
        public int BitAddress { get; }
        public bool IsArray { get; }
        // STEP 7 bounds text, e.g. "1..10" or "1..4,0..2".
        public string ArrayBounds { get; }
        // Current value stored in the offline DB for elementary members (invariant text).
        public string ActualValue { get; }
        public IReadOnlyList<Step7DataRowModel> Children { get; }
    }

    // A decoded OB, FC, FB, SFC or SFB. System blocks only carry an interface.
    public sealed class Step7CodeBlockModel
    {
        public Step7CodeBlockModel(
            Step7BlockKind kind,
            int number,
            IReadOnlyList<Step7InterfaceParameterModel> interfaceParameters,
            IReadOnlyList<Step7NetworkModel> networks,
            string decodeError)
        {
            Kind = kind;
            Number = number;
            InterfaceParameters = interfaceParameters ?? new Step7InterfaceParameterModel[0];
            Networks = networks ?? new Step7NetworkModel[0];
            DecodeError = string.IsNullOrWhiteSpace(decodeError) ? null : decodeError;
        }

        public Step7BlockKind Kind { get; }
        public int Number { get; }
        // Formal parameters (IN, OUT, IN_OUT sections only).
        public IReadOnlyList<Step7InterfaceParameterModel> InterfaceParameters { get; }
        public IReadOnlyList<Step7NetworkModel> Networks { get; }
        public string DecodeError { get; }
    }

    public sealed class Step7InterfaceParameterModel
    {
        public Step7InterfaceParameterModel(string section, string name, string elementaryType, int? userDataTypeNumber)
            : this(section, name, elementaryType, userDataTypeNumber, null)
        {
        }

        public Step7InterfaceParameterModel(
            string section,
            string name,
            string elementaryType,
            int? userDataTypeNumber,
            IReadOnlyList<Step7InterfaceMemberModel> members)
            : this(section, name, elementaryType, userDataTypeNumber, members, null, 0)
        {
        }

        public Step7InterfaceParameterModel(
            string section,
            string name,
            string elementaryType,
            int? userDataTypeNumber,
            IReadOnlyList<Step7InterfaceMemberModel> members,
            int? byteAddress,
            int bitAddress)
        {
            ByteAddress = byteAddress;
            BitAddress = bitAddress;
            Section = section ?? string.Empty;
            Name = name ?? string.Empty;
            ElementaryType = elementaryType ?? string.Empty;
            UserDataTypeNumber = userDataTypeNumber;
            Members = members ?? new Step7InterfaceMemberModel[0];
        }

        // IN, OUT, IN_OUT, or TEMP (temporaries are kept so named local bits can be rendered).
        public string Section { get; }
        public string Name { get; }
        public string ElementaryType { get; }
        public int? UserDataTypeNumber { get; }
        // Flattened members of a UDT/STRUCT parameter, addressed relative to the parameter's start.
        public IReadOnlyList<Step7InterfaceMemberModel> Members { get; }
        // Address within its section (for TEMP: the local-data address, e.g. LW28 = 28.0).
        public int? ByteAddress { get; }
        public int BitAddress { get; }
    }

    public sealed class Step7InterfaceMemberModel
    {
        public Step7InterfaceMemberModel(string path, string elementaryType, int byteAddress, int bitAddress)
        {
            Path = path ?? string.Empty;
            ElementaryType = elementaryType ?? string.Empty;
            ByteAddress = byteAddress;
            BitAddress = bitAddress;
        }

        // Relative member path, e.g. "SG" or "Status.Running".
        public string Path { get; }
        public string ElementaryType { get; }
        public int ByteAddress { get; }
        public int BitAddress { get; }
    }

    public sealed class Step7NetworkModel
    {
        public Step7NetworkModel(int number, string title, string comment, IReadOnlyList<Step7StatementModel> statements)
        {
            Number = number;
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
            Statements = statements ?? new Step7StatementModel[0];
        }

        // 1-based position within the block.
        public int Number { get; }
        public string Title { get; }
        public string Comment { get; }
        public IReadOnlyList<Step7StatementModel> Statements { get; }
    }

    // One decompiled STL statement. Operands are kept in absolute form (e.g. "DB11.DBX 0.3",
    // "M0.1"); resolution to symbols happens in the mappers against the DB layouts.
    public sealed class Step7StatementModel
    {
        public Step7StatementModel(
            string label,
            string command,
            string operand,
            string operandSymbol,
            string calledBlock,
            string instanceDataBlock,
            IReadOnlyList<Step7CallArgumentModel> callArguments)
            : this(label, command, operand, operandSymbol, calledBlock, instanceDataBlock, callArguments, null)
        {
        }

        public Step7StatementModel(
            string label,
            string command,
            string operand,
            string operandSymbol,
            string calledBlock,
            string instanceDataBlock,
            IReadOnlyList<Step7CallArgumentModel> callArguments,
            IReadOnlyList<Step7StatementModel> postCallStatements)
        {
            PostCallStatements = postCallStatements ?? new Step7StatementModel[0];
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            Command = (command ?? string.Empty).Trim();
            Operand = (operand ?? string.Empty).Trim();
            OperandSymbol = string.IsNullOrWhiteSpace(operandSymbol) ? null : operandSymbol;
            CalledBlock = string.IsNullOrWhiteSpace(calledBlock) ? null : calledBlock.Trim();
            InstanceDataBlock = string.IsNullOrWhiteSpace(instanceDataBlock) ? null : instanceDataBlock.Trim();
            CallArguments = callArguments ?? new Step7CallArgumentModel[0];
        }

        public string Label { get; }
        public string Command { get; }
        public string Operand { get; }
        // Symbol-table name of the operand when it is a global symbol (e.g. "glb.One").
        public string OperandSymbol { get; }
        // Absolute called block for CALL/UC/CC, e.g. "FC50", "SFB14".
        public string CalledBlock { get; }
        // Instance DB of an FB/SFB call, e.g. "DI5005".
        public string InstanceDataBlock { get; }
        public IReadOnlyList<Step7CallArgumentModel> CallArguments { get; }
        // Statements the decompiler folds into a CALL row after the UC/CC: the copies of
        // output actuals out of local data, e.g. "A L 1.1; OPN DB10; = DBX16.1".
        public IReadOnlyList<Step7StatementModel> PostCallStatements { get; }
    }

    public sealed class Step7CallArgumentModel
    {
        public Step7CallArgumentModel(string name, string section, string elementaryType, string value)
        {
            Name = name ?? string.Empty;
            Section = section ?? string.Empty;
            ElementaryType = elementaryType ?? string.Empty;
            Value = (value ?? string.Empty).Trim();
        }

        public string Name { get; }
        // IN, OUT or IN_OUT as decoded at the call site.
        public string Section { get; }
        public string ElementaryType { get; }
        // Actual parameter as decompiled, e.g. "P#DB50.DBX 400.0", "L 0.0", "M0.1".
        public string Value { get; }
    }

    public sealed class Step7LoadDiagnostic
    {
        public Step7LoadDiagnostic(string severity, string code, string source, string message)
        {
            Severity = severity;
            Code = code;
            Source = source;
            Message = message;
        }

        public string Severity { get; }
        public string Code { get; }
        public string Source { get; }
        public string Message { get; }
    }
}
