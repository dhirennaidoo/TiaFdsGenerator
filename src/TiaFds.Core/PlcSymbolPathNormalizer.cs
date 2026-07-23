using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaFds.Core
{
    public sealed class SymbolPathNormalizationResult
    {
        public SymbolPathNormalizationResult(string originalExpression, string normalizedPath, bool isSymbolicMemberPath)
        {
            OriginalExpression = originalExpression;
            NormalizedPath = normalizedPath;
            IsSymbolicMemberPath = isSymbolicMemberPath;
        }

        public string OriginalExpression { get; }
        public string NormalizedPath { get; }
        public bool IsSymbolicMemberPath { get; }
    }

    public sealed class PlcSymbolPathNormalizer
    {
        private static readonly Regex AbsoluteAddress = new Regex(
            @"^(?:DB\d+\.)?(?:DB[XBWD]\d+(?:\.\d+)?|[IQM][XBWD]?\d+(?:\.\d+)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public SymbolPathNormalizationResult Normalize(string expression)
        {
            string original = expression;
            if (string.IsNullOrWhiteSpace(expression))
                return new SymbolPathNormalizationResult(original, null, false);

            string value = expression.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal) ||
                value.StartsWith("P#", StringComparison.OrdinalIgnoreCase) ||
                AbsoluteAddress.IsMatch(value))
                return new SymbolPathNormalizationResult(original, null, false);

            var result = new StringBuilder();
            var token = new StringBuilder();
            bool quoted = false;
            bool sawSeparator = false;
            for (var index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (quoted)
                {
                    if (current == '"')
                    {
                        if (index + 1 < value.Length && value[index + 1] == '"')
                        {
                            token.Append('"');
                            index++;
                        }
                        else quoted = false;
                    }
                    else token.Append(current);
                    continue;
                }

                if (current == '"') { quoted = true; continue; }
                if (current == '.')
                {
                    if (!AppendToken(result, token)) return Invalid(original);
                    result.Append('.');
                    sawSeparator = true;
                    continue;
                }

                if (!char.IsWhiteSpace(current)) token.Append(current);
            }

            if (quoted || !AppendToken(result, token) || !sawSeparator)
                return Invalid(original);

            string normalized = result.ToString();
            return new SymbolPathNormalizationResult(original, normalized, true);
        }

        private static bool AppendToken(StringBuilder result, StringBuilder token)
        {
            if (token.Length == 0) return false;
            result.Append(token);
            token.Length = 0;
            return true;
        }

        private static SymbolPathNormalizationResult Invalid(string original)
        {
            return new SymbolPathNormalizationResult(original, null, false);
        }
    }
}
