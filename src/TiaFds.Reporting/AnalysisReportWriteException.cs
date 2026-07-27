using System;

namespace TiaFds.Reporting
{
    public sealed class AnalysisReportWriteException : Exception
    {
        public AnalysisReportWriteException(string outputPath, Exception innerException)
            : base("Could not write analysis report '" + outputPath + "': " +
                (innerException == null ? "Unknown error." : innerException.Message),
                innerException)
        {
            OutputPath = outputPath;
        }

        public string OutputPath { get; }
    }
}
