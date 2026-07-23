using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlcTagTableInfo
    {
        [JsonConstructor]
        public PlcTagTableInfo(string name, string groupPath, int tagCount)
        {
            Name = name;
            GroupPath = groupPath;
            TagCount = tagCount;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("groupPath")]
        public string GroupPath { get; }

        [JsonProperty("tagCount")]
        public int TagCount { get; }
    }
}
