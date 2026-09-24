using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Binds block-interface references (#Seq.SG, #Seq.StepNo == 110) to the actual argument of
    // the block's only call site, e.g. Seq := P#DB200.DBX 180.0 -> db.seq.SeqRollerMills.
    // With exactly one caller this is exact; with none or several the references are left as
    // they are (Partial), because the actual would depend on the caller.
    internal sealed class Step7ParameterBinder
    {
        private static readonly Regex InterfaceReference = new Regex(
            @"#(?<parameter>[A-Za-z_][A-Za-z0-9_]*)(?<rest>(\.[A-Za-z_][A-Za-z0-9_]*)*)",
            RegexOptions.CultureInvariant);
        private static readonly Regex ComparisonOperand = new Regex(
            @"^(?<left>\S+) (?<op>==|<>|>=|<=|>|<) (?<right>\S+)$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, Dictionary<string, Binding>> bindingsByBlock =
            new Dictionary<string, Dictionary<string, Binding>>(StringComparer.OrdinalIgnoreCase);
        private readonly Step7AddressResolver addresses;

        public Step7ParameterBinder(IEnumerable<BlockCallInfo> calls, Step7AddressResolver addresses)
        {
            this.addresses = addresses;
            foreach (IGrouping<string, BlockCallInfo> group in calls
                         .Where(call => !string.IsNullOrWhiteSpace(call.CalledBlockName))
                         .GroupBy(call => call.CalledBlockName, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() != 1) continue;
                var bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
                foreach (CallParameterInfo parameter in group.Single().Parameters)
                {
                    if (string.IsNullOrWhiteSpace(parameter.ResolvedMemberPath)) continue;
                    string blockName = addresses.BlockNameOf(parameter.ResolvedMemberPath);
                    if (blockName != null) bindings[parameter.FormalName] = new Binding(parameter.ResolvedMemberPath, blockName);
                }
                if (bindings.Count > 0) bindingsByBlock[group.Key] = bindings;
            }
        }

        public ExtractedLogicAssignment Bind(ExtractedLogicAssignment assignment)
        {
            Dictionary<string, Binding> bindings;
            if (assignment == null || !bindingsByBlock.TryGetValue(assignment.BlockName ?? string.Empty, out bindings))
                return assignment;

            bool changed = false;
            ExtractedBooleanExpression source = BindExpression(assignment.SourceExpression, bindings, ref changed);
            string destination = assignment.DestinationExpression;
            string destinationPath = assignment.ResolvedDestinationPath;
            if (destinationPath == null)
            {
                string boundPath, boundDisplay;
                if (TryBindReference(destination, bindings, out boundPath, out boundDisplay))
                {
                    destination = boundDisplay;
                    destinationPath = boundPath;
                    changed = true;
                }
            }
            if (!changed) return assignment;

            return new ExtractedLogicAssignment(
                destination, destinationPath, source, assignment.OriginalSourceText,
                assignment.BlockName, assignment.BlockNumber, assignment.BlockType, assignment.BlockLanguage,
                assignment.NetworkNumber, assignment.NetworkTitle, assignment.NetworkComment,
                assignment.StatementOrder, Step7StlLogicParser.Expressions.Status(source), assignment.Operation);
        }

        private ExtractedBooleanExpression BindExpression(
            ExtractedBooleanExpression expression, IDictionary<string, Binding> bindings, ref bool changed)
        {
            if (expression == null) return null;
            if (expression.Kind == ExtractedBooleanExpressionKind.Operand && expression.ResolvedPath == null)
            {
                string path, display;
                if (TryBindReference(expression.DisplayText, bindings, out path, out display) ||
                    TryBindComparison(expression.DisplayText, bindings, out path, out display))
                {
                    changed = true;
                    return new ExtractedBooleanExpression(expression.Kind, display, path, expression.ConstantValue, null);
                }
                return expression;
            }
            if (expression.Children.Count == 0) return expression;

            bool childChanged = false;
            var children = expression.Children.Select(child => BindExpression(child, bindings, ref childChanged)).ToList();
            if (!childChanged) return expression;
            changed = true;
            return new ExtractedBooleanExpression(expression.Kind, Render(expression.Kind, children),
                expression.ResolvedPath, expression.ConstantValue, children);
        }

        private bool TryBindReference(string text, IDictionary<string, Binding> bindings, out string path, out string display)
        {
            path = null;
            display = null;
            Match match = InterfaceReference.Match(text ?? string.Empty);
            if (!match.Success || match.Index != 0 || match.Length != text.Length) return false;
            Binding binding;
            if (!bindings.TryGetValue(match.Groups["parameter"].Value, out binding)) return false;
            string candidate = binding.MemberPath + match.Groups["rest"].Value;
            if (!addresses.IsKnownPath(candidate)) return false;
            path = candidate;
            display = "\"" + binding.BlockName + "\"" + candidate.Substring(binding.BlockName.Length);
            return true;
        }

        // "#Seq.StepNo == 110" -> "\"db.seq\".SeqRollerMills.StepNo == 110", path = the member.
        private bool TryBindComparison(string text, IDictionary<string, Binding> bindings, out string path, out string display)
        {
            path = null;
            display = null;
            Match match = ComparisonOperand.Match(text ?? string.Empty);
            if (!match.Success) return false;
            string left = match.Groups["left"].Value, right = match.Groups["right"].Value;
            string leftPath, leftDisplay, rightPath, rightDisplay;
            bool leftBound = TryBindReference(left, bindings, out leftPath, out leftDisplay);
            bool rightBound = TryBindReference(right, bindings, out rightPath, out rightDisplay);
            if (!leftBound && !rightBound) return false;
            display = (leftBound ? leftDisplay : left) + " " + match.Groups["op"].Value + " " + (rightBound ? rightDisplay : right);
            if (leftBound && !rightBound && IsLiteral(right)) path = leftPath;
            else if (rightBound && !leftBound && IsLiteral(left)) path = rightPath;
            return true;
        }

        private static bool IsLiteral(string text)
        {
            return Regex.IsMatch(text ?? string.Empty, @"^-?\d+(\.\d+)?$|^[A-Z0-9]+#[^#]", RegexOptions.IgnoreCase);
        }

        private static string Render(ExtractedBooleanExpressionKind kind, IReadOnlyList<ExtractedBooleanExpression> children)
        {
            if (kind == ExtractedBooleanExpressionKind.Not && children.Count == 1)
                return "NOT " + Wrap(children[0]);
            string separator = kind == ExtractedBooleanExpressionKind.And ? " AND " : " OR ";
            return string.Join(separator, children.Select(Wrap));
        }

        private static string Wrap(ExtractedBooleanExpression value)
        {
            return value.Kind == ExtractedBooleanExpressionKind.And || value.Kind == ExtractedBooleanExpressionKind.Or
                ? "(" + value.DisplayText + ")"
                : value.DisplayText;
        }

        private sealed class Binding
        {
            public Binding(string memberPath, string blockName)
            {
                MemberPath = memberPath;
                BlockName = blockName;
            }

            public string MemberPath { get; }
            public string BlockName { get; }
        }
    }
}
