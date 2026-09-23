using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaFds.Analysis
{
    internal sealed class BehaviourExpressionSimplificationResult
    {
        public BehaviourExpression OriginalExpression { get; set; }
        public BehaviourExpression SimplifiedExpression { get; set; }
        public bool Changed { get; set; }
        public bool HasUnresolvedOperand { get; set; }
        public bool HasUnsupportedNode { get; set; }
        public bool HasCatalogueConflict { get; set; }
        public bool HasAmbiguousNumericLiteral { get; set; }
        public IReadOnlyList<ResolvedBooleanConstant> Constants { get; set; }

        public bool? EffectiveValue
        {
            get
            {
                return SimplifiedExpression != null &&
                       SimplifiedExpression.Kind == BehaviourExpressionKind.Constant
                    ? SimplifiedExpression.ConstantValue
                    : null;
            }
        }
    }

    internal sealed class BehaviourExpressionSimplifier
    {
        private readonly BooleanConstantCatalogue catalogue;

        public BehaviourExpressionSimplifier(BooleanConstantCatalogue catalogue)
        {
            this.catalogue = catalogue ?? BooleanConstantCatalogue.Default;
        }

        public BehaviourExpressionSimplificationResult Simplify(
            BehaviourExpression expression)
        {
            return Visit(expression);
        }

        private BehaviourExpressionSimplificationResult Visit(
            BehaviourExpression expression)
        {
            if (expression == null)
                return Unsupported("Expression is missing.");

            switch (expression.Kind)
            {
                case BehaviourExpressionKind.Operand:
                    return Operand(expression);
                case BehaviourExpressionKind.Constant:
                    return Constant(expression);
                case BehaviourExpressionKind.Not:
                    return Not(expression);
                case BehaviourExpressionKind.And:
                    return Compound(expression, true);
                case BehaviourExpressionKind.Or:
                    return Compound(expression, false);
                default:
                    return Unsupported(expression.DisplayText);
            }
        }

        private BehaviourExpressionSimplificationResult Operand(
            BehaviourExpression expression)
        {
            if (expression.ResolvedConstant != null)
                return new BehaviourExpressionSimplificationResult
                {
                    OriginalExpression = expression,
                    SimplifiedExpression = ConstantExpression(
                        expression.ResolvedConstant.Value,
                        expression.ResolvedConstant),
                    Changed = true,
                    Constants = new[] { expression.ResolvedConstant }
                };
            ResolvedBooleanConstant constant;
            bool conflict;
            if (catalogue.TryResolve(expression.DisplayText,
                    expression.ResolvedPath, out constant, out conflict))
            {
                var original = Copy(expression, expression.Children, constant);
                return new BehaviourExpressionSimplificationResult
                {
                    OriginalExpression = original,
                    SimplifiedExpression = ConstantExpression(
                        constant.Value, constant),
                    Changed = true,
                    Constants = new[] { constant }
                };
            }
            string text = (expression.DisplayText ?? string.Empty).Trim();
            return new BehaviourExpressionSimplificationResult
            {
                OriginalExpression = expression,
                SimplifiedExpression = expression,
                HasUnresolvedOperand =
                    string.IsNullOrWhiteSpace(expression.ResolvedPath),
                HasCatalogueConflict = conflict,
                HasAmbiguousNumericLiteral = text == "0" || text == "1",
                Constants = new ResolvedBooleanConstant[0]
            };
        }

        private static BehaviourExpressionSimplificationResult Constant(
            BehaviourExpression expression)
        {
            if (!expression.ConstantValue.HasValue)
                return Unsupported(expression.DisplayText);
            var constant = expression.ResolvedConstant ??
                new ResolvedBooleanConstant(
                    expression.ConstantValue.Value, expression.DisplayText,
                    expression.ResolvedPath, BooleanConstantSource.Literal,
                    "TIA_BOOLEAN_LITERAL_" +
                    (expression.ConstantValue.Value ? "TRUE" : "FALSE"));
            var annotated = Copy(expression, expression.Children, constant);
            return new BehaviourExpressionSimplificationResult
            {
                OriginalExpression = annotated,
                SimplifiedExpression = ConstantExpression(
                    constant.Value, constant),
                Constants = new[] { constant }
            };
        }

        private BehaviourExpressionSimplificationResult Not(
            BehaviourExpression expression)
        {
            BehaviourExpressionSimplificationResult child =
                expression.Children.Count == 1
                    ? Visit(expression.Children[0])
                    : Unsupported("NOT requires one operand.");
            BehaviourExpression original = Copy(expression,
                new[] { child.OriginalExpression }, expression.ResolvedConstant);
            if (IsConstant(child.SimplifiedExpression))
            {
                bool value = !child.SimplifiedExpression.ConstantValue.Value;
                return Merge(original, ConstantExpression(
                    value, Propagated(value, expression.DisplayText)),
                    new[] { child }, true);
            }
            return Merge(original, new BehaviourExpression(
                    BehaviourExpressionKind.Not,
                    "NOT (" + child.SimplifiedExpression.DisplayText + ")",
                    null, null, null,
                    new[] { child.SimplifiedExpression }),
                new[] { child }, child.Changed);
        }

        private BehaviourExpressionSimplificationResult Compound(
            BehaviourExpression expression, bool isAnd)
        {
            var children = expression.Children.Select(Visit).ToArray();
            BehaviourExpression original = Copy(expression,
                children.Select(item => item.OriginalExpression).ToArray(),
                expression.ResolvedConstant);
            bool absorbingValue = !isAnd;
            foreach (BehaviourExpressionSimplificationResult child in children)
                if (IsConstant(child.SimplifiedExpression) &&
                    child.SimplifiedExpression.ConstantValue.Value ==
                    absorbingValue)
                    return Merge(original, ConstantExpression(
                            absorbingValue,
                            Propagated(absorbingValue, expression.DisplayText)),
                        children, true);

            bool neutralValue = isAnd;
            var effective = children
                .Select(item => item.SimplifiedExpression)
                .Where(item => !IsConstant(item) ||
                    item.ConstantValue.Value != neutralValue)
                .ToList();
            bool changed = children.Any(item => item.Changed) ||
                           effective.Count != children.Length;
            if (effective.Count == 0)
                return Merge(original, ConstantExpression(
                        neutralValue,
                        Propagated(neutralValue, expression.DisplayText)),
                    children, true);
            if (effective.Count == 1)
                return Merge(original, effective[0], children, true);

            string separator = isAnd ? " AND " : " OR ";
            string display = "(" + string.Join(separator,
                effective.Select(item => item.DisplayText)) + ")";
            var simplified = new BehaviourExpression(
                isAnd ? BehaviourExpressionKind.And : BehaviourExpressionKind.Or,
                display, null, null, null, effective);
            return Merge(original, simplified, children, changed);
        }

        private static BehaviourExpressionSimplificationResult Merge(
            BehaviourExpression original, BehaviourExpression simplified,
            IEnumerable<BehaviourExpressionSimplificationResult> children,
            bool changed)
        {
            BehaviourExpressionSimplificationResult[] source = children.ToArray();
            return new BehaviourExpressionSimplificationResult
            {
                OriginalExpression = original,
                SimplifiedExpression = simplified,
                Changed = changed,
                HasUnresolvedOperand = source.Any(item => item.HasUnresolvedOperand),
                HasUnsupportedNode = source.Any(item => item.HasUnsupportedNode),
                HasCatalogueConflict = source.Any(item => item.HasCatalogueConflict),
                HasAmbiguousNumericLiteral =
                    source.Any(item => item.HasAmbiguousNumericLiteral),
                Constants = source.SelectMany(item => item.Constants)
                    .GroupBy(item => item.Evidence + "\u001f" +
                        item.OriginalSourceText, StringComparer.Ordinal)
                    .Select(group => group.First()).ToArray()
            };
        }

        private static BehaviourExpressionSimplificationResult Unsupported(
            string display)
        {
            var expression = new BehaviourExpression(
                BehaviourExpressionKind.Unknown, display, null, null, null,
                new BehaviourExpression[0]);
            return new BehaviourExpressionSimplificationResult
            {
                OriginalExpression = expression,
                SimplifiedExpression = expression,
                HasUnsupportedNode = true,
                Constants = new ResolvedBooleanConstant[0]
            };
        }

        private static BehaviourExpression Copy(
            BehaviourExpression source,
            IReadOnlyList<BehaviourExpression> children,
            ResolvedBooleanConstant constant)
        {
            return new BehaviourExpression(
                source.Kind, source.DisplayText, source.Operand,
                source.ResolvedPath, source.ConstantValue, children, constant);
        }

        private static BehaviourExpression ConstantExpression(
            bool value, ResolvedBooleanConstant source)
        {
            return new BehaviourExpression(
                BehaviourExpressionKind.Constant,
                value ? "TRUE" : "FALSE", null, source == null
                    ? null
                    : source.ResolvedPath,
                value, new BehaviourExpression[0], source);
        }

        private static ResolvedBooleanConstant Propagated(
            bool value, string original)
        {
            return new ResolvedBooleanConstant(
                value, original, null,
                BooleanConstantSource.PropagatedExpression,
                "BOOLEAN_CONSTANT_PROPAGATION");
        }

        private static bool IsConstant(BehaviourExpression expression)
        {
            return expression != null &&
                   expression.Kind == BehaviourExpressionKind.Constant &&
                   expression.ConstantValue.HasValue;
        }
    }
}
