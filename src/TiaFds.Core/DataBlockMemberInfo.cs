using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DataBlockMemberInfo
    {
        [JsonConstructor]
        public DataBlockMemberInfo(
            string name,
            string memberPath,
            string dataTypeName,
            string comment,
            int nestingLevel,
            bool isArray,
            string arrayBounds,
            IReadOnlyList<DataBlockMemberInfo> children)
        {
            Name = name ?? string.Empty;
            MemberPath = memberPath ?? string.Empty;
            DataTypeName = dataTypeName ?? string.Empty;
            Comment = comment;
            NestingLevel = nestingLevel;
            IsArray = isArray;
            ArrayBounds = arrayBounds;
            Children = CopyAndSort(children);
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("memberPath")]
        public string MemberPath { get; }

        [JsonProperty("dataTypeName")]
        public string DataTypeName { get; }

        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public string Comment { get; }

        [JsonProperty("nestingLevel")]
        public int NestingLevel { get; }

        [JsonProperty("isArray")]
        public bool IsArray { get; }

        [JsonProperty("arrayBounds", NullValueHandling = NullValueHandling.Ignore)]
        public string ArrayBounds { get; }

        [JsonProperty("children")]
        public IReadOnlyList<DataBlockMemberInfo> Children { get; }

        private static IReadOnlyList<DataBlockMemberInfo> CopyAndSort(
            IReadOnlyList<DataBlockMemberInfo> source)
        {
            if (source == null || source.Count == 0)
            {
                return new DataBlockMemberInfo[0];
            }

            var copy = new List<DataBlockMemberInfo>(source);
            copy.Sort((left, right) => CompareText(left.MemberPath, right.MemberPath));
            return copy.ToArray();
        }

        private static int CompareText(string left, string right)
        {
            int result = StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }
    }
}
