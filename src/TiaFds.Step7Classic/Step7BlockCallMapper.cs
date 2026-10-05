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
            return Map(program, symbols, addresses, diagnostics, null);
        }

        // traces (from Step7StlLogicParser) resolve actuals that LAD passes through local data.
        internal IReadOnlyList<BlockCallInfo> Map(
            Step7ProgramModel program,
            Step7SymbolLookup symbols,
            Step7AddressResolver addresses,
            ICollection<InventoryDiagnostic> diagnostics,
            IDictionary<string, Step7CallTrace> traces)
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
                    for (var index = 0; index < network.Statements.Count; index++)
                    {
                        Step7StatementModel statement = network.Statements[index];
                        if (!CallCommands.Contains(statement.Command) || statement.CalledBlock == null) continue;
                        ordinal++;
                        Step7CallTrace trace = null;
                        if (traces != null)
                            traces.TryGetValue(Step7CallTrace.Key(block.Kind, block.Number, network.Number, index), out trace);
                        calls.Add(MapCall(block, callerName, callerType, callerPath, network, ordinal,
                            statement, interfaces, symbols, addresses, normalizer, traces != null, trace));
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
            PlcSymbolPathNormalizer normalizer,
            bool tracing,
            Step7CallTrace trace)
        {
            var callDiagnostics = new List<InventoryDiagnostic>();
            PostCallCopies copies = PostCallCopies.Read(statement.PostCallStatements, symbols, addresses);
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
                string raw = null;
                ExtractedBooleanExpression tree = null;
                string traceStatus = null;
                if (actual != null && tracing && IsLocalData(actual))
                {
                    string reason;
                    Step7TracedValue traced = direction == "Input"
                        ? TraceInput(actual, trace, out tree, out reason)
                        : copies.Trace(actual, out reason);
                    if (traced != null)
                    {
                        raw = actual;
                        actual = traced.Display;
                        resolved = traced.ResolvedPath;
                        traceStatus = CallParameterTraceStatus.Traced;
                    }
                    else
                    {
                        tree = null;
                        traceStatus = CallParameterTraceStatus.NotTraced;
                        callDiagnostics.Add(new InventoryDiagnostic("Warning",
                            direction == "Input" ? Step7DiagnosticCodes.CallInputNotTraced : Step7DiagnosticCodes.CallOutputNotTraced,
                            source, "Parameter '" + argument.Name + "' (" + actual + ") could not be traced: " + reason));
                    }
                }
                else if (actual == null)
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
                    {
                        resolved = address.MemberPath;
                        // Show the symbol the engineer wired; keep the pointer as the raw actual.
                        raw = actual;
                        actual = Quote(address.BlockName) + address.MemberPath.Substring(address.BlockName.Length);
                    }
                    else if (address.IsAddress)
                        callDiagnostics.Add(new InventoryDiagnostic("Warning", Step7DiagnosticCodes.PointerNotResolved, source,
                            "Parameter '" + argument.Name + "' (" + actual + ") could not be resolved: " + address.FailureReason));
                    else
                    {
                        resolved = ResolveSymbolic(actual, addresses, normalizer, source, callDiagnostics);
                        string symbol = resolved == null ? symbols.SymbolOf(Compact(actual)) : null;
                        if (symbol != null)
                        {
                            // Absolute global operands (M 0.4) are shown by symbol ("glb.DontCare").
                            raw = actual;
                            actual = Quote(symbol);
                        }
                    }
                }

                parameters.Add(new CallParameterInfo(argument.Name, direction, formalType, actual, resolved,
                    raw, tree, traceStatus));
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

        private static readonly Regex LocalBitActual = new Regex(
            @"^L(?<byte>\d+)\.(?<bit>[0-7])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LocalWordActual = new Regex(
            @"^L[BWD]\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool IsLocalData(string actual)
        {
            string compact = Compact(actual);
            return LocalBitActual.IsMatch(compact) || LocalWordActual.IsMatch(compact);
        }

        // An input actual in local data is whatever the network assigned to it before the
        // call: "A "ioDI".X; = L 0.0" makes RN := L 0.0 read "ioDI".X.
        private static Step7TracedValue TraceInput(string actual, Step7CallTrace trace,
            out ExtractedBooleanExpression tree, out string reason)
        {
            tree = null;
            reason = null;
            if (trace == null)
            {
                reason = "the call is not on the straight-line path of its network.";
                return null;
            }
            if (trace.TaintReason != null)
            {
                reason = trace.TaintReason;
                return null;
            }

            string key = Compact(actual).ToUpperInvariant();
            if (LocalWordActual.IsMatch(key))
            {
                Step7TracedValue word;
                if (trace.LocalWords.TryGetValue(key, out word) && word.Display != "?")
                    return word;
                reason = "no value is transferred to " + key + " earlier in the network.";
                return null;
            }

            ExtractedBooleanExpression expression;
            if (!trace.LocalBits.TryGetValue(key, out expression))
            {
                reason = "no assignment to " + key + " precedes the call in the network.";
                return null;
            }
            if (ContainsUnknown(expression))
            {
                reason = "the logic assigned to " + key + " is not supported (" + expression.DisplayText + ").";
                return null;
            }
            tree = expression;
            return new Step7TracedValue(expression.DisplayText,
                expression.Kind == ExtractedBooleanExpressionKind.Operand ? expression.ResolvedPath : null);
        }

        private static bool ContainsUnknown(ExtractedBooleanExpression expression)
        {
            return expression.Kind == ExtractedBooleanExpressionKind.Unknown || expression.Children.Any(ContainsUnknown);
        }

        private static string Quote(string symbol)
        {
            return "\"" + symbol + "\"";
        }

        private static string Compact(string text)
        {
            return (text ?? string.Empty).Replace(" ", string.Empty).Trim();
        }

        // The copies a CALL row performs after the UC/CC, moving output actuals out of local
        // data. Only the plain compiler patterns are accepted:
        //   A L 1.1; [OPN DB10;] = DBX16.1 [; = ...]     (bit outputs)
        //   L LW1;   [OPN DB12;] T DBW2                  (byte/word/dword outputs)
        // Any other logic on the way makes that local value untraceable.
        private sealed class PostCallCopies
        {
            private readonly Dictionary<string, List<Step7TracedValue>> destinations =
                new Dictionary<string, List<Step7TracedValue>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> unsupported =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public static PostCallCopies Read(IReadOnlyList<Step7StatementModel> statements,
                Step7SymbolLookup symbols, Step7AddressResolver addresses)
            {
                var result = new PostCallCopies();
                string source = null;
                bool bitSource = false;
                int? openDb = null;
                foreach (Step7StatementModel statement in statements)
                {
                    string command = statement.Command.ToUpperInvariant();
                    string operand = Compact(statement.Operand);
                    switch (command)
                    {
                        case "BLD":
                        case "NOP":
                            continue;
                        case "OPN":
                            Match db = Regex.Match(operand, @"^DB(?<db>\d+)$", RegexOptions.IgnoreCase);
                            openDb = db.Success ? int.Parse(db.Groups["db"].Value, CultureInfo.InvariantCulture) : (int?)null;
                            continue;
                        case "A":
                            if (LocalBitActual.IsMatch(operand))
                            {
                                result.Abandon(source, "the output copy contains additional logic.");
                                source = operand.ToUpperInvariant();
                                bitSource = true;
                                continue;
                            }
                            break;
                        case "L":
                            if (LocalWordActual.IsMatch(operand))
                            {
                                source = operand.ToUpperInvariant();
                                bitSource = false;
                                continue;
                            }
                            break;
                        case "=":
                            if (source != null && bitSource)
                            {
                                result.Add(source, Render(statement, operand, openDb, "BOOL", symbols, addresses));
                                continue;
                            }
                            break;
                        case "T":
                            if (source != null && !bitSource)
                            {
                                result.Add(source, Render(statement, operand, openDb, null, symbols, addresses));
                                source = null;
                                continue;
                            }
                            break;
                    }
                    result.Abandon(source, "the output copy contains '" + statement.Command + " " + statement.Operand + "'.");
                    source = null;
                }
                return result;
            }

            public Step7TracedValue Trace(string actual, out string reason)
            {
                string key = Compact(actual).ToUpperInvariant();
                if (unsupported.TryGetValue(key, out reason)) return null;
                List<Step7TracedValue> values;
                if (!destinations.TryGetValue(key, out values) || values.Count == 0)
                {
                    reason = "no copy of " + key + " follows the call.";
                    return null;
                }
                reason = null;
                return values.Count == 1
                    ? values[0]
                    : new Step7TracedValue(string.Join(", ", values.Select(value => value.Display)), null);
            }

            private void Add(string source, Step7TracedValue value)
            {
                if (unsupported.ContainsKey(source)) return;
                List<Step7TracedValue> values;
                if (!destinations.TryGetValue(source, out values))
                {
                    values = new List<Step7TracedValue>();
                    destinations.Add(source, values);
                }
                values.Add(value);
            }

            private void Abandon(string source, string reason)
            {
                if (source == null || destinations.ContainsKey(source)) return;
                unsupported[source] = reason;
            }

            private static Step7TracedValue Render(Step7StatementModel statement, string operand, int? openDb,
                string expectedType, Step7SymbolLookup symbols, Step7AddressResolver addresses)
            {
                string qualified = operand;
                if (openDb.HasValue && Regex.IsMatch(operand, @"^DB[XBWD]\d", RegexOptions.IgnoreCase))
                    qualified = "DB" + openDb.Value.ToString(CultureInfo.InvariantCulture) + "." + operand;
                Step7AddressResolution address = addresses.Resolve(qualified, expectedType);
                if (address.IsResolved)
                    return new Step7TracedValue(Quote(address.BlockName) + address.MemberPath.Substring(address.BlockName.Length),
                        address.MemberPath);
                string symbol = statement.OperandSymbol ?? symbols.SymbolOf(operand);
                return new Step7TracedValue(symbol != null ? Quote(symbol) : qualified, null);
            }
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
