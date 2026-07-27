using System.Collections.Generic;
using Newtonsoft.Json;

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

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisReport
    {
        public const string CurrentSchemaVersion = "1.1";

        public AnalysisReport(
            AnalysisProjectInfo project,
            AnalysisPlcInventorySummary plcInventory,
            AnalysisReportSummary summary,
            IReadOnlyList<AnalysisFamilySummary> families,
            IReadOnlyList<AnalysisVariantSummary> processingVariants,
            IReadOnlyList<AnalysisModule> modules,
            IReadOnlyList<AnalysisDiagnosticSummary> diagnosticSummary,
            IReadOnlyList<AnalysisDiagnostic> diagnostics,
            IReadOnlyList<ManualReviewItem> manualReview,
            AnalysisBehaviourSummary behaviourSummary = null,
            IReadOnlyList<AnalysisBehaviouralCondition> behaviouralConditions = null,
            IReadOnlyList<AnalysisBehaviourManualReviewItem> behaviourManualReview = null,
            AnalysisDiagnosticCounts baselineDiagnosticCounts = null)
        {
            SchemaVersion = CurrentSchemaVersion;
            Project = project ?? new AnalysisProjectInfo(null, null);
            PlcInventory = plcInventory ?? AnalysisPlcInventorySummary.Empty;
            Summary = summary;
            DiagnosticCounts = new AnalysisDiagnosticCounts(
                summary == null ? 0 : summary.WarningCount,
                summary == null ? 0 : summary.ErrorCount);
            BaselineDiagnosticCounts = baselineDiagnosticCounts ?? DiagnosticCounts;
            Families = Copy(families);
            ProcessingVariants = Copy(processingVariants);
            Modules = Copy(modules);
            DiagnosticSummary = Copy(diagnosticSummary);
            Diagnostics = Copy(diagnostics);
            ManualReview = Copy(manualReview);
            BehaviourSummary = behaviourSummary ?? AnalysisBehaviourSummary.Empty;
            BehaviouralConditions = Copy(behaviouralConditions);
            BehaviourManualReview = Copy(behaviourManualReview);
        }

        public AnalysisReport(
            AnalysisReportSummary summary,
            IReadOnlyList<AnalysisFamilySummary> families,
            IReadOnlyList<AnalysisVariantSummary> processingVariants,
            IReadOnlyList<AnalysisModule> modules,
            IReadOnlyList<AnalysisDiagnosticSummary> diagnosticSummary,
            IReadOnlyList<AnalysisDiagnostic> diagnostics,
            IReadOnlyList<ManualReviewItem> manualReview)
            : this(null, null, summary, families, processingVariants, modules,
                diagnosticSummary, diagnostics, manualReview)
        {
        }

        [JsonProperty("schemaVersion", Order = 1)]
        public string SchemaVersion { get; }
        [JsonProperty("project", Order = 2)]
        public AnalysisProjectInfo Project { get; }
        [JsonProperty("plcInventory", Order = 3)]
        public AnalysisPlcInventorySummary PlcInventory { get; }
        [JsonProperty("moduleSummary", Order = 4)]
        public AnalysisReportSummary Summary { get; }
        [JsonProperty("familySummaries", Order = 5)]
        public IReadOnlyList<AnalysisFamilySummary> Families { get; }
        [JsonProperty("processingVariants", Order = 6)]
        public IReadOnlyList<AnalysisVariantSummary> ProcessingVariants { get; }
        [JsonProperty("diagnosticSummary", Order = 7)]
        public AnalysisDiagnosticCounts DiagnosticCounts { get; }
        [JsonProperty("preBehaviourDiagnosticSummary", Order = 8)]
        public AnalysisDiagnosticCounts BaselineDiagnosticCounts { get; }
        [JsonProperty("behaviourSummary", Order = 9)]
        public AnalysisBehaviourSummary BehaviourSummary { get; }
        [JsonProperty("behaviouralConditions", Order = 10)]
        public IReadOnlyList<AnalysisBehaviouralCondition> BehaviouralConditions { get; }
        [JsonProperty("diagnosticsByCode", Order = 11)]
        public IReadOnlyList<AnalysisDiagnosticSummary> DiagnosticSummary { get; }
        [JsonProperty("modules", Order = 12)]
        public IReadOnlyList<AnalysisModule> Modules { get; }
        [JsonProperty("diagnostics", Order = 13)]
        public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }
        [JsonProperty("manualReviewItems", Order = 14)]
        public IReadOnlyList<ManualReviewItem> ManualReview { get; }
        [JsonProperty("behaviourManualReviewItems", Order = 15)]
        public IReadOnlyList<AnalysisBehaviourManualReviewItem> BehaviourManualReview { get; }

        internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0) return new T[0];
            var copy = new T[source.Count];
            for (var index = 0; index < source.Count; index++) copy[index] = source[index];
            return copy;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisProjectInfo
    {
        public AnalysisProjectInfo(string projectName, string plcName)
        {
            ProjectName = projectName;
            PlcName = plcName;
        }

        [JsonProperty("projectName", Order = 1)]
        public string ProjectName { get; }
        [JsonProperty("plcName", Order = 2)]
        public string PlcName { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisPlcInventorySummary
    {
        internal static readonly AnalysisPlcInventorySummary Empty =
            new AnalysisPlcInventorySummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public AnalysisPlcInventorySummary(
            int programBlockCount, int functionCount, int functionBlockCount,
            int globalDataBlockCount, int instanceDataBlockCount,
            int organizationBlockCount, int tagTableCount, int plcDataTypeCount,
            int diagnosticCount, int dataBlockStructureCount)
        {
            ProgramBlockCount = programBlockCount;
            FunctionCount = functionCount;
            FunctionBlockCount = functionBlockCount;
            GlobalDataBlockCount = globalDataBlockCount;
            InstanceDataBlockCount = instanceDataBlockCount;
            OrganizationBlockCount = organizationBlockCount;
            TagTableCount = tagTableCount;
            PlcDataTypeCount = plcDataTypeCount;
            DiagnosticCount = diagnosticCount;
            DataBlockStructureCount = dataBlockStructureCount;
        }

        [JsonProperty("programBlockCount", Order = 1)]
        public int ProgramBlockCount { get; }
        [JsonProperty("functionCount", Order = 2)]
        public int FunctionCount { get; }
        [JsonProperty("functionBlockCount", Order = 3)]
        public int FunctionBlockCount { get; }
        [JsonProperty("globalDataBlockCount", Order = 4)]
        public int GlobalDataBlockCount { get; }
        [JsonProperty("instanceDataBlockCount", Order = 5)]
        public int InstanceDataBlockCount { get; }
        [JsonProperty("organizationBlockCount", Order = 6)]
        public int OrganizationBlockCount { get; }
        [JsonProperty("tagTableCount", Order = 7)]
        public int TagTableCount { get; }
        [JsonProperty("plcDataTypeCount", Order = 8)]
        public int PlcDataTypeCount { get; }
        [JsonProperty("diagnosticCount", Order = 9)]
        public int DiagnosticCount { get; }
        [JsonProperty("dataBlockStructureCount", Order = 10)]
        public int DataBlockStructureCount { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
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
        [JsonProperty("totalCount", Order = 1)]
        public int TotalModules { get; }
        [JsonProperty("correlatedCount", Order = 2)]
        public int CorrelatedModules { get; }
        [JsonProperty("unreferencedCount", Order = 3)]
        public int UnreferencedModules { get; }
        [JsonProperty("multipleCallCount", Order = 4)]
        public int MultipleCallModules { get; }
        [JsonProperty("unresolvedCount", Order = 5)]
        public int UnresolvedModules { get; }
        [JsonProperty("unsupportedCallCount", Order = 6)]
        public int UnsupportedCallModules { get; }
        [JsonProperty("familyMismatchCount", Order = 7)]
        public int FamilyMismatchModules { get; }
        [JsonIgnore]
        public int WarningCount { get; }
        [JsonIgnore]
        public int ErrorCount { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
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
        [JsonProperty("family")] public string ModuleFamily { get; }
        [JsonProperty("total")] public int Total { get; }
        [JsonProperty("correlated")] public int Correlated { get; }
        [JsonProperty("unreferenced")] public int Unreferenced { get; }
        [JsonProperty("multipleCalls")] public int MultipleCalls { get; }
        [JsonProperty("unresolved")] public int Unresolved { get; }
        [JsonProperty("unsupportedCalls")] public int UnsupportedCalls { get; }
        [JsonProperty("familyMismatch")] public int FamilyMismatch { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisVariantSummary
    {
        public AnalysisVariantSummary(string moduleFamily, string processingVariant, int count)
        {
            ModuleFamily = moduleFamily;
            ProcessingVariant = processingVariant;
            Count = count;
        }
        [JsonProperty("family")] public string ModuleFamily { get; }
        [JsonProperty("variant")] public string ProcessingVariant { get; }
        [JsonProperty("count")] public int Count { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisModule
    {
        public AnalysisModule(
            string moduleName, string moduleFamily, string description,
            string containerDbName, int? containerDbNumber, string memberPath,
            string dataTypeName, string discoveryStatus,
            AnalysisImplementationStatus implementationStatus,
            IReadOnlyList<AnalysisCallSite> callSites,
            IReadOnlyList<AnalysisBehaviouralCondition> behaviouralConditions = null)
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
            BehaviouralConditions = AnalysisReport.Copy(behaviouralConditions);
        }
        [JsonProperty("name")] public string ModuleName { get; }
        [JsonProperty("family")] public string ModuleFamily { get; }
        [JsonProperty("description")] public string Description { get; }
        [JsonProperty("containerName")] public string ContainerDbName { get; }
        [JsonProperty("containerNumber")] public int? ContainerDbNumber { get; }
        [JsonProperty("memberPath")] public string MemberPath { get; }
        [JsonProperty("dataType")] public string DataTypeName { get; }
        [JsonProperty("discoveryStatus")] public string DiscoveryStatus { get; }
        [JsonProperty("status")] public AnalysisImplementationStatus ImplementationStatus { get; }
        [JsonProperty("processingCalls")] public IReadOnlyList<AnalysisCallSite> CallSites { get; }
        [JsonProperty("startCommands")]
        public IReadOnlyList<AnalysisBehaviouralCondition> StartCommands
        {
            get { return Filter(AnalysisBehaviouralConditionKind.StartCommand); }
        }
        [JsonProperty("controlRequests")]
        public IReadOnlyList<AnalysisBehaviouralCondition> ControlRequests
        {
            get { return Filter(AnalysisBehaviouralConditionKind.ControlRequest); }
        }
        [JsonProperty("interlocks")]
        public IReadOnlyList<AnalysisBehaviouralCondition> Interlocks
        {
            get { return Filter(AnalysisBehaviouralConditionKind.Interlock); }
        }
        [JsonIgnore]
        public IReadOnlyList<AnalysisBehaviouralCondition> BehaviouralConditions { get; }

        private IReadOnlyList<AnalysisBehaviouralCondition> Filter(
            AnalysisBehaviouralConditionKind kind)
        {
            var result = new List<AnalysisBehaviouralCondition>();
            foreach (AnalysisBehaviouralCondition condition in BehaviouralConditions)
                if (condition.Kind == kind) result.Add(condition);
            return result.ToArray();
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisCallSite
    {
        public AnalysisCallSite(
            string processingFunctionName, int? processingFunctionNumber,
            string processingVariant, string callingBlockName,
            int? callingBlockNumber, string callingBlockType,
            int? networkNumber, string networkTitle, int callOrdinal,
            string inOutFormalParameterName, string inOutActualExpression)
            : this(processingFunctionName, processingFunctionNumber, processingVariant,
                callingBlockName, callingBlockNumber, callingBlockType, networkNumber,
                networkTitle, callOrdinal, inOutFormalParameterName,
                inOutActualExpression, null)
        {
        }

        public AnalysisCallSite(
            string processingFunctionName, int? processingFunctionNumber,
            string processingVariant, string callingBlockName,
            int? callingBlockNumber, string callingBlockType,
            int? networkNumber, string networkTitle, int callOrdinal,
            string inOutFormalParameterName, string inOutActualExpression,
            string resolvedMemberPath)
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
            ResolvedMemberPath = resolvedMemberPath;
        }
        [JsonProperty("processingFunction")] public string ProcessingFunctionName { get; }
        [JsonProperty("processingFunctionNumber")] public int? ProcessingFunctionNumber { get; }
        [JsonProperty("variant")] public string ProcessingVariant { get; }
        [JsonProperty("callerBlockName")] public string CallingBlockName { get; }
        [JsonProperty("callerBlockNumber")] public int? CallingBlockNumber { get; }
        [JsonProperty("callerBlockType")] public string CallingBlockType { get; }
        [JsonProperty("networkNumber")] public int? NetworkNumber { get; }
        [JsonProperty("networkTitle")] public string NetworkTitle { get; }
        [JsonProperty("callOrdinal")] public int CallOrdinal { get; }
        [JsonProperty("parameterName")] public string InOutFormalParameterName { get; }
        [JsonProperty("actualExpression")] public string InOutActualExpression { get; }
        [JsonProperty("resolvedMemberPath")] public string ResolvedMemberPath { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisDiagnostic
    {
        public AnalysisDiagnostic(string severity, string code, string source, string message)
        {
            Severity = severity;
            Code = code;
            Source = source;
            Message = message;
        }
        [JsonProperty("severity")] public string Severity { get; }
        [JsonProperty("code")] public string Code { get; }
        [JsonProperty("subject")] public string Source { get; }
        [JsonProperty("message")] public string Message { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisDiagnosticSummary
    {
        public AnalysisDiagnosticSummary(string code, string severity, int count)
        {
            Code = code;
            Severity = severity;
            Count = count;
        }
        [JsonProperty("code")] public string Code { get; }
        [JsonProperty("severity")] public string Severity { get; }
        [JsonProperty("count")] public int Count { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisDiagnosticCounts
    {
        public AnalysisDiagnosticCounts(int warningCount, int errorCount)
        {
            WarningCount = warningCount;
            ErrorCount = errorCount;
        }

        [JsonProperty("warningCount", Order = 1)]
        public int WarningCount { get; }
        [JsonProperty("errorCount", Order = 2)]
        public int ErrorCount { get; }
    }

    public enum AnalysisBehaviouralConditionKind
    {
        StartCommand,
        ControlRequest,
        Interlock
    }

    public enum AnalysisBehaviouralResolutionStatus
    {
        Complete,
        Partial,
        Unsupported,
        Unresolved,
        Ambiguous
    }

    public enum AnalysisBehaviourExpressionKind
    {
        Operand,
        Constant,
        Not,
        And,
        Or,
        Unknown
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisBehaviourSummary
    {
        internal static readonly AnalysisBehaviourSummary Empty =
            new AnalysisBehaviourSummary(0, 0, 0, 0, 0, 0, 0, 0, 0);

        public AnalysisBehaviourSummary(
            int totalConditionCount, int startCommandCount, int controlRequestCount,
            int interlockCount, int completeCount, int partialCount,
            int unsupportedCount, int unresolvedCount, int ambiguousCount)
        {
            TotalConditionCount = totalConditionCount;
            StartCommandCount = startCommandCount;
            ControlRequestCount = controlRequestCount;
            InterlockCount = interlockCount;
            CompleteCount = completeCount;
            PartialCount = partialCount;
            UnsupportedCount = unsupportedCount;
            UnresolvedCount = unresolvedCount;
            AmbiguousCount = ambiguousCount;
        }
        [JsonProperty("totalConditionCount")] public int TotalConditionCount { get; }
        [JsonProperty("startCommandCount")] public int StartCommandCount { get; }
        [JsonProperty("controlRequestCount")] public int ControlRequestCount { get; }
        [JsonProperty("interlockCount")] public int InterlockCount { get; }
        [JsonProperty("completeCount")] public int CompleteCount { get; }
        [JsonProperty("partialCount")] public int PartialCount { get; }
        [JsonProperty("unsupportedCount")] public int UnsupportedCount { get; }
        [JsonProperty("unresolvedCount")] public int UnresolvedCount { get; }
        [JsonProperty("ambiguousCount")] public int AmbiguousCount { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisBehaviourExpression
    {
        public AnalysisBehaviourExpression(
            AnalysisBehaviourExpressionKind kind, string displayText, string operand,
            string resolvedPath, bool? constantValue,
            IReadOnlyList<AnalysisBehaviourExpression> children)
        {
            Kind = kind;
            DisplayText = displayText;
            Operand = operand;
            ResolvedPath = resolvedPath;
            ConstantValue = constantValue;
            Children = AnalysisReport.Copy(children);
        }
        [JsonProperty("kind")] public AnalysisBehaviourExpressionKind Kind { get; }
        [JsonProperty("displayText")] public string DisplayText { get; }
        [JsonProperty("operand")] public string Operand { get; }
        [JsonProperty("resolvedPath")] public string ResolvedPath { get; }
        [JsonProperty("constantValue")] public bool? ConstantValue { get; }
        [JsonProperty("children")] public IReadOnlyList<AnalysisBehaviourExpression> Children { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisBehaviouralCondition
    {
        public AnalysisBehaviouralCondition(
            string moduleFamily, string moduleName, string moduleMemberPath,
            AnalysisBehaviouralConditionKind kind, string member, int? index,
            string destinationExpression, string resolvedDestinationPath,
            AnalysisBehaviourExpression expression, string sourceExpression,
            IReadOnlyList<string> sourceOperands, IReadOnlyList<string> resolvedOperandPaths,
            string description, int? blockNumber, string blockName, string blockType,
            string blockLanguage, int? networkNumber, string networkTitle,
            string networkComment, int statementOrder,
            AnalysisBehaviouralResolutionStatus resolutionStatus, int diagnosticCount)
        {
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            ModuleMemberPath = moduleMemberPath;
            Kind = kind;
            Member = member;
            Index = index;
            DestinationExpression = destinationExpression;
            ResolvedDestinationPath = resolvedDestinationPath;
            Expression = expression;
            SourceExpression = sourceExpression;
            SourceOperands = AnalysisReport.Copy(sourceOperands);
            ResolvedOperandPaths = AnalysisReport.Copy(resolvedOperandPaths);
            Description = description;
            BlockNumber = blockNumber;
            BlockName = blockName;
            BlockType = blockType;
            BlockLanguage = blockLanguage;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            NetworkComment = networkComment;
            StatementOrder = statementOrder;
            ResolutionStatus = resolutionStatus;
            DiagnosticCount = diagnosticCount;
        }
        [JsonProperty("moduleFamily")] public string ModuleFamily { get; }
        [JsonProperty("moduleName")] public string ModuleName { get; }
        [JsonProperty("moduleMemberPath")] public string ModuleMemberPath { get; }
        [JsonProperty("kind")] public AnalysisBehaviouralConditionKind Kind { get; }
        [JsonProperty("member")] public string Member { get; }
        [JsonProperty("index")] public int? Index { get; }
        [JsonProperty("destination")] public string DestinationExpression { get; }
        [JsonProperty("resolvedDestinationPath")] public string ResolvedDestinationPath { get; }
        [JsonProperty("expression")] public AnalysisBehaviourExpression Expression { get; }
        [JsonProperty("sourceExpression")] public string SourceExpression { get; }
        [JsonProperty("sourceOperands")] public IReadOnlyList<string> SourceOperands { get; }
        [JsonProperty("resolvedOperandPaths")] public IReadOnlyList<string> ResolvedOperandPaths { get; }
        [JsonProperty("description")] public string Description { get; }
        [JsonProperty("blockNumber")] public int? BlockNumber { get; }
        [JsonProperty("blockName")] public string BlockName { get; }
        [JsonProperty("blockType")] public string BlockType { get; }
        [JsonProperty("blockLanguage")] public string BlockLanguage { get; }
        [JsonProperty("networkNumber")] public int? NetworkNumber { get; }
        [JsonProperty("networkTitle")] public string NetworkTitle { get; }
        [JsonProperty("networkComment")] public string NetworkComment { get; }
        [JsonProperty("statementOrder")] public int StatementOrder { get; }
        [JsonProperty("resolutionStatus")] public AnalysisBehaviouralResolutionStatus ResolutionStatus { get; }
        [JsonProperty("diagnosticCount")] public int DiagnosticCount { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class AnalysisBehaviourManualReviewItem
    {
        public AnalysisBehaviourManualReviewItem(
            string code, string moduleFamily, string moduleName, string memberPath,
            string conditionKind, string conditionMember, int? blockNumber,
            string blockName, int? networkNumber, string expression, string reason)
        {
            Code = code;
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            MemberPath = memberPath;
            ConditionKind = conditionKind;
            ConditionMember = conditionMember;
            BlockNumber = blockNumber;
            BlockName = blockName;
            NetworkNumber = networkNumber;
            Expression = expression;
            Reason = reason;
        }
        [JsonProperty("code")] public string Code { get; }
        [JsonProperty("family")] public string ModuleFamily { get; }
        [JsonProperty("module")] public string ModuleName { get; }
        [JsonProperty("memberPath")] public string MemberPath { get; }
        [JsonProperty("conditionKind")] public string ConditionKind { get; }
        [JsonProperty("conditionMember")] public string ConditionMember { get; }
        [JsonProperty("blockNumber")] public int? BlockNumber { get; }
        [JsonProperty("blockName")] public string BlockName { get; }
        [JsonProperty("networkNumber")] public int? NetworkNumber { get; }
        [JsonProperty("expression")] public string Expression { get; }
        [JsonProperty("reason")] public string Reason { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
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
        [JsonProperty("family")] public string ModuleFamily { get; }
        [JsonProperty("module")] public string ModuleName { get; }
        [JsonProperty("memberPath")] public string MemberPath { get; }
        [JsonProperty("category")] public AnalysisImplementationStatus Status { get; }
        [JsonProperty("reason")] public string Reason { get; }
    }

    public sealed class AnalysisReportFilter
    {
        public string ModuleFamily { get; set; }
        public string ModuleName { get; set; }
        public AnalysisImplementationStatus? ImplementationStatus { get; set; }
    }
}
