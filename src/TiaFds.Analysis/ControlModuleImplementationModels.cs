using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public enum ControlModuleImplementationStatus
    {
        Correlated,
        Unreferenced,
        MultipleCalls,
        UnresolvedParameter,
        UnsupportedCall,
        FamilyMismatch
    }

    public sealed class ControlModuleCallSite
    {
        public ControlModuleCallSite(
            string processingFunctionName, int? processingFunctionNumber, string processingVariant,
            string callingBlockName, int? callingBlockNumber, string callingBlockType,
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

    public sealed class ControlModuleImplementation
    {
        public ControlModuleImplementation(
            ControlModuleInfo declaration,
            ControlModuleImplementationStatus status,
            IReadOnlyList<ControlModuleCallSite> callSites)
        {
            Declaration = declaration;
            Status = status;
            CallSites = callSites ?? new ControlModuleCallSite[0];
        }

        public ControlModuleInfo Declaration { get; }
        public string ModuleName => Declaration.Name;
        public string ModuleFamily => Declaration.ModuleFamily;
        public string MemberPath => Declaration.MemberPath;
        public string Description => Declaration.Description;
        public ControlModuleImplementationStatus Status { get; }
        public IReadOnlyList<ControlModuleCallSite> CallSites { get; }
    }

    public sealed class ControlModuleImplementationDiagnostic
    {
        public ControlModuleImplementationDiagnostic(string severity, string code, string message, string source)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Source = source;
        }
        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string Source { get; }
    }

    public sealed class ControlModuleImplementationResult
    {
        public ControlModuleImplementationResult(
            IReadOnlyList<ControlModuleImplementation> modules,
            IReadOnlyList<ControlModuleImplementationDiagnostic> diagnostics,
            bool dataBlockStructuresAvailable,
            bool blockCallsAvailable)
        {
            var moduleList = modules == null ? new List<ControlModuleImplementation>() : new List<ControlModuleImplementation>(modules);
            moduleList.Sort(CompareModules);
            Modules = moduleList.ToArray();
            var diagnosticList = diagnostics == null ? new List<ControlModuleImplementationDiagnostic>() : new List<ControlModuleImplementationDiagnostic>(diagnostics);
            diagnosticList.Sort((left, right) =>
            {
                int value = StringComparer.Ordinal.Compare(left.Code ?? string.Empty, right.Code ?? string.Empty);
                return value != 0 ? value : StringComparer.Ordinal.Compare(left.Source ?? string.Empty, right.Source ?? string.Empty);
            });
            Diagnostics = diagnosticList.ToArray();
            DataBlockStructuresAvailable = dataBlockStructuresAvailable;
            BlockCallsAvailable = blockCallsAvailable;
        }

        public IReadOnlyList<ControlModuleImplementation> Modules { get; }
        public IReadOnlyList<ControlModuleImplementationDiagnostic> Diagnostics { get; }
        public bool DataBlockStructuresAvailable { get; }
        public bool BlockCallsAvailable { get; }

        private static int CompareModules(ControlModuleImplementation left, ControlModuleImplementation right)
        {
            int value = CompareText(left.ModuleFamily, right.ModuleFamily);
            if (value != 0) return value;
            value = CompareText(left.ModuleName, right.ModuleName);
            return value != 0 ? value : CompareText(left.MemberPath, right.MemberPath);
        }

        internal static int CompareText(string left, string right)
        {
            int value = StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
            return value != 0 ? value : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }
    }

    public sealed class ControlModuleImplementationFilter
    {
        public string ModuleFamily { get; set; }
        public string ModuleName { get; set; }
        public ControlModuleImplementationStatus? Status { get; set; }
    }
}
