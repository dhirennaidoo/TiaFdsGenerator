using System;

namespace TiaFds.Core
{
    internal static class EngineeringSnapshotValidator
    {
        public static void Validate(EngineeringSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new SnapshotValidationException("Snapshot JSON does not contain a root object.");
            }

            if (!string.Equals(
                    snapshot.SchemaVersion,
                    SnapshotSchema.CurrentVersion,
                    StringComparison.Ordinal))
            {
                throw new SnapshotValidationException(string.Format(
                    "Unsupported snapshot schema version '{0}'. Supported version: {1}.",
                    snapshot.SchemaVersion ?? string.Empty,
                    SnapshotSchema.CurrentVersion));
            }

            if (string.IsNullOrWhiteSpace(snapshot.GeneratorVersion))
            {
                throw new SnapshotValidationException("Snapshot generatorVersion is required.");
            }

            if (snapshot.ExportedAtUtc == default(DateTimeOffset))
            {
                throw new SnapshotValidationException("Snapshot exportedAtUtc is required.");
            }

            if (snapshot.Project == null)
            {
                throw new SnapshotValidationException("Snapshot project object is required.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.Project.Name))
            {
                throw new SnapshotValidationException("Snapshot project name is required.");
            }

            if (string.IsNullOrWhiteSpace(snapshot.Project.SourceFileName))
            {
                throw new SnapshotValidationException("Snapshot project sourceFileName is required.");
            }

            if (snapshot.Project.SelectedPlc == null)
            {
                throw new SnapshotValidationException("Snapshot selectedPlc object is required.");
            }

            if (snapshot.Project.Inventory == null)
            {
                throw new SnapshotValidationException("Snapshot inventory object is required.");
            }
        }
    }
}
