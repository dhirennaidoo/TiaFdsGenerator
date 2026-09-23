using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProjectSnapshot
    {
        public ProjectSnapshot(
            string name,
            string sourceFileName,
            string sourcePath,
            PlcInfo selectedPlc,
            PlcInventory inventory)
            : this(name, sourceFileName, sourcePath, null, selectedPlc, inventory)
        {
        }

        [JsonConstructor]
        public ProjectSnapshot(
            string name,
            string sourceFileName,
            string sourcePath,
            string sourceKind,
            PlcInfo selectedPlc,
            PlcInventory inventory)
        {
            Name = name;
            SourceFileName = sourceFileName;
            SourcePath = sourcePath;
            SourceKind = string.IsNullOrWhiteSpace(sourceKind) ? SnapshotSourceKind.TiaPortal : sourceKind;
            SelectedPlc = selectedPlc;
            Inventory = inventory;
        }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("sourceFileName")]
        public string SourceFileName { get; }

        [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Ignore)]
        public string SourcePath { get; }

        // Snapshots written before schema 1.4 have no sourceKind and are TIA Portal exports.
        [JsonProperty("sourceKind")]
        public string SourceKind { get; }

        [JsonProperty("selectedPlc")]
        public PlcInfo SelectedPlc { get; }

        [JsonProperty("inventory")]
        public PlcInventory Inventory { get; }
    }
}
