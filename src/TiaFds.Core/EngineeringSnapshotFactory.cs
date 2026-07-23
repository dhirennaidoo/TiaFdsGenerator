using System;
using System.IO;

namespace TiaFds.Core
{
    public sealed class EngineeringSnapshotFactory
    {
        public EngineeringSnapshot Create(
            TiaProjectSummary project,
            PlcInfo selectedPlc,
            PlcInventory inventory,
            string sourceInput,
            bool includeSourcePath,
            DateTimeOffset exportedAtUtc)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (selectedPlc == null)
            {
                throw new ArgumentNullException(nameof(selectedPlc));
            }

            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (string.IsNullOrWhiteSpace(sourceInput))
            {
                throw new ArgumentException("A source input path is required.", nameof(sourceInput));
            }

            string fullSourcePath = Path.GetFullPath(sourceInput);
            var projectSnapshot = new ProjectSnapshot(
                project.Name,
                Path.GetFileName(fullSourcePath),
                includeSourcePath ? fullSourcePath : null,
                selectedPlc,
                inventory);

            return new EngineeringSnapshot(
                SnapshotSchema.CurrentVersion,
                ProductVersion.Current,
                exportedAtUtc.ToUniversalTime(),
                projectSnapshot);
        }
    }
}
