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
        {
            Identity = identity ?? new Step7ProgramIdentity(null, null, null);
            Symbols = symbols ?? new Step7SymbolModel[0];
            Blocks = blocks ?? new Step7BlockModel[0];
            DataBlocks = dataBlocks ?? new Step7DataBlockModel[0];
            DataBlocksIncluded = dataBlocksIncluded;
            Diagnostics = diagnostics ?? new Step7LoadDiagnostic[0];
        }

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
        {
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
        public IReadOnlyList<Step7DataRowModel> Children { get; }
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
