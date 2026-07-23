using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlcDataTypeInfo
    {
        [JsonConstructor]
        public PlcDataTypeInfo(string name, string groupPath)
        {
            Name = name;
            GroupPath = groupPath;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("groupPath")]
        public string GroupPath { get; }
    }
}
