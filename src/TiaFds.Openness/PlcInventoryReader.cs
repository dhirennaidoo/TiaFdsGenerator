using System;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaFds.Core;

namespace TiaFds.Openness
{
    internal sealed class PlcInventoryReader
    {
        public PlcInventory Read(PlcSoftware plcSoftware)
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

            return builder.Build();
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
