using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Turns decompiled CALL/UC/CC statements into the shared BlockCallInfo model, using the
    // TIA conventions: directions Input/Output/InOut, a 1-based call ordinal per calling
    // block, caller path "<group>/<name>", called type FC/FB/SFC/SFB.
    public sealed class Step7BlockCallMapper
    {
        private static readonly Regex AbsoluteBlock = new Regex(
            @"^\s*(?<type>SFC|SFB|FC|FB)\s*(?<number>\d+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> CallCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CALL", "UC", "CC" };

        internal IReadOnlyList<BlockCallInfo> Map(
            Step7ProgramModel program,
            Step7SymbolLookup symbols,
            Step7AddressResolver addresses,
            ICollection<InventoryDiagnostic> diagnostics)
        {
            var interfaces = new Dictionary<string, Step7CodeBlockModel>(StringComparer.OrdinalIgnoreCase);
            foreach (Step7CodeBlockModel block in program.CodeBlocks)
                interfaces[Prefix(block.Kind) + block.Number.ToString(CultureInfo.InvariantCulture)] = block;

            var calls = new List<BlockCallInfo>();
            var normalizer = new PlcSymbolPathNormalizer();
            foreach (Step7CodeBlockModel block in program.CodeBlocks
                         .Where(item => item.Kind == Step7BlockKind.OrganizationBlock ||
                                        item.Kind == Step7BlockKind.Function ||
                                        item.Kind == Step7BlockKind.FunctionBlock)
                         .OrderBy(item => item.Kind).ThenBy(item => item.Number))
            {
                string callerName = symbols.NameOf(Prefix(block.Kind), block.Number);
                string callerType = Step7InventoryMapper.BlockTypeName(block.Kind, false);
                string callerPath = PlcInventoryBuilder.BuildGroupPath(Step7InventoryMapper.BlocksGroupPath, callerName);
                if (block.DecodeError != null)
                {
                    diagnostics.Add(new InventoryDiagnostic("Error", "CM111_BLOCK_CALL_EXTRACTION_FAILED", callerPath,
                        "Executable-block call extraction failed: " + block.DecodeError));
                    continue;
                }

                var ordinal = 0;
                foreach (Step7NetworkModel network in block.Networks)
                {
                    foreach (Step7StatementModel statement in network.Statements)
                    {
                        if (!CallCommands.Contains(statement.Command) || statement.CalledBlock == null) continue;
                        ordinal++;
                        calls.Add(MapCall(block, callerName, callerType, callerPath, network, ordinal,
                            statement, interfaces, symbols, addresses, normalizer));
                    }
                }
            }
            return calls;
        }

        private static BlockCallInfo MapCall(
            Step7CodeBlockModel caller,
            string callerName,
            string callerType,
            string callerPath,
            Step7NetworkModel network,
            int ordinal,
            Step7StatementModel statement,
            IDictionary<string, Step7CodeBlockModel> interfaces,
            Step7SymbolLookup symbols,
            Step7AddressResolver addresses,
            PlcSymbolPathNormalizer normalizer)
        {
            var callDiagnostics = new List<InventoryDiagnostic>();
            Match absolute = AbsoluteBlock.Match(statement.CalledBlock);
            string calledType = absolute.Success ? absolute.Groups["type"].Value.ToUpperInvariant() : null;
            int? calledNumber = absolute.Success
                ? int.Parse(absolute.Groups["number"].Value, CultureInfo.InvariantCulture)
                : (int?)null;
            string calledName = symbols.SymbolOf(statement.CalledBlock) ?? statement.CalledBlock;
            string source = callerPath + "/Network " + network.Number.ToString(CultureInfo.InvariantCulture);

            Step7CodeBlockModel called;
            interfaces.TryGetValue(SymbolKey(statement.CalledBlock), out called);
            if (called == null)
                callDiagnostics.Add(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.CalledBlockNotInProject, source,
                    "Called block '" + statement.CalledBlock + "' is not in the offline block folder; formal datatypes are unknown."));

            var parameters = new List<CallParameterInfo>();
            foreach (Step7CallArgumentModel argument in statement.CallArguments)
            {
                Step7InterfaceParameterModel formal = called == null
                    ? null
                    : called.InterfaceParameters.FirstOrDefault(item =>
                        IsFormal(item) &&
                        string.Equals(item.Name, argument.Name, StringComparison.OrdinalIgnoreCase));
                string direction = Direction(formal == null ? argument.Section : formal.Section);
                string formalType = formal != null
                    ? symbols.TypeName(formal.ElementaryType, formal.UserDataTypeNumber)
                    : (string.Equals(argument.ElementaryType, "UDT", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : argument.ElementaryType);

                string actual = argument.Value.Length == 0 ? null : argument.Value;
                string resolved = null;
                if (actual == null)
                {
                    // FC parameters are mandatory in STEP 7; FB/SFB parameters live in the
                    // instance DB and may legitimately be left unassigned.
                    if (direction == "InOut" && (calledType == "FC" || calledType == "SFC"))
                        callDiagnostics.Add(new InventoryDiagnostic("Warning", "CM122_INOUT_CONNECTION_INCOMPLETE", source,
                            "InOut parameter '" + argument.Name + "' has no actual parameter."));
                }
                else
                {
                    Step7AddressResolution address = addresses.Resolve(actual, formalType);
                    if (address.IsResolved)
                        resolved = address.MemberPath;
                    else if (address.IsAddress)
                        callDiagnostics.Add(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.PointerNotResolved, source,
                            "Parameter '" + argument.Name + "' (" + actual + ") could not be resolved: " + address.FailureReason));
                    else
                        resolved = ResolveSymbolic(actual, addresses, normalizer, source, callDiagnostics);
                }

                parameters.Add(new CallParameterInfo(argument.Name, direction, formalType, actual, resolved));
            }

            if (parameters.Count == 0 && called != null && called.InterfaceParameters.Any(IsFormal))
                callDiagnostics.Add(new InventoryDiagnostic("Warning", "CM115_CALL_WITHOUT_PARAMETER_ASSIGNMENTS", source,
                    "The call has no parameter assignments although '" + calledName + "' declares parameters."));

            return new BlockCallInfo(
                callerName, caller.Number, callerType, callerPath,
                calledName, calledNumber, calledType,
                network.Number, network.Title, ordinal,
                parameters, callDiagnostics);
        }

        private static string ResolveSymbolic(
            string actual,
            Step7AddressResolver addresses,
            PlcSymbolPathNormalizer normalizer,
            string source,
            ICollection<InventoryDiagnostic> diagnostics)
        {
            SymbolPathNormalizationResult normalized = normalizer.Normalize(actual);
            if (!normalized.IsSymbolicMemberPath) return null;
            if (addresses.IsKnownPath(normalized.NormalizedPath)) return normalized.NormalizedPath;
            // Global symbols (glb.One) and I/O are not DB members; only report paths that
            // name an extracted DB but miss its members.
            if (addresses.IsKnownBlock(normalized.NormalizedPath))
                diagnostics.Add(new InventoryDiagnostic("Warning", "CM121_RESOLVED_PATH_NOT_IN_DB_STRUCTURES", source,
                    "Connected symbol '" + normalized.NormalizedPath + "' is not present in the extracted DB structures."));
            return null;
        }

        private static bool IsFormal(Step7InterfaceParameterModel parameter)
        {
            return !string.Equals(parameter.Section, "TEMP", StringComparison.OrdinalIgnoreCase);
        }

        private static string Direction(string section)
        {
            switch ((section ?? string.Empty).ToUpperInvariant())
            {
                case "IN": return "Input";
                case "OUT": return "Output";
                case "IN_OUT": return "InOut";
                default: return section;
            }
        }

        private static string SymbolKey(string calledBlock)
        {
            return Step7SymbolLookup.Normalize(calledBlock);
        }

        internal static string Prefix(Step7BlockKind kind)
        {
            switch (kind)
            {
                case Step7BlockKind.OrganizationBlock: return "OB";
                case Step7BlockKind.Function: return "FC";
                case Step7BlockKind.FunctionBlock: return "FB";
                case Step7BlockKind.DataBlock: return "DB";
                case Step7BlockKind.UserDataType: return "UDT";
                case Step7BlockKind.SystemFunction: return "SFC";
                case Step7BlockKind.SystemFunctionBlock: return "SFB";
                case Step7BlockKind.VariableTable: return "VAT";
                default: return "BLK";
            }
        }
    }
}
