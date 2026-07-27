using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlcInventory
    {
        [JsonConstructor]
        public PlcInventory(
            string plcName,
            IReadOnlyList<ProgramBlockInfo> programBlocks,
            IReadOnlyList<PlcTagTableInfo> tagTables,
            IReadOnlyList<PlcDataTypeInfo> dataTypes,
            IReadOnlyList<InventoryDiagnostic> diagnostics,
            IReadOnlyList<DataBlockStructureInfo> dataBlockStructures,
            bool dataBlockStructuresIncluded,
            IReadOnlyList<BlockCallInfo> blockCalls,
            bool blockCallsIncluded,
            IReadOnlyList<ExtractedLogicAssignment> logicAssignments,
            bool logicAssignmentsIncluded)
        {
            PlcName = plcName;
            ProgramBlocks = Copy(programBlocks);
            TagTables = Copy(tagTables);
            DataTypes = Copy(dataTypes);
            Diagnostics = Copy(diagnostics);
            DataBlockStructures = CopyAndSortDataBlockStructures(dataBlockStructures);
            DataBlockStructuresIncluded = dataBlockStructuresIncluded;
            BlockCalls = CopyAndSortBlockCalls(blockCalls);
            BlockCallsIncluded = blockCallsIncluded;
            LogicAssignments = CopyAndSortLogicAssignments(logicAssignments);
            LogicAssignmentsIncluded = logicAssignmentsIncluded;
            ProgramBlockCategories = CountBlockCategories(ProgramBlocks);
        }

        public PlcInventory(
            string plcName,
            IReadOnlyList<ProgramBlockInfo> programBlocks,
            IReadOnlyList<PlcTagTableInfo> tagTables,
            IReadOnlyList<PlcDataTypeInfo> dataTypes,
            IReadOnlyList<InventoryDiagnostic> diagnostics)
            : this(plcName, programBlocks, tagTables, dataTypes, diagnostics,
                null, false, null, false, null, false)
        {
        }

        public PlcInventory(
            string plcName,
            IReadOnlyList<ProgramBlockInfo> programBlocks,
            IReadOnlyList<PlcTagTableInfo> tagTables,
            IReadOnlyList<PlcDataTypeInfo> dataTypes,
            IReadOnlyList<InventoryDiagnostic> diagnostics,
            IReadOnlyList<DataBlockStructureInfo> dataBlockStructures,
            bool dataBlockStructuresIncluded)
            : this(plcName, programBlocks, tagTables, dataTypes, diagnostics,
                dataBlockStructures, dataBlockStructuresIncluded, null, false, null, false)
        {
        }

        public PlcInventory(
            string plcName,
            IReadOnlyList<ProgramBlockInfo> programBlocks,
            IReadOnlyList<PlcTagTableInfo> tagTables,
            IReadOnlyList<PlcDataTypeInfo> dataTypes,
            IReadOnlyList<InventoryDiagnostic> diagnostics,
            IReadOnlyList<DataBlockStructureInfo> dataBlockStructures,
            bool dataBlockStructuresIncluded,
            IReadOnlyList<BlockCallInfo> blockCalls,
            bool blockCallsIncluded)
            : this(plcName, programBlocks, tagTables, dataTypes, diagnostics,
                dataBlockStructures, dataBlockStructuresIncluded, blockCalls,
                blockCallsIncluded, null, false)
        {
        }

        [JsonProperty("plcName")]
        public string PlcName { get; }

        [JsonProperty("programBlocks")]
        public IReadOnlyList<ProgramBlockInfo> ProgramBlocks { get; }

        [JsonProperty("tagTables")]
        public IReadOnlyList<PlcTagTableInfo> TagTables { get; }

        [JsonProperty("dataTypes")]
        public IReadOnlyList<PlcDataTypeInfo> DataTypes { get; }

        [JsonProperty("diagnostics")]
        public IReadOnlyList<InventoryDiagnostic> Diagnostics { get; }

        [JsonProperty("dataBlockStructures")]
        public IReadOnlyList<DataBlockStructureInfo> DataBlockStructures { get; }

        [JsonProperty("dataBlockStructuresIncluded")]
        public bool DataBlockStructuresIncluded { get; }

        [JsonProperty("blockCalls")]
        public IReadOnlyList<BlockCallInfo> BlockCalls { get; }

        [JsonProperty("blockCallsIncluded")]
        public bool BlockCallsIncluded { get; }

        [JsonProperty("logicAssignments")]
        public IReadOnlyList<ExtractedLogicAssignment> LogicAssignments { get; }

        [JsonProperty("logicAssignmentsIncluded")]
        public bool LogicAssignmentsIncluded { get; }

        public IReadOnlyList<ProgramBlockCategoryCount> ProgramBlockCategories { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return new T[0];
            }

            var copy = new T[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }

        private static IReadOnlyList<DataBlockStructureInfo> CopyAndSortDataBlockStructures(
            IReadOnlyList<DataBlockStructureInfo> source)
        {
            if (source == null || source.Count == 0)
            {
                return new DataBlockStructureInfo[0];
            }

            var copy = new List<DataBlockStructureInfo>(source);
            copy.Sort((left, right) =>
            {
                int result;
                if (left.BlockNumber.HasValue && right.BlockNumber.HasValue)
                    result = left.BlockNumber.Value.CompareTo(right.BlockNumber.Value);
                else if (left.BlockNumber.HasValue)
                    result = -1;
                else
                    result = right.BlockNumber.HasValue ? 1 : 0;
                if (result != 0) return result;

                result = StringComparer.OrdinalIgnoreCase.Compare(
                    left.GroupPath ?? string.Empty,
                    right.GroupPath ?? string.Empty);
                if (result != 0) return result;
                return StringComparer.Ordinal.Compare(
                    left.BlockName ?? string.Empty,
                    right.BlockName ?? string.Empty);
            });
            return copy.ToArray();
        }

        private static IReadOnlyList<ProgramBlockCategoryCount> CountBlockCategories(
            IReadOnlyList<ProgramBlockInfo> blocks)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (ProgramBlockInfo block in blocks)
            {
                string blockType = string.IsNullOrWhiteSpace(block.BlockType) ? "Other" : block.BlockType;
                int count;
                counts.TryGetValue(blockType, out count);
                counts[blockType] = count + 1;
            }

            var categories = new List<ProgramBlockCategoryCount>();
            foreach (KeyValuePair<string, int> count in counts)
            {
                categories.Add(new ProgramBlockCategoryCount(count.Key, count.Value));
            }

            return categories.ToArray();
        }

        private static IReadOnlyList<BlockCallInfo> CopyAndSortBlockCalls(IReadOnlyList<BlockCallInfo> source)
        {
            if (source == null || source.Count == 0) return new BlockCallInfo[0];
            var copy = new List<BlockCallInfo>(source);
            copy.Sort((left, right) =>
            {
                int result = CompareNullable(left.CallingBlockNumber, right.CallingBlockNumber);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(left.CallingBlockName ?? string.Empty, right.CallingBlockName ?? string.Empty);
                if (result != 0) return result;
                result = CompareNullable(left.NetworkNumber, right.NetworkNumber);
                if (result != 0) return result;
                result = left.CallOrdinal.CompareTo(right.CallOrdinal);
                return result != 0 ? result : StringComparer.Ordinal.Compare(left.CalledBlockName ?? string.Empty, right.CalledBlockName ?? string.Empty);
            });
            return copy.ToArray();
        }

        private static IReadOnlyList<ExtractedLogicAssignment> CopyAndSortLogicAssignments(
            IReadOnlyList<ExtractedLogicAssignment> source)
        {
            if (source == null || source.Count == 0) return new ExtractedLogicAssignment[0];
            var copy = new List<ExtractedLogicAssignment>(source);
            copy.Sort((left, right) =>
            {
                int result = CompareNullable(left.BlockNumber, right.BlockNumber);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(
                    left.BlockName ?? string.Empty, right.BlockName ?? string.Empty);
                if (result != 0) return result;
                result = CompareNullable(left.NetworkNumber, right.NetworkNumber);
                if (result != 0) return result;
                result = left.StatementOrder.CompareTo(right.StatementOrder);
                return result != 0 ? result : StringComparer.Ordinal.Compare(
                    left.DestinationExpression ?? string.Empty,
                    right.DestinationExpression ?? string.Empty);
            });
            return copy.ToArray();
        }

        private static int CompareNullable(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            return right.HasValue ? 1 : 0;
        }
    }
}
