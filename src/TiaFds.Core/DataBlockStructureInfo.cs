using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DataBlockStructureInfo
    {
        [JsonConstructor]
        public DataBlockStructureInfo(
            string blockName,
            int? blockNumber,
            string groupPath,
            IReadOnlyList<DataBlockMemberInfo> members,
            IReadOnlyList<InventoryDiagnostic> diagnostics)
        {
            BlockName = blockName ?? string.Empty;
            BlockNumber = blockNumber;
            GroupPath = groupPath ?? string.Empty;
            Members = CopyMembers(members);
            Diagnostics = CopyDiagnostics(diagnostics);
        }

        [JsonProperty("blockName")]
        public string BlockName { get; }

        [JsonProperty("blockNumber")]
        public int? BlockNumber { get; }

        [JsonProperty("groupPath")]
        public string GroupPath { get; }

        [JsonProperty("members")]
        public IReadOnlyList<DataBlockMemberInfo> Members { get; }

        [JsonProperty("diagnostics")]
        public IReadOnlyList<InventoryDiagnostic> Diagnostics { get; }

        private static IReadOnlyList<DataBlockMemberInfo> CopyMembers(IReadOnlyList<DataBlockMemberInfo> source)
        {
            var copy = source == null ? new List<DataBlockMemberInfo>() : new List<DataBlockMemberInfo>(source);
            copy.Sort((left, right) => CompareText(left.MemberPath, right.MemberPath));
            return copy.ToArray();
        }

        private static IReadOnlyList<InventoryDiagnostic> CopyDiagnostics(IReadOnlyList<InventoryDiagnostic> source)
        {
            var copy = source == null ? new List<InventoryDiagnostic>() : new List<InventoryDiagnostic>(source);
            copy.Sort((left, right) =>
            {
                int result = CompareText(left.Severity, right.Severity);
                if (result != 0) return result;
                result = CompareText(left.Source, right.Source);
                return result != 0 ? result : CompareText(left.Message, right.Message);
            });
            return copy.ToArray();
        }

        private static int CompareText(string left, string right)
        {
            int result = StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }
    }
}
