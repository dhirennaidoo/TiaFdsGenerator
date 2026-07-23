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
        public BlockCallParseResult(IReadOnlyList<BlockCallInfo> calls, IReadOnlyList<InventoryDiagnostic> diagnostics)
        {
            Calls = calls ?? new BlockCallInfo[0];
            Diagnostics = diagnostics ?? new InventoryDiagnostic[0];
        }
        public IReadOnlyList<BlockCallInfo> Calls { get; }
        public IReadOnlyList<InventoryDiagnostic> Diagnostics { get; }
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
            string language = programmingLanguage ?? ValueOfNamedAttribute(document.Root, "ProgrammingLanguage");
            if (!IsSupportedLanguage(language))
            {
                diagnostics.Add(Diagnostic("Warning", "CM110_UNSUPPORTED_BLOCK_LANGUAGE", callingBlockPath,
                    "Block-call parsing is not supported for language '" + (language ?? "Unknown") + "'."));
                return new BlockCallParseResult(calls, diagnostics);
            }

            List<XElement> networks = FindNetworks(document);
            var seenCalls = new HashSet<XElement>();
            var normalizer = new PlcSymbolPathNormalizer();
            var ordinal = 0;
            foreach (XElement network in networks)
            {
                var graph = new NetworkGraph(network);
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
            return new BlockCallParseResult(calls, diagnostics);
        }

        private sealed class NetworkGraph
        {
            private readonly Dictionary<string, XElement> nodes =
                new Dictionary<string, XElement>(StringComparer.Ordinal);
            private readonly List<XElement> wires;

            public NetworkGraph(XElement network)
            {
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
                .Where(element => element.Descendants().Any(item => Local(item) == "CallInfo")).ToList();
            if (result.Count == 0 && document.Descendants().Any(item => Local(item) == "CallInfo"))
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
