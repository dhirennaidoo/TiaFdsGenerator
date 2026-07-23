using System;
using System.Collections.Generic;
using System.Globalization;

namespace TiaFds.Core
{
    public sealed class PlcInventoryBuilder
    {
        private readonly string plcName;
        private readonly List<ProgramBlockInfo> blocks = new List<ProgramBlockInfo>();
        private readonly List<PlcTagTableInfo> tagTables = new List<PlcTagTableInfo>();
        private readonly List<PlcDataTypeInfo> dataTypes = new List<PlcDataTypeInfo>();
        private readonly List<InventoryDiagnostic> diagnostics = new List<InventoryDiagnostic>();
        private readonly List<DataBlockStructureInfo> dataBlockStructures = new List<DataBlockStructureInfo>();
        private readonly HashSet<string> blockKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> tagTableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dataTypeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> dataBlockStructureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool dataBlockStructuresIncluded;

        public PlcInventoryBuilder(string plcName)
        {
            this.plcName = plcName ?? string.Empty;
        }

        public bool AddProgramBlock(ProgramBlockInfo block)
        {
            if (block == null)
            {
                throw new ArgumentNullException(nameof(block));
            }

            string key = JoinKey(
                block.GroupPath,
                block.BlockType,
                block.Number.HasValue
                    ? block.Number.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                block.Name);

            if (!blockKeys.Add(key))
            {
                AddDuplicateDiagnostic("Program block", block.GroupPath, block.Name);
                return false;
            }

            blocks.Add(block);
            return true;
        }

        public bool AddTagTable(PlcTagTableInfo tagTable)
        {
            if (tagTable == null)
            {
                throw new ArgumentNullException(nameof(tagTable));
            }

            if (!tagTableKeys.Add(JoinKey(tagTable.GroupPath, tagTable.Name)))
            {
                AddDuplicateDiagnostic("PLC tag table", tagTable.GroupPath, tagTable.Name);
                return false;
            }

            tagTables.Add(tagTable);
            return true;
        }

        public bool AddDataType(PlcDataTypeInfo dataType)
        {
            if (dataType == null)
            {
                throw new ArgumentNullException(nameof(dataType));
            }

            if (!dataTypeKeys.Add(JoinKey(dataType.GroupPath, dataType.Name)))
            {
                AddDuplicateDiagnostic("PLC data type", dataType.GroupPath, dataType.Name);
                return false;
            }

            dataTypes.Add(dataType);
            return true;
        }

        public void AddDiagnostic(InventoryDiagnostic diagnostic)
        {
            if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        public void MarkDataBlockStructuresIncluded()
        {
            dataBlockStructuresIncluded = true;
        }

        public bool AddDataBlockStructure(DataBlockStructureInfo structure)
        {
            if (structure == null) throw new ArgumentNullException(nameof(structure));
            string key = JoinKey(
                structure.GroupPath,
                structure.BlockNumber.HasValue
                    ? structure.BlockNumber.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty,
                structure.BlockName);
            if (!dataBlockStructureKeys.Add(key))
            {
                AddDuplicateDiagnostic("Data-block structure", structure.GroupPath, structure.BlockName);
                return false;
            }

            dataBlockStructures.Add(structure);
            return true;
        }

        public PlcInventory Build()
        {
            blocks.Sort(CompareBlocks);
            tagTables.Sort(CompareTagTables);
            dataTypes.Sort(CompareDataTypes);
            diagnostics.Sort(CompareDiagnostics);
            dataBlockStructures.Sort(CompareDataBlockStructures);

            return new PlcInventory(
                plcName,
                blocks,
                tagTables,
                dataTypes,
                diagnostics,
                dataBlockStructures,
                dataBlockStructuresIncluded);
        }

        public static string BuildGroupPath(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return string.Empty;
            }

            var pathParts = new List<string>();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                string[] nestedParts = part.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string nestedPart in nestedParts)
                {
                    string trimmed = nestedPart.Trim();
                    if (trimmed.Length > 0)
                    {
                        pathParts.Add(trimmed);
                    }
                }
            }

            return string.Join("/", pathParts);
        }

        private void AddDuplicateDiagnostic(string kind, string groupPath, string name)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "Warning",
                BuildGroupPath(groupPath, name),
                kind + " duplicate ignored."));
        }

        private static int CompareBlocks(ProgramBlockInfo left, ProgramBlockInfo right)
        {
            int result = CompareText(left.GroupPath, right.GroupPath);
            if (result != 0)
            {
                return result;
            }

            result = CompareText(left.BlockType, right.BlockType);
            if (result != 0)
            {
                return result;
            }

            result = CompareNullableNumber(left.Number, right.Number);
            return result != 0 ? result : CompareText(left.Name, right.Name);
        }

        private static int CompareTagTables(PlcTagTableInfo left, PlcTagTableInfo right)
        {
            int result = CompareText(left.GroupPath, right.GroupPath);
            return result != 0 ? result : CompareText(left.Name, right.Name);
        }

        private static int CompareDataTypes(PlcDataTypeInfo left, PlcDataTypeInfo right)
        {
            int result = CompareText(left.GroupPath, right.GroupPath);
            return result != 0 ? result : CompareText(left.Name, right.Name);
        }

        private static int CompareDiagnostics(InventoryDiagnostic left, InventoryDiagnostic right)
        {
            int result = CompareText(left.Severity, right.Severity);
            if (result != 0)
            {
                return result;
            }

            result = CompareText(left.Source, right.Source);
            return result != 0 ? result : CompareText(left.Message, right.Message);
        }

        private static int CompareDataBlockStructures(DataBlockStructureInfo left, DataBlockStructureInfo right)
        {
            int result = CompareNullableNumber(left.BlockNumber, right.BlockNumber);
            if (result != 0) return result;
            result = CompareText(left.GroupPath, right.GroupPath);
            return result != 0 ? result : CompareText(left.BlockName, right.BlockName);
        }

        private static int CompareNullableNumber(int? left, int? right)
        {
            if (left.HasValue && right.HasValue)
            {
                return left.Value.CompareTo(right.Value);
            }

            if (left.HasValue)
            {
                return -1;
            }

            return right.HasValue ? 1 : 0;
        }

        private static int CompareText(string left, string right)
        {
            string leftValue = left ?? string.Empty;
            string rightValue = right ?? string.Empty;
            int result = StringComparer.OrdinalIgnoreCase.Compare(leftValue, rightValue);
            return result != 0 ? result : StringComparer.Ordinal.Compare(leftValue, rightValue);
        }

        private static string JoinKey(params string[] parts)
        {
            return string.Join("\u001f", parts ?? new string[0]);
        }
    }
}
