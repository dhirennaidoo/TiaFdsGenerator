using System;
using System.Collections.Generic;
using System.Globalization;

namespace TiaFds.Step7Classic
{
    // Absolute operand -> symbol, e.g. "DB50" -> "db.cm.Drv", "UDT50" -> "Udt.cm.Drv".
    internal sealed class Step7SymbolLookup
    {
        private readonly Dictionary<string, string> byOperand =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Step7SymbolLookup(IEnumerable<Step7SymbolModel> symbols)
        {
            foreach (Step7SymbolModel symbol in symbols)
            {
                string operand = Normalize(symbol.Operand);
                // STEP 7 rejects duplicate operands; keep the first defensively.
                if (operand.Length > 0 && symbol.Symbol.Length > 0 && !byOperand.ContainsKey(operand))
                    byOperand.Add(operand, symbol.Symbol);
            }
        }

        public string SymbolOf(string prefix, int number)
        {
            return SymbolOf(prefix + number.ToString(CultureInfo.InvariantCulture));
        }

        public string SymbolOf(string operand)
        {
            string symbol;
            return byOperand.TryGetValue(Normalize(operand), out symbol) ? symbol : null;
        }

        public string NameOf(string prefix, int number)
        {
            return SymbolOf(prefix, number) ?? prefix + number.ToString(CultureInfo.InvariantCulture);
        }

        // Snapshot datatype name: UDTs by symbol (absolute "UDTn" when unnamed), STRUCT as
        // "Struct", elementary types as the S7 keyword.
        public string TypeName(string elementaryType, int? userDataTypeNumber)
        {
            if (string.Equals(elementaryType, "UDT", StringComparison.OrdinalIgnoreCase) && userDataTypeNumber.HasValue)
                return NameOf("UDT", userDataTypeNumber.Value);
            if (string.Equals(elementaryType, "STRUCT", StringComparison.OrdinalIgnoreCase))
                return "Struct";
            return elementaryType;
        }

        public static string Normalize(string operand)
        {
            return (operand ?? string.Empty).Replace(" ", string.Empty).Trim();
        }
    }
}
