using System;
using System.Collections.Generic;
using System.Globalization;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Maps the neutral STEP 7 V5 model onto the shared snapshot model so the existing
    // offline analysis runs unchanged. Names follow the TIA conventions: blocks and members
    // are identified by symbolic name, member paths are "<DB symbol>.<member>...".
    public sealed class Step7InventoryMapper
    {
        public const string BlocksGroupPath = "Blocks";
        public const string SymbolTableName = "Symbols";
        public const string ProgramSoftwareTypeName = "S7Program";

        public TiaProjectSummary MapSummary(Step7ProjectModel project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var deviceNames = new List<string>();
            var hardware = new List<HardwareDeviceInfo>();
            foreach (Step7StationModel station in project.Stations)
            {
                deviceNames.Add(station.Name);
                var items = new List<HardwareItemInfo>();
                foreach (string cpuName in station.CpuNames)
                {
                    var programItems = new List<HardwareItemInfo>();
                    HardwareSoftwareInfo software = null;
                    foreach (Step7ProgramIdentity program in project.Programs)
                    {
                        if (!string.Equals(program.StationName, station.Name, StringComparison.Ordinal) ||
                            !string.Equals(program.CpuName, cpuName, StringComparison.Ordinal))
                            continue;
                        if (software == null)
                            software = new HardwareSoftwareInfo(program.Name, ProgramSoftwareTypeName);
                        else
                            programItems.Add(new HardwareItemInfo(program.Name,
                                new HardwareSoftwareInfo(program.Name, ProgramSoftwareTypeName),
                                new HardwareItemInfo[0]));
                    }
                    items.Add(new HardwareItemInfo(cpuName, software, programItems));
                }
                hardware.Add(new HardwareDeviceInfo(station.Name, items));
            }

            var plcs = new List<PlcInfo>();
            foreach (Step7ProgramIdentity program in project.Programs)
                plcs.Add(ToPlcInfo(program));

            return new TiaProjectSummary(project.Name, project.Path, deviceNames, plcs, hardware);
        }

        public static PlcInfo ToPlcInfo(Step7ProgramIdentity program)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            return new PlcInfo(program.Name, program.StationName ?? string.Empty, program.CpuName ?? string.Empty);
        }

        public PlcInventory MapInventory(Step7ProgramModel program)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));

            var symbols = new SymbolLookup(program.Symbols);
            var builder = new PlcInventoryBuilder(program.Identity.Name);

            foreach (Step7LoadDiagnostic diagnostic in program.Diagnostics)
                builder.AddDiagnostic(new InventoryDiagnostic(
                    diagnostic.Severity, diagnostic.Code, diagnostic.Source, diagnostic.Message));

            foreach (Step7BlockModel block in program.Blocks)
                AddBlock(block, symbols, builder);

            builder.AddTagTable(new PlcTagTableInfo(SymbolTableName, string.Empty, program.Symbols.Count));

            if (program.DataBlocksIncluded)
            {
                builder.MarkDataBlockStructuresIncluded();
                var reportedUdts = new HashSet<int>();
                foreach (Step7DataBlockModel dataBlock in program.DataBlocks)
                    builder.AddDataBlockStructure(MapDataBlock(dataBlock, symbols, reportedUdts, builder));
            }

            return builder.Build();
        }

        private static void AddBlock(Step7BlockModel block, SymbolLookup symbols, PlcInventoryBuilder builder)
        {
            string name = block.SymbolName ?? symbols.NameOf(OperandPrefix(block.Kind), block.Number);
            if (block.KnowHowProtected)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Warning",
                    Step7DiagnosticCodes.BlockKnowHowProtected,
                    PlcInventoryBuilder.BuildGroupPath(BlocksGroupPath, name),
                    "Block is know-how protected; its logic may be incomplete or unavailable."));
            }

            switch (block.Kind)
            {
                case Step7BlockKind.UserDataType:
                    builder.AddDataType(new PlcDataTypeInfo(name, BlocksGroupPath));
                    return;
                case Step7BlockKind.VariableTable:
                case Step7BlockKind.Other:
                    // Variable tables are commissioning aids, not program blocks (TIA watch
                    // tables are not inventoried either).
                    return;
            }

            builder.AddProgramBlock(new ProgramBlockInfo(
                name,
                BlockTypeName(block),
                block.Number,
                LanguageName(block.Language),
                BlocksGroupPath,
                // STEP 7 V5 stores no consistency state; timestamp conflicts are a compile-time
                // concept that later phases can diagnose separately.
                true));
        }

        private static DataBlockStructureInfo MapDataBlock(
            Step7DataBlockModel dataBlock,
            SymbolLookup symbols,
            ISet<int> reportedUdts,
            PlcInventoryBuilder builder)
        {
            string blockName = symbols.NameOf("DB", dataBlock.Number);
            var diagnostics = new List<InventoryDiagnostic>();
            if (dataBlock.DecodeError != null)
            {
                diagnostics.Add(new InventoryDiagnostic(
                    "Error",
                    Step7DiagnosticCodes.DataBlockDecodeFailed,
                    blockName,
                    "Data-block structure could not be decoded: " + dataBlock.DecodeError));
            }

            var members = new List<DataBlockMemberInfo>();
            foreach (Step7DataRowModel row in dataBlock.Rows)
                members.Add(MapRow(row, blockName, 0, symbols, reportedUdts, builder));

            return new DataBlockStructureInfo(blockName, dataBlock.Number, BlocksGroupPath, members, diagnostics);
        }

        private static DataBlockMemberInfo MapRow(
            Step7DataRowModel row,
            string parentPath,
            int nestingLevel,
            SymbolLookup symbols,
            ISet<int> reportedUdts,
            PlcInventoryBuilder builder)
        {
            string memberPath = parentPath + "." + row.Name;
            var children = new List<DataBlockMemberInfo>();
            foreach (Step7DataRowModel child in row.Children)
                children.Add(MapRow(child, memberPath, nestingLevel + 1, symbols, reportedUdts, builder));

            return new DataBlockMemberInfo(
                row.Name,
                memberPath,
                DataTypeName(row, symbols, reportedUdts, builder),
                row.Comment,
                nestingLevel,
                row.IsArray,
                row.ArrayBounds,
                row.ByteAddress.ToString(CultureInfo.InvariantCulture) + "." +
                row.BitAddress.ToString(CultureInfo.InvariantCulture),
                children);
        }

        private static string DataTypeName(
            Step7DataRowModel row,
            SymbolLookup symbols,
            ISet<int> reportedUdts,
            PlcInventoryBuilder builder)
        {
            if (string.Equals(row.ElementaryType, "UDT", StringComparison.OrdinalIgnoreCase) &&
                row.UserDataTypeNumber.HasValue)
            {
                int number = row.UserDataTypeNumber.Value;
                string symbol = symbols.SymbolOf("UDT", number);
                if (symbol != null) return symbol;
                if (reportedUdts.Add(number))
                {
                    builder.AddDiagnostic(new InventoryDiagnostic(
                        "Warning",
                        Step7DiagnosticCodes.UserDataTypeSymbolNotFound,
                        "UDT" + number.ToString(CultureInfo.InvariantCulture),
                        "UDT has no symbol; members of this type cannot be recognised as control modules."));
                }
                return "UDT" + number.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(row.ElementaryType, "STRUCT", StringComparison.OrdinalIgnoreCase))
                return "Struct";
            return row.ElementaryType;
        }

        private static string BlockTypeName(Step7BlockModel block)
        {
            switch (block.Kind)
            {
                case Step7BlockKind.OrganizationBlock: return "OrganizationBlock";
                case Step7BlockKind.Function: return "Function";
                case Step7BlockKind.FunctionBlock: return "FunctionBlock";
                case Step7BlockKind.DataBlock:
                    return block.IsInstanceDataBlock ? "InstanceDataBlock" : "GlobalDataBlock";
                case Step7BlockKind.SystemFunction: return "SystemFunction";
                case Step7BlockKind.SystemFunctionBlock: return "SystemFunctionBlock";
                default: return "Other";
            }
        }

        // STEP 7 V5 stores German mnemonics; the snapshot uses the TIA/English names.
        public static string LanguageName(string language)
        {
            switch ((language ?? string.Empty).ToUpperInvariant())
            {
                case "AWL": return "STL";
                case "KOP": return "LAD";
                case "FUP": return "FBD";
                case "FAWL": return "F_STL";
                case "FKOP": return "F_LAD";
                case "FFUP": return "F_FBD";
                case "": return "Unknown";
                default: return language;
            }
        }

        private static string OperandPrefix(Step7BlockKind kind)
        {
            switch (kind)
            {
                case Step7BlockKind.OrganizationBlock: return "OB";
                case Step7BlockKind.Function: return "FC";
                case Step7BlockKind.FunctionBlock: return "FB";
                case Step7BlockKind.DataBlock: return "DB";
                case Step7BlockKind.UserDataType: return "UDT";
                case Step7BlockKind.SystemFunction: return "SFC";
                case Step7BlockKind.SystemFunctionBlock: return "SFB";
                case Step7BlockKind.VariableTable: return "VAT";
                default: return "BLK";
            }
        }

        private sealed class SymbolLookup
        {
            private readonly Dictionary<string, string> byOperand =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public SymbolLookup(IEnumerable<Step7SymbolModel> symbols)
            {
                foreach (Step7SymbolModel symbol in symbols)
                {
                    string operand = Normalize(symbol.Operand);
                    // STEP 7 rejects duplicate operands; keep the first defensively.
                    if (operand.Length > 0 && symbol.Symbol.Length > 0 && !byOperand.ContainsKey(operand))
                        byOperand.Add(operand, symbol.Symbol);
                }
            }

            public string SymbolOf(string prefix, int number)
            {
                string symbol;
                return byOperand.TryGetValue(prefix + number.ToString(CultureInfo.InvariantCulture), out symbol)
                    ? symbol
                    : null;
            }

            public string NameOf(string prefix, int number)
            {
                return SymbolOf(prefix, number) ?? prefix + number.ToString(CultureInfo.InvariantCulture);
            }

            private static string Normalize(string operand)
            {
                return (operand ?? string.Empty).Replace(" ", string.Empty).Trim();
            }
        }
    }
}
