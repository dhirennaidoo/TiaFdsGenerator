using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    public sealed class EngineeringSnapshotJsonReader
    {
        public EngineeringSnapshot Read(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("A snapshot input path is required.", nameof(inputPath));
            }

            string fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Snapshot JSON file was not found.", fullPath);
            }

            try
            {
                string json;
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                {
                    json = reader.ReadToEnd();
                }

                EngineeringSnapshot snapshot = JsonConvert.DeserializeObject<EngineeringSnapshot>(
                    json,
                    SnapshotJsonSettings.Create());
                EngineeringSnapshotValidator.Validate(snapshot);
                return snapshot;
            }
            catch (SnapshotValidationException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new SnapshotSerializationException(
                    "Snapshot JSON is malformed: " + exception.Message,
                    exception);
            }
        }

    }
}
