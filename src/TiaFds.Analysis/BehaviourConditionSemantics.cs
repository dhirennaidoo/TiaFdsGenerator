using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public sealed class BehaviourSemanticInterpretation
    {
        public BehaviourSemanticInterpretation(
            BehaviourReviewClassification classification,
            string ruleIdentifier, bool requiresReview)
        {
            Classification = classification;
            RuleIdentifier = ruleIdentifier;
            RequiresReview = requiresReview;
        }
        public BehaviourReviewClassification Classification { get; }
        public string RuleIdentifier { get; }
        public bool RequiresReview { get; }
    }

    public interface IBehaviourConditionSemantics
    {
        BehaviourSemanticInterpretation Interpret(
            string moduleFamily, IReadOnlyList<string> processingVariants,
            BehaviouralConditionKind kind, string member,
            BehaviourConditionEffect effect, bool? effectiveValue);
    }

    public sealed class DefaultBehaviourConditionSemantics
        : IBehaviourConditionSemantics
    {
        public static readonly DefaultBehaviourConditionSemantics Instance =
            new DefaultBehaviourConditionSemantics();

        private DefaultBehaviourConditionSemantics()
        {
        }

        public BehaviourSemanticInterpretation Interpret(
            string moduleFamily, IReadOnlyList<string> processingVariants,
            BehaviouralConditionKind kind, string member,
            BehaviourConditionEffect effect, bool? effectiveValue)
        {
            if (effect == BehaviourConditionEffect.Dynamic)
                return new BehaviourSemanticInterpretation(
                    BehaviourReviewClassification.NormalDynamicCondition,
                    "DYNAMIC_RUNTIME_CONDITION", false);

            if (!effectiveValue.HasValue)
                return new BehaviourSemanticInterpretation(
                    BehaviourReviewClassification.RequiresManualInterpretation,
                    "INCOMPLETE_STATIC_ANALYSIS", true);

            bool confirmedDriveVariant = string.Equals(
                    moduleFamily, "Drive",
                    StringComparison.OrdinalIgnoreCase) &&
                HasOnlyDriveVariants(processingVariants);

            if (kind == BehaviouralConditionKind.Interlock &&
                confirmedDriveVariant)
                return new BehaviourSemanticInterpretation(
                    effectiveValue.Value
                        ? BehaviourReviewClassification.BridgedOrBypassed
                        : BehaviourReviewClassification.PermanentlyAsserted,
                    "DRV_ILK_ACTIVE_HIGH_PERMISSIVE", false);

            if (kind == BehaviouralConditionKind.StartCommand ||
                kind == BehaviouralConditionKind.ControlRequest)
                return new BehaviourSemanticInterpretation(
                    effectiveValue.Value
                        ? BehaviourReviewClassification.PermanentlyEnabled
                        : BehaviourReviewClassification.PermanentlyDisabled,
                    confirmedDriveVariant &&
                    kind == BehaviouralConditionKind.ControlRequest
                        ? "DRV_CR_ACTIVE_HIGH_ENABLE"
                        : (kind == BehaviouralConditionKind.StartCommand
                            ? "SA_ACTIVE_HIGH_REQUEST"
                            : "CR_ACTIVE_HIGH_REQUEST"),
                    false);

            // Drive variants share the confirmed polarity. Keep non-Drive
            // families and missing processing-variant evidence conservative.
            return new BehaviourSemanticInterpretation(
                BehaviourReviewClassification.RequiresManualInterpretation,
                "ILK_POLARITY_UNCONFIRMED", true);
        }

        private static bool HasOnlyDriveVariants(
            IReadOnlyList<string> processingVariants)
        {
            if (processingVariants == null || processingVariants.Count == 0)
                return false;
            foreach (string variant in processingVariants)
            {
                bool knownDriveVariant = false;
                foreach (ControlModuleFunctionDefinition definition in
                    ControlModuleFunctionCatalogue.Definitions)
                    if (string.Equals(definition.ModuleFamily, "Drive",
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(definition.VariantName, variant,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        knownDriveVariant = true;
                        break;
                    }
                if (!knownDriveVariant)
                    return false;
            }
            return true;
        }
    }
}
