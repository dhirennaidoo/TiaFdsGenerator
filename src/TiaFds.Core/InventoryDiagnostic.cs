namespace TiaFds.Core
{
    public sealed class InventoryDiagnostic
    {
        public InventoryDiagnostic(string severity, string source, string message)
        {
            Severity = severity;
            Source = source;
            Message = message;
        }

        public string Severity { get; }

        public string Source { get; }

        public string Message { get; }
    }
}
