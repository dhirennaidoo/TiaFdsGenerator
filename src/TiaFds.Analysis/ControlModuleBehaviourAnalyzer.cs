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
                    assignment.NetworkComment, assignment.StatementOrder, status));

                if (problemCode != null)
                    AddProblem(problemCode, problemMessage, assignment, module,
                        item.Member, item.Kind, diagnostics, reviews);
            }

            return new ControlModuleBehaviourResult(
                conditions, diagnostics, reviews, true);
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

        private static BehaviourExpression TraceExpression(
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
                    return TraceExpression(prior[0].SourceExpression, prior[0],
                        assignments, visiting, depth + 1, out status);
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
