using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProjectSnapshot
    {
        [JsonConstructor]
        public ProjectSnapshot(
            string name,
            string sourceFileName,
            string sourcePath,
            PlcInfo selectedPlc,
            PlcInventory inventory)
        {
            Name = name;
            SourceFileName = sourceFileName;
            SourcePath = sourcePath;
            SelectedPlc = selectedPlc;
            Inventory = inventory;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("sourceFileName")]
        public string SourceFileName { get; }

        [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Ignore)]
        public string SourcePath { get; }

        [JsonProperty("selectedPlc")]
        public PlcInfo SelectedPlc { get; }

        [JsonProperty("inventory")]
        public PlcInventory Inventory { get; }
    }
}
