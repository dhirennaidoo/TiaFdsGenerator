using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TiaFds.Core;

namespace TiaFds.Openness.Xml
{
    public sealed class BlockCallParseResult
    {
        public BlockCallParseResult(
            IReadOnlyList<BlockCallInfo> calls,
            IReadOnlyList<InventoryDiagnostic> diagnostics,
            IReadOnlyList<ExtractedLogicAssignment> assignments = null)
        {
            Calls = calls ?? new BlockCallInfo[0];
            Diagnostics = diagnostics ?? new InventoryDiagnostic[0];
            Assignments = assignments ?? new ExtractedLogicAssignment[0];
        }
        public IReadOnlyList<BlockCallInfo> Calls { get; }
        public IReadOnlyList<InventoryDiagnostic> Diagnostics { get; }
        public IReadOnlyList<ExtractedLogicAssignment> Assignments { get; }
    }

    public sealed class BlockCallXmlParser
    {
        public BlockCallParseResult Parse(
            string inputPath, string callingBlockName, int? callingBlockNumber,
            string callingBlockType, string callingBlockPath, string programmingLanguage)
        {
            return Parse(inputPath, callingBlockName, callingBlockNumber, callingBlockType,
                callingBlockPath, programmingLanguage, null);
        }

        public BlockCallParseResult Parse(
            string inputPath, string callingBlockName, int? callingBlockNumber,
            string callingBlockType, string callingBlockPath, string programmingLanguage,
            ISet<string> knownMemberPaths)
        {
            XDocument document = LoadSecurely(inputPath);
            var calls = new List<BlockCallInfo>();
            var diagnostics = new List<InventoryDiagnostic>();
            var assignments = new List<ExtractedLogicAssignment>();
            string language = programmingLanguage ?? ValueOfNamedAttribute(document.Root, "ProgrammingLanguage");
            if (!IsSupportedLanguage(language))
            {
                diagnostics.Add(Diagnostic("Warning", "CM110_UNSUPPORTED_BLOCK_LANGUAGE", callingBlockPath,
                    "Block-call parsing is not supported for language '" + (language ?? "Unknown") + "'."));
                return new BlockCallParseResult(calls, diagnostics, assignments);
            }

            List<XElement> networks = FindNetworks(document);
            var seenCalls = new HashSet<XElement>();
            var normalizer = new PlcSymbolPathNormalizer();
            var ordinal = 0;
            var statementOrder = 0;
            foreach (XElement network in networks)
            {
                var graph = new NetworkGraph(network, knownMemberPaths);
                foreach (ExtractedLogicAssignment assignment in graph.ExtractAssignments(
                    callingBlockName, callingBlockNumber, callingBlockType, language,
                    ReadNetworkNumber(network), ReadNetworkTitle(network),
                    ReadNetworkComment(network), ref statementOrder))
                    assignments.Add(assignment);
                foreach (XElement callInfo in network.Descendants().Where(item => Local(item) == "CallInfo"))
                {
                    if (!seenCalls.Add(callInfo)) continue;
                    ordinal++;
                    XElement part = callInfo.Ancestors().FirstOrDefault(item =>
                        Local(item) == "Call" || Local(item) == "Part");
                    var callDiagnostics = new List<InventoryDiagnostic>();
                    var parameters = new List<CallParameterInfo>();
                    foreach (XElement formal in callInfo.Descendants().Where(item => Local(item) == "Parameter"))
                    {
                        string formalName = Attribute(formal, "Name");
                        string direction = Attribute(formal, "Section") ?? Attribute(formal, "Direction");
                        OperandResolution operand = graph.ResolveOperand(part, formal, formalName);
                        if (!operand.HasConnection && !string.IsNullOrWhiteSpace(Attribute(formal, "Actual")))
                            operand = new OperandResolution(Attribute(formal, "Actual"));
                        if (operand.Ambiguous)
                            callDiagnostics.Add(Diagnostic("Warning", "CM117_PARAMETER_CONNECTION_AMBIGUOUS",
                                callingBlockPath, "Parameter '" + formalName + "' connects to multiple operands."));
                        else if (operand.BrokenReference)
                            callDiagnostics.Add(Diagnostic("Warning", "CM120_CONNECTION_REFERENCE_NOT_FOUND",
                                callingBlockPath, "A connection for parameter '" + formalName + "' references an unknown node."));
                        else if (operand.Unsupported)
                            callDiagnostics.Add(Diagnostic("Warning", "CM118_CONNECTED_OPERAND_NOT_SUPPORTED",
                                callingBlockPath, "The operand connected to parameter '" + formalName + "' is unsupported."));
                        else if (operand.RenderFailed)
                            callDiagnostics.Add(Diagnostic("Warning", "CM119_ACCESS_EXPRESSION_RENDER_FAILED",
                                callingBlockPath, "The operand connected to parameter '" + formalName + "' could not be rendered."));
                        else if (!operand.HasConnection &&
                                 string.Equals(direction, "InOut", StringComparison.OrdinalIgnoreCase))
                            callDiagnostics.Add(Diagnostic("Warning", "CM122_INOUT_CONNECTION_INCOMPLETE",
                                callingBlockPath, "InOut parameter '" + formalName + "' has no extractable connected operand."));

                        string actual = operand.Expression;
                        SymbolPathNormalizationResult normalized = normalizer.Normalize(actual);
                        string resolved = null;
                        if (normalized.IsSymbolicMemberPath)
                        {
                            if (knownMemberPaths == null || knownMemberPaths.Contains(normalized.NormalizedPath))
                                resolved = normalized.NormalizedPath;
                            else
                                callDiagnostics.Add(Diagnostic("Warning", "CM121_RESOLVED_PATH_NOT_IN_DB_STRUCTURES",
                                    callingBlockPath, "Connected symbol '" + normalized.NormalizedPath +
                                    "' is not present in the extracted DB structures."));
                        }
                        parameters.Add(new CallParameterInfo(formalName, direction,
                            Attribute(formal, "Type") ?? Attribute(formal, "Datatype"), actual, resolved));
                    }

                    if (parameters.Count == 0)
                        callDiagnostics.Add(Diagnostic("Warning", "CM115_CALL_WITHOUT_PARAMETER_ASSIGNMENTS",
                            callingBlockPath, "The exported call has no parameter assignments."));

                    calls.Add(new BlockCallInfo(
                        callingBlockName, callingBlockNumber, callingBlockType, callingBlockPath,
                        Attribute(callInfo, "Name"), ReadCalledNumber(callInfo),
                        Attribute(callInfo, "BlockType") ?? Attribute(callInfo, "Type"),
                        ReadNetworkNumber(network), ReadNetworkTitle(network), ordinal,
                        parameters, callDiagnostics));
                }
            }
            return new BlockCallParseResult(calls, diagnostics, assignments);
        }

        private sealed class NetworkGraph
        {
            private readonly Dictionary<string, XElement> nodes =
                new Dictionary<string, XElement>(StringComparer.Ordinal);
            private readonly List<XElement> wires;
            private readonly ISet<string> knownMemberPaths;
            private readonly PlcSymbolPathNormalizer normalizer = new PlcSymbolPathNormalizer();

            public NetworkGraph(XElement network, ISet<string> knownMemberPaths)
            {
                this.knownMemberPaths = knownMemberPaths;
                foreach (XElement element in network.Descendants())
                {
                    string uid = Attribute(element, "UId");
                    if (!string.IsNullOrWhiteSpace(uid) &&
                        (Local(element) == "Access" || Local(element) == "Call" || Local(element) == "Part" ||
                         Local(element) == "Parameter" || Local(element) == "Constant"))
                        nodes[uid] = element;
                }
                wires = network.Descendants().Where(item => Local(item) == "Wire").ToList();
            }

            public IReadOnlyList<ExtractedLogicAssignment> ExtractAssignments(
                string blockName, int? blockNumber, string blockType, string language,
                int? networkNumber, string networkTitle, string networkComment,
                ref int statementOrder)
            {
                var result = new List<ExtractedLogicAssignment>();
                foreach (XElement part in nodes.Values.Where(item =>
                {
                    string name = Attribute(item, "Name");
                    return Local(item) == "Part" &&
                           (string.Equals(name, "Coil", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "Assign", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "Assignment", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "=", StringComparison.OrdinalIgnoreCase));
                }).OrderBy(item => ParseUid(Attribute(item, "UId"))))
                {
                    statementOrder++;
                    ExtractedBooleanExpression destination = ResolvePortOperand(
                        Attribute(part, "UId"), "operand");
                    if (destination == null)
                        destination = ResolvePortOperand(Attribute(part, "UId"), "out");
                    ExtractedBooleanExpression source = BuildFromPort(
                        Attribute(part, "UId"), "in", new HashSet<string>(StringComparer.Ordinal), 0);
                    if (source == null)
                        source = Unknown("No supported source connection was found.");

                    string destinationText = destination == null ? null : destination.DisplayText;
                    string destinationPath = destination == null ? null : destination.ResolvedPath;
                    ExtractedLogicResolutionStatus status = ExpressionStatus(source);
                    if (string.IsNullOrWhiteSpace(destinationText))
                        status = ExtractedLogicResolutionStatus.Unsupported;
                    else if (string.IsNullOrWhiteSpace(destinationPath) &&
                             status == ExtractedLogicResolutionStatus.Complete)
                        status = ExtractedLogicResolutionStatus.Partial;

                    result.Add(new ExtractedLogicAssignment(
                        destinationText, destinationPath, source, source.DisplayText,
                        blockName, blockNumber, blockType, language, networkNumber,
                        networkTitle, networkComment, statementOrder, status));
                }
                return result.ToArray();
            }

            private ExtractedBooleanExpression BuildFromPort(
                string partUid, string port, ISet<string> visiting, int depth)
            {
                if (depth > 32) return Unknown("Maximum expression depth exceeded.");
                XElement wire = FindWire(partUid, port);
                if (wire == null) return null;
                var candidates = new List<ExtractedBooleanExpression>();
                foreach (XElement connection in wire.Elements())
                {
                    if (Local(connection) == "Powerrail")
                    {
                        candidates.Add(Constant(true));
                        continue;
                    }
                    string uid = Attribute(connection, "UId");
                    if (string.IsNullOrWhiteSpace(uid) ||
                        string.Equals(uid, partUid, StringComparison.Ordinal))
                        continue;
                    XElement node;
                    if (!nodes.TryGetValue(uid, out node)) continue;
                    if (Local(connection) == "IdentCon" &&
                        (Local(node) == "Access" || Local(node) == "Constant"))
                    {
                        candidates.Add(ExpressionForOperand(node));
                        continue;
                    }
                    if (Local(connection) == "NameCon" &&
                        IsOutputPort(Attribute(connection, "Name")) &&
                        Local(node) == "Part")
                        candidates.Add(BuildPart(node, visiting, depth + 1));
                }
                return candidates.Count == 1
                    ? candidates[0]
                    : candidates.Count == 0
                        ? null
                        : Unknown("Multiple source nodes share one logical input.");
            }

            private ExtractedBooleanExpression BuildPart(
                XElement part, ISet<string> visiting, int depth)
            {
                string uid = Attribute(part, "UId");
                if (!visiting.Add(uid)) return Unknown("Circular expression graph.");
                try
                {
                    string name = Attribute(part, "Name") ?? string.Empty;
                    if (string.Equals(name, "Contact", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractedBooleanExpression input =
                            BuildFromPort(uid, "in", visiting, depth);
                        ExtractedBooleanExpression operand = ResolvePortOperand(uid, "operand") ??
                            Unknown("Contact operand was not resolved.");
                        if (part.Descendants().Any(item =>
                            Local(item) == "Negated" &&
                            string.Equals(Attribute(item, "Name"), "operand",
                                StringComparison.OrdinalIgnoreCase)))
                            operand = Unary(ExtractedBooleanExpressionKind.Not, operand);
                        return IsTrue(input)
                            ? operand
                            : Binary(ExtractedBooleanExpressionKind.And,
                                input ?? Unknown("Contact input was not resolved."), operand);
                    }
                    if (string.Equals(name, "O", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "A", StringComparison.OrdinalIgnoreCase))
                    {
                        var children = new List<ExtractedBooleanExpression>();
                        foreach (string inputName in InputPorts(uid))
                        {
                            ExtractedBooleanExpression child =
                                BuildFromPort(uid, inputName, visiting, depth);
                            if (child != null) children.Add(child);
                        }
                        return Nary(
                            string.Equals(name, "O", StringComparison.OrdinalIgnoreCase)
                                ? ExtractedBooleanExpressionKind.Or
                                : ExtractedBooleanExpressionKind.And,
                            children);
                    }
                    if (string.Equals(name, "NOT", StringComparison.OrdinalIgnoreCase))
                        return Unary(ExtractedBooleanExpressionKind.Not,
                            BuildFromPort(uid, "in", visiting, depth) ??
                            Unknown("NOT input was not resolved."));
                    return Unknown("Part '" + name + "' is not supported.");
                }
                finally { visiting.Remove(uid); }
            }

            private ExtractedBooleanExpression ResolvePortOperand(string partUid, string port)
            {
                XElement wire = FindWire(partUid, port);
                if (wire == null) return null;
                var operands = new List<XElement>();
                foreach (XElement connection in wire.Elements())
                {
                    if (Local(connection) != "IdentCon") continue;
                    XElement node;
                    if (nodes.TryGetValue(Attribute(connection, "UId"), out node) &&
                        (Local(node) == "Access" || Local(node) == "Constant"))
                        operands.Add(node);
                }
                return operands.Count == 1
                    ? ExpressionForOperand(operands[0])
                    : operands.Count == 0 ? null : Unknown("Multiple operands were connected.");
            }

            private ExtractedBooleanExpression ExpressionForOperand(XElement operand)
            {
                string text;
                bool supported;
                if (!TryRenderOperand(operand, out text, out supported) ||
                    string.IsNullOrWhiteSpace(text))
                    return Unknown("Operand could not be rendered.");
                bool constant;
                if (bool.TryParse(text, out constant)) return Constant(constant);
                SymbolPathNormalizationResult normalized = normalizer.Normalize(text);
                string resolved = null;
                if (normalized.IsSymbolicMemberPath &&
                    (knownMemberPaths == null ||
                     knownMemberPaths.Contains(normalized.NormalizedPath)))
                    resolved = normalized.NormalizedPath;
                return new ExtractedBooleanExpression(
                    ExtractedBooleanExpressionKind.Operand, text, resolved, null,
                    new ExtractedBooleanExpression[0]);
            }

            private XElement FindWire(string uid, string port)
            {
                return wires.FirstOrDefault(wire => wire.Elements().Any(connection =>
                    Local(connection) == "NameCon" &&
                    string.Equals(Attribute(connection, "UId"), uid, StringComparison.Ordinal) &&
                    string.Equals(Attribute(connection, "Name"), port,
                        StringComparison.OrdinalIgnoreCase)));
            }

            private IEnumerable<string> InputPorts(string uid)
            {
                return wires.SelectMany(wire => wire.Elements())
                    .Where(connection => Local(connection) == "NameCon" &&
                        string.Equals(Attribute(connection, "UId"), uid, StringComparison.Ordinal) &&
                        (string.Equals(Attribute(connection, "Name"), "in",
                            StringComparison.OrdinalIgnoreCase) ||
                         (Attribute(connection, "Name") ?? string.Empty).StartsWith(
                            "in", StringComparison.OrdinalIgnoreCase)))
                    .Select(connection => Attribute(connection, "Name"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            }

            private static bool IsOutputPort(string port)
            {
                return string.Equals(port, "out", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(port, "Q", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(port, "eno", StringComparison.OrdinalIgnoreCase);
            }

            private static int ParseUid(string value)
            {
                int result;
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out result) ? result : int.MaxValue;
            }

            private static bool IsTrue(ExtractedBooleanExpression expression)
            {
                return expression != null &&
                       expression.Kind == ExtractedBooleanExpressionKind.Constant &&
                       expression.ConstantValue == true;
            }

            private static ExtractedBooleanExpression Constant(bool value)
            {
                return new ExtractedBooleanExpression(
                    ExtractedBooleanExpressionKind.Constant,
                    value ? "TRUE" : "FALSE", null, value,
                    new ExtractedBooleanExpression[0]);
            }

            private static ExtractedBooleanExpression Unknown(string text)
            {
                return new ExtractedBooleanExpression(
                    ExtractedBooleanExpressionKind.Unknown, text, null, null,
                    new ExtractedBooleanExpression[0]);
            }

            private static ExtractedBooleanExpression Unary(
                ExtractedBooleanExpressionKind kind,
                ExtractedBooleanExpression child)
            {
                return new ExtractedBooleanExpression(kind,
                    "NOT (" + child.DisplayText + ")", null, null,
                    new[] { child });
            }

            private static ExtractedBooleanExpression Binary(
                ExtractedBooleanExpressionKind kind,
                ExtractedBooleanExpression left,
                ExtractedBooleanExpression right)
            {
                return Nary(kind, new[] { left, right });
            }

            private static ExtractedBooleanExpression Nary(
                ExtractedBooleanExpressionKind kind,
                IEnumerable<ExtractedBooleanExpression> children)
            {
                var values = children == null
                    ? new List<ExtractedBooleanExpression>()
                    : children.Where(item => item != null).ToList();
                if (values.Count == 0) return Unknown("Logical operator has no inputs.");
                if (values.Count == 1) return values[0];
                string separator = kind == ExtractedBooleanExpressionKind.Or ? " OR " : " AND ";
                return new ExtractedBooleanExpression(kind,
                    "(" + string.Join(separator,
                        values.Select(item => item.DisplayText)) + ")",
                    null, null, values.ToArray());
            }

            private static ExtractedLogicResolutionStatus ExpressionStatus(
                ExtractedBooleanExpression expression)
            {
                if (expression == null ||
                    expression.Kind == ExtractedBooleanExpressionKind.Unknown)
                    return ExtractedLogicResolutionStatus.Unsupported;
                bool partial = expression.Kind == ExtractedBooleanExpressionKind.Operand &&
                               string.IsNullOrWhiteSpace(expression.ResolvedPath) &&
                               !string.Equals(expression.DisplayText, "TRUE",
                                   StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(expression.DisplayText, "FALSE",
                                   StringComparison.OrdinalIgnoreCase);
                foreach (ExtractedBooleanExpression child in expression.Children)
                {
                    ExtractedLogicResolutionStatus childStatus = ExpressionStatus(child);
                    if (childStatus == ExtractedLogicResolutionStatus.Unsupported)
                        return childStatus;
                    if (childStatus == ExtractedLogicResolutionStatus.Partial) partial = true;
                }
                return partial
                    ? ExtractedLogicResolutionStatus.Partial
                    : ExtractedLogicResolutionStatus.Complete;
            }

            public OperandResolution ResolveOperand(
                XElement callPart, XElement formal, string formalName)
            {
                string partUid = Attribute(callPart, "UId");
                string formalUid = Attribute(formal, "UId") ??
                                   Attribute(formal, "ConnectionUId") ??
                                   Attribute(formal, "PortUId");
                var matchingWires = new List<XElement>();
                foreach (XElement wire in wires)
                {
                    bool matchesFormalUid = !string.IsNullOrWhiteSpace(formalUid) &&
                        wire.Elements().Any(connection =>
                            string.Equals(Attribute(connection, "UId"), formalUid, StringComparison.Ordinal));
                    bool matchesNamedPort = wire.Elements().Any(connection =>
                        Local(connection) == "NameCon" &&
                        string.Equals(Attribute(connection, "UId"), partUid, StringComparison.Ordinal) &&
                        string.Equals(Attribute(connection, "Name"), formalName, StringComparison.OrdinalIgnoreCase));
                    if (matchesFormalUid || matchesNamedPort) matchingWires.Add(wire);
                }

                if (matchingWires.Count == 0)
                    return OperandResolution.Unconnected;

                var operandElements = new List<XElement>();
                bool broken = false;
                foreach (XElement wire in matchingWires)
                {
                    foreach (XElement connection in wire.Elements())
                    {
                        string uid = Attribute(connection, "UId");
                        if (string.IsNullOrWhiteSpace(uid) ||
                            string.Equals(uid, partUid, StringComparison.Ordinal) ||
                            string.Equals(uid, formalUid, StringComparison.Ordinal))
                            continue;
                        XElement node;
                        if (!nodes.TryGetValue(uid, out node)) { broken = true; continue; }
                        if (Local(node) == "Access" || Local(node) == "Constant")
                            if (!operandElements.Contains(node)) operandElements.Add(node);
                    }
                }
                if (operandElements.Count > 1) return OperandResolution.Multiple;
                if (operandElements.Count == 0)
                    return broken ? OperandResolution.Broken : OperandResolution.UnsupportedOperand;
                string expression;
                bool supported;
                bool rendered = TryRenderOperand(operandElements[0], out expression, out supported);
                if (!supported) return OperandResolution.UnsupportedOperand;
                if (!rendered || string.IsNullOrWhiteSpace(expression)) return OperandResolution.Failed;
                return new OperandResolution(expression);
            }
        }

        private sealed class OperandResolution
        {
            public static readonly OperandResolution Unconnected = new OperandResolution();
            public static readonly OperandResolution Multiple = new OperandResolution { Ambiguous = true, HasConnection = true };
            public static readonly OperandResolution Broken = new OperandResolution { BrokenReference = true, HasConnection = true };
            public static readonly OperandResolution UnsupportedOperand = new OperandResolution { Unsupported = true, HasConnection = true };
            public static readonly OperandResolution Failed = new OperandResolution { RenderFailed = true, HasConnection = true };
            private OperandResolution() { }
            public OperandResolution(string expression) { Expression = expression; HasConnection = true; }
            public string Expression { get; private set; }
            public bool HasConnection { get; private set; }
            public bool Ambiguous { get; private set; }
            public bool BrokenReference { get; private set; }
            public bool Unsupported { get; private set; }
            public bool RenderFailed { get; private set; }
        }

        private static bool TryRenderOperand(XElement operand, out string expression, out bool supported)
        {
            expression = null;
            supported = true;
            string local = Local(operand);
            if (local == "Constant")
            {
                expression = ReadConstant(operand);
                return !string.IsNullOrWhiteSpace(expression);
            }
            if (local != "Access") { supported = false; return false; }

            string scope = Attribute(operand, "Scope");
            XElement symbol = operand.Elements().FirstOrDefault(item => Local(item) == "Symbol");
            if (symbol != null)
            {
                var components = new List<string>();
                foreach (XElement component in symbol.Elements().Where(item => Local(item) == "Component"))
                {
                    string name = Attribute(component, "Name");
                    if (string.IsNullOrWhiteSpace(name)) { supported = false; return false; }
                    var indexes = new List<string>();
                    foreach (XElement indexAccess in component.Descendants().Where(item => Local(item) == "Access"))
                    {
                        string index = ReadConstant(indexAccess);
                        if (!string.IsNullOrWhiteSpace(index)) indexes.Add(index);
                    }
                    components.Add(RenderSymbolComponent(name) +
                        (indexes.Count == 0 ? string.Empty : "[" + string.Join(",", indexes) + "]"));
                }
                if (components.Count == 0) { supported = false; return false; }
                expression = string.Join(".", components);
                if ((string.Equals(scope, "LocalVariable", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "BlockInterface", StringComparison.OrdinalIgnoreCase)) &&
                    !expression.StartsWith("#", StringComparison.Ordinal))
                    expression = "#" + expression;
                return true;
            }

            string constant = ReadConstant(operand);
            if (!string.IsNullOrWhiteSpace(constant)) { expression = constant; return true; }
            XElement address = operand.Descendants().FirstOrDefault(item => Local(item) == "Address");
            if (address != null)
            {
                expression = RenderAddress(address);
                return !string.IsNullOrWhiteSpace(expression);
            }
            supported = false;
            return false;
        }

        private static string ReadConstant(XElement root)
        {
            XElement value = root.DescendantsAndSelf().FirstOrDefault(item =>
                Local(item) == "ConstantValue" || Local(item) == "Literal");
            return value == null ? null : value.Value.Trim();
        }

        private static string RenderAddress(XElement address)
        {
            string area = Attribute(address, "Area");
            string type = Attribute(address, "Type");
            string block = Attribute(address, "BlockNumber");
            string offset = Attribute(address, "ByteOffset") ?? Attribute(address, "Offset");
            string bit = Attribute(address, "BitOffset");
            if (string.Equals(area, "DB", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(block) && !string.IsNullOrWhiteSpace(type) &&
                !string.IsNullOrWhiteSpace(offset))
                return "DB" + block + ".DB" + type.ToUpperInvariant() + offset +
                       (string.IsNullOrWhiteSpace(bit) ? string.Empty : "." + bit);
            return null;
        }

        private static string RenderSymbolComponent(string name)
        {
            if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"') return name;
            if (name.IndexOf('.') < 0) return name;
            return "\"" + name.Replace("\"", "\"\"") + "\"";
        }

        private static List<XElement> FindNetworks(XDocument document)
        {
            List<XElement> result = document.Descendants()
                .Where(element => Local(element) == "SW.Blocks.CompileUnit" ||
                                  Local(element) == "CompileUnit" || Local(element) == "Network")
                .Where(element => element.Descendants().Any(item =>
                    Local(item) == "CallInfo" ||
                    (Local(item) == "Part" &&
                     (string.Equals(Attribute(item, "Name"), "Coil",
                          StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(Attribute(item, "Name"), "Assign",
                          StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(Attribute(item, "Name"), "Assignment",
                          StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(Attribute(item, "Name"), "=",
                          StringComparison.OrdinalIgnoreCase))))).ToList();
            if (result.Count == 0 && document.Descendants().Any(item =>
                Local(item) == "CallInfo" || Local(item) == "Part"))
                result.Add(document.Root);
            return result;
        }

        private static XDocument LoadSecurely(string path)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (XmlReader reader = XmlReader.Create(stream, settings))
                    return XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Exported executable-block XML is malformed: " + exception.Message, exception);
            }
        }

        private static int? ReadNetworkNumber(XElement network)
        {
            int number;
            string value = Attribute(network, "Number") ?? ValueOfNamedAttribute(network, "NetworkNumber");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? (int?)number : null;
        }

        private static string ReadNetworkTitle(XElement network)
        {
            XElement title = network.Descendants().FirstOrDefault(item =>
                (Local(item) == "Text" || Local(item) == "Title") &&
                (Local(item) == "Title" || item.Ancestors().Any(parent =>
                    string.Equals(Attribute(parent, "CompositionName"), "Title", StringComparison.OrdinalIgnoreCase))));
            return title == null ? null : title.Value;
        }

        private static string ReadNetworkComment(XElement network)
        {
            XElement comment = network.Descendants().FirstOrDefault(item =>
                (Local(item) == "Text" || Local(item) == "Comment") &&
                (Local(item) == "Comment" || item.Ancestors().Any(parent =>
                    string.Equals(Attribute(parent, "CompositionName"), "Comment",
                        StringComparison.OrdinalIgnoreCase))));
            return comment == null ? null : comment.Value;
        }

        private static int? ReadCalledNumber(XElement call)
        {
            int number;
            string value = ValueOfNamedAttribute(call, "BlockNumber") ??
                           ValueOfNamedAttribute(call, "Number") ??
                           Attribute(call, "Number");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? (int?)number : null;
        }

        private static string ValueOfNamedAttribute(XElement root, string name)
        {
            if (root == null) return null;
            XElement value = root.Descendants().FirstOrDefault(item =>
                string.Equals(Attribute(item, "Name"), name, StringComparison.OrdinalIgnoreCase));
            return value == null ? null : value.Value.Trim();
        }

        private static bool IsSupportedLanguage(string language)
        {
            return string.Equals(language, "LAD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(language, "FBD", StringComparison.OrdinalIgnoreCase);
        }

        private static string Local(XElement element) { return element == null ? null : element.Name.LocalName; }
        private static string Attribute(XElement element, string name)
        {
            if (element == null) return null;
            XAttribute attribute = element.Attributes().FirstOrDefault(item =>
                string.Equals(item.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? null : attribute.Value;
        }
        private static InventoryDiagnostic Diagnostic(string severity, string code, string source, string message)
        {
            return new InventoryDiagnostic(severity, code, source, message);
        }
    }
}
