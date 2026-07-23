using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace TiaFds.Core
{
    public sealed class EngineeringSnapshotJsonWriter
    {
        public void Write(EngineeringSnapshot snapshot, string outputPath, bool overwrite)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("A snapshot output path is required.", nameof(outputPath));
            }

            EngineeringSnapshotValidator.Validate(snapshot);

            string destination = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("The snapshot output path must include a file name.", nameof(outputPath));
            }

            Directory.CreateDirectory(directory);
            if (File.Exists(destination) && !overwrite)
            {
                throw new SnapshotFileExistsException(destination);
            }

            string temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteTemporaryFile(snapshot, temporaryPath);
                CommitTemporaryFile(temporaryPath, destination, overwrite);
            }
            catch (SnapshotFileExistsException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new SnapshotSerializationException(
                    "Snapshot could not be written: " + exception.Message,
                    exception);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void WriteTemporaryFile(EngineeringSnapshot snapshot, string temporaryPath)
        {
            string json = JsonConvert.SerializeObject(snapshot, SnapshotJsonSettings.Create());
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }
        }

        private static void CommitTemporaryFile(
            string temporaryPath,
            string destination,
            bool overwrite)
        {
            if (!File.Exists(destination))
            {
                File.Move(temporaryPath, destination);
                return;
            }

            if (!overwrite)
            {
                throw new SnapshotFileExistsException(destination);
            }

            string backupPath = destination + "." + Guid.NewGuid().ToString("N") + ".bak";
            File.Move(destination, backupPath);
            try
            {
                File.Move(temporaryPath, destination);
                TryDelete(backupPath);
            }
            catch
            {
                if (!File.Exists(destination) && File.Exists(backupPath))
                {
                    File.Move(backupPath, destination);
                }

                throw;
            }
            finally
            {
                TryDelete(backupPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Preserve the original write failure when best-effort cleanup cannot complete.
            }
        }
    }
}
