using Newtonsoft.Json;

namespace TiaFds.Core
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class InventoryDiagnostic
    {
        public InventoryDiagnostic(string severity, string source, string message)
            : this(severity, null, source, message)
        {
        }

        [JsonConstructor]
        public InventoryDiagnostic(string severity, string code, string source, string message)
        {
            Severity = severity;
            Code = code;
            Source = source;
            Message = message;
        }

        [JsonProperty("severity")]
        public string Severity { get; }

        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)]
        public string Code { get; }

        [JsonProperty("source")]
        public string Source { get; }

        [JsonProperty("message")]
        public string Message { get; }
    }
}
