using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace TiaFds.Reporting
{
    public sealed class AnalysisReportJsonWriter
    {
        private static readonly JsonSerializerSettings Settings = CreateSettings();

        public void Write(AnalysisReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("A JSON report output path is required.", nameof(outputPath));

            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string json = JsonConvert.SerializeObject(report, Settings);
                File.WriteAllText(fullPath, json + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException ||
                exception is System.Security.SecurityException ||
                exception is JsonException)
            {
                throw new AnalysisReportWriteException(outputPath, exception);
            }
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Error
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }
}
