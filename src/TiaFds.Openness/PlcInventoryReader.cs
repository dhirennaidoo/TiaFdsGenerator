using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaFds.Core;
using TiaFds.Openness.Xml;

namespace TiaFds.Openness
{
    internal sealed class PlcInventoryReader
    {
        public PlcInventory Read(
            PlcSoftware plcSoftware,
            bool includeDataBlockStructures,
            bool includeBlockCalls)
        {
            if (plcSoftware == null)
            {
                throw new ArgumentNullException(nameof(plcSoftware));
            }

            string plcName = ReadValue(
                () => plcSoftware.Name,
                "Unknown PLC",
                null,
                "PLC",
                "Name");
            var builder = new PlcInventoryBuilder(plcName);

            ReadProgramBlocks(plcSoftware, builder);
            ReadTagTables(plcSoftware, builder);
            ReadDataTypes(plcSoftware, builder);
            if (includeDataBlockStructures)
            {
                builder.MarkDataBlockStructuresIncluded();
                ReadDataBlockStructures(plcSoftware, builder);
            }
            if (includeBlockCalls)
            {
                builder.MarkBlockCallsIncluded();
                ReadBlockCalls(plcSoftware, builder);
            }

            return builder.Build();
        }

        public PlcInventory Read(PlcSoftware plcSoftware)
        {
            return Read(plcSoftware, false, false);
        }

        private static void ReadBlockCalls(PlcSoftware plcSoftware, PlcInventoryBuilder builder)
        {
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "TiaFds", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                PlcBlockSystemGroup root = plcSoftware.BlockGroup;
                string rootPath = ReadValue(() => root.Name, "Program blocks", builder, "Program blocks", "Name");
                ISet<string> knownMemberPaths = builder.DataBlockStructuresIncluded
                    ? builder.GetDataBlockMemberPaths()
                    : null;
                ReadExecutableBlockGroup(root, rootPath, temporaryDirectory, builder, knownMemberPaths);
            }
            catch (Exception exception)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Error", "CM111_BLOCK_CALL_EXTRACTION_FAILED", "Program blocks",
                    "Block-call extraction failed (" + exception.GetType().Name + ")."));
            }
            finally { TryDeleteDirectory(temporaryDirectory); }
        }

        private static void ReadExecutableBlockGroup(
            PlcBlockGroup group,
            string groupPath,
            string temporaryDirectory,
            PlcInventoryBuilder builder,
            ISet<string> knownMemberPaths)
        {
            try
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    if (block is OB || block is FC || block is FB)
                        ReadExecutableBlock(block, groupPath, temporaryDirectory, builder, knownMemberPaths);
                }
            }
            catch (Exception exception)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Error", "CM111_BLOCK_CALL_EXTRACTION_FAILED", groupPath,
                    "Executable blocks could not be enumerated (" + exception.GetType().Name + ")."));
            }

            try
            {
                foreach (PlcBlockUserGroup child in group.Groups)
                {
                    string name = ReadValue(() => child.Name, "Unknown group", builder, groupPath, "Name");
                    ReadExecutableBlockGroup(child,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, name),
                        temporaryDirectory, builder, knownMemberPaths);
                }
            }
            catch (Exception exception)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Error", "CM111_BLOCK_CALL_EXTRACTION_FAILED", groupPath,
                    "Nested executable block groups could not be enumerated (" + exception.GetType().Name + ")."));
            }
        }

        private static void ReadExecutableBlock(
            PlcBlock block,
            string groupPath,
            string temporaryDirectory,
            PlcInventoryBuilder builder,
            ISet<string> knownMemberPaths)
        {
            string name = ReadValue(() => block.Name, "Unknown executable block", builder, groupPath, "Name");
            int? number = ReadNullableInt(() => block.Number, builder, name, "Number");
            string language = ReadValue(() => block.ProgrammingLanguage.ToString(), "Unknown", builder, name, "ProgrammingLanguage");
            string blockType = ClassifyBlock(block, builder, name);
            string path = PlcInventoryBuilder.BuildGroupPath(groupPath, name);
            string exportPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                block.Export(new FileInfo(exportPath), ExportOptions.WithDefaults);
                BlockCallParseResult parsed = new BlockCallXmlParser().Parse(
                    exportPath, name, number, blockType, path, language, knownMemberPaths);
                foreach (BlockCallInfo call in parsed.Calls) builder.AddBlockCall(call);
                foreach (InventoryDiagnostic diagnostic in parsed.Diagnostics) builder.AddDiagnostic(diagnostic);
            }
            catch (Exception exception)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Error", "CM111_BLOCK_CALL_EXTRACTION_FAILED", path,
                    "Executable-block call extraction failed (" + exception.GetType().Name + ")."));
            }
            finally { TryDeleteFile(exportPath); }
        }

        private static void ReadDataBlockStructures(PlcSoftware plcSoftware, PlcInventoryBuilder builder)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "TiaFds",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                PlcBlockSystemGroup root = plcSoftware.BlockGroup;
                string rootPath = ReadValue(
                    () => root.Name,
                    "Program blocks",
                    builder,
                    "Program blocks",
                    "Name");
                ReadDataBlockGroup(root, rootPath, temporaryDirectory, builder);
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, "Program blocks", "global DB structures", exception);
            }
            finally
            {
                TryDeleteDirectory(temporaryDirectory);
            }
        }

        private static void ReadDataBlockGroup(
            PlcBlockGroup group,
            string groupPath,
            string temporaryDirectory,
            PlcInventoryBuilder builder)
        {
            try
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    var globalDb = block as GlobalDB;
                    if (globalDb != null)
                    {
                        ReadGlobalDataBlock(globalDb, groupPath, temporaryDirectory, builder);
                    }
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "global data blocks", exception);
            }

            try
            {
                foreach (PlcBlockUserGroup childGroup in group.Groups)
                {
                    string groupName = ReadValue(
                        () => childGroup.Name,
                        "Unknown group",
                        builder,
                        groupPath,
                        "Name");
                    ReadDataBlockGroup(
                        childGroup,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, groupName),
                        temporaryDirectory,
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "nested global data-block groups", exception);
            }
        }

        private static void ReadGlobalDataBlock(
            GlobalDB block,
            string groupPath,
            string temporaryDirectory,
            PlcInventoryBuilder builder)
        {
            string name = ReadValue(() => block.Name, "Unknown global DB", builder, groupPath, "Name");
            int? number = ReadNullableInt(() => block.Number, builder, name, "Number");
            string exportPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                block.Export(new FileInfo(exportPath), ExportOptions.WithDefaults);
                DataBlockStructureInfo structure = new DataBlockDeclarationXmlParser().Parse(
                    exportPath,
                    name,
                    number,
                    groupPath);
                builder.AddDataBlockStructure(structure);
            }
            catch (Exception exception)
            {
                builder.AddDataBlockStructure(new DataBlockStructureInfo(
                    name,
                    number,
                    groupPath,
                    new DataBlockMemberInfo[0],
                    new[]
                    {
                        new InventoryDiagnostic(
                            "Error",
                            PlcInventoryBuilder.BuildGroupPath(groupPath, name),
                            "Global DB declaration extraction failed (" + exception.GetType().Name + ").")
                    }));
            }
            finally
            {
                TryDeleteFile(exportPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private static void ReadProgramBlocks(PlcSoftware plcSoftware, PlcInventoryBuilder builder)
        {
            PlcBlockSystemGroup root;
            try
            {
                root = plcSoftware.BlockGroup;
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, "Program blocks", "root block group", exception);
                return;
            }

            string rootPath = ReadValue(
                () => root.Name,
                "Program blocks",
                builder,
                "Program blocks",
                "Name");
            ReadBlockGroup(root, rootPath, builder);

            try
            {
                foreach (PlcSystemBlockGroup systemGroup in root.SystemBlockGroups)
                {
                    string groupName = ReadValue(
                        () => systemGroup.Name,
                        "System blocks",
                        builder,
                        rootPath,
                        "Name");
                    ReadSystemBlockGroup(
                        systemGroup,
                        PlcInventoryBuilder.BuildGroupPath(rootPath, groupName),
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, rootPath, "system block groups", exception);
            }
        }

        private static void ReadBlockGroup(
            PlcBlockGroup group,
            string groupPath,
            PlcInventoryBuilder builder)
        {
            try
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    ReadBlock(block, groupPath, builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "blocks", exception);
            }

            try
            {
                foreach (PlcBlockUserGroup childGroup in group.Groups)
                {
                    string groupName = ReadValue(
                        () => childGroup.Name,
                        "Unknown group",
                        builder,
                        groupPath,
                        "Name");
                    ReadBlockGroup(
                        childGroup,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, groupName),
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "nested block groups", exception);
            }
        }

        private static void ReadSystemBlockGroup(
            PlcSystemBlockGroup group,
            string groupPath,
            PlcInventoryBuilder builder)
        {
            try
            {
                foreach (PlcBlock block in group.Blocks)
                {
                    ReadBlock(block, groupPath, builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "system blocks", exception);
            }

            try
            {
                foreach (PlcSystemBlockGroup childGroup in group.Groups)
                {
                    string groupName = ReadValue(
                        () => childGroup.Name,
                        "Unknown system group",
                        builder,
                        groupPath,
                        "Name");
                    ReadSystemBlockGroup(
                        childGroup,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, groupName),
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "nested system block groups", exception);
            }
        }

        private static void ReadBlock(
            PlcBlock block,
            string groupPath,
            PlcInventoryBuilder builder)
        {
            string name = ReadValue(
                () => block.Name,
                "Unknown block",
                builder,
                groupPath,
                "Name");
            string source = PlcInventoryBuilder.BuildGroupPath(groupPath, name);
            string blockType = ClassifyBlock(block, builder, source);
            int? number = ReadNullableInt(() => block.Number, builder, source, "Number");
            string language = ReadValue(
                () => block.ProgrammingLanguage.ToString(),
                "Unknown",
                builder,
                source,
                "ProgrammingLanguage");
            bool isConsistent = ReadValue(
                () => block.IsConsistent,
                false,
                builder,
                source,
                "IsConsistent");

            builder.AddProgramBlock(new ProgramBlockInfo(
                name,
                blockType,
                number,
                language,
                groupPath,
                isConsistent));
        }

        private static string ClassifyBlock(
            PlcBlock block,
            PlcInventoryBuilder builder,
            string source)
        {
            if (block is OB)
            {
                return "OrganizationBlock";
            }

            if (block is FB)
            {
                return "FunctionBlock";
            }

            if (block is FC)
            {
                return "Function";
            }

            if (block is GlobalDB)
            {
                return "GlobalDataBlock";
            }

            if (block is InstanceDB)
            {
                return "InstanceDataBlock";
            }

            if (block is ArrayDB)
            {
                return "ArrayDataBlock";
            }

            if (block is DataBlock)
            {
                return "DataBlock";
            }

            builder.AddDiagnostic(new InventoryDiagnostic(
                "Warning",
                source,
                "Block type could not be classified and was recorded as Other."));
            return "Other";
        }

        private static void ReadTagTables(PlcSoftware plcSoftware, PlcInventoryBuilder builder)
        {
            PlcTagTableSystemGroup root;
            try
            {
                root = plcSoftware.TagTableGroup;
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, "PLC tag tables", "root tag-table group", exception);
                return;
            }

            string rootPath = ReadValue(
                () => root.Name,
                "PLC tag tables",
                builder,
                "PLC tag tables",
                "Name");
            ReadTagTableGroup(root, rootPath, builder);
        }

        private static void ReadTagTableGroup(
            PlcTagTableGroup group,
            string groupPath,
            PlcInventoryBuilder builder)
        {
            try
            {
                foreach (PlcTagTable tagTable in group.TagTables)
                {
                    string name = ReadValue(
                        () => tagTable.Name,
                        "Unknown tag table",
                        builder,
                        groupPath,
                        "Name");
                    string source = PlcInventoryBuilder.BuildGroupPath(groupPath, name);
                    int tagCount = ReadValue(
                        () => tagTable.Tags.Count,
                        0,
                        builder,
                        source,
                        "TagCount");
                    builder.AddTagTable(new PlcTagTableInfo(name, groupPath, tagCount));
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "tag tables", exception);
            }

            try
            {
                foreach (PlcTagTableUserGroup childGroup in group.Groups)
                {
                    string groupName = ReadValue(
                        () => childGroup.Name,
                        "Unknown group",
                        builder,
                        groupPath,
                        "Name");
                    ReadTagTableGroup(
                        childGroup,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, groupName),
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "nested tag-table groups", exception);
            }
        }

        private static void ReadDataTypes(PlcSoftware plcSoftware, PlcInventoryBuilder builder)
        {
            PlcTypeSystemGroup root;
            try
            {
                root = plcSoftware.TypeGroup;
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, "PLC data types", "root data-type group", exception);
                return;
            }

            string rootPath = ReadValue(
                () => root.Name,
                "PLC data types",
                builder,
                "PLC data types",
                "Name");
            ReadDataTypeGroup(root, rootPath, builder);
        }

        private static void ReadDataTypeGroup(
            PlcTypeGroup group,
            string groupPath,
            PlcInventoryBuilder builder)
        {
            try
            {
                foreach (PlcType dataType in group.Types)
                {
                    string name = ReadValue(
                        () => dataType.Name,
                        "Unknown data type",
                        builder,
                        groupPath,
                        "Name");
                    builder.AddDataType(new PlcDataTypeInfo(name, groupPath));
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "PLC data types", exception);
            }

            try
            {
                foreach (PlcTypeUserGroup childGroup in group.Groups)
                {
                    string groupName = ReadValue(
                        () => childGroup.Name,
                        "Unknown group",
                        builder,
                        groupPath,
                        "Name");
                    ReadDataTypeGroup(
                        childGroup,
                        PlcInventoryBuilder.BuildGroupPath(groupPath, groupName),
                        builder);
                }
            }
            catch (Exception exception)
            {
                AddEnumerationDiagnostic(builder, groupPath, "nested data-type groups", exception);
            }
        }

        private static T ReadValue<T>(
            Func<T> read,
            T fallback,
            PlcInventoryBuilder builder,
            string source,
            string propertyName)
        {
            try
            {
                return read();
            }
            catch (Exception exception)
            {
                if (builder != null)
                {
                    builder.AddDiagnostic(new InventoryDiagnostic(
                        "Warning",
                        source,
                        string.Format(
                            "Property '{0}' could not be read ({1}).",
                            propertyName,
                            exception.GetType().Name)));
                }

                return fallback;
            }
        }

        private static int? ReadNullableInt(
            Func<int> read,
            PlcInventoryBuilder builder,
            string source,
            string propertyName)
        {
            try
            {
                return read();
            }
            catch (Exception exception)
            {
                builder.AddDiagnostic(new InventoryDiagnostic(
                    "Warning",
                    source,
                    string.Format(
                        "Property '{0}' could not be read ({1}).",
                        propertyName,
                        exception.GetType().Name)));
                return null;
            }
        }

        private static void AddEnumerationDiagnostic(
            PlcInventoryBuilder builder,
            string source,
            string itemKind,
            Exception exception)
        {
            builder.AddDiagnostic(new InventoryDiagnostic(
                "Error",
                source,
                string.Format(
                    "Could not enumerate {0} ({1}).",
                    itemKind,
                    exception.GetType().Name)));
        }
    }
}
