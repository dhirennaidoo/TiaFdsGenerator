using System;
using System.Collections.Generic;
using System.IO;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleImplementationConsoleRenderer
    {
        public void PrintSummary(TextWriter writer, ControlModuleImplementationResult result)
        {
            writer.WriteLine("Advansys control-module implementation analysis");
            writer.WriteLine();
            writer.WriteLine("Modules:");
            writer.WriteLine("  Total:             {0,5}", result.Modules.Count);
            PrintCount(writer, "Correlated:", result, ControlModuleImplementationStatus.Correlated);
            PrintCount(writer, "Unreferenced:", result, ControlModuleImplementationStatus.Unreferenced);
            PrintCount(writer, "Multiple calls:", result, ControlModuleImplementationStatus.MultipleCalls);
            PrintCount(writer, "Unresolved:", result, ControlModuleImplementationStatus.UnresolvedParameter);
            PrintCount(writer, "Family mismatch:", result, ControlModuleImplementationStatus.FamilyMismatch);
            writer.WriteLine();
            writer.WriteLine("Processing variants:");
            var variants = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (ControlModuleImplementation module in result.Modules)
                foreach (ControlModuleCallSite site in module.CallSites)
                {
                    string key = module.ModuleFamily + " / " + site.ProcessingVariant;
                    int count;
                    variants.TryGetValue(key, out count);
                    variants[key] = count + 1;
                }
            if (variants.Count == 0) writer.WriteLine("  None");
            foreach (KeyValuePair<string, int> item in variants) writer.WriteLine("  {0}: {1}", item.Key, item.Value);
            writer.WriteLine();
            int warnings = 0, errors = 0;
            foreach (ControlModuleImplementationDiagnostic diagnostic in result.Diagnostics)
                if (string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase)) errors++;
                else warnings++;
            writer.WriteLine("Diagnostics:");
            writer.WriteLine("  Warnings:          {0,5}", warnings);
            writer.WriteLine("  Errors:            {0,5}", errors);
        }

        public void PrintDetails(
            TextWriter writer,
            ControlModuleImplementationResult result,
            ControlModuleImplementationFilter filter)
        {
            var modules = Filter(result.Modules, filter);
            writer.WriteLine();
            if (modules.Count == 0) { writer.WriteLine("No modules match the selected filters."); return; }
            string currentFamily = null;
            foreach (ControlModuleImplementation module in modules)
            {
                if (!string.Equals(currentFamily, module.ModuleFamily, StringComparison.Ordinal))
                {
                    currentFamily = module.ModuleFamily;
                    writer.WriteLine("{0} modules", currentFamily);
                }
                writer.WriteLine();
                writer.WriteLine("{0} — {1}", module.ModuleName,
                    string.IsNullOrEmpty(module.Description) ? module.Status.ToString() : module.Description);
                writer.WriteLine("  Status: {0}", module.Status);
                writer.WriteLine("  Member path: {0}", module.MemberPath);
                if (module.CallSites.Count == 0) continue;
                writer.WriteLine("  Processing FC              Variant      Caller             Network");
                foreach (ControlModuleCallSite site in module.CallSites)
                {
                    string fc = (site.ProcessingFunctionNumber.HasValue
                        ? "FC" + site.ProcessingFunctionNumber.Value + " " : string.Empty) +
                        site.ProcessingFunctionName;
                    string caller = (site.CallingBlockNumber.HasValue
                        ? BlockPrefix(site.CallingBlockType) + site.CallingBlockNumber.Value + " " : string.Empty) +
                        site.CallingBlockName;
                    string network = site.NetworkNumber.HasValue ? site.NetworkNumber.Value.ToString() : "-";
                    writer.WriteLine("  {0,-26} {1,-12} {2,-18} {3}", fc, site.ProcessingVariant, caller, network);
                    if (!string.IsNullOrWhiteSpace(site.NetworkTitle))
                        writer.WriteLine("    Network title: {0}", site.NetworkTitle);
                    writer.WriteLine("    {0} := {1}", site.InOutFormalParameterName, site.InOutActualExpression);
                }
            }
        }

        private static IReadOnlyList<ControlModuleImplementation> Filter(
            IReadOnlyList<ControlModuleImplementation> source,
            ControlModuleImplementationFilter filter)
        {
            var result = new List<ControlModuleImplementation>();
            foreach (ControlModuleImplementation item in source)
            {
                if (filter != null && !string.IsNullOrWhiteSpace(filter.ModuleFamily) &&
                    !string.Equals(item.ModuleFamily, filter.ModuleFamily, StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && !string.IsNullOrWhiteSpace(filter.ModuleName) &&
                    !string.Equals(item.ModuleName, filter.ModuleName, StringComparison.OrdinalIgnoreCase)) continue;
                if (filter != null && filter.Status.HasValue && item.Status != filter.Status.Value) continue;
                result.Add(item);
            }
            return result;
        }

        private static void PrintCount(TextWriter writer, string label,
            ControlModuleImplementationResult result, ControlModuleImplementationStatus status)
        {
            int count = 0;
            foreach (ControlModuleImplementation item in result.Modules) if (item.Status == status) count++;
            writer.WriteLine("  {0,-18} {1,5}", label, count);
        }

        private static string BlockPrefix(string type)
        {
            if (string.Equals(type, "Function", StringComparison.OrdinalIgnoreCase)) return "FC";
            if (string.Equals(type, "FunctionBlock", StringComparison.OrdinalIgnoreCase)) return "FB";
            if (string.Equals(type, "OrganizationBlock", StringComparison.OrdinalIgnoreCase)) return "OB";
            return string.Empty;
        }
    }
}
