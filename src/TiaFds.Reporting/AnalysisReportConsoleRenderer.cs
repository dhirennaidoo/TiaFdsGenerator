using System;
using System.Collections.Generic;
using System.IO;

namespace TiaFds.Reporting
{
    public sealed class AnalysisReportConsoleRenderer
    {
        public void PrintSummary(TextWriter writer, AnalysisReport report)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (report == null) throw new ArgumentNullException(nameof(report));
            AnalysisReportSummary summary = report.Summary;
            writer.WriteLine("Advansys engineering analysis");
            writer.WriteLine();
            writer.WriteLine("Modules:");
            writer.WriteLine("  Total:             {0,5}", summary.TotalModules);
            writer.WriteLine("  Correlated:        {0,5}", summary.CorrelatedModules);
            writer.WriteLine("  Unreferenced:      {0,5}", summary.UnreferencedModules);
            writer.WriteLine("  Multiple calls:    {0,5}", summary.MultipleCallModules);
            writer.WriteLine("  Unresolved:        {0,5}", summary.UnresolvedModules);
            writer.WriteLine("  Unsupported calls: {0,5}", summary.UnsupportedCallModules);
            writer.WriteLine("  Family mismatch:   {0,5}", summary.FamilyMismatchModules);
            writer.WriteLine();
            AnalysisBehaviourSummary behaviour = report.BehaviourSummary;
            writer.WriteLine("Behavioural conditions:");
            writer.WriteLine("  Total:             {0,5}", behaviour.TotalConditionCount);
            writer.WriteLine("  Start commands:    {0,5}", behaviour.StartCommandCount);
            writer.WriteLine("  Control requests:  {0,5}", behaviour.ControlRequestCount);
            writer.WriteLine("  Interlocks:        {0,5}", behaviour.InterlockCount);
            writer.WriteLine("  Complete:          {0,5}", behaviour.CompleteCount);
            writer.WriteLine("  Partial:           {0,5}", behaviour.PartialCount);
            writer.WriteLine("  Unsupported:       {0,5}", behaviour.UnsupportedCount);
            writer.WriteLine("  Unresolved:        {0,5}", behaviour.UnresolvedCount);
            writer.WriteLine("  Ambiguous:         {0,5}", behaviour.AmbiguousCount);
            writer.WriteLine();
            writer.WriteLine("Processing variants:");
            if (report.ProcessingVariants.Count == 0) writer.WriteLine("  None");
            foreach (AnalysisVariantSummary variant in report.ProcessingVariants)
                writer.WriteLine("  {0} / {1}: {2}", variant.ModuleFamily, variant.ProcessingVariant, variant.Count);
            writer.WriteLine();
            writer.WriteLine("Diagnostics:");
            writer.WriteLine("  Warnings:          {0,5}", summary.WarningCount);
            writer.WriteLine("  Errors:            {0,5}", summary.ErrorCount);
            writer.WriteLine();
            writer.WriteLine("Diagnostics by code:");
            if (report.DiagnosticSummary.Count == 0) writer.WriteLine("  None");
            foreach (AnalysisDiagnosticSummary diagnostic in report.DiagnosticSummary)
                writer.WriteLine("  {0} / {1}: {2}",
                    diagnostic.Code, diagnostic.Severity, diagnostic.Count);
        }

        public void PrintDetails(
            TextWriter writer,
            AnalysisReport report,
            AnalysisReportFilter filter)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (report == null) throw new ArgumentNullException(nameof(report));
            IReadOnlyList<AnalysisModule> modules = Filter(report.Modules, filter);
            writer.WriteLine();
            if (modules.Count == 0) writer.WriteLine("No modules match the selected filters.");
            string currentFamily = null;
            foreach (AnalysisModule module in modules)
            {
                if (!string.Equals(currentFamily, module.ModuleFamily, StringComparison.Ordinal))
                {
                    currentFamily = module.ModuleFamily;
                    writer.WriteLine("{0} modules", currentFamily);
                }
                writer.WriteLine();
                writer.WriteLine("{0} - {1}", module.ModuleName,
                    string.IsNullOrWhiteSpace(module.Description)
                        ? module.ImplementationStatus.ToString()
                        : module.Description);
                writer.WriteLine("  Status: {0}", module.ImplementationStatus);
                writer.WriteLine("  Container: {0}{1}", module.ContainerDbName,
                    module.ContainerDbNumber.HasValue ? " (DB" + module.ContainerDbNumber.Value + ")" : string.Empty);
                writer.WriteLine("  Datatype: {0}", module.DataTypeName);
                writer.WriteLine("  Member path: {0}", module.MemberPath);
                if (module.CallSites.Count == 0) continue;
                writer.WriteLine("  Processing FC              Variant      Caller             Network");
                foreach (AnalysisCallSite site in module.CallSites)
                {
                    string function = (site.ProcessingFunctionNumber.HasValue
                        ? "FC" + site.ProcessingFunctionNumber.Value + " " : string.Empty) +
                        site.ProcessingFunctionName;
                    string caller = (site.CallingBlockNumber.HasValue
                        ? Prefix(site.CallingBlockType) + site.CallingBlockNumber.Value + " " : string.Empty) +
                        site.CallingBlockName;
                    writer.WriteLine("  {0,-26} {1,-12} {2,-18} {3}",
                        function, site.ProcessingVariant, caller,
                        site.NetworkNumber.HasValue ? site.NetworkNumber.Value.ToString() : "-");
                    if (!string.IsNullOrWhiteSpace(site.NetworkTitle))
                        writer.WriteLine("    Network title: {0}", site.NetworkTitle);
                    writer.WriteLine("    {0} := {1}",
                        site.InOutFormalParameterName, site.InOutActualExpression);
                }
            }

            writer.WriteLine();
            writer.WriteLine("Diagnostics:");
            if (report.Diagnostics.Count == 0) writer.WriteLine("  None");
            foreach (AnalysisDiagnostic diagnostic in report.Diagnostics)
                writer.WriteLine("  {0} {1} [{2}] {3}",
                    diagnostic.Severity, diagnostic.Code, diagnostic.Source, diagnostic.Message);
        }

        private static IReadOnlyList<AnalysisModule> Filter(
            IReadOnlyList<AnalysisModule> modules,
            AnalysisReportFilter filter)
        {
            var result = new List<AnalysisModule>();
            foreach (AnalysisModule module in modules)
            {
                if (filter != null && !string.IsNullOrWhiteSpace(filter.ModuleFamily) &&
                    !string.Equals(module.ModuleFamily, filter.ModuleFamily, StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && !string.IsNullOrWhiteSpace(filter.ModuleName) &&
                    !string.Equals(module.ModuleName, filter.ModuleName, StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && filter.ImplementationStatus.HasValue &&
                    module.ImplementationStatus != filter.ImplementationStatus.Value) continue;
                result.Add(module);
            }
            return result.ToArray();
        }

        private static string Prefix(string blockType)
        {
            if (string.Equals(blockType, "Function", StringComparison.OrdinalIgnoreCase)) return "FC";
            if (string.Equals(blockType, "FunctionBlock", StringComparison.OrdinalIgnoreCase)) return "FB";
            if (string.Equals(blockType, "OrganizationBlock", StringComparison.OrdinalIgnoreCase)) return "OB";
            return string.Empty;
        }
    }
}
