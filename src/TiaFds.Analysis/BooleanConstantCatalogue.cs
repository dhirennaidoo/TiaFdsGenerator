using System;
using System.Collections.Generic;
using System.Text;

namespace TiaFds.Analysis
{
    public sealed class BooleanConstantMapping
    {
        public BooleanConstantMapping(string symbol, bool value, string ruleIdentifier)
            : this(symbol, value, ruleIdentifier, false)
        {
        }

        public BooleanConstantMapping(string symbol, bool value, string ruleIdentifier, bool isBypassFlag)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("A constant symbol is required.", nameof(symbol));
            Symbol = symbol;
            Value = value;
            RuleIdentifier = ruleIdentifier;
            IsBypassFlag = isBypassFlag;
        }
        public string Symbol { get; }
        public bool Value { get; }
        public string RuleIdentifier { get; }

        // A deliberately permanent TRUE flag used to bridge parts of conditions (glb.Bridge).
        // Unlike glb.One it is not a neutral constant: every branch it short-circuits is a
        // bypass that must be reported.
        public bool IsBypassFlag { get; }
    }

    public sealed class BooleanConstantCatalogue
    {
        private readonly IReadOnlyDictionary<string, BooleanConstantMapping> mappings;
        private readonly ISet<string> conflicts;

        public static readonly BooleanConstantCatalogue Default =
            new BooleanConstantCatalogue(new[]
            {
                new BooleanConstantMapping("glb.One", true, "BP_GLB_ONE"),
                new BooleanConstantMapping("glb.Zero", false, "BP_GLB_ZERO"),
                new BooleanConstantMapping("glb.Bridge", true, "BP_GLB_BRIDGE", true)
            });

        public BooleanConstantCatalogue(
            IEnumerable<BooleanConstantMapping> source)
        {
            var values = new Dictionary<string, BooleanConstantMapping>(
                StringComparer.OrdinalIgnoreCase);
            var conflicting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
                foreach (BooleanConstantMapping mapping in source)
                {
                    if (mapping == null) continue;
                    string key = Normalize(mapping.Symbol);
                    BooleanConstantMapping existing;
                    if (values.TryGetValue(key, out existing) &&
                        existing.Value != mapping.Value)
                    {
                        conflicting.Add(key);
                        continue;
                    }
                    if (!values.ContainsKey(key)) values.Add(key, mapping);
                }
            mappings = values;
            conflicts = conflicting;
        }

        public bool TryResolve(
            string originalExpression, string resolvedPath,
            out ResolvedBooleanConstant constant, out bool conflict)
        {
            constant = null;
            conflict = false;
            string original = (originalExpression ?? string.Empty).Trim();
            if (string.Equals(original, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(original, "FALSE", StringComparison.OrdinalIgnoreCase))
            {
                bool value = string.Equals(original, "TRUE",
                    StringComparison.OrdinalIgnoreCase);
                constant = new ResolvedBooleanConstant(
                    value, originalExpression, resolvedPath,
                    BooleanConstantSource.Literal,
                    "TIA_BOOLEAN_LITERAL_" + (value ? "TRUE" : "FALSE"));
                return true;
            }

            string[] candidates = { resolvedPath, originalExpression };
            foreach (string candidate in candidates)
            {
                string key = Normalize(candidate);
                if (key.Length == 0) continue;
                if (conflicts.Contains(key))
                {
                    conflict = true;
                    return false;
                }
                BooleanConstantMapping mapping;
                if (!mappings.TryGetValue(key, out mapping)) continue;
                constant = new ResolvedBooleanConstant(
                    mapping.Value, originalExpression, resolvedPath,
                    BooleanConstantSource.KnownProjectSymbol,
                    mapping.RuleIdentifier);
                return true;
            }
            return false;
        }

        // True when a resolved constant's evidence comes from a bypass-flag mapping, directly
        // or through a temporary trace ("TEMPORARY_TRACE:#t->BP_GLB_BRIDGE").
        public bool IsBypassEvidence(string evidence, out string symbol)
        {
            symbol = null;
            if (string.IsNullOrWhiteSpace(evidence)) return false;
            foreach (BooleanConstantMapping mapping in mappings.Values)
                if (mapping.IsBypassFlag && !string.IsNullOrWhiteSpace(mapping.RuleIdentifier) &&
                    (evidence == mapping.RuleIdentifier ||
                     evidence.EndsWith("->" + mapping.RuleIdentifier, StringComparison.Ordinal)))
                {
                    symbol = mapping.Symbol;
                    return true;
                }
            return false;
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var result = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
                if (character != '"' && !char.IsWhiteSpace(character))
                    result.Append(character);
            return result.ToString();
        }
    }
}
