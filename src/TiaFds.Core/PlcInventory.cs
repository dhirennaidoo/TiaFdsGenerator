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
            IReadOnlyList<InventoryDiagnostic> diagnostics)
        {
            PlcName = plcName;
            ProgramBlocks = Copy(programBlocks);
            TagTables = Copy(tagTables);
            DataTypes = Copy(dataTypes);
            Diagnostics = Copy(diagnostics);
            ProgramBlockCategories = CountBlockCategories(ProgramBlocks);
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
