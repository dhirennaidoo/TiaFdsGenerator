using System;
using System.Collections.Generic;
using TiaFds.Core;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleCallAnalyzer
    {
        public ControlModuleImplementationResult Analyze(
            EngineeringSnapshot snapshot,
            ControlModuleDiscoveryResult discovery)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (discovery == null) throw new ArgumentNullException(nameof(discovery));
            PlcInventory inventory = snapshot.Project.Inventory;
            var diagnostics = new List<ControlModuleImplementationDiagnostic>();
            var callsByPath = new Dictionary<string, List<ControlModuleCallSite>>(StringComparer.OrdinalIgnoreCase);
            var mismatchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unresolvedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateSites = new HashSet<string>(StringComparer.Ordinal);
            var modulesByPath = new Dictionary<string, ControlModuleInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (ControlModuleInfo module in discovery.Modules) modulesByPath[module.MemberPath] = module;

            if (!inventory.DataBlockStructuresIncluded)
                diagnostics.Add(Diagnostic("Error", "CM101_DB_STRUCTURES_NOT_EXTRACTED", "PLC",
                    "Data-block structures were not included; re-export with --include-db-structures."));
            if (!inventory.BlockCallsIncluded)
                diagnostics.Add(Diagnostic("Error", "CM100_BLOCK_CALLS_NOT_EXTRACTED", "PLC",
                    "Block calls were not included; re-export with --include-block-calls."));
            foreach (InventoryDiagnostic extraction in inventory.Diagnostics)
                if (!string.IsNullOrWhiteSpace(extraction.Code) &&
                    extraction.Code.StartsWith("CM1", StringComparison.Ordinal))
                    diagnostics.Add(Diagnostic(extraction.Severity, extraction.Code,
                        extraction.Source, extraction.Message));

            foreach (BlockCallInfo call in inventory.BlockCalls)
            {
                foreach (InventoryDiagnostic extraction in call.Diagnostics)
                    diagnostics.Add(Diagnostic(extraction.Severity, extraction.Code ?? "CM111_BLOCK_CALL_EXTRACTION_FAILED",
                        call.CallingBlockPath, extraction.Message));

                ControlModuleFunctionDefinition definition =
                    ControlModuleFunctionCatalogue.FindByFunctionName(call.CalledBlockName);
                if (definition == null)
                {
                    if (!string.IsNullOrWhiteSpace(call.CalledBlockName) &&
                        call.CalledBlockName.StartsWith("cm.", StringComparison.OrdinalIgnoreCase) &&
                        call.CalledBlockName.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0)
                        diagnostics.Add(Diagnostic("Warning", "CM109_UNRECOGNISED_CONTROL_MODULE_FC",
                            CallSource(call), "No catalogue definition exists for '" + call.CalledBlockName + "'."));
                    continue;
                }

                if (definition.ExpectedFunctionNumber.HasValue && call.CalledBlockNumber.HasValue &&
                    definition.ExpectedFunctionNumber.Value != call.CalledBlockNumber.Value)
                    diagnostics.Add(Diagnostic("Warning", "CM113_FUNCTION_NUMBER_MISMATCH", call.CalledBlockName,
                        string.Format("Expected FC{0}, but the snapshot contains FC{1}.",
                            definition.ExpectedFunctionNumber.Value, call.CalledBlockNumber.Value)));

                string reasonCode;
                CallParameterInfo parameter = SelectModuleParameter(call, definition, modulesByPath, out reasonCode);
                if (parameter == null)
                {
                    diagnostics.Add(Diagnostic("Warning", reasonCode, CallSource(call),
                        reasonCode == "CM103_AMBIGUOUS_INOUT_PARAMETER"
                            ? "More than one InOut parameter could represent the control module."
                            : "The recognised control-module FC has no uniquely identifiable module InOut parameter."));
                    continue;
                }

                string path = parameter.ResolvedMemberPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    SymbolPathNormalizationResult normalized =
                        new PlcSymbolPathNormalizer().Normalize(parameter.ActualExpression);
                    path = normalized.IsSymbolicMemberPath ? normalized.NormalizedPath : null;
                }
                if (string.IsNullOrWhiteSpace(path))
                {
                    unresolvedFamilies.Add(definition.ModuleFamily);
                    diagnostics.Add(Diagnostic("Warning", "CM104_ACTUAL_PARAMETER_NOT_RESOLVED", CallSource(call),
                        string.IsNullOrWhiteSpace(parameter.ActualExpression)
                            ? "The recognised module InOut parameter exists, but its connected actual operand was not extracted."
                            : "The module actual parameter could not be resolved: " + parameter.ActualExpression));
                    continue;
                }

                ControlModuleInfo module;
                if (!modulesByPath.TryGetValue(path, out module))
                {
                    diagnostics.Add(Diagnostic("Warning", "CM105_MODULE_PATH_NOT_FOUND", CallSource(call),
                        "No discovered control module matches member path '" + path + "'."));
                    continue;
                }

                var site = new ControlModuleCallSite(
                    call.CalledBlockName, call.CalledBlockNumber, definition.VariantName,
                    call.CallingBlockName, call.CallingBlockNumber, call.CallingBlockType,
                    call.NetworkNumber, call.NetworkTitle, call.CallOrdinal,
                    parameter.FormalName, parameter.ActualExpression);
                string siteKey = SiteKey(module.MemberPath, call);
                if (!duplicateSites.Add(siteKey))
                {
                    diagnostics.Add(Diagnostic("Warning", "CM112_DUPLICATE_CALL_SITE", CallSource(call),
                        "An exact duplicate call-site record was ignored."));
                    continue;
                }

                List<ControlModuleCallSite> sites;
                if (!callsByPath.TryGetValue(module.MemberPath, out sites))
                {
                    sites = new List<ControlModuleCallSite>();
                    callsByPath.Add(module.MemberPath, sites);
                }
                sites.Add(site);

                if (!string.Equals(module.ModuleFamily, definition.ModuleFamily, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchPaths.Add(module.MemberPath);
                    diagnostics.Add(Diagnostic("Warning", "CM106_MODULE_FAMILY_MISMATCH", module.MemberPath,
                        "Module family '" + module.ModuleFamily + "' is connected to a '" +
                        definition.ModuleFamily + "' processing FC."));
                }
                if (!string.IsNullOrWhiteSpace(parameter.FormalDataType) &&
                    !string.Equals(parameter.FormalDataType, definition.ExpectedModuleDataType, StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(Diagnostic("Warning", "CM114_FORMAL_DATATYPE_MISMATCH", CallSource(call),
                        "Module formal datatype '" + parameter.FormalDataType +
                        "' does not match expected datatype '" + definition.ExpectedModuleDataType + "'."));
            }

            var implementations = new List<ControlModuleImplementation>();
            foreach (ControlModuleInfo module in discovery.Modules)
            {
                List<ControlModuleCallSite> sites;
                callsByPath.TryGetValue(module.MemberPath, out sites);
                sites = sites ?? new List<ControlModuleCallSite>();
                sites.Sort(CompareSites);
                ControlModuleImplementationStatus status;
                if (mismatchPaths.Contains(module.MemberPath)) status = ControlModuleImplementationStatus.FamilyMismatch;
                else if (sites.Count > 1)
                {
                    status = ControlModuleImplementationStatus.MultipleCalls;
                    diagnostics.Add(Diagnostic("Warning", "CM108_MODULE_CALLED_MULTIPLE_TIMES", module.MemberPath,
                        "The module is connected at " + sites.Count + " distinct call sites."));
                }
                else if (sites.Count == 1) status = ControlModuleImplementationStatus.Correlated;
                else
                {
                    status = ControlModuleImplementationStatus.Unreferenced;
                    if (!unresolvedFamilies.Contains(module.ModuleFamily))
                        diagnostics.Add(Diagnostic("Warning", "CM107_MODULE_NOT_CALLED", module.MemberPath,
                            "No recognised processing FC call resolves to this module."));
                }
                implementations.Add(new ControlModuleImplementation(module, status, sites.ToArray()));
            }

            return new ControlModuleImplementationResult(
                implementations, diagnostics,
                inventory.DataBlockStructuresIncluded,
                inventory.BlockCallsIncluded);
        }

        private static CallParameterInfo SelectModuleParameter(
            BlockCallInfo call,
            ControlModuleFunctionDefinition definition,
            IDictionary<string, ControlModuleInfo> modules,
            out string failureCode)
        {
            var inOut = Find(call.Parameters, parameter =>
                string.Equals(parameter.Direction, "InOut", StringComparison.OrdinalIgnoreCase));
            var typed = Find(inOut, parameter =>
                string.Equals(parameter.FormalDataType, definition.ExpectedModuleDataType, StringComparison.OrdinalIgnoreCase));
            if (typed.Count == 1) { failureCode = null; return typed[0]; }
            if (typed.Count > 1) { failureCode = "CM103_AMBIGUOUS_INOUT_PARAMETER"; return null; }
            if (inOut.Count == 1) { failureCode = null; return inOut[0]; }

            var named = Find(call.Parameters, parameter => Contains(definition.CandidateInOutParameterNames, parameter.FormalName));
            if (named.Count == 1) { failureCode = null; return named[0]; }
            if (named.Count > 1) { failureCode = "CM103_AMBIGUOUS_INOUT_PARAMETER"; return null; }

            var resolved = Find(call.Parameters, parameter =>
                !string.IsNullOrWhiteSpace(parameter.ResolvedMemberPath) &&
                modules.ContainsKey(parameter.ResolvedMemberPath));
            if (resolved.Count == 1) { failureCode = null; return resolved[0]; }
            failureCode = resolved.Count > 1
                ? "CM103_AMBIGUOUS_INOUT_PARAMETER"
                : "CM102_RECOGNISED_FC_PARAMETER_NOT_FOUND";
            return null;
        }

        private static List<CallParameterInfo> Find(
            IReadOnlyList<CallParameterInfo> source, Predicate<CallParameterInfo> predicate)
        {
            var result = new List<CallParameterInfo>();
            foreach (CallParameterInfo item in source) if (predicate(item)) result.Add(item);
            return result;
        }

        private static bool Contains(IReadOnlyList<string> values, string candidate)
        {
            foreach (string value in values)
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int CompareSites(ControlModuleCallSite left, ControlModuleCallSite right)
        {
            int value = CompareNullable(left.CallingBlockNumber, right.CallingBlockNumber);
            if (value != 0) return value;
            value = ControlModuleImplementationResult.CompareText(left.CallingBlockName, right.CallingBlockName);
            if (value != 0) return value;
            value = CompareNullable(left.NetworkNumber, right.NetworkNumber);
            return value != 0 ? value : left.CallOrdinal.CompareTo(right.CallOrdinal);
        }

        private static int CompareNullable(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            return right.HasValue ? 1 : 0;
        }

        private static string SiteKey(string path, BlockCallInfo call)
        {
            return string.Join("\u001f", path ?? string.Empty, call.CallingBlockPath ?? string.Empty,
                call.NetworkNumber.HasValue ? call.NetworkNumber.Value.ToString() : string.Empty,
                call.CallOrdinal.ToString(), call.CalledBlockName ?? string.Empty);
        }

        private static string CallSource(BlockCallInfo call)
        {
            return (call.CallingBlockPath ?? call.CallingBlockName ?? "Unknown caller") +
                (call.NetworkNumber.HasValue ? "/Network " + call.NetworkNumber.Value : string.Empty);
        }

        private static ControlModuleImplementationDiagnostic Diagnostic(
            string severity, string code, string source, string message)
        {
            return new ControlModuleImplementationDiagnostic(severity, code, message, source);
        }
    }
}
