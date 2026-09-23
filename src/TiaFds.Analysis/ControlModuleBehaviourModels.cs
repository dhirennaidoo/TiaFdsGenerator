using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public enum BehaviouralConditionKind
    {
        StartCommand,
        ControlRequest,
        Interlock
    }

    public enum BehaviouralConditionResolutionStatus
    {
        Complete,
        Partial,
        Unsupported,
        Unresolved,
        Ambiguous
    }

    public enum BehaviourExpressionKind
    {
        Operand,
        Constant,
        Not,
        And,
        Or,
        Unknown
    }

    public enum BooleanConstantSource
    {
        Literal,
        DeclaredConstant,
        KnownProjectSymbol,
        PropagatedExpression,
        TemporaryTrace,
        Unknown
    }

    public enum BehaviourExpressionSimplificationStatus
    {
        NotSimplified,
        Simplified,
        ConstantTrue,
        ConstantFalse,
        Partial,
        Unsupported
    }

    public enum BehaviourConditionEffect
    {
        Dynamic,
        PermanentlyTrue,
        PermanentlyFalse,
        PartiallySimplified,
        Unknown
    }

    public enum BehaviourReviewClassification
    {
        NormalDynamicCondition,
        PermanentlyEnabled,
        PermanentlyDisabled,
        BridgedOrBypassed,
        PermanentlyAsserted,
        RequiresManualInterpretation
    }

    public enum BehaviourConstantFindingKind
    {
        ConditionPermanentlyTrue,
        ConditionPermanentlyFalse,
        ConditionBridged,
        ConditionDisabled,
        ConditionPermanentlyAsserted,
        ConstantBranchRemoved,
        KnownConstantResolved,
        ConstantSemanticsUnknown
    }

    public sealed class ResolvedBooleanConstant
    {
        public ResolvedBooleanConstant(
            bool value, string originalSourceText, string resolvedPath,
            BooleanConstantSource source, string evidence)
        {
            Value = value;
            OriginalSourceText = originalSourceText;
            ResolvedPath = resolvedPath;
            Source = source;
            Evidence = evidence;
        }
        public bool Value { get; }
        public string OriginalSourceText { get; }
        public string ResolvedPath { get; }
        public BooleanConstantSource Source { get; }
        public string Evidence { get; }
    }

    public sealed class BehaviourExpression
    {
        public BehaviourExpression(
            BehaviourExpressionKind kind, string displayText, string operand,
            string resolvedPath, bool? constantValue,
            IReadOnlyList<BehaviourExpression> children)
            : this(kind, displayText, operand, resolvedPath, constantValue,
                children, null)
        {
        }

        public BehaviourExpression(
            BehaviourExpressionKind kind, string displayText, string operand,
            string resolvedPath, bool? constantValue,
            IReadOnlyList<BehaviourExpression> children,
            ResolvedBooleanConstant resolvedConstant)
        {
            Kind = kind;
            DisplayText = displayText;
            Operand = operand;
            ResolvedPath = resolvedPath;
            ConstantValue = constantValue;
            Children = Copy(children);
            ResolvedConstant = resolvedConstant;
        }
        public BehaviourExpressionKind Kind { get; }
        public string DisplayText { get; }
        public string Operand { get; }
        public string ResolvedPath { get; }
        public bool? ConstantValue { get; }
        public IReadOnlyList<BehaviourExpression> Children { get; }
        public ResolvedBooleanConstant ResolvedConstant { get; }

        private static IReadOnlyList<BehaviourExpression> Copy(
            IReadOnlyList<BehaviourExpression> source)
        {
            if (source == null || source.Count == 0) return new BehaviourExpression[0];
            var result = new BehaviourExpression[source.Count];
            for (var index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }
    }

    public sealed class BehaviourConstantFinding
    {
        public BehaviourConstantFinding(
            BehaviourConstantFindingKind findingKind, string severity,
            string moduleFamily, string moduleName, string moduleMemberPath,
            BehaviouralConditionKind conditionKind, string conditionMember,
            string originalExpression, string simplifiedExpression,
            bool? constantValue, BehaviourReviewClassification reviewClassification,
            string semanticRule, int? blockNumber, string blockName,
            int? networkNumber, string networkTitle, string message)
        {
            FindingKind = findingKind;
            Severity = severity;
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            ModuleMemberPath = moduleMemberPath;
            ConditionKind = conditionKind;
            ConditionMember = conditionMember;
            OriginalExpression = originalExpression;
            SimplifiedExpression = simplifiedExpression;
            ConstantValue = constantValue;
            ReviewClassification = reviewClassification;
            SemanticRule = semanticRule;
            BlockNumber = blockNumber;
            BlockName = blockName;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            Message = message;
        }
        public BehaviourConstantFindingKind FindingKind { get; }
        public string Severity { get; }
        public string ModuleFamily { get; }
        public string ModuleName { get; }
        public string ModuleMemberPath { get; }
        public BehaviouralConditionKind ConditionKind { get; }
        public string ConditionMember { get; }
        public string OriginalExpression { get; }
        public string SimplifiedExpression { get; }
        public bool? ConstantValue { get; }
        public BehaviourReviewClassification ReviewClassification { get; }
        public string SemanticRule { get; }
        public int? BlockNumber { get; }
        public string BlockName { get; }
        public int? NetworkNumber { get; }
        public string NetworkTitle { get; }
        public string Message { get; }
    }

    public sealed class BehaviouralCondition
    {
        public BehaviouralCondition(
            string moduleFamily, string moduleName, string moduleMemberPath,
            BehaviouralConditionKind kind, string member, int? index,
            string destinationExpression, string resolvedDestinationPath,
            BehaviourExpression expression, string sourceExpression,
            IReadOnlyList<string> sourceOperands, IReadOnlyList<string> resolvedOperandPaths,
            string description, int? blockNumber, string blockName, string blockType,
            string blockLanguage, int? networkNumber, string networkTitle,
            string networkComment, int statementOrder,
            BehaviouralConditionResolutionStatus resolutionStatus)
            : this(moduleFamily, moduleName, moduleMemberPath, kind, member, index,
                destinationExpression, resolvedDestinationPath, expression,
                sourceExpression, sourceOperands, resolvedOperandPaths, description,
                blockNumber, blockName, blockType, blockLanguage, networkNumber,
                networkTitle, networkComment, statementOrder, resolutionStatus,
                expression, null, BehaviourExpressionSimplificationStatus.NotSimplified,
                BehaviourConditionEffect.Unknown,
                BehaviourReviewClassification.RequiresManualInterpretation,
                null, null)
        {
        }

        public BehaviouralCondition(
            string moduleFamily, string moduleName, string moduleMemberPath,
            BehaviouralConditionKind kind, string member, int? index,
            string destinationExpression, string resolvedDestinationPath,
            BehaviourExpression originalExpression, string sourceExpression,
            IReadOnlyList<string> sourceOperands, IReadOnlyList<string> resolvedOperandPaths,
            string description, int? blockNumber, string blockName, string blockType,
            string blockLanguage, int? networkNumber, string networkTitle,
            string networkComment, int statementOrder,
            BehaviouralConditionResolutionStatus resolutionStatus,
            BehaviourExpression simplifiedExpression, bool? effectiveConstantValue,
            BehaviourExpressionSimplificationStatus simplificationStatus,
            BehaviourConditionEffect effect,
            BehaviourReviewClassification reviewClassification,
            string semanticRule, IReadOnlyList<BehaviourConstantFinding> constantFindings)
        {
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            ModuleMemberPath = moduleMemberPath;
            Kind = kind;
            Member = member;
            Index = index;
            DestinationExpression = destinationExpression;
            ResolvedDestinationPath = resolvedDestinationPath;
            Expression = originalExpression;
            SourceExpression = sourceExpression;
            SourceOperands = Copy(sourceOperands);
            ResolvedOperandPaths = Copy(resolvedOperandPaths);
            Description = description;
            BlockNumber = blockNumber;
            BlockName = blockName;
            BlockType = blockType;
            BlockLanguage = blockLanguage;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            NetworkComment = networkComment;
            StatementOrder = statementOrder;
            ResolutionStatus = resolutionStatus;
            SimplifiedExpression = simplifiedExpression;
            EffectiveConstantValue = effectiveConstantValue;
            SimplificationStatus = simplificationStatus;
            Effect = effect;
            ReviewClassification = reviewClassification;
            SemanticRule = semanticRule;
            ConstantFindings = Copy(constantFindings);
        }
        public string ModuleFamily { get; }
        public string ModuleName { get; }
        public string ModuleMemberPath { get; }
        public BehaviouralConditionKind Kind { get; }
        public string Member { get; }
        public int? Index { get; }
        public string DestinationExpression { get; }
        public string ResolvedDestinationPath { get; }
        public BehaviourExpression Expression { get; }
        public BehaviourExpression OriginalExpression { get { return Expression; } }
        public BehaviourExpression SimplifiedExpression { get; }
        public bool? EffectiveConstantValue { get; }
        public BehaviourExpressionSimplificationStatus SimplificationStatus { get; }
        public BehaviourConditionEffect Effect { get; }
        public BehaviourReviewClassification ReviewClassification { get; }
        public string SemanticRule { get; }
        public IReadOnlyList<BehaviourConstantFinding> ConstantFindings { get; }
        public string SourceExpression { get; }
        public IReadOnlyList<string> SourceOperands { get; }
        public IReadOnlyList<string> ResolvedOperandPaths { get; }
        public string Description { get; }
        public int? BlockNumber { get; }
        public string BlockName { get; }
        public string BlockType { get; }
        public string BlockLanguage { get; }
        public int? NetworkNumber { get; }
        public string NetworkTitle { get; }
        public string NetworkComment { get; }
        public int StatementOrder { get; }
        public BehaviouralConditionResolutionStatus ResolutionStatus { get; }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0) return new string[0];
            var result = new string[source.Count];
            for (var index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }

        private static IReadOnlyList<BehaviourConstantFinding> Copy(
            IReadOnlyList<BehaviourConstantFinding> source)
        {
            if (source == null || source.Count == 0)
                return new BehaviourConstantFinding[0];
            var result = new BehaviourConstantFinding[source.Count];
            for (var index = 0; index < source.Count; index++)
                result[index] = source[index];
            return result;
        }
    }

    public sealed class BehaviouralDiagnostic
    {
        public BehaviouralDiagnostic(
            string severity, string code, string message, string modulePath,
            string member, int? blockNumber, string blockName,
            int? networkNumber, string networkTitle, string sourceExpression)
        {
            Severity = severity;
            Code = code;
            Message = message;
            ModulePath = modulePath;
            Member = member;
            BlockNumber = blockNumber;
            BlockName = blockName;
            NetworkNumber = networkNumber;
            NetworkTitle = networkTitle;
            SourceExpression = sourceExpression;
        }
        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string ModulePath { get; }
        public string Member { get; }
        public int? BlockNumber { get; }
        public string BlockName { get; }
        public int? NetworkNumber { get; }
        public string NetworkTitle { get; }
        public string SourceExpression { get; }
    }

    public sealed class BehaviouralManualReviewItem
    {
        public BehaviouralManualReviewItem(
            string code, string moduleFamily, string moduleName, string modulePath,
            BehaviouralConditionKind? kind, string member, int? blockNumber,
            string blockName, int? networkNumber, string expression, string reason)
        {
            Code = code;
            ModuleFamily = moduleFamily;
            ModuleName = moduleName;
            ModulePath = modulePath;
            Kind = kind;
            Member = member;
            BlockNumber = blockNumber;
            BlockName = blockName;
            NetworkNumber = networkNumber;
            Expression = expression;
            Reason = reason;
        }
        public string Code { get; }
        public string ModuleFamily { get; }
        public string ModuleName { get; }
        public string ModulePath { get; }
        public BehaviouralConditionKind? Kind { get; }
        public string Member { get; }
        public int? BlockNumber { get; }
        public string BlockName { get; }
        public int? NetworkNumber { get; }
        public string Expression { get; }
        public string Reason { get; }
    }

    public sealed class ControlModuleBehaviourResult
    {
        public ControlModuleBehaviourResult(
            IReadOnlyList<BehaviouralCondition> conditions,
            IReadOnlyList<BehaviouralDiagnostic> diagnostics,
            IReadOnlyList<BehaviouralManualReviewItem> manualReview,
            bool logicAssignmentsAvailable)
        {
            Conditions = CopyAndSort(conditions);
            Diagnostics = CopyAndSort(diagnostics);
            ManualReview = CopyAndSort(manualReview);
            var findings = new List<BehaviourConstantFinding>();
            foreach (BehaviouralCondition condition in Conditions)
                findings.AddRange(condition.ConstantFindings);
            findings.Sort((left, right) =>
            {
                int value = Compare(left.ModuleFamily, right.ModuleFamily);
                if (value != 0) return value;
                value = Compare(left.ModuleMemberPath, right.ModuleMemberPath);
                if (value != 0) return value;
                value = left.ConditionKind.CompareTo(right.ConditionKind);
                if (value != 0) return value;
                return left.FindingKind.CompareTo(right.FindingKind);
            });
            ConstantFindings = findings.ToArray();
            LogicAssignmentsAvailable = logicAssignmentsAvailable;
        }
        public IReadOnlyList<BehaviouralCondition> Conditions { get; }
        public IReadOnlyList<BehaviouralDiagnostic> Diagnostics { get; }
        public IReadOnlyList<BehaviouralManualReviewItem> ManualReview { get; }
        public IReadOnlyList<BehaviourConstantFinding> ConstantFindings { get; }
        public bool LogicAssignmentsAvailable { get; }

        private static IReadOnlyList<BehaviouralCondition> CopyAndSort(
            IReadOnlyList<BehaviouralCondition> source)
        {
            var result = source == null
                ? new List<BehaviouralCondition>()
                : new List<BehaviouralCondition>(source);
            result.Sort((left, right) =>
            {
                int value = Compare(left.ModuleFamily, right.ModuleFamily);
                if (value != 0) return value;
                value = Compare(left.ModuleMemberPath, right.ModuleMemberPath);
                if (value != 0) return value;
                value = left.Kind.CompareTo(right.Kind);
                if (value != 0) return value;
                value = Nullable.Compare(left.Index, right.Index);
                if (value != 0) return value;
                value = Compare(left.Member, right.Member);
                if (value != 0) return value;
                value = Nullable.Compare(left.BlockNumber, right.BlockNumber);
                return value != 0
                    ? value
                    : left.StatementOrder.CompareTo(right.StatementOrder);
            });
            return result.ToArray();
        }

        private static IReadOnlyList<BehaviouralDiagnostic> CopyAndSort(
            IReadOnlyList<BehaviouralDiagnostic> source)
        {
            var result = source == null
                ? new List<BehaviouralDiagnostic>()
                : new List<BehaviouralDiagnostic>(source);
            result.Sort((left, right) =>
            {
                int value = Compare(left.Code, right.Code);
                if (value != 0) return value;
                value = Compare(left.ModulePath, right.ModulePath);
                return value != 0 ? value : Compare(left.Message, right.Message);
            });
            return result.ToArray();
        }

        private static IReadOnlyList<BehaviouralManualReviewItem> CopyAndSort(
            IReadOnlyList<BehaviouralManualReviewItem> source)
        {
            var result = source == null
                ? new List<BehaviouralManualReviewItem>()
                : new List<BehaviouralManualReviewItem>(source);
            result.Sort((left, right) =>
            {
                int value = Compare(left.Code, right.Code);
                if (value != 0) return value;
                value = Compare(left.ModuleFamily, right.ModuleFamily);
                if (value != 0) return value;
                return Compare(left.ModulePath, right.ModulePath);
            });
            return result.ToArray();
        }

        private static int Compare(string left, string right)
        {
            int value = StringComparer.OrdinalIgnoreCase.Compare(
                left ?? string.Empty, right ?? string.Empty);
            return value != 0
                ? value
                : StringComparer.Ordinal.Compare(
                    left ?? string.Empty, right ?? string.Empty);
        }
    }
}
