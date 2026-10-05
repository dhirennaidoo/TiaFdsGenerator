using System.Collections.Generic;
using System.Globalization;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // The logic parser's view of a network's local data at one CALL statement: what each
    // anonymous local bit and local word holds when the call reads its input actuals. LAD
    // passes every boolean input through such a bit ("A "ioDI".X; = L 0.0 ... RN := L 0.0").
    internal sealed class Step7CallTrace
    {
        public Step7CallTrace(
            IDictionary<string, ExtractedBooleanExpression> localBits,
            IDictionary<string, Step7TracedValue> localWords,
            string taintReason)
        {
            LocalBits = new Dictionary<string, ExtractedBooleanExpression>(localBits);
            LocalWords = new Dictionary<string, Step7TracedValue>(localWords);
            TaintReason = taintReason;
        }

        // "L0.0" -> expression assigned earlier in the network.
        public IReadOnlyDictionary<string, ExtractedBooleanExpression> LocalBits { get; }
        // "LW1" / "LD1" / "LB1" -> value transferred earlier in the network.
        public IReadOnlyDictionary<string, Step7TracedValue> LocalWords { get; }
        // Set when control flow before the call makes local data unreliable.
        public string TaintReason { get; }

        public static string Key(Step7BlockKind kind, int blockNumber, int networkNumber, int statementIndex)
        {
            return kind.ToString() + "/" + blockNumber.ToString(CultureInfo.InvariantCulture) + "/" +
                   networkNumber.ToString(CultureInfo.InvariantCulture) + "/" +
                   statementIndex.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class Step7TracedValue
    {
        public Step7TracedValue(string display, string resolvedPath)
        {
            Display = display;
            ResolvedPath = resolvedPath;
        }

        public string Display { get; }
        public string ResolvedPath { get; }
    }
}
