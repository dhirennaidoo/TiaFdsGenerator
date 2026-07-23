using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProgramBlockInfo
    {
        [JsonConstructor]
        public ProgramBlockInfo(
            string name,
            string blockType,
            int? number,
            string programmingLanguage,
            string groupPath,
            bool isConsistent)
        {
            Name = name;
            BlockType = blockType;
            Number = number;
            ProgrammingLanguage = programmingLanguage;
            GroupPath = groupPath;
            IsConsistent = isConsistent;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("blockType")]
        public string BlockType { get; }

        [JsonProperty("number")]
        public int? Number { get; }

        [JsonProperty("programmingLanguage")]
        public string ProgrammingLanguage { get; }

        [JsonProperty("groupPath")]
        public string GroupPath { get; }

        [JsonProperty("isConsistent")]
        public bool IsConsistent { get; }
    }
}
