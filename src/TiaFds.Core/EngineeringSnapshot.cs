using System;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class EngineeringSnapshot
    {
        [JsonConstructor]
        public EngineeringSnapshot(
            string schemaVersion,
            string generatorVersion,
            DateTimeOffset exportedAtUtc,
            ProjectSnapshot project)
        {
            SchemaVersion = schemaVersion;
            GeneratorVersion = generatorVersion;
            ExportedAtUtc = exportedAtUtc;
            Project = project;
        }

        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; }

        [JsonProperty("generatorVersion")]
        public string GeneratorVersion { get; }

        [JsonProperty("exportedAtUtc")]
        public DateTimeOffset ExportedAtUtc { get; }

        [JsonProperty("project")]
        public ProjectSnapshot Project { get; }
    }
}
