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
            bool dataBlockStructuresIncluded)
        {
            PlcName = plcName;
            ProgramBlocks = Copy(programBlocks);
            TagTables = Copy(tagTables);
            DataTypes = Copy(dataTypes);
            Diagnostics = Copy(diagnostics);
            DataBlockStructures = CopyAndSortDataBlockStructures(dataBlockStructures);
            DataBlockStructuresIncluded = dataBlockStructuresIncluded;
            ProgramBlockCategories = CountBlockCategories(ProgramBlocks);
        }

        public PlcInventory(
            string plcName,
            IReadOnlyList<ProgramBlockInfo> programBlocks,
            IReadOnlyList<PlcTagTableInfo> tagTables,
            IReadOnlyList<PlcDataTypeInfo> dataTypes,
            IReadOnlyList<InventoryDiagnostic> diagnostics)
            : this(plcName, programBlocks, tagTables, dataTypes, diagnostics, null, false)
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
    }
}
