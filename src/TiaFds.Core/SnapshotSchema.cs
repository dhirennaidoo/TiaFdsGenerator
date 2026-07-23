namespace TiaFds.Core
{
    public static class SnapshotSchema
    {
        public const string CurrentVersion = "1.1";
        public const string LegacyVersion = "1.0";

        public static bool IsSupported(string version)
        {
            return string.Equals(version, CurrentVersion, System.StringComparison.Ordinal) ||
                   string.Equals(version, LegacyVersion, System.StringComparison.Ordinal);
        }
    }
}
