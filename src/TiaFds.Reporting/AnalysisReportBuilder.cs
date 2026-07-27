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
            return Build(snapshot, discovery, implementation,
                new ControlModuleBehaviourResult(null, null, null, false));
        }

        public AnalysisReport Build(
            EngineeringSnapshot snapshot,
            ControlModuleDiscoveryResult discovery,
            ControlModuleImplementationResult implementation,
            ControlModuleBehaviourResult behaviour)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (discovery == null) throw new ArgumentNullException(nameof(discovery));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));

            var behaviourConditions = new List<AnalysisBehaviouralCondition>();
            foreach (BehaviouralCondition condition in behaviour.Conditions)
                behaviourConditions.Add(CopyBehaviourCondition(condition, behaviour.Diagnostics));

            var modules = new List<AnalysisModule>();
            foreach (ControlModuleImplementation source in implementation.Modules)
                modules.Add(CopyModule(source, behaviourConditions));
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
            int baselineWarnings = warnings;
            int baselineErrors = errors;
            foreach (BehaviouralDiagnostic source in behaviour.Diagnostics)
            {
                var diagnostic = new AnalysisDiagnostic(
                    source.Severity, source.Code,
                    source.ModulePath ?? source.BlockName, source.Message);
                diagnostics.Add(diagnostic);
                if (string.Equals(source.Severity, "Error",
                    StringComparison.OrdinalIgnoreCase)) errors++;
                else warnings++;
                string key = (source.Code ?? string.Empty) + "\u001f" +
                    (source.Severity ?? string.Empty);
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
                int result = AnalysisReportOrdering.CompareSeverity(left.Severity, right.Severity);
                return result != 0 ? result : AnalysisReportOrdering.CompareText(left.Code, right.Code);
            });

            var variantSummary = new List<AnalysisVariantSummary>();
            foreach (VariantAccumulator item in variants.Values)
                variantSummary.Add(new AnalysisVariantSummary(item.Family, item.Variant, item.Count));
            variantSummary.Sort((left, right) =>
            {
                int result = AnalysisReportOrdering.CompareFamily(
                    left.ModuleFamily, right.ModuleFamily);
                return result != 0
                    ? result
                    : AnalysisReportOrdering.CompareText(
                        left.ProcessingVariant, right.ProcessingVariant);
            });

            IReadOnlyList<AnalysisFamilySummary> families = BuildFamilySummaries(modules);
            var manualReview = new List<ManualReviewItem>();
            foreach (AnalysisModule module in modules)
                if (module.ImplementationStatus != AnalysisImplementationStatus.Correlated)
                    manualReview.Add(new ManualReviewItem(
                        module.ModuleFamily, module.ModuleName, module.MemberPath,
                        module.ImplementationStatus, ReviewReason(module.ImplementationStatus)));
            manualReview.Sort(CompareManualReview);
            var behaviourReview = new List<AnalysisBehaviourManualReviewItem>();
            foreach (BehaviouralManualReviewItem item in behaviour.ManualReview)
                behaviourReview.Add(new AnalysisBehaviourManualReviewItem(
                    item.Code, item.ModuleFamily, item.ModuleName, item.ModulePath,
                    item.Kind.HasValue ? item.Kind.Value.ToString() : null,
                    item.Member, item.BlockNumber, item.BlockName,
                    item.NetworkNumber, item.Expression, item.Reason));

            AnalysisBehaviourSummary behaviourSummary =
                BuildBehaviourSummary(behaviourConditions);

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
                new AnalysisProjectInfo(
                    snapshot.Project == null ? null : snapshot.Project.Name,
                    snapshot.Project == null || snapshot.Project.SelectedPlc == null
                        ? null
                        : snapshot.Project.SelectedPlc.Name),
                BuildInventorySummary(snapshot),
                summary, families, variantSummary.ToArray(), modules.ToArray(),
                diagnosticSummary.ToArray(), diagnostics.ToArray(), manualReview.ToArray(),
                behaviourSummary, behaviourConditions.ToArray(),
                behaviourReview.ToArray(),
                new AnalysisDiagnosticCounts(baselineWarnings, baselineErrors));
        }

        private static AnalysisModule CopyModule(
            ControlModuleImplementation source,
            IReadOnlyList<AnalysisBehaviouralCondition> behaviourConditions)
        {
            var sites = new List<AnalysisCallSite>();
            foreach (ControlModuleCallSite site in source.CallSites)
                sites.Add(new AnalysisCallSite(
                    site.ProcessingFunctionName, site.ProcessingFunctionNumber,
                    site.ProcessingVariant, site.CallingBlockName, site.CallingBlockNumber,
                    site.CallingBlockType, site.NetworkNumber, site.NetworkTitle,
                    site.CallOrdinal, site.InOutFormalParameterName, site.InOutActualExpression,
                    source.Declaration.MemberPath));
            sites.Sort(CompareCallSites);
            ControlModuleInfo declaration = source.Declaration;
            var moduleBehaviour = new List<AnalysisBehaviouralCondition>();
            foreach (AnalysisBehaviouralCondition condition in behaviourConditions)
                if (string.Equals(condition.ModuleMemberPath, declaration.MemberPath,
                    StringComparison.OrdinalIgnoreCase))
                    moduleBehaviour.Add(condition);
            return new AnalysisModule(
                declaration.Name, declaration.ModuleFamily, declaration.Description,
                declaration.ContainerDbName, declaration.ContainerDbNumber,
                declaration.MemberPath, declaration.DataTypeName,
                declaration.Status.ToString(), MapStatus(source.Status), sites.ToArray(),
                moduleBehaviour.ToArray());
        }

        private static AnalysisBehaviouralCondition CopyBehaviourCondition(
            BehaviouralCondition source,
            IReadOnlyList<BehaviouralDiagnostic> diagnostics)
        {
            var count = 0;
            foreach (BehaviouralDiagnostic diagnostic in diagnostics)
                if (string.Equals(diagnostic.ModulePath, source.ModuleMemberPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(diagnostic.Member, source.Member,
                        StringComparison.OrdinalIgnoreCase))
                    count++;
            return new AnalysisBehaviouralCondition(
                source.ModuleFamily, source.ModuleName, source.ModuleMemberPath,
                (AnalysisBehaviouralConditionKind)Enum.Parse(
                    typeof(AnalysisBehaviouralConditionKind), source.Kind.ToString(), false),
                source.Member, source.Index, source.DestinationExpression,
                source.ResolvedDestinationPath, CopyExpression(source.Expression),
                source.SourceExpression, source.SourceOperands, source.ResolvedOperandPaths,
                source.Description, source.BlockNumber, source.BlockName, source.BlockType,
                source.BlockLanguage, source.NetworkNumber, source.NetworkTitle,
                source.NetworkComment, source.StatementOrder,
                (AnalysisBehaviouralResolutionStatus)Enum.Parse(
                    typeof(AnalysisBehaviouralResolutionStatus),
                    source.ResolutionStatus.ToString(), false),
                count);
        }

        private static AnalysisBehaviourExpression CopyExpression(
            BehaviourExpression source)
        {
            if (source == null) return null;
            var children = new List<AnalysisBehaviourExpression>();
            foreach (BehaviourExpression child in source.Children)
                children.Add(CopyExpression(child));
            return new AnalysisBehaviourExpression(
                (AnalysisBehaviourExpressionKind)Enum.Parse(
                    typeof(AnalysisBehaviourExpressionKind), source.Kind.ToString(), false),
                source.DisplayText, source.Operand, source.ResolvedPath,
                source.ConstantValue, children.ToArray());
        }

        private static AnalysisBehaviourSummary BuildBehaviourSummary(
            IReadOnlyList<AnalysisBehaviouralCondition> conditions)
        {
            return new AnalysisBehaviourSummary(
                conditions.Count,
                Count(conditions, AnalysisBehaviouralConditionKind.StartCommand),
                Count(conditions, AnalysisBehaviouralConditionKind.ControlRequest),
                Count(conditions, AnalysisBehaviouralConditionKind.Interlock),
                Count(conditions, AnalysisBehaviouralResolutionStatus.Complete),
                Count(conditions, AnalysisBehaviouralResolutionStatus.Partial),
                Count(conditions, AnalysisBehaviouralResolutionStatus.Unsupported),
                Count(conditions, AnalysisBehaviouralResolutionStatus.Unresolved),
                Count(conditions, AnalysisBehaviouralResolutionStatus.Ambiguous));
        }

        private static int Count(
            IReadOnlyList<AnalysisBehaviouralCondition> conditions,
            AnalysisBehaviouralConditionKind kind)
        {
            var count = 0;
            foreach (AnalysisBehaviouralCondition condition in conditions)
                if (condition.Kind == kind) count++;
            return count;
        }

        private static int Count(
            IReadOnlyList<AnalysisBehaviouralCondition> conditions,
            AnalysisBehaviouralResolutionStatus status)
        {
            var count = 0;
            foreach (AnalysisBehaviouralCondition condition in conditions)
                if (condition.ResolutionStatus == status) count++;
            return count;
        }

        private static IReadOnlyList<AnalysisFamilySummary> BuildFamilySummaries(
            IReadOnlyList<AnalysisModule> modules)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedNames = new List<string>();
            foreach (string family in AnalysisReportOrdering.KnownFamilies)
                if (known.Add(family)) orderedNames.Add(family);
            var unexpected = new List<string>();
            foreach (AnalysisModule module in modules)
                if (known.Add(module.ModuleFamily ?? string.Empty)) unexpected.Add(module.ModuleFamily);
            unexpected.Sort(AnalysisReportOrdering.CompareText);
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
            int result = AnalysisReportOrdering.CompareFamily(
                left.ModuleFamily, right.ModuleFamily);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(left.MemberPath, right.MemberPath);
            return result != 0
                ? result
                : AnalysisReportOrdering.CompareText(left.ModuleName, right.ModuleName);
        }

        private static int CompareCallSites(AnalysisCallSite left, AnalysisCallSite right)
        {
            int result = AnalysisReportOrdering.CompareNullable(
                left.CallingBlockNumber, right.CallingBlockNumber);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareNullable(
                left.NetworkNumber, right.NetworkNumber);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(
                left.ProcessingFunctionName, right.ProcessingFunctionName);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(
                left.InOutFormalParameterName, right.InOutFormalParameterName);
            return result != 0 ? result : left.CallOrdinal.CompareTo(right.CallOrdinal);
        }

        private static int CompareDiagnostics(AnalysisDiagnostic left, AnalysisDiagnostic right)
        {
            int result = AnalysisReportOrdering.CompareSeverity(left.Severity, right.Severity);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(left.Code, right.Code);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(left.Source, right.Source);
            return result != 0
                ? result
                : AnalysisReportOrdering.CompareText(left.Message, right.Message);
        }

        private static int CompareManualReview(ManualReviewItem left, ManualReviewItem right)
        {
            int result = left.Status.CompareTo(right.Status);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareFamily(
                left.ModuleFamily, right.ModuleFamily);
            if (result != 0) return result;
            result = AnalysisReportOrdering.CompareText(left.MemberPath, right.MemberPath);
            return result != 0
                ? result
                : AnalysisReportOrdering.CompareText(left.ModuleName, right.ModuleName);
        }

        private static AnalysisPlcInventorySummary BuildInventorySummary(
            EngineeringSnapshot snapshot)
        {
            PlcInventory inventory = snapshot.Project == null ? null : snapshot.Project.Inventory;
            if (inventory == null) return AnalysisPlcInventorySummary.Empty;
            return new AnalysisPlcInventorySummary(
                inventory.ProgramBlocks.Count,
                CategoryCount(inventory, "Function"),
                CategoryCount(inventory, "FunctionBlock"),
                CategoryCount(inventory, "GlobalDataBlock"),
                CategoryCount(inventory, "InstanceDataBlock"),
                CategoryCount(inventory, "OrganizationBlock"),
                inventory.TagTables.Count,
                inventory.DataTypes.Count,
                inventory.Diagnostics.Count,
                inventory.DataBlockStructures.Count);
        }

        private static int CategoryCount(PlcInventory inventory, string blockType)
        {
            foreach (ProgramBlockCategoryCount category in inventory.ProgramBlockCategories)
                if (string.Equals(category.BlockType, blockType, StringComparison.OrdinalIgnoreCase))
                    return category.Count;
            return 0;
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
