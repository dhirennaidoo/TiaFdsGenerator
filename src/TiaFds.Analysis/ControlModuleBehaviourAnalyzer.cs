using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TiaFds.Core;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleBehaviourAnalyzer
    {
        private const int MaximumTraceDepth = 16;
        private static readonly Regex MemberPattern = new Regex(
            "^(SA|CR|ILK)(?:\\[(\\d+)\\]|(\\d+))?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private readonly BooleanConstantCatalogue constantCatalogue;
        private readonly IBehaviourConditionSemantics semantics;

        public ControlModuleBehaviourAnalyzer()
            : this(BooleanConstantCatalogue.Default,
                DefaultBehaviourConditionSemantics.Instance)
        {
        }

        public ControlModuleBehaviourAnalyzer(
            BooleanConstantCatalogue constantCatalogue,
            IBehaviourConditionSemantics semantics)
        {
            this.constantCatalogue = constantCatalogue ??
                BooleanConstantCatalogue.Default;
            this.semantics = semantics ??
                DefaultBehaviourConditionSemantics.Instance;
        }

        public ControlModuleBehaviourResult Analyze(
            EngineeringSnapshot snapshot,
            ControlModuleDiscoveryResult discovery,
            ControlModuleImplementationResult implementation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (discovery == null) throw new ArgumentNullException(nameof(discovery));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            PlcInventory inventory = snapshot.Project == null ? null : snapshot.Project.Inventory;
            if (inventory == null || !inventory.LogicAssignmentsIncluded)
                return new ControlModuleBehaviourResult(null, null, null, false);

            var conditions = new List<BehaviouralCondition>();
            var diagnostics = new List<BehaviouralDiagnostic>();
            var reviews = new List<BehaviouralManualReviewItem>();
            var assignmentsByScope = IndexAssignments(inventory.LogicAssignments);
            var behavioural = new List<AssignmentContext>();

            foreach (ExtractedLogicAssignment assignment in inventory.LogicAssignments)
            {
                ControlModuleInfo module = FindOwner(discovery.Modules,
                    assignment.ResolvedDestinationPath);
                if (module == null && !IsWithinKnownContainer(
                        discovery.Modules, assignment.ResolvedDestinationPath))
                    continue;
                string member = module == null
                    ? TerminalMember(assignment.ResolvedDestinationPath ??
                        assignment.DestinationExpression)
                    : RelativeMember(module.MemberPath,
                        assignment.ResolvedDestinationPath);
                BehaviouralConditionKind kind;
                int? index;
                if (!TryClassify(member, out kind, out index))
                {
                    if (module != null && LooksBehavioural(member))
                        AddProblem("BEH107_MEMBER_PATTERN_NOT_SUPPORTED",
                            "The destination member pattern is not supported.", assignment,
                            module, member, null, diagnostics, reviews);
                    continue;
                }
                behavioural.Add(new AssignmentContext(assignment, module, kind, member, index));
            }

            var duplicateCounts = behavioural
                .Where(item => !string.IsNullOrWhiteSpace(
                    item.Assignment.ResolvedDestinationPath))
                .GroupBy(item => item.Assignment.ResolvedDestinationPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (AssignmentContext item in behavioural)
            {
                ExtractedLogicAssignment assignment = item.Assignment;
                ControlModuleInfo module = item.Module;
                BehaviouralConditionResolutionStatus status;
                string problemCode = null;
                string problemMessage = null;

                BehaviourExpression expression = TraceExpression(
                    assignment.SourceExpression, assignment, assignmentsByScope,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0,
                    out status);
                BehaviouralConditionResolutionStatus tracedStatus = status;
                var simplification =
                    new BehaviourExpressionSimplifier(constantCatalogue)
                        .Simplify(expression);
                expression = simplification.OriginalExpression;

                if (simplification.EffectiveValue.HasValue)
                    status = BehaviouralConditionResolutionStatus.Complete;
                else if (tracedStatus ==
                         BehaviouralConditionResolutionStatus.Ambiguous)
                    status = tracedStatus;
                else if (simplification.HasCatalogueConflict ||
                         simplification.HasAmbiguousNumericLiteral ||
                         simplification.HasUnresolvedOperand)
                    status = BehaviouralConditionResolutionStatus.Partial;
                else if (simplification.HasUnsupportedNode)
                    status = BehaviouralConditionResolutionStatus.Unsupported;
                else
                    status = BehaviouralConditionResolutionStatus.Complete;

                int duplicateCount;
                if (!string.IsNullOrWhiteSpace(assignment.ResolvedDestinationPath) &&
                    duplicateCounts.TryGetValue(
                        assignment.ResolvedDestinationPath, out duplicateCount) &&
                    duplicateCount > 1)
                {
                    status = BehaviouralConditionResolutionStatus.Ambiguous;
                    problemCode = "BEH104_MULTIPLE_ASSIGNMENTS";
                    problemMessage = "The behavioural member has " + duplicateCount +
                        " extracted assignments; no overwrite was selected.";
                }
                else if (module == null)
                {
                    status = string.IsNullOrWhiteSpace(
                        assignment.ResolvedDestinationPath)
                        ? BehaviouralConditionResolutionStatus.Unresolved
                        : BehaviouralConditionResolutionStatus.Unresolved;
                    problemCode = string.IsNullOrWhiteSpace(
                        assignment.ResolvedDestinationPath)
                        ? "BEH100_BEHAVIOURAL_DESTINATION_UNRESOLVED"
                        : "BEH101_MODULE_OWNER_NOT_FOUND";
                    problemMessage = module == null &&
                        !string.IsNullOrWhiteSpace(assignment.ResolvedDestinationPath)
                        ? "No discovered control module owns the behavioural destination."
                        : "The behavioural destination could not be resolved.";
                }
                else if (status == BehaviouralConditionResolutionStatus.Unsupported)
                {
                    problemCode = ContainsTemporary(assignment.SourceExpression)
                        ? "BEH106_TEMPORARY_TRACE_INCOMPLETE"
                        : "BEH102_EXPRESSION_NOT_SUPPORTED";
                    problemMessage = ContainsTemporary(assignment.SourceExpression)
                        ? "The temporary-variable expression could not be traced unambiguously."
                        : "The behavioural expression contains an unsupported graph node.";
                }
                else if (status == BehaviouralConditionResolutionStatus.Ambiguous)
                {
                    problemCode = "BEH105_ASSIGNMENT_AMBIGUOUS";
                    problemMessage = "The behavioural expression has ambiguous assignment evidence.";
                }
                else if (status == BehaviouralConditionResolutionStatus.Partial)
                {
                    problemCode = expression != null &&
                        expression.Kind == BehaviourExpressionKind.Operand
                        ? "BEH103_OPERAND_NOT_RESOLVED"
                        : "BEH109_EXPRESSION_PARTIALLY_RESOLVED";
                    problemMessage = "The behavioural expression retains one or more unresolved operands.";
                }

                if (simplification.HasCatalogueConflict)
                {
                    problemCode = "BEH119_CONSTANT_CATALOGUE_CONFLICT";
                    problemMessage = "Conflicting boolean constant mappings match an expression operand.";
                }
                else if (simplification.HasAmbiguousNumericLiteral)
                {
                    problemCode = "BEH118_BOOLEAN_LITERAL_DATATYPE_AMBIGUOUS";
                    problemMessage = "Numeric 0/1 was not treated as boolean without datatype evidence.";
                }

                BehaviourExpressionSimplificationStatus simplificationStatus =
                    SimplificationStatus(simplification);
                BehaviourConditionEffect effect = Effect(simplification);
                IReadOnlyList<string> variants = ProcessingVariants(
                    implementation, module);
                BehaviourSemanticInterpretation interpretation =
                    semantics.Interpret(
                        module == null ? null : module.ModuleFamily,
                        variants, item.Kind, item.Member, effect,
                        simplification.EffectiveValue);
                IReadOnlyList<BehaviourConstantFinding> findings = BuildFindings(
                    item, simplification, effect, interpretation);

                var operands = new List<string>();
                var paths = new List<string>();
                CollectOperands(expression, operands, paths);
                conditions.Add(new BehaviouralCondition(
                    module == null ? null : module.ModuleFamily,
                    module == null ? null : module.Name,
                    module == null ? null : module.MemberPath,
                    item.Kind, item.Member, item.Index,
                    assignment.DestinationExpression,
                    assignment.ResolvedDestinationPath,
                    expression, assignment.OriginalSourceText,
                    operands.ToArray(), paths.ToArray(),
                    assignment.NetworkTitle,
                    assignment.BlockNumber, assignment.BlockName,
                    assignment.BlockType, assignment.BlockLanguage,
                    assignment.NetworkNumber, assignment.NetworkTitle,
                    assignment.NetworkComment, assignment.StatementOrder, status,
                    simplification.SimplifiedExpression,
                    simplification.EffectiveValue, simplificationStatus, effect,
                    interpretation.Classification,
                    interpretation.RuleIdentifier, findings));

                if (problemCode != null)
                    AddProblem(problemCode, problemMessage, assignment, module,
                        item.Member, item.Kind, diagnostics, reviews);

                if (simplification.EffectiveValue.HasValue)
                    AddDiagnostic(
                        simplification.EffectiveValue.Value
                            ? "BEH111_EXPRESSION_PERMANENTLY_TRUE"
                            : "BEH112_EXPRESSION_PERMANENTLY_FALSE",
                        "The behavioural expression simplifies to " +
                        (simplification.EffectiveValue.Value ? "TRUE." : "FALSE."),
                        assignment, module, item.Member, diagnostics);

                if (interpretation.RequiresReview &&
                    simplification.EffectiveValue.HasValue)
                    AddProblem("BEH115_CONSTANT_SEMANTICS_UNKNOWN",
                        "The expression is constant, but the condition polarity or engineering semantics are not confirmed.",
                        assignment, module, item.Member, item.Kind,
                        diagnostics, reviews);
            }

            return new ControlModuleBehaviourResult(
                conditions, diagnostics, reviews, true);
        }

        private static BehaviourExpressionSimplificationStatus SimplificationStatus(
            BehaviourExpressionSimplificationResult result)
        {
            if (result.EffectiveValue == true)
                return BehaviourExpressionSimplificationStatus.ConstantTrue;
            if (result.EffectiveValue == false)
                return BehaviourExpressionSimplificationStatus.ConstantFalse;
            if (result.HasUnsupportedNode)
                return BehaviourExpressionSimplificationStatus.Unsupported;
            if (result.HasUnresolvedOperand ||
                result.HasCatalogueConflict ||
                result.HasAmbiguousNumericLiteral)
                return BehaviourExpressionSimplificationStatus.Partial;
            return result.Changed
                ? BehaviourExpressionSimplificationStatus.Simplified
                : BehaviourExpressionSimplificationStatus.NotSimplified;
        }

        private static BehaviourConditionEffect Effect(
            BehaviourExpressionSimplificationResult result)
        {
            if (result.EffectiveValue == true)
                return BehaviourConditionEffect.PermanentlyTrue;
            if (result.EffectiveValue == false)
                return BehaviourConditionEffect.PermanentlyFalse;
            if ((result.HasUnresolvedOperand || result.HasUnsupportedNode ||
                 result.HasCatalogueConflict ||
                 result.HasAmbiguousNumericLiteral) && result.Changed)
                return BehaviourConditionEffect.PartiallySimplified;
            if (result.HasUnresolvedOperand || result.HasUnsupportedNode ||
                result.HasCatalogueConflict ||
                result.HasAmbiguousNumericLiteral)
                return BehaviourConditionEffect.Unknown;
            return BehaviourConditionEffect.Dynamic;
        }

        private static IReadOnlyList<string> ProcessingVariants(
            ControlModuleImplementationResult implementation,
            ControlModuleInfo module)
        {
            if (module == null) return new string[0];
            var values = new List<string>();
            foreach (ControlModuleImplementation item in implementation.Modules)
                if (string.Equals(item.MemberPath, module.MemberPath,
                        StringComparison.OrdinalIgnoreCase))
                    foreach (ControlModuleCallSite site in item.CallSites)
                        if (!string.IsNullOrWhiteSpace(site.ProcessingVariant) &&
                            !values.Contains(site.ProcessingVariant))
                            values.Add(site.ProcessingVariant);
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return values.ToArray();
        }

        private static IReadOnlyList<BehaviourConstantFinding> BuildFindings(
            AssignmentContext item,
            BehaviourExpressionSimplificationResult simplification,
            BehaviourConditionEffect effect,
            BehaviourSemanticInterpretation interpretation)
        {
            var result = new List<BehaviourConstantFinding>();
            foreach (ResolvedBooleanConstant constant in simplification.Constants)
                if (constant.Source ==
                        BooleanConstantSource.KnownProjectSymbol ||
                    constant.Source ==
                        BooleanConstantSource.TemporaryTrace)
                    result.Add(Finding(item,
                        BehaviourConstantFindingKind.KnownConstantResolved,
                        "Information", simplification, interpretation,
                        constant.Value,
                        "Resolved exact boolean constant symbol '" +
                        constant.OriginalSourceText + "' using " +
                        constant.Evidence + "."));

            if (simplification.Changed &&
                !simplification.EffectiveValue.HasValue)
                result.Add(Finding(item,
                    BehaviourConstantFindingKind.ConstantBranchRemoved,
                    "Information", simplification, interpretation, null,
                    "The supported expression was simplified without changing its dynamic meaning."));

            if (simplification.EffectiveValue.HasValue)
                result.Add(Finding(item,
                    simplification.EffectiveValue.Value
                        ? BehaviourConstantFindingKind.ConditionPermanentlyTrue
                        : BehaviourConstantFindingKind.ConditionPermanentlyFalse,
                    "Warning", simplification, interpretation,
                    simplification.EffectiveValue,
                    "The behavioural condition simplifies to a permanent boolean value."));

            if (interpretation.Classification ==
                BehaviourReviewClassification.PermanentlyDisabled)
                result.Add(Finding(item,
                    BehaviourConstantFindingKind.ConditionDisabled,
                    "Warning", simplification, interpretation,
                    simplification.EffectiveValue,
                    "The active-high command/request channel is permanently disabled."));
            else if (interpretation.Classification ==
                     BehaviourReviewClassification.BridgedOrBypassed)
                result.Add(Finding(item,
                    BehaviourConstantFindingKind.ConditionBridged,
                    "Warning", simplification, interpretation,
                    simplification.EffectiveValue,
                    "The confirmed semantic rule identifies a bridged or bypassed condition."));
            else if (interpretation.Classification ==
                     BehaviourReviewClassification.PermanentlyAsserted)
                result.Add(Finding(item,
                    BehaviourConstantFindingKind.ConditionPermanentlyAsserted,
                    "Warning", simplification, interpretation,
                    simplification.EffectiveValue,
                    "The confirmed semantic rule identifies a permanently asserted condition."));
            else if (interpretation.RequiresReview &&
                     simplification.EffectiveValue.HasValue)
                result.Add(Finding(item,
                    BehaviourConstantFindingKind.ConstantSemanticsUnknown,
                    "Warning", simplification, interpretation,
                    simplification.EffectiveValue,
                    "The raw constant value is known, but its engineering meaning requires confirmation."));
            return result.ToArray();
        }

        private static BehaviourConstantFinding Finding(
            AssignmentContext item, BehaviourConstantFindingKind kind,
            string severity,
            BehaviourExpressionSimplificationResult simplification,
            BehaviourSemanticInterpretation interpretation,
            bool? value, string message)
        {
            ExtractedLogicAssignment assignment = item.Assignment;
            ControlModuleInfo module = item.Module;
            return new BehaviourConstantFinding(
                kind, severity,
                module == null ? null : module.ModuleFamily,
                module == null ? null : module.Name,
                module == null ? assignment.ResolvedDestinationPath : module.MemberPath,
                item.Kind, item.Member,
                assignment.OriginalSourceText,
                simplification.SimplifiedExpression == null
                    ? null
                    : simplification.SimplifiedExpression.DisplayText,
                value, interpretation.Classification,
                interpretation.RuleIdentifier,
                assignment.BlockNumber, assignment.BlockName,
                assignment.NetworkNumber, assignment.NetworkTitle, message);
        }

        private static Dictionary<string, List<ExtractedLogicAssignment>> IndexAssignments(
            IReadOnlyList<ExtractedLogicAssignment> assignments)
        {
            var result = new Dictionary<string, List<ExtractedLogicAssignment>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ExtractedLogicAssignment assignment in assignments)
            {
                string key = ScopeKey(assignment, assignment.DestinationExpression);
                List<ExtractedLogicAssignment> values;
                if (!result.TryGetValue(key, out values))
                {
                    values = new List<ExtractedLogicAssignment>();
                    result.Add(key, values);
                }
                values.Add(assignment);
            }
            return result;
        }

        private BehaviourExpression TraceExpression(
            ExtractedBooleanExpression source,
            ExtractedLogicAssignment owner,
            IDictionary<string, List<ExtractedLogicAssignment>> assignments,
            ISet<string> visiting,
            int depth,
            out BehaviouralConditionResolutionStatus status)
        {
            if (source == null || depth > MaximumTraceDepth)
            {
                status = BehaviouralConditionResolutionStatus.Unsupported;
                return Unknown(depth > MaximumTraceDepth
                    ? "Maximum temporary-variable trace depth exceeded."
                    : "Expression is missing.");
            }
            if (source.Kind == ExtractedBooleanExpressionKind.Operand &&
                (source.DisplayText ?? string.Empty).StartsWith("#",
                    StringComparison.Ordinal))
            {
                string key = ScopeKey(owner, source.DisplayText);
                List<ExtractedLogicAssignment> candidates;
                if (!assignments.TryGetValue(key, out candidates))
                {
                    status = BehaviouralConditionResolutionStatus.Partial;
                    return Copy(source, null);
                }
                var prior = candidates.Where(item =>
                    item.StatementOrder < owner.StatementOrder).ToList();
                if (prior.Count != 1 || !visiting.Add(key))
                {
                    status = prior.Count > 1
                        ? BehaviouralConditionResolutionStatus.Ambiguous
                        : BehaviouralConditionResolutionStatus.Unsupported;
                    return Unknown(prior.Count > 1
                        ? "Temporary variable has multiple prior assignments."
                        : "Temporary-variable trace is circular or incomplete.");
                }
                try
                {
                    BehaviourExpression traced = TraceExpression(
                        prior[0].SourceExpression, prior[0],
                        assignments, visiting, depth + 1, out status);
                    return MarkTemporaryTrace(traced, source.DisplayText);
                }
                finally { visiting.Remove(key); }
            }

            var children = new List<BehaviourExpression>();
            status = source.Kind == ExtractedBooleanExpressionKind.Unknown
                ? BehaviouralConditionResolutionStatus.Unsupported
                : source.Kind == ExtractedBooleanExpressionKind.Operand &&
                  string.IsNullOrWhiteSpace(source.ResolvedPath)
                    ? BehaviouralConditionResolutionStatus.Partial
                    : BehaviouralConditionResolutionStatus.Complete;
            foreach (ExtractedBooleanExpression child in source.Children)
            {
                BehaviouralConditionResolutionStatus childStatus;
                children.Add(TraceExpression(child, owner, assignments, visiting,
                    depth + 1, out childStatus));
                status = Worse(status, childStatus);
            }
            return Copy(source, children.ToArray());
        }

        private BehaviourExpression MarkTemporaryTrace(
            BehaviourExpression expression, string temporaryName)
        {
            if (expression == null) return null;
            ResolvedBooleanConstant resolved = expression.ResolvedConstant;
            if (resolved == null)
            {
                bool conflict;
                constantCatalogue.TryResolve(
                    expression.DisplayText, expression.ResolvedPath,
                    out resolved, out conflict);
                if (resolved == null && expression.Kind ==
                    BehaviourExpressionKind.Constant &&
                    expression.ConstantValue.HasValue)
                    resolved = new ResolvedBooleanConstant(
                        expression.ConstantValue.Value,
                        expression.DisplayText, expression.ResolvedPath,
                        BooleanConstantSource.Literal,
                        "TIA_BOOLEAN_LITERAL");
            }
            if (resolved != null)
                resolved = new ResolvedBooleanConstant(
                    resolved.Value, temporaryName, resolved.ResolvedPath,
                    BooleanConstantSource.TemporaryTrace,
                    "TEMPORARY_TRACE:" + temporaryName + "->" +
                    resolved.Evidence);
            return new BehaviourExpression(
                expression.Kind, expression.DisplayText, expression.Operand,
                expression.ResolvedPath, expression.ConstantValue,
                expression.Children, resolved);
        }

        private static BehaviouralConditionResolutionStatus Worse(
            BehaviouralConditionResolutionStatus left,
            BehaviouralConditionResolutionStatus right)
        {
            return Rank(right) > Rank(left) ? right : left;
        }

        private static int Rank(BehaviouralConditionResolutionStatus status)
        {
            switch (status)
            {
                case BehaviouralConditionResolutionStatus.Complete: return 0;
                case BehaviouralConditionResolutionStatus.Partial: return 1;
                case BehaviouralConditionResolutionStatus.Unsupported: return 2;
                case BehaviouralConditionResolutionStatus.Unresolved: return 3;
                case BehaviouralConditionResolutionStatus.Ambiguous: return 4;
                default: return 5;
            }
        }

        private static BehaviourExpression Copy(
            ExtractedBooleanExpression source,
            IReadOnlyList<BehaviourExpression> children)
        {
            return new BehaviourExpression(
                (BehaviourExpressionKind)Enum.Parse(
                    typeof(BehaviourExpressionKind), source.Kind.ToString(), false),
                source.DisplayText,
                source.Kind == ExtractedBooleanExpressionKind.Operand
                    ? source.DisplayText
                    : null,
                source.ResolvedPath, source.ConstantValue,
                children ?? new BehaviourExpression[0]);
        }

        private static BehaviourExpression Unknown(string message)
        {
            return new BehaviourExpression(
                BehaviourExpressionKind.Unknown, message, null, null, null,
                new BehaviourExpression[0]);
        }

        private static void CollectOperands(
            BehaviourExpression expression, IList<string> operands, IList<string> paths)
        {
            if (expression == null) return;
            if (expression.Kind == BehaviourExpressionKind.Operand)
            {
                if (!string.IsNullOrWhiteSpace(expression.Operand) &&
                    !operands.Contains(expression.Operand))
                    operands.Add(expression.Operand);
                if (!string.IsNullOrWhiteSpace(expression.ResolvedPath) &&
                    !paths.Contains(expression.ResolvedPath))
                    paths.Add(expression.ResolvedPath);
            }
            foreach (BehaviourExpression child in expression.Children)
                CollectOperands(child, operands, paths);
        }

        private static bool ContainsTemporary(ExtractedBooleanExpression expression)
        {
            if (expression == null) return false;
            if (expression.Kind == ExtractedBooleanExpressionKind.Operand &&
                (expression.DisplayText ?? string.Empty).StartsWith("#",
                    StringComparison.Ordinal)) return true;
            foreach (ExtractedBooleanExpression child in expression.Children)
                if (ContainsTemporary(child)) return true;
            return false;
        }

        private static ControlModuleInfo FindOwner(
            IReadOnlyList<ControlModuleInfo> modules, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return null;
            ControlModuleInfo best = null;
            foreach (ControlModuleInfo module in modules)
                if (destination.StartsWith(module.MemberPath + ".",
                        StringComparison.OrdinalIgnoreCase) &&
                    (best == null ||
                     module.MemberPath.Length > best.MemberPath.Length))
                    best = module;
            return best;
        }

        private static bool IsWithinKnownContainer(
            IReadOnlyList<ControlModuleInfo> modules, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return false;
            foreach (ControlModuleInfo module in modules)
                if (!string.IsNullOrWhiteSpace(module.ContainerDbName) &&
                    destination.StartsWith(module.ContainerDbName + ".",
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string RelativeMember(string owner, string destination)
        {
            if (string.IsNullOrWhiteSpace(owner) ||
                string.IsNullOrWhiteSpace(destination) ||
                !destination.StartsWith(owner + ".", StringComparison.OrdinalIgnoreCase))
                return null;
            return destination.Substring(owner.Length + 1);
        }

        private static string TerminalMember(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            int dot = value.LastIndexOf('.');
            return dot < 0 ? value : value.Substring(dot + 1);
        }

        private static bool TryClassify(
            string member, out BehaviouralConditionKind kind, out int? index)
        {
            kind = default(BehaviouralConditionKind);
            index = null;
            Match match = MemberPattern.Match(member ?? string.Empty);
            if (!match.Success) return false;
            string category = match.Groups[1].Value;
            kind = string.Equals(category, "SA", StringComparison.OrdinalIgnoreCase)
                ? BehaviouralConditionKind.StartCommand
                : string.Equals(category, "CR", StringComparison.OrdinalIgnoreCase)
                    ? BehaviouralConditionKind.ControlRequest
                    : BehaviouralConditionKind.Interlock;
            string number = match.Groups[2].Success
                ? match.Groups[2].Value
                : match.Groups[3].Value;
            int parsed;
            if (!string.IsNullOrWhiteSpace(number) &&
                int.TryParse(number, out parsed)) index = parsed;
            return true;
        }

        private static bool LooksBehavioural(string member)
        {
            return (member ?? string.Empty).StartsWith("SA",
                       StringComparison.OrdinalIgnoreCase) ||
                   (member ?? string.Empty).StartsWith("CR",
                       StringComparison.OrdinalIgnoreCase) ||
                   (member ?? string.Empty).StartsWith("ILK",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ScopeKey(
            ExtractedLogicAssignment assignment, string variable)
        {
            return (assignment.BlockNumber.HasValue
                ? assignment.BlockNumber.Value.ToString()
                : assignment.BlockName ?? string.Empty) + "\u001f" +
                (variable ?? string.Empty);
        }

        private static void AddProblem(
            string code, string message, ExtractedLogicAssignment assignment,
            ControlModuleInfo module, string member, BehaviouralConditionKind? kind,
            IList<BehaviouralDiagnostic> diagnostics,
            IList<BehaviouralManualReviewItem> reviews)
        {
            diagnostics.Add(new BehaviouralDiagnostic(
                "Warning", code, message,
                module == null ? assignment.ResolvedDestinationPath : module.MemberPath,
                member, assignment.BlockNumber, assignment.BlockName,
                assignment.NetworkNumber, assignment.NetworkTitle,
                assignment.OriginalSourceText));
            reviews.Add(new BehaviouralManualReviewItem(
                code, module == null ? null : module.ModuleFamily,
                module == null ? null : module.Name,
                module == null ? assignment.ResolvedDestinationPath : module.MemberPath,
                kind, member, assignment.BlockNumber, assignment.BlockName,
                assignment.NetworkNumber, assignment.OriginalSourceText, message));
        }

        private static void AddDiagnostic(
            string code, string message, ExtractedLogicAssignment assignment,
            ControlModuleInfo module, string member,
            IList<BehaviouralDiagnostic> diagnostics)
        {
            diagnostics.Add(new BehaviouralDiagnostic(
                "Warning", code, message,
                module == null
                    ? assignment.ResolvedDestinationPath
                    : module.MemberPath,
                member, assignment.BlockNumber, assignment.BlockName,
                assignment.NetworkNumber, assignment.NetworkTitle,
                assignment.OriginalSourceText));
        }

        private sealed class AssignmentContext
        {
            public AssignmentContext(
                ExtractedLogicAssignment assignment, ControlModuleInfo module,
                BehaviouralConditionKind kind, string member, int? index)
            {
                Assignment = assignment;
                Module = module;
                Kind = kind;
                Member = member;
                Index = index;
            }
            public ExtractedLogicAssignment Assignment { get; }
            public ControlModuleInfo Module { get; }
            public BehaviouralConditionKind Kind { get; }
            public string Member { get; }
            public int? Index { get; }
        }
    }
}
