using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlcInfo
    {
        [JsonConstructor]
        public PlcInfo(string name, string deviceName, string deviceItemName)
        {
            Name = name;
            DeviceName = deviceName;
            DeviceItemName = deviceItemName;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("deviceName")]
        public string DeviceName { get; }

        [JsonProperty("deviceItemName")]
        public string DeviceItemName { get; }
    }
}
