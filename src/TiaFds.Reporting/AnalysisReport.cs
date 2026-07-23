using System.Collections.Generic;

namespace TiaFds.Reporting
{
    public enum AnalysisImplementationStatus
    {
        Correlated,
        Unreferenced,
        MultipleCalls,
        UnresolvedParameter,
        UnsupportedCall,
        FamilyMismatch
    }

    public sealed class AnalysisReport
    {
        public AnalysisReport(
            AnalysisReportSummary summary,
            IReadOnlyList<AnalysisFamilySummary> families,
            IReadOnlyList<AnalysisVariantSummary> processingVariants,
            IReadOnlyList<AnalysisModule> modules,
            IReadOnlyList<AnalysisDiagnosticSummary> diagnosticSummary,
            IReadOnlyList<AnalysisDiagnostic> diagnostics,
            IReadOnlyList<ManualReviewItem> manualReview)
        {
            Summary = summary;
            Families = Copy(families);
            ProcessingVariants = Copy(processingVariants);
            Modules = Copy(modules);
            DiagnosticSummary = Copy(diagnosticSummary);
            Diagnostics = Copy(diagnostics);
            ManualReview = Copy(manualReview);
        }

        public AnalysisReportSummary Summary { get; }
        public IReadOnlyList<AnalysisFamilySummary> Families { get; }
        public IReadOnlyList<AnalysisVariantSummary> ProcessingVariants { get; }
        public IReadOnlyList<AnalysisModule> Modules { get; }
        public IReadOnlyList<AnalysisDiagnosticSummary> DiagnosticSummary { get; }
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
        public IReadOnlyList<ManualReviewItem> ManualReview { get; }

        internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return new T[0];
            var copy = new T[source.Count];
            for (var index = 0; index < source.Count; index++) copy[index] = source[index];
            return copy;
        }
    }

    public sealed class AnalysisReportSummary
    {
        public AnalysisReportSummary(
            int totalModules, int correlatedModules, int unreferencedModules,
            int multipleCallModules, int unresolvedModules, int unsupportedCallModules,
            int familyMismatchModules, int warningCount, int errorCount)
        {
            TotalModules = totalModules;
            CorrelatedModules = correlatedModules;
            UnreferencedModules = unreferencedModules;
            MultipleCallModules = multipleCallModules;
            UnresolvedModules = unresolvedModules;
            UnsupportedCallModules = unsupportedCallModules;
            FamilyMismatchModules = familyMismatchModules;
            WarningCount = warningCount;
            ErrorCount = errorCount;
        }
        public int TotalModules { get; }
        public int CorrelatedModules { get; }
        public int UnreferencedModules { get; }
        public int MultipleCallModules { get; }
        public int UnresolvedModules { get; }
        public int UnsupportedCallModules { get; }
        public int FamilyMismatchModules { get; }
        public int WarningCount { get; }
        public int ErrorCount { get; }
    }

    public sealed class AnalysisFamilySummary
    {
        public AnalysisFamilySummary(
            string moduleFamily, int total, int correlated, int unreferenced,
            int multipleCalls, int unresolved, int unsupportedCalls, int familyMismatch)
        {
            ModuleFamily = moduleFamily;
            Total = total;
            Correlated = correlated;
            Unreferenced = unreferenced;
            MultipleCalls = multipleCalls;
            Unresolved = unresolved;
            UnsupportedCalls = unsupportedCalls;
            FamilyMismatch = familyMismatch;
        }
        public string ModuleFamily { get; }
        public int Total { get; }
        public int Correlated { get; }
        public int Unreferenced { get; }
        public int MultipleCalls { get; }
        public int Unresolved { get; }
        public int UnsupportedCalls { get; }
        public int FamilyMismatch { get; }
    }

    public sealed class AnalysisVariantSummary
    {
        public AnalysisVariantSummary(string moduleFamily, string processingVariant, int count)
        {
            ModuleFamily = moduleFamily;
            ProcessingVariant = processingVariant;
            Count = count;
        }
        public string ModuleFamily { get; }
        public string ProcessingVariant { get; }
        public int Count { get; }
    }

    public sealed class AnalysisModule
    {
        public AnalysisModule(
            string moduleName, string moduleFamily, string description,
            string containerDbName, int? containerDbNumber, string memberPath,
            string dataTypeName, string discoveryStatus,
            AnalysisImplementationStatus implementationStatus,
            IReadOnlyList<AnalysisCallSite> callSites)
        {
            ModuleName = moduleName;
            ModuleFamily = moduleFamily;
            Description = description;
            ContainerDbName = containerDbName;
            ContainerDbNumber = containerDbNumber;
            MemberPath = memberPath;
            DataTypeName = dataTypeName;
            DiscoveryStatus = discoveryStatus;
            ImplementationStatus = implementationStatus;
            CallSites = AnalysisReport.Copy(callSites);
        }
        public string ModuleName { get; }
        public string ModuleFamily { get; }
        public string Description { get; }
        public string ContainerDbName { get; }
        public int? ContainerDbNumber { get; }
        public string MemberPath { get; }
        public string DataTypeName { get; }
        public string DiscoveryStatus { get; }
        public AnalysisImplementationStatus ImplementationStatus { get; }
        public IReadOnlyList<AnalysisCallSite> CallSites { get; }
    }

    public sealed class AnalysisCallSite
    {
        public AnalysisCallSite(
            string processingFunctionName, int? processingFunctionNumber,
            string processingVariant, string callingBlockName,
            int? callingBlockNumber, string callingBlockType,
            int? networkNumber, string networkTitle, int callOrdinal,
            string inOutFormalParameterName, string inOutActualExpression)
        {
            ProcessingFunctionName = processingFunctionName;
            ProcessingFunctionNumber = processingFunctionNumber;
            ProcessingVariant = processingVariant;
            CallingBlockName = callingBlockName;
            CallingBlockNumber = callingBlockNumber;
            CallingBlockType = callingBlockType;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            CallOrdinal = callOrdinal;
            InOutFormalParameterName = inOutFormalParameterName;
            InOutActualExpression = inOutActualExpression;
        }
        public string ProcessingFunctionName { get; }
        public int? ProcessingFunctionNumber { get; }
        public string ProcessingVariant { get; }
        public string CallingBlockName { get; }
        public int? CallingBlockNumber { get; }
        public string CallingBlockType { get; }
        public int? NetworkNumber { get; }
        public string NetworkTitle { get; }
        public int CallOrdinal { get; }
        public string InOutFormalParameterName { get; }
        public string InOutActualExpression { get; }
    }

    public sealed class AnalysisDiagnostic
    {
        public AnalysisDiagnostic(string severity, string code, string source, string message)
        {
            Severity = severity;
            Code = code;
            Source = source;
            Message = message;
        }
        public string Severity { get; }
        public string Code { get; }
        public string Source { get; }
        public string Message { get; }
    }

    public sealed class AnalysisDiagnosticSummary
    {
        public AnalysisDiagnosticSummary(string code, string severity, int count)
        {
            Code = code;
            Severity = severity;
            Count = count;
        }
        public string Code { get; }
        public string Severity { get; }
        public int Count { get; }
    }

    public sealed class ManualReviewItem
    {
        public ManualReviewItem(
            string moduleFamily, string moduleName, string memberPath,
            AnalysisImplementationStatus status, string reason)
        {
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            MemberPath = memberPath;
            Status = status;
            Reason = reason;
        }
        public string ModuleFamily { get; }
        public string ModuleName { get; }
        public string MemberPath { get; }
        public AnalysisImplementationStatus Status { get; }
        public string Reason { get; }
    }

    public sealed class AnalysisReportFilter
    {
        public string ModuleFamily { get; set; }
        public string ModuleName { get; set; }
        public AnalysisImplementationStatus? ImplementationStatus { get; set; }
    }
}
