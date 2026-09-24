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

            var symbols = new Step7SymbolLookup(program.Symbols);
            var builder = new PlcInventoryBuilder(program.Identity.Name);

            foreach (Step7LoadDiagnostic diagnostic in program.Diagnostics)
                builder.AddDiagnostic(new InventoryDiagnostic(
                    diagnostic.Severity, diagnostic.Code, diagnostic.Source, diagnostic.Message));

            foreach (Step7BlockModel block in program.Blocks)
                AddBlock(block, symbols, builder);

            builder.AddTagTable(new PlcTagTableInfo(SymbolTableName, string.Empty, program.Symbols.Count));

            // Structures are always mapped when decoded, because call extraction resolves
            // pointers against them; they are only emitted when requested.
            var structures = new List<DataBlockStructureInfo>();
            var reportedUdts = new HashSet<int>();
            foreach (Step7DataBlockModel dataBlock in program.DataBlocks)
                structures.Add(MapDataBlock(dataBlock, symbols, reportedUdts, builder));

            if (program.DataBlocksIncluded)
            {
                builder.MarkDataBlockStructuresIncluded();
                foreach (DataBlockStructureInfo structure in structures) builder.AddDataBlockStructure(structure);
            }

            if (program.CodeBlocksIncluded)
            {
                var addresses = new Step7AddressResolver(structures);
                builder.MarkBlockCallsIncluded();
                var diagnostics = new List<InventoryDiagnostic>();
                IReadOnlyList<BlockCallInfo> calls = new Step7BlockCallMapper().Map(program, symbols, addresses, diagnostics);
                foreach (BlockCallInfo call in calls) builder.AddBlockCall(call);
                foreach (InventoryDiagnostic diagnostic in diagnostics) builder.AddDiagnostic(diagnostic);

                // The same decompiled networks carry the boolean logic (=, S, R). Interface
                // references are bound to the actual argument of a block's only call site.
                builder.MarkLogicAssignmentsIncluded();
                var parser = new Step7StlLogicParser(symbols, addresses);
                var binder = new Step7ParameterBinder(calls, addresses);
                foreach (Step7CodeBlockModel block in program.CodeBlocks)
                {
                    if (block.DecodeError != null || !IsProgramCode(block.Kind)) continue;
                    Step7BlockModel listed = FindListed(program, block);
                    foreach (ExtractedLogicAssignment assignment in parser.Parse(
                                 block,
                                 symbols.NameOf(Step7BlockCallMapper.Prefix(block.Kind), block.Number),
                                 BlockTypeName(block.Kind, false),
                                 LanguageName(listed == null ? null : listed.Language)))
                        builder.AddLogicAssignment(binder.Bind(assignment));
                }
            }

            return builder.Build();
        }

        private static void AddBlock(Step7BlockModel block, Step7SymbolLookup symbols, PlcInventoryBuilder builder)
        {
            string name = block.SymbolName ?? symbols.NameOf(Step7BlockCallMapper.Prefix(block.Kind), block.Number);
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
                BlockTypeName(block.Kind, block.IsInstanceDataBlock),
                block.Number,
                LanguageName(block.Language),
                BlocksGroupPath,
                // STEP 7 V5 stores no consistency state; timestamp conflicts are a compile-time
                // concept that later phases can diagnose separately.
                true));
        }

        private static DataBlockStructureInfo MapDataBlock(
            Step7DataBlockModel dataBlock,
            Step7SymbolLookup symbols,
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
            Step7SymbolLookup symbols,
            ISet<int> reportedUdts,
            PlcInventoryBuilder builder)
        {
            string memberPath = parentPath + "." + row.Name;
            var children = new List<DataBlockMemberInfo>();
            foreach (Step7DataRowModel child in row.Children)
                children.Add(MapRow(child, memberPath, nestingLevel + 1, symbols, reportedUdts, builder));

            if (string.Equals(row.ElementaryType, "UDT", StringComparison.OrdinalIgnoreCase) &&
                row.UserDataTypeNumber.HasValue &&
                symbols.SymbolOf("UDT", row.UserDataTypeNumber.Value) == null &&
                reportedUdts.Add(row.UserDataTypeNumber.Value))
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Warning",
                    Step7DiagnosticCodes.UserDataTypeSymbolNotFound,
                    "UDT" + row.UserDataTypeNumber.Value.ToString(CultureInfo.InvariantCulture),
                    "UDT has no symbol; members of this type cannot be recognised as control modules."));
            }

            return new DataBlockMemberInfo(
                row.Name,
                memberPath,
                symbols.TypeName(row.ElementaryType, row.UserDataTypeNumber),
                row.Comment,
                nestingLevel,
                row.IsArray,
                row.ArrayBounds,
                row.ByteAddress.ToString(CultureInfo.InvariantCulture) + "." +
                row.BitAddress.ToString(CultureInfo.InvariantCulture),
                children);
        }

        private static bool IsProgramCode(Step7BlockKind kind)
        {
            return kind == Step7BlockKind.OrganizationBlock || kind == Step7BlockKind.Function ||
                   kind == Step7BlockKind.FunctionBlock;
        }

        private static Step7BlockModel FindListed(Step7ProgramModel program, Step7CodeBlockModel block)
        {
            foreach (Step7BlockModel listed in program.Blocks)
                if (listed.Kind == block.Kind && listed.Number == block.Number) return listed;
            return null;
        }

        internal static string BlockTypeName(Step7BlockKind kind, bool isInstanceDataBlock)
        {
            switch (kind)
            {
                case Step7BlockKind.OrganizationBlock: return "OrganizationBlock";
                case Step7BlockKind.Function: return "Function";
                case Step7BlockKind.FunctionBlock: return "FunctionBlock";
                case Step7BlockKind.DataBlock:
                    return isInstanceDataBlock ? "InstanceDataBlock" : "GlobalDataBlock";
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
    }
}
