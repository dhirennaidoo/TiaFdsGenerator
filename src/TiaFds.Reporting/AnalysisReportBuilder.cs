using System;
using System.Collections.Generic;
using TiaFds.Analysis;
using TiaFds.Core;

namespace TiaFds.Reporting
{
    public sealed class AnalysisReportBuilder
    {
        public AnalysisReport Build(
            EngineeringSnapshot snapshot,
            ControlModuleDiscoveryResult discovery,
            ControlModuleImplementationResult implementation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (discovery == null) throw new ArgumentNullException(nameof(discovery));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));

            var modules = new List<AnalysisModule>();
            foreach (ControlModuleImplementation source in implementation.Modules)
                modules.Add(CopyModule(source));
            modules.Sort(CompareModules);

            var diagnostics = new List<AnalysisDiagnostic>();
            var diagnosticCounts = new Dictionary<string, DiagnosticAccumulator>(StringComparer.Ordinal);
            var variants = new Dictionary<string, VariantAccumulator>(StringComparer.OrdinalIgnoreCase);
            int warnings = 0, errors = 0;
            foreach (ControlModuleImplementationDiagnostic source in implementation.Diagnostics)
            {
                var diagnostic = new AnalysisDiagnostic(
                    source.Severity, source.Code, source.Source, source.Message);
                diagnostics.Add(diagnostic);
                if (string.Equals(source.Severity, "Error", StringComparison.OrdinalIgnoreCase)) errors++;
                else warnings++;
                string key = (source.Code ?? string.Empty) + "\u001f" + (source.Severity ?? string.Empty);
                DiagnosticAccumulator accumulator;
                if (!diagnosticCounts.TryGetValue(key, out accumulator))
                {
                    accumulator = new DiagnosticAccumulator(source.Code, source.Severity);
                    diagnosticCounts.Add(key, accumulator);
                }
                accumulator.Count++;
            }
            diagnostics.Sort(CompareDiagnostics);

            foreach (AnalysisModule module in modules)
                foreach (AnalysisCallSite site in module.CallSites)
                {
                    if (string.IsNullOrWhiteSpace(site.ProcessingVariant)) continue;
                    string key = (module.ModuleFamily ?? string.Empty) + "\u001f" + site.ProcessingVariant;
                    VariantAccumulator accumulator;
                    if (!variants.TryGetValue(key, out accumulator))
                    {
                        accumulator = new VariantAccumulator(module.ModuleFamily, site.ProcessingVariant);
                        variants.Add(key, accumulator);
                    }
                    accumulator.Count++;
                }

            var diagnosticSummary = new List<AnalysisDiagnosticSummary>();
            foreach (DiagnosticAccumulator item in diagnosticCounts.Values)
                diagnosticSummary.Add(new AnalysisDiagnosticSummary(item.Code, item.Severity, item.Count));
            diagnosticSummary.Sort((left, right) =>
            {
                int result = CompareText(left.Code, right.Code);
                return result != 0 ? result : CompareText(left.Severity, right.Severity);
            });

            var variantSummary = new List<AnalysisVariantSummary>();
            foreach (VariantAccumulator item in variants.Values)
                variantSummary.Add(new AnalysisVariantSummary(item.Family, item.Variant, item.Count));
            variantSummary.Sort((left, right) =>
            {
                int result = CompareText(left.ModuleFamily, right.ModuleFamily);
                return result != 0 ? result : CompareText(left.ProcessingVariant, right.ProcessingVariant);
            });

            IReadOnlyList<AnalysisFamilySummary> families = BuildFamilySummaries(modules);
            var manualReview = new List<ManualReviewItem>();
            foreach (AnalysisModule module in modules)
                if (module.ImplementationStatus != AnalysisImplementationStatus.Correlated)
                    manualReview.Add(new ManualReviewItem(
                        module.ModuleFamily, module.ModuleName, module.MemberPath,
                        module.ImplementationStatus, ReviewReason(module.ImplementationStatus)));

            var summary = new AnalysisReportSummary(
                modules.Count,
                Count(modules, AnalysisImplementationStatus.Correlated),
                Count(modules, AnalysisImplementationStatus.Unreferenced),
                Count(modules, AnalysisImplementationStatus.MultipleCalls),
                Count(modules, AnalysisImplementationStatus.UnresolvedParameter),
                Count(modules, AnalysisImplementationStatus.UnsupportedCall),
                Count(modules, AnalysisImplementationStatus.FamilyMismatch),
                warnings,
                errors);

            return new AnalysisReport(
                summary, families, variantSummary.ToArray(), modules.ToArray(),
                diagnosticSummary.ToArray(), diagnostics.ToArray(), manualReview.ToArray());
        }

        private static AnalysisModule CopyModule(ControlModuleImplementation source)
        {
            var sites = new List<AnalysisCallSite>();
            foreach (ControlModuleCallSite site in source.CallSites)
                sites.Add(new AnalysisCallSite(
                    site.ProcessingFunctionName, site.ProcessingFunctionNumber,
                    site.ProcessingVariant, site.CallingBlockName, site.CallingBlockNumber,
                    site.CallingBlockType, site.NetworkNumber, site.NetworkTitle,
                    site.CallOrdinal, site.InOutFormalParameterName, site.InOutActualExpression));
            sites.Sort(CompareCallSites);
            ControlModuleInfo declaration = source.Declaration;
            return new AnalysisModule(
                declaration.Name, declaration.ModuleFamily, declaration.Description,
                declaration.ContainerDbName, declaration.ContainerDbNumber,
                declaration.MemberPath, declaration.DataTypeName,
                declaration.Status.ToString(), MapStatus(source.Status), sites.ToArray());
        }

        private static IReadOnlyList<AnalysisFamilySummary> BuildFamilySummaries(
            IReadOnlyList<AnalysisModule> modules)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedNames = new List<string>();
            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
                if (known.Add(definition.ModuleFamily)) orderedNames.Add(definition.ModuleFamily);
            var unexpected = new List<string>();
            foreach (AnalysisModule module in modules)
                if (known.Add(module.ModuleFamily ?? string.Empty)) unexpected.Add(module.ModuleFamily);
            unexpected.Sort(CompareText);
            orderedNames.AddRange(unexpected);

            var result = new List<AnalysisFamilySummary>();
            foreach (string family in orderedNames)
            {
                var familyModules = new List<AnalysisModule>();
                foreach (AnalysisModule module in modules)
                    if (string.Equals(module.ModuleFamily, family, StringComparison.OrdinalIgnoreCase))
                        familyModules.Add(module);
                result.Add(new AnalysisFamilySummary(
                    family, familyModules.Count,
                    Count(familyModules, AnalysisImplementationStatus.Correlated),
                    Count(familyModules, AnalysisImplementationStatus.Unreferenced),
                    Count(familyModules, AnalysisImplementationStatus.MultipleCalls),
                    Count(familyModules, AnalysisImplementationStatus.UnresolvedParameter),
                    Count(familyModules, AnalysisImplementationStatus.UnsupportedCall),
                    Count(familyModules, AnalysisImplementationStatus.FamilyMismatch)));
            }
            return result.ToArray();
        }

        private static int Count(
            IReadOnlyList<AnalysisModule> modules,
            AnalysisImplementationStatus status)
        {
            var count = 0;
            foreach (AnalysisModule module in modules)
                if (module.ImplementationStatus == status) count++;
            return count;
        }

        private static AnalysisImplementationStatus MapStatus(ControlModuleImplementationStatus status)
        {
            return (AnalysisImplementationStatus)Enum.Parse(
                typeof(AnalysisImplementationStatus), status.ToString(), false);
        }

        private static string ReviewReason(AnalysisImplementationStatus status)
        {
            switch (status)
            {
                case AnalysisImplementationStatus.Unreferenced:
                    return "No recognised processing call was correlated.";
                case AnalysisImplementationStatus.MultipleCalls:
                    return "More than one processing call was correlated.";
                case AnalysisImplementationStatus.UnresolvedParameter:
                    return "A processing call could not be resolved to a module.";
                case AnalysisImplementationStatus.FamilyMismatch:
                    return "The module family does not match the processing function family.";
                case AnalysisImplementationStatus.UnsupportedCall:
                    return "The processing call is not supported by the analyser.";
                default:
                    return string.Empty;
            }
        }

        private static int CompareModules(AnalysisModule left, AnalysisModule right)
        {
            int result = CompareText(left.ModuleFamily, right.ModuleFamily);
            if (result != 0) return result;
            result = CompareText(left.ModuleName, right.ModuleName);
            return result != 0 ? result : CompareText(left.MemberPath, right.MemberPath);
        }

        private static int CompareCallSites(AnalysisCallSite left, AnalysisCallSite right)
        {
            int result = CompareNullable(left.CallingBlockNumber, right.CallingBlockNumber);
            if (result != 0) return result;
            result = CompareText(left.CallingBlockName, right.CallingBlockName);
            if (result != 0) return result;
            result = CompareNullable(left.NetworkNumber, right.NetworkNumber);
            return result != 0 ? result : left.CallOrdinal.CompareTo(right.CallOrdinal);
        }

        private static int CompareDiagnostics(AnalysisDiagnostic left, AnalysisDiagnostic right)
        {
            int result = CompareText(left.Code, right.Code);
            if (result != 0) return result;
            result = CompareText(left.Severity, right.Severity);
            if (result != 0) return result;
            result = CompareText(left.Source, right.Source);
            return result != 0 ? result : CompareText(left.Message, right.Message);
        }

        private static int CompareNullable(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            return right.HasValue ? 1 : 0;
        }

        internal static int CompareText(string left, string right)
        {
            int result = StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }

        private sealed class DiagnosticAccumulator
        {
            public DiagnosticAccumulator(string code, string severity) { Code = code; Severity = severity; }
            public string Code { get; }
            public string Severity { get; }
            public int Count { get; set; }
        }

        private sealed class VariantAccumulator
        {
            public VariantAccumulator(string family, string variant) { Family = family; Variant = variant; }
            public string Family { get; }
            public string Variant { get; }
            public int Count { get; set; }
        }
    }
}
