using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DotNetSiemensPLCToolBoxLibrary.DataTypes;
using DotNetSiemensPLCToolBoxLibrary.DataTypes.AWL.Step7V5;
using DotNetSiemensPLCToolBoxLibrary.DataTypes.Blocks;
using DotNetSiemensPLCToolBoxLibrary.DataTypes.Blocks.Step7V5;
using DotNetSiemensPLCToolBoxLibrary.DataTypes.Projectfolders;
using DotNetSiemensPLCToolBoxLibrary.DataTypes.Projectfolders.Step7V5;
using DotNetSiemensPLCToolBoxLibrary.Projectfiles;

namespace TiaFds.Step7Classic
{
    // The only type that touches DotNetSiemensPLCToolBoxLibrary. It reads the STEP 7 V5.x
    // project files (read-only) and copies what later stages need into the neutral model.
    public sealed class Step7LibraryProjectLoader : IDisposable
    {
        private readonly Step7ProjectV5 project;
        private readonly List<ProgramEntry> programs = new List<ProgramEntry>();

        private Step7LibraryProjectLoader(Step7ProjectV5 project, Step7ProjectModel model)
        {
            this.project = project;
            Model = model;
        }

        public Step7ProjectModel Model { get; private set; }

        public static bool IsSupportedInput(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            return string.Equals(extension, ".s7p", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
        }

        public static Step7LibraryProjectLoader Open(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("An input path is required.", nameof(inputPath));

            // The library splits paths on '\'; a forward-slash path silently yields an empty
            // project. GetFullPath normalises separators on Windows.
            string fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("The STEP 7 project or archive was not found.", fullPath);
            if (!IsSupportedInput(fullPath))
                throw new NotSupportedException("Input must be a STEP 7 V5.x project (.s7p) or a zipped project (.zip).");

            var project = new Step7ProjectV5(fullPath, false);
            try
            {
                var loader = new Step7LibraryProjectLoader(project, null);
                loader.Model = loader.ReadStructure(fullPath);
                if (loader.Model.Programs.Count == 0)
                    throw new InvalidDataException(
                        "No S7 programs were found in '" + fullPath + "'. The project may be empty, " +
                        "damaged, or not a STEP 7 V5.x project.");
                return loader;
            }
            catch
            {
                project.Dispose();
                throw;
            }
        }

        public Step7ProgramModel LoadProgram(Step7ProgramIdentity identity, bool includeDataBlocks)
        {
            return LoadProgram(identity, includeDataBlocks, false);
        }

        // includeDataBlocks controls whether DB structures are emitted; includeCodeBlocks
        // decodes networks for call extraction and also decodes DB layouts, because call
        // pointers (P#DB50.DBX 400.0) can only be resolved against them.
        public Step7ProgramModel LoadProgram(Step7ProgramIdentity identity, bool includeDataBlocks, bool includeCodeBlocks)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            ProgramEntry entry = programs.FirstOrDefault(item => ReferenceEquals(item.Identity, identity));
            if (entry == null) throw new ArgumentException("The program does not belong to this project.", nameof(identity));

            var diagnostics = new List<Step7LoadDiagnostic>();
            string source = identity.Path;
            List<Step7SymbolModel> symbols = ReadSymbols(entry.Folder, source, diagnostics);

            var blocks = new List<Step7BlockModel>();
            var dataBlocks = new List<Step7DataBlockModel>();
            var codeBlocks = new List<Step7CodeBlockModel>();
            bool keepDataBlocks = includeDataBlocks || includeCodeBlocks;
            BlocksOfflineFolder blockFolder = entry.Folder.BlocksOfflineFolder;
            List<ProjectBlockInfo> blockInfos = null;
            if (blockFolder == null)
            {
                diagnostics.Add(new Step7LoadDiagnostic("Error", Step7DiagnosticCodes.BlockListReadFailed, source,
                    "The program has no offline blocks folder."));
            }
            else
            {
                try
                {
                    blockInfos = blockFolder.readPlcBlocksList();
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new Step7LoadDiagnostic("Error", Step7DiagnosticCodes.BlockListReadFailed, source,
                        "The offline block list could not be read: " + exception.Message));
                }
            }

            foreach (ProjectBlockInfo info in blockInfos ?? new List<ProjectBlockInfo>())
            {
                if (info.Deleted) continue;
                var plcInfo = info as ProjectPlcBlockInfo;
                if (plcInfo == null) continue;

                Step7BlockKind kind = MapKind(info.BlockType);
                var s7Info = info as S7ProjectBlockInfo;
                bool isInstance = info.IsInstance;
                if (kind == Step7BlockKind.DataBlock)
                {
                    // The block list does not reliably flag instance DBs, so every DB is decoded
                    // (cheap: the whole A-Mill program decodes in under a second) and classified
                    // from its header. Only global DB structures are kept, as for TIA.
                    DecodedDataBlock decoded = DecodeDataBlock(blockFolder, plcInfo);
                    isInstance = decoded.IsInstance;
                    if (keepDataBlocks && !isInstance) dataBlocks.Add(decoded.Model);
                }
                else if (includeCodeBlocks && IsCodeBlock(kind))
                {
                    codeBlocks.Add(DecodeCodeBlock(blockFolder, plcInfo, kind));
                }

                blocks.Add(new Step7BlockModel(
                    kind,
                    plcInfo.BlockNumber,
                    plcInfo.SymbolTabelEntry == null ? null : plcInfo.SymbolTabelEntry.Symbol,
                    info.BlockLanguage.ToString(),
                    isInstance,
                    s7Info != null && s7Info.KnowHowProtection));
            }

            return new Step7ProgramModel(identity, symbols, blocks, dataBlocks, includeDataBlocks,
                codeBlocks, includeCodeBlocks, diagnostics);
        }

        private static bool IsCodeBlock(Step7BlockKind kind)
        {
            return kind == Step7BlockKind.OrganizationBlock || kind == Step7BlockKind.Function ||
                   kind == Step7BlockKind.FunctionBlock || kind == Step7BlockKind.SystemFunction ||
                   kind == Step7BlockKind.SystemFunctionBlock;
        }

        private static Step7CodeBlockModel DecodeCodeBlock(
            BlocksOfflineFolder folder, ProjectPlcBlockInfo info, Step7BlockKind kind)
        {
            try
            {
                var block = folder.GetBlock(info, ConvertingOptions()) as S7FunctionBlock;
                if (block == null)
                    return new Step7CodeBlockModel(kind, info.BlockNumber, null, null,
                        "The block did not decode as a code block.");

                var parameters = ReadInterface(block.Parameter);
                // System blocks in the offline folder are interface stubs; their code is not used.
                bool keepNetworks = kind != Step7BlockKind.SystemFunction && kind != Step7BlockKind.SystemFunctionBlock;
                var networks = new List<Step7NetworkModel>();
                if (keepNetworks && block.Networks != null)
                {
                    var number = 0;
                    foreach (Network network in block.Networks)
                        networks.Add(ReadNetwork(network, ++number));
                }
                return new Step7CodeBlockModel(kind, info.BlockNumber, parameters, networks, null);
            }
            catch (Exception exception)
            {
                return new Step7CodeBlockModel(kind, info.BlockNumber, null, null, exception.Message);
            }
        }

        private static S7ConvertingOptions ConvertingOptions()
        {
            // Symbol replacement only affects rendering; operands are read in absolute form
            // (Parameter) and resolved against the DB layouts by the mappers.
            return new S7ConvertingOptions(MnemonicLanguage.English)
            {
                UseComments = true,
                CombineDBOpenAndDBAccess = true,
                ReplaceDBAccessesWithSymbolNames = true,
                ReplaceLokalDataAddressesWithSymbolNames = true,
                ReplaceDIAccessesWithSymbolNames = true,
                GenerateCallsfromUCs = true
            };
        }

        private static List<Step7InterfaceParameterModel> ReadInterface(S7DataRow root)
        {
            var parameters = new List<Step7InterfaceParameterModel>();
            if (root == null) return parameters;
            foreach (object sectionObject in root.Children)
            {
                var section = sectionObject as S7DataRow;
                if (section == null) continue;
                string sectionName = (section.Name ?? string.Empty).ToUpperInvariant();
                if (sectionName == "RETURN") sectionName = "OUT";
                if (sectionName != "IN" && sectionName != "OUT" && sectionName != "IN_OUT" && sectionName != "TEMP")
                    continue;
                foreach (object parameterObject in section.Children)
                {
                    var parameter = parameterObject as S7DataRow;
                    if (parameter == null) continue;
                    parameters.Add(new Step7InterfaceParameterModel(
                        sectionName,
                        parameter.Name,
                        parameter.DataType.ToString(),
                        parameter.DataType == S7DataRowType.UDT ? parameter.DataTypeBlockNumber : (int?)null,
                        ReadMembers(parameter),
                        parameter.BlockAddress == null ? (int?)null : parameter.BlockAddress.ByteAddress,
                        parameter.BlockAddress == null ? 0 : parameter.BlockAddress.BitAddress));
                }
            }
            return parameters;
        }

        // Member offsets inside an interface UDT are reported inconsistently by the library:
        // IN_OUT members are relative to the UDT (Drv.MO @0.0 while Drv is @34.0), IN members
        // are absolute (glbFlags.EStop @2.0 with glbFlags @2.0). Normalise to relative.
        private static List<Step7InterfaceMemberModel> ReadMembers(S7DataRow parameter)
        {
            var members = new List<Step7InterfaceMemberModel>();
            if (parameter.Children == null || parameter.Children.Count == 0) return members;
            int baseByte = 0, baseBit = 0;
            var first = parameter.Children[0] as S7DataRow;
            if (first != null && first.BlockAddress != null && parameter.BlockAddress != null &&
                first.BlockAddress.ByteAddress == parameter.BlockAddress.ByteAddress &&
                first.BlockAddress.BitAddress == parameter.BlockAddress.BitAddress)
            {
                baseByte = parameter.BlockAddress.ByteAddress;
                baseBit = parameter.BlockAddress.BitAddress;
            }
            FlattenMembers(parameter, null, baseByte * 8 + baseBit, members);
            return members;
        }

        private static void FlattenMembers(S7DataRow row, string prefix, int baseBitOffset, List<Step7InterfaceMemberModel> target)
        {
            foreach (object childObject in row.Children)
            {
                var child = childObject as S7DataRow;
                if (child == null || child.BlockAddress == null) continue;
                string path = prefix == null ? child.Name : prefix + "." + child.Name;
                int bitOffset = child.BlockAddress.ByteAddress * 8 + child.BlockAddress.BitAddress - baseBitOffset;
                if (bitOffset < 0) continue;
                target.Add(new Step7InterfaceMemberModel(path, child.DataType.ToString(), bitOffset / 8, bitOffset % 8));
                if (child.Children != null && child.Children.Count > 0 && !child.IsArray)
                    FlattenMembers(child, path, baseBitOffset, target);
            }
        }

        private static Step7NetworkModel ReadNetwork(Network network, int number)
        {
            var statements = new List<Step7StatementModel>();
            foreach (object rowObject in network.AWLCode)
            {
                var row = rowObject as S7FunctionBlockRow;
                if (row == null) continue;
                var arguments = new List<Step7CallArgumentModel>();
                if (row.CallParameter != null)
                    foreach (S7FunctionBlockParameter parameter in row.CallParameter)
                        arguments.Add(new Step7CallArgumentModel(
                            parameter.Name,
                            parameter.ParameterType.ToString(),
                            parameter.ParameterDataType.ToString(),
                            parameter.Value));
                statements.Add(new Step7StatementModel(
                    row.Label,
                    row.Command,
                    row.Parameter,
                    row.SymbolTableEntry == null ? null : row.SymbolTableEntry.Symbol,
                    row.CalledBlock,
                    row.DiName,
                    arguments,
                    PostCallStatements(row)));
            }
            return new Step7NetworkModel(number, network.Name, network.Comment, statements);
        }

        // A CALL row's combined commands hold the parameter setup, the UC/CC itself and then
        // the copies of output actuals out of local data; only the part after the UC/CC is kept.
        private static List<Step7StatementModel> PostCallStatements(S7FunctionBlockRow row)
        {
            var result = new List<Step7StatementModel>();
            if (row.CombinedCommands == null || row.CombinedCommands.Count == 0) return result;
            bool afterCall = false;
            foreach (FunctionBlockRow combined in row.CombinedCommands)
            {
                string command = (combined.Command ?? string.Empty).Trim();
                if (!afterCall)
                {
                    afterCall = string.Equals(command, "UC", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(command, "CC", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                var detail = combined as S7FunctionBlockRow;
                result.Add(new Step7StatementModel(
                    combined.Label,
                    command,
                    detail == null ? null : detail.Parameter,
                    detail == null || detail.SymbolTableEntry == null ? null : detail.SymbolTableEntry.Symbol,
                    null,
                    null,
                    null));
            }
            return result;
        }

        public void Dispose()
        {
            project.Dispose();
        }

        private Step7ProjectModel ReadStructure(string fullPath)
        {
            var stations = new List<Step7StationModel>();
            var seen = new HashSet<S7ProgrammFolder>();
            foreach (ProjectFolder child in project.ProjectStructure.SubItems.OfType<ProjectFolder>())
                Walk(child, null, null, stations, seen);

            // Programs that are not assigned to a CPU sit directly below the project.
            foreach (S7ProgrammFolder folder in project.S7ProgrammFolders)
            {
                if (seen.Add(folder))
                    programs.Add(new ProgramEntry(new Step7ProgramIdentity(folder.Name, null, null), folder));
            }

            string name = string.IsNullOrWhiteSpace(project.ProjectName)
                ? Path.GetFileNameWithoutExtension(fullPath)
                : project.ProjectName;
            return new Step7ProjectModel(name, fullPath, stations,
                programs.Select(program => program.Identity).ToList());
        }

        private void Walk(
            ProjectFolder folder,
            StationEntry station,
            string cpuName,
            List<Step7StationModel> stations,
            ISet<S7ProgrammFolder> seen)
        {
            var stationFolder = folder as StationConfigurationFolder;
            if (stationFolder != null)
            {
                var entry = new StationEntry(stationFolder.Name);
                foreach (ProjectFolder child in folder.SubItems.OfType<ProjectFolder>())
                    Walk(child, entry, null, stations, seen);
                stations.Add(new Step7StationModel(entry.Name, entry.CpuNames));
                return;
            }

            var cpuFolder = folder as CPUFolder;
            if (cpuFolder != null)
            {
                if (station != null) station.CpuNames.Add(cpuFolder.Name);
                cpuName = cpuFolder.Name;
            }

            var programFolder = folder as S7ProgrammFolder;
            if (programFolder != null && seen.Add(programFolder))
            {
                programs.Add(new ProgramEntry(
                    new Step7ProgramIdentity(programFolder.Name, station == null ? null : station.Name, cpuName),
                    programFolder));
                return;
            }

            foreach (ProjectFolder child in folder.SubItems.OfType<ProjectFolder>())
                Walk(child, station, cpuName, stations, seen);
        }

        private static List<Step7SymbolModel> ReadSymbols(
            S7ProgrammFolder folder, string source, ICollection<Step7LoadDiagnostic> diagnostics)
        {
            var symbols = new List<Step7SymbolModel>();
            ISymbolTable table = folder.SymbolTable;
            if (table == null)
            {
                diagnostics.Add(new Step7LoadDiagnostic("Warning", Step7DiagnosticCodes.SymbolTableNotFound, source,
                    "The program has no symbol table; blocks and data types keep their absolute names."));
                return symbols;
            }

            foreach (SymbolTableEntry entry in table.SymbolTableEntrys)
            {
                string operand = string.IsNullOrWhiteSpace(entry.Operand) ? entry.OperandIEC : entry.Operand;
                symbols.Add(new Step7SymbolModel(entry.Symbol, operand, entry.DataType, entry.Comment));
            }
            return symbols;
        }

        private static DecodedDataBlock DecodeDataBlock(BlocksOfflineFolder folder, ProjectPlcBlockInfo info)
        {
            try
            {
                // Actual values (not just declared start values) carry configuration such as
                // each drive's DrvType in db.Setup.Drv.
                var dataBlock = folder.GetBlock(info, new S7ConvertingOptions(MnemonicLanguage.English)
                {
                    UseDBActualValues = true
                }) as S7DataBlock;
                if (dataBlock == null)
                    return new DecodedDataBlock(info.IsInstance,
                        new Step7DataBlockModel(info.BlockNumber, null, "The block did not decode as a data block."));
                var root = dataBlock.Structure as S7DataRow;
                if (root == null)
                    return new DecodedDataBlock(dataBlock.IsInstanceDB,
                        new Step7DataBlockModel(info.BlockNumber, null, "The data block has no declaration structure."));
                return new DecodedDataBlock(dataBlock.IsInstanceDB,
                    new Step7DataBlockModel(info.BlockNumber, MapChildren(root), null));
            }
            catch (Exception exception)
            {
                return new DecodedDataBlock(info.IsInstance,
                    new Step7DataBlockModel(info.BlockNumber, null, exception.Message));
            }
        }

        private sealed class DecodedDataBlock
        {
            public DecodedDataBlock(bool isInstance, Step7DataBlockModel model)
            {
                IsInstance = isInstance;
                Model = model;
            }

            public bool IsInstance { get; }
            public Step7DataBlockModel Model { get; }
        }

        private static List<Step7DataRowModel> MapChildren(S7DataRow row)
        {
            var rows = new List<Step7DataRowModel>();
            foreach (object child in row.Children)
            {
                var dataRow = child as S7DataRow;
                if (dataRow != null) rows.Add(MapRow(dataRow));
            }
            return rows;
        }

        private static Step7DataRowModel MapRow(S7DataRow row)
        {
            bool isUdt = row.DataType == S7DataRowType.UDT;
            return new Step7DataRowModel(
                row.Name,
                row.DataType.ToString(),
                isUdt ? row.DataTypeBlockNumber : (int?)null,
                row.Comment,
                row.BlockAddress == null ? 0 : row.BlockAddress.ByteAddress,
                row.BlockAddress == null ? 0 : row.BlockAddress.BitAddress,
                row.IsArray,
                row.IsArray ? ArrayBounds(row) : null,
                row.Children == null || row.Children.Count == 0 ? ValueText(row.Value) : null,
                MapChildren(row));
        }

        private static string ValueText(object value)
        {
            if (value == null) return null;
            if (value is bool) return (bool)value ? "TRUE" : "FALSE";
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static string ArrayBounds(S7DataRow row)
        {
            if (row.ArrayStart == null || row.ArrayStop == null) return null;
            int count = Math.Min(row.ArrayStart.Count, row.ArrayStop.Count);
            var dimensions = new List<string>();
            for (int index = 0; index < count; index++)
                dimensions.Add(row.ArrayStart[index].ToString(CultureInfo.InvariantCulture) + ".." +
                               row.ArrayStop[index].ToString(CultureInfo.InvariantCulture));
            return string.Join(",", dimensions);
        }

        private static Step7BlockKind MapKind(PLCBlockType type)
        {
            switch (type)
            {
                case PLCBlockType.OB: return Step7BlockKind.OrganizationBlock;
                case PLCBlockType.FC: return Step7BlockKind.Function;
                case PLCBlockType.FB: return Step7BlockKind.FunctionBlock;
                case PLCBlockType.DB: return Step7BlockKind.DataBlock;
                case PLCBlockType.UDT: return Step7BlockKind.UserDataType;
                case PLCBlockType.SFC: return Step7BlockKind.SystemFunction;
                case PLCBlockType.SFB: return Step7BlockKind.SystemFunctionBlock;
                case PLCBlockType.VAT: return Step7BlockKind.VariableTable;
                default: return Step7BlockKind.Other;
            }
        }

        private sealed class ProgramEntry
        {
            public ProgramEntry(Step7ProgramIdentity identity, S7ProgrammFolder folder)
            {
                Identity = identity;
                Folder = folder;
            }

            public Step7ProgramIdentity Identity { get; }
            public S7ProgrammFolder Folder { get; }
        }

        private sealed class StationEntry
        {
            public StationEntry(string name) { Name = name; }
            public string Name { get; }
            public List<string> CpuNames { get; } = new List<string>();
        }
    }
}
