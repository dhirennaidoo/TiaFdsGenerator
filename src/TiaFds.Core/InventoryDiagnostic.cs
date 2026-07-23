using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class InventoryDiagnostic
    {
        [JsonConstructor]
        public InventoryDiagnostic(string severity, string source, string message)
        {
            Severity = severity;
            Source = source;
            Message = message;
        }

        [JsonProperty("severity")]
        public string Severity { get; }

        [JsonProperty("source")]
        public string Source { get; }

        [JsonProperty("message")]
        public string Message { get; }
    }
}
