using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    // Extracts boolean assignments (=, S, R) from decompiled STL by evaluating the logic string
    // the way the CPU does (RLO, first check, standalone O, nesting stack). LAD and FBD blocks
    // compile to the same STL, so all three languages are covered.
    //
    // Supported straight-line evaluation per network:
    // - A/AN/O/ON with operands, A(/AN(/O(/ON( ... ), standalone O, NOT, SET, CLR
    // - comparisons (==I, <>D, >R, ...) rendered as operands such as "#Seq.StepNo == 110"
    // - anonymous LAD local bits (= L 0.0 ... A L 0.0) substituted within the network
    // - InOut/UDT parameter access through the compiler's pointer preamble
    //   (L P##Seq ... LAR1 ... A [AR1,P#0.0]) rendered as "#Seq.SG"
    // - forward jumps that only skip non-boolean code (the EN/ENO pattern of MOVE boxes)
    //
    // Anything else that could make an assignment conditional (jumps over writes, backward
    // jumps, MCR, BEC, XOR, edges) is kept as an Unknown source, never guessed.
    internal sealed class Step7StlLogicParser
    {
        private static readonly Regex LocalBit = new Regex(
            @"^L(?<byte>\d+)\.(?<bit>[0-7])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AreaPointer = new Regex(
            @"^(?<width>[BWD])?\[AR1,P#(?<byte>\d+)\.(?<bit>[0-7])\]$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LocalWord = new Regex(
            @"^L[BWD]\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OpenDataBlock = new Regex(
            @"^DB(?<db>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex UnqualifiedDbBit = new Regex(
            @"^DBX(?<byte>\d+)\.(?<bit>[0-7])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Comparison = new Regex(
            @"^(==|<>|>=|<=|>|<)(I|D|R)$", RegexOptions.CultureInvariant);
        private static readonly Regex Arithmetic = new Regex(
            @"^[+\-*/](I|D|R)$|^MOD$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Jumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "JU", "JC", "JCN", "JCB", "JNB", "JBI", "JNBI", "JO", "JOS", "JZ", "JN", "JP", "JM", "JPZ", "JMZ", "JUO"
        };

        // Status-word bits read as operands (A BR, A OV, A ==0 ...).
        private static readonly HashSet<string> StatusBits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BR", "OV", "OS", "UO", "==0", "<>0", ">0", "<0", ">=0", "<=0"
        };

        // Commands that neither read nor write the RLO.
        private static readonly HashSet<string> Neutral = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BLD", "NOP", "SAVE", "LAR2", "TAR1", "TAR2", "CAR", "+AR1", "+AR2", "INC", "DEC",
            "INVI", "INVD", "NEGI", "NEGD", "NEGR", "ITD", "ITB", "BTI", "DTB", "BTD", "DTR", "RND",
            "RND+", "RND-", "TRUNC", "ABS", "SQRT", "SQR", "LN", "EXP", "SIN", "COS", "TAN", "ASIN",
            "ACOS", "ATAN", "CAW", "CAD", "PUSH", "POP", "ENT", "LEAVE", "SLW", "SRW", "SLD", "SRD",
            "RLD", "RRD", "SSI", "SSD", "AW", "OW", "XOW", "AD", "OD", "XOD", "CDB", "TDB", "LC", "+"
        };

        // Commands that consume the RLO without writing a tracked boolean destination.
        private static readonly HashSet<string> Consumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SP", "SE", "SD", "SS", "SF", "CU", "CD", "FR"
        };

        private readonly Step7SymbolLookup symbols;
        private readonly Step7AddressResolver addresses;

        public Step7StlLogicParser(Step7SymbolLookup symbols, Step7AddressResolver addresses)
        {
            this.symbols = symbols;
            this.addresses = addresses;
        }

        public IReadOnlyList<ExtractedLogicAssignment> Parse(
            Step7CodeBlockModel block, string blockName, string blockType, string blockLanguage)
        {
            return Parse(block, blockName, blockType, blockLanguage, null);
        }

        // traces, when given, receives the local-data state at every CALL (keyed by
        // Step7CallTrace.Key) so call actuals passed through local data can be resolved.
        public IReadOnlyList<ExtractedLogicAssignment> Parse(
            Step7CodeBlockModel block, string blockName, string blockType, string blockLanguage,
            IDictionary<string, Step7CallTrace> traces)
        {
            var assignments = new List<ExtractedLogicAssignment>();
            var statementOrder = 0;
            bool blockEnded = false;
            bool blockTainted = false;
            string blockTaintReason = null;
            foreach (Step7NetworkModel network in block.Networks)
            {
                if (blockEnded) break;
                var context = new NetworkContext(block, blockName, blockType, blockLanguage, network);
                if (blockTainted) context.Taint(blockTaintReason);
                ParseNetwork(context, assignments, ref statementOrder, traces);
                if (context.BlockEnded) blockEnded = true;
                if (context.BlockTaintReason != null && !blockTainted)
                {
                    blockTainted = true;
                    blockTaintReason = context.BlockTaintReason;
                }
            }
            return assignments;
        }

        private void ParseNetwork(NetworkContext context, List<ExtractedLogicAssignment> assignments, ref int statementOrder,
            IDictionary<string, Step7CallTrace> traces)
        {
            ProcessStatements(context, context.Network.Statements, assignments, ref statementOrder, traces);
        }

        // traces is null for the statements a CALL row carries after its UC/CC.
        private void ProcessStatements(NetworkContext context, IReadOnlyList<Step7StatementModel> statements,
            List<ExtractedLogicAssignment> assignments, ref int statementOrder, IDictionary<string, Step7CallTrace> traces)
        {
            for (var index = 0; index < statements.Count; index++)
            {
                Step7StatementModel statement = statements[index];
                string command = statement.Command.ToUpperInvariant();
                string operand = Compact(statement.Operand);

                switch (command)
                {
                    case "A":
                    case "AN":
                    case "O":
                    case "ON":
                        if (operand.Length == 0 && command == "O")
                        {
                            context.OrGroup();
                            context.Line("O");
                            continue;
                        }
                        ExtractedBooleanExpression value = ReadBoolean(context, statement, operand);
                        string operandText = value.DisplayText;
                        if (command.EndsWith("N", StringComparison.Ordinal)) value = Expressions.Not(value);
                        if (command.StartsWith("A", StringComparison.Ordinal)) context.And(value);
                        else context.Or(value);
                        context.Line(command + " " + operandText);
                        continue;
                    case "X":
                    case "XN":
                        context.Replace(Expressions.Unknown("XOR logic is not supported (" + command + " " + statement.Operand + ")."));
                        context.Line(command + " " + statement.Operand);
                        continue;
                    case "A(":
                    case "AN(":
                    case "O(":
                    case "ON(":
                        context.Open(command);
                        context.Line(command);
                        continue;
                    case "X(":
                    case "XN(":
                        context.Open(command);
                        context.Line(command);
                        continue;
                    case ")":
                        context.Close();
                        context.Line(")");
                        continue;
                    case "NOT":
                        context.Negate();
                        context.Line("NOT");
                        continue;
                    case "SET":
                    case "CLR":
                        context.SetConstant(command == "SET");
                        context.Line(command);
                        continue;
                    case "FP":
                    case "FN":
                        context.Replace(Expressions.Unknown("Edge detection (" + command + " " + statement.Operand + ") is not supported."));
                        context.Line(command + " " + statement.Operand);
                        continue;
                    case "=":
                        Write(context, statement, operand, ExtractedLogicOperation.Assign, assignments, ref statementOrder);
                        continue;
                    case "S":
                    case "R":
                        if (IsTimerOrCounter(operand))
                        {
                            context.EndString();
                            continue;
                        }
                        Write(context, statement, operand,
                            command == "S" ? ExtractedLogicOperation.Set : ExtractedLogicOperation.Reset,
                            assignments, ref statementOrder);
                        continue;
                    case "L":
                        Load(context, statement, operand);
                        continue;
                    case "TAK":
                        context.SwapAccumulators();
                        continue;
                    case "LAR1":
                        context.LoadAddressRegister(operand);
                        continue;
                    case "OPN":
                        Match db = OpenDataBlock.Match(operand);
                        context.OpenDb = db.Success ? int.Parse(db.Groups["db"].Value, CultureInfo.InvariantCulture) : (int?)null;
                        continue;
                    case "CALL":
                    case "UC":
                    case "CC":
                        if (traces != null)
                            traces[Step7CallTrace.Key(context.Block.Kind, context.Block.Number, context.Network.Number, index)] =
                                context.TraceForCall();
                        context.AfterCall(statement, symbols);
                        if (statement.PostCallStatements.Count > 0)
                            ProcessStatements(context, statement.PostCallStatements, assignments, ref statementOrder, null);
                        continue;
                    case "T":
                        Match localWord = LocalWord.Match(operand);
                        if (localWord.Success) context.StoreLocalWord(localWord.Value.ToUpperInvariant());
                        continue;
                    case "BEU":
                    case "BE":
                        context.BlockEnded = true;
                        return;
                    case "BEC":
                        context.TaintBlock("A conditional block end (BEC) makes later assignments conditional.");
                        context.EndString();
                        continue;
                    case "MCRA":
                    case "MCRD":
                    case "MCR(":
                    case ")MCR":
                        context.TaintBlock("Master control relay (" + command + ") makes assignments conditional.");
                        continue;
                    case "JL":
                    case "LOOP":
                        context.Taint("Jump lists and loops are not supported.");
                        continue;
                }

                if (Jumps.Contains(command))
                {
                    index = Jump(context, statements, index, command, statement.Operand);
                    continue;
                }
                if (Comparison.IsMatch(command))
                {
                    context.And(context.AccumulatorComparison(command));
                    context.Line(command);
                    continue;
                }
                if (Arithmetic.IsMatch(command))
                {
                    context.Arithmetic(command);
                    continue;
                }
                if (Consumers.Contains(command))
                {
                    context.EndString();
                    continue;
                }
                if (Neutral.Contains(command) || command.Length == 0)
                    continue;

                context.Taint("Unsupported instruction '" + statement.Command + "'.");
            }
        }

        private int Jump(NetworkContext context, IReadOnlyList<Step7StatementModel> statements, int index,
            string command, string target)
        {
            string label = (target ?? string.Empty).Trim();
            int destination = -1;
            for (var candidate = index + 1; candidate < statements.Count; candidate++)
                if (string.Equals(statements[candidate].Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    destination = candidate;
                    break;
                }

            if (destination < 0)
            {
                context.Taint("Jump '" + command + " " + label + "' leaves the network or jumps backwards.");
                context.EndString();
                return index;
            }

            bool skipsWrites = false;
            for (var skipped = index + 1; skipped < destination; skipped++)
            {
                string skippedCommand = statements[skipped].Command.ToUpperInvariant();
                if (skippedCommand == "=" ||
                    ((skippedCommand == "S" || skippedCommand == "R") && !IsTimerOrCounter(Compact(statements[skipped].Operand))))
                {
                    skipsWrites = true;
                    break;
                }
            }

            if (skipsWrites)
            {
                // The skipped writes are conditional on the jump; evaluate linearly but
                // mark everything that follows as unsupported.
                context.Taint("Jump '" + command + " " + label + "' makes boolean writes conditional.");
                context.EndString();
                return index;
            }

            // EN/ENO pattern: the jump only bypasses word operations. Both paths reach the
            // label with a new logic string.
            context.EndString();
            return destination - 1;
        }

        private void Write(
            NetworkContext context,
            Step7StatementModel statement,
            string operand,
            ExtractedLogicOperation operation,
            List<ExtractedLogicAssignment> assignments,
            ref int statementOrder)
        {
            ExtractedBooleanExpression source = context.Current() ??
                Expressions.Unknown("The assignment has no preceding logic operation.");
            string mnemonic = operation == ExtractedLogicOperation.Assign ? "="
                : operation == ExtractedLogicOperation.Set ? "S" : "R";

            if (context.TaintReason != null)
                source = Expressions.Unknown(context.TaintReason);

            string temporary;
            if (context.TryAnonymousLocalBit(operand, out temporary))
            {
                context.StoreLocalBit(temporary, source);
                context.Line(mnemonic + " " + statement.Operand);
                context.EndString();
                return;
            }

            RenderedOperand destination = RenderOperand(context, statement, operand, true);

            context.Line(mnemonic + " " + destination.Display);
            statementOrder++;
            assignments.Add(new ExtractedLogicAssignment(
                destination.Display,
                destination.ResolvedPath,
                source,
                context.SourceText(),
                context.BlockName,
                context.Block.Number,
                context.BlockType,
                context.BlockLanguage,
                context.Network.Number,
                context.Network.Title,
                context.Network.Comment,
                statementOrder,
                Expressions.Status(source),
                operation));
            context.EndString();
        }

        private void Load(NetworkContext context, Step7StatementModel statement, string operand)
        {
            if (operand.StartsWith("P##", StringComparison.OrdinalIgnoreCase))
            {
                context.BeginPointerPreamble(operand.Substring(3));
                context.Push(operand, null);
                return;
            }
            RenderedOperand value = RenderOperand(context, statement, operand, false);
            context.Push(value.Display, value.ResolvedPath);
        }

        private ExtractedBooleanExpression ReadBoolean(NetworkContext context, Step7StatementModel statement, string operand)
        {
            ExtractedBooleanExpression local;
            if (context.TryReadLocalBit(operand, out local)) return local;
            RenderedOperand rendered = RenderOperand(context, statement, operand, true);
            if (rendered.Unsupported) return Expressions.Unknown("Operand '" + statement.Operand + "' is not supported.");
            return Expressions.Operand(rendered.Display, rendered.ResolvedPath);
        }

        private RenderedOperand RenderOperand(NetworkContext context, Step7StatementModel statement, string operand, bool boolean)
        {
            if (operand.Length == 0) return RenderedOperand.Unresolved(statement.Operand ?? string.Empty);
            if (operand.StartsWith("#", StringComparison.Ordinal)) return RenderedOperand.Unresolved(operand);
            if (Regex.IsMatch(operand, @"^-?\d+(\.\d+)?$|^[A-Z]+#", RegexOptions.IgnoreCase))
                return RenderedOperand.Unresolved(statement.Operand.Trim());

            Match pointer = AreaPointer.Match(operand);
            if (pointer.Success)
            {
                string member = context.PointerMember(
                    int.Parse(pointer.Groups["byte"].Value, CultureInfo.InvariantCulture),
                    int.Parse(pointer.Groups["bit"].Value, CultureInfo.InvariantCulture),
                    boolean && !pointer.Groups["width"].Success);
                return member == null ? RenderedOperand.NotSupported(operand) : RenderedOperand.Unresolved(member);
            }

            if (LocalBit.IsMatch(operand)) return RenderedOperand.Unresolved("L " + operand.Substring(1));
            if (StatusBits.Contains(operand)) return RenderedOperand.NotSupported(operand);

            string dbOperand = operand;
            Match unqualified = UnqualifiedDbBit.Match(operand);
            if (unqualified.Success && context.OpenDb.HasValue)
                dbOperand = "DB" + context.OpenDb.Value.ToString(CultureInfo.InvariantCulture) + "." + operand;

            if (Step7AddressResolver.IsDbAddress(dbOperand))
            {
                Step7AddressResolution resolution = addresses.Resolve(dbOperand, boolean ? "BOOL" : null);
                if (resolution.IsResolved)
                    return RenderedOperand.Resolved(Quote(resolution.BlockName) +
                        resolution.MemberPath.Substring(resolution.BlockName.Length), resolution.MemberPath);
                return RenderedOperand.Unresolved(statement.Operand.Trim());
            }

            if (operand.IndexOf('[') >= 0) return RenderedOperand.NotSupported(operand);

            // Global symbols (M, I, Q, T, C). As for TIA tags, only DB members carry a
            // resolved path; the constant catalogue recognises symbols such as "glb.One".
            string symbol = statement.OperandSymbol ?? symbols.SymbolOf(operand);
            return RenderedOperand.Unresolved(symbol != null ? Quote(symbol) : statement.Operand.Trim());
        }

        private static bool IsTimerOrCounter(string operand)
        {
            return Regex.IsMatch(operand ?? string.Empty, @"^[TCZ]\d+$", RegexOptions.IgnoreCase);
        }

        private static string Quote(string symbol)
        {
            return "\"" + symbol + "\"";
        }

        private static string Compact(string operand)
        {
            return (operand ?? string.Empty).Replace(" ", string.Empty).Trim();
        }

        private sealed class RenderedOperand
        {
            private RenderedOperand(string display, string resolvedPath, bool unsupported)
            {
                Display = display;
                ResolvedPath = resolvedPath;
                Unsupported = unsupported;
            }

            public string Display { get; }
            public string ResolvedPath { get; }
            public bool Unsupported { get; }

            public static RenderedOperand Resolved(string display, string path) { return new RenderedOperand(display, path, false); }
            public static RenderedOperand Unresolved(string display) { return new RenderedOperand(display, null, false); }
            public static RenderedOperand NotSupported(string display) { return new RenderedOperand(display, null, true); }
        }

        // Evaluation state for one network.
        private sealed class NetworkContext
        {
            private readonly Dictionary<string, Step7TracedValue> localWords =
                new Dictionary<string, Step7TracedValue>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, ExtractedBooleanExpression> localBits =
                new Dictionary<string, ExtractedBooleanExpression>(StringComparer.OrdinalIgnoreCase);
            private readonly Stack<Frame> nesting = new Stack<Frame>();
            private readonly List<string> lines = new List<string>();
            private ExtractedBooleanExpression rlo;
            private ExtractedBooleanExpression orAccumulator;
            private bool firstCheck = true;
            private AccumulatorValue accumulator1;
            private AccumulatorValue accumulator2;
            private string pendingPointer;
            private int pointerLoads;
            private string addressRegisterParameter;

            public NetworkContext(Step7CodeBlockModel block, string blockName, string blockType, string blockLanguage,
                Step7NetworkModel network)
            {
                Block = block;
                BlockName = blockName;
                BlockType = blockType;
                BlockLanguage = blockLanguage;
                Network = network;
            }

            public Step7CodeBlockModel Block { get; }
            public string BlockName { get; }
            public string BlockType { get; }
            public string BlockLanguage { get; }
            public Step7NetworkModel Network { get; }
            public int? OpenDb { get; set; }
            public bool BlockEnded { get; set; }
            public string TaintReason { get; private set; }
            public string BlockTaintReason { get; private set; }

            public void Taint(string reason)
            {
                if (TaintReason == null) TaintReason = reason;
            }

            public void TaintBlock(string reason)
            {
                Taint(reason);
                if (BlockTaintReason == null) BlockTaintReason = reason;
            }

            public ExtractedBooleanExpression Current()
            {
                if (orAccumulator != null && rlo != null) return Expressions.Or(orAccumulator, rlo);
                return orAccumulator ?? rlo;
            }

            public void And(ExtractedBooleanExpression value)
            {
                BeginIfNeeded();
                rlo = rlo == null ? value : Expressions.And(rlo, value);
            }

            public void Or(ExtractedBooleanExpression value)
            {
                BeginIfNeeded();
                ExtractedBooleanExpression current = Current();
                rlo = current == null ? value : Expressions.Or(current, value);
                orAccumulator = null;
            }

            public void OrGroup()
            {
                BeginIfNeeded();
                if (rlo == null) return;
                orAccumulator = orAccumulator == null ? rlo : Expressions.Or(orAccumulator, rlo);
                rlo = null;
            }

            public void Replace(ExtractedBooleanExpression value)
            {
                BeginIfNeeded();
                rlo = value;
                orAccumulator = null;
            }

            public void Negate()
            {
                ExtractedBooleanExpression current = Current();
                rlo = current == null ? Expressions.Unknown("NOT without a logic operation.") : Expressions.Not(current);
                orAccumulator = null;
            }

            public void SetConstant(bool value)
            {
                BeginIfNeeded();
                rlo = Expressions.Constant(value);
                orAccumulator = null;
                // SET/CLR end the logic string; a following assignment still uses the value.
                firstCheck = true;
            }

            public void Open(string command)
            {
                BeginIfNeeded();
                nesting.Push(new Frame(rlo, orAccumulator, command));
                rlo = null;
                orAccumulator = null;
            }

            public void Close()
            {
                if (nesting.Count == 0)
                {
                    Taint("Unbalanced closing bracket.");
                    return;
                }
                ExtractedBooleanExpression inner = Current() ?? Expressions.Unknown("Empty bracket.");
                Frame frame = nesting.Pop();
                rlo = frame.Rlo;
                orAccumulator = frame.OrAccumulator;
                switch (frame.Command)
                {
                    case "A(": rlo = rlo == null ? inner : Expressions.And(rlo, inner); break;
                    case "AN(":
                        ExtractedBooleanExpression negated = Expressions.Not(inner);
                        rlo = rlo == null ? negated : Expressions.And(rlo, negated);
                        break;
                    case "O(":
                    case "ON(":
                        ExtractedBooleanExpression operand = frame.Command == "ON(" ? Expressions.Not(inner) : inner;
                        ExtractedBooleanExpression current = Current();
                        rlo = current == null ? operand : Expressions.Or(current, operand);
                        orAccumulator = null;
                        break;
                    default:
                        rlo = Expressions.Unknown("XOR logic is not supported (" + frame.Command + ").");
                        orAccumulator = null;
                        break;
                }
            }

            public void EndString()
            {
                firstCheck = true;
            }

            public void Line(string text)
            {
                lines.Add(text);
            }

            public string SourceText()
            {
                return string.Join(Environment.NewLine, lines);
            }

            public void Push(string display, string resolvedPath)
            {
                accumulator2 = accumulator1;
                accumulator1 = new AccumulatorValue(display, resolvedPath);
            }

            public void SwapAccumulators()
            {
                AccumulatorValue swap = accumulator1;
                accumulator1 = accumulator2;
                accumulator2 = swap;
            }

            // A comparison of one resolved member with a literal is a resolved, dynamic
            // operand; its path names the compared member (e.g. db.seq.SeqFeed.StepNo).
            public ExtractedBooleanExpression AccumulatorComparison(string command)
            {
                string op = command.Substring(0, command.Length - 1);
                string text = Display(accumulator2) + " " + op + " " + Display(accumulator1);
                return Expressions.Operand(text, ComparedMember(accumulator2, accumulator1));
            }

            public void Arithmetic(string command)
            {
                string op = command.Length > 1 ? command.Substring(0, command.Length - 1) : command;
                accumulator1 = new AccumulatorValue(
                    "(" + Display(accumulator2) + " " + op + " " + Display(accumulator1) + ")", null);
            }

            private static string Display(AccumulatorValue value)
            {
                return value == null ? "?" : value.Display;
            }

            private static string ComparedMember(AccumulatorValue left, AccumulatorValue right)
            {
                if (left == null || right == null) return null;
                if (left.ResolvedPath != null && right.ResolvedPath == null && IsLiteral(right.Display)) return left.ResolvedPath;
                if (right.ResolvedPath != null && left.ResolvedPath == null && IsLiteral(left.Display)) return right.ResolvedPath;
                return null;
            }

            public static bool IsLiteral(string text)
            {
                return Regex.IsMatch(text ?? string.Empty, @"^-?\d+(\.\d+)?$|^[A-Z0-9]+#[^#]", RegexOptions.IgnoreCase);
            }

            public void BeginPointerPreamble(string parameter)
            {
                pendingPointer = parameter;
                pointerLoads = 0;
            }

            // The compiler loads the parameter pointer into AR1, reads the DB number and area
            // pointer, then loads AR1 again; after the second LAR1, AR1 addresses the parameter.
            public void LoadAddressRegister(string operand)
            {
                if (operand.Length > 0 || pendingPointer == null)
                {
                    addressRegisterParameter = null;
                    pendingPointer = null;
                    return;
                }
                pointerLoads++;
                if (pointerLoads == 2)
                {
                    addressRegisterParameter = pendingPointer;
                    pendingPointer = null;
                }
            }

            public string PointerMember(int byteAddress, int bitAddress, bool boolean)
            {
                if (addressRegisterParameter == null) return null;
                Step7InterfaceParameterModel parameter = Block.InterfaceParameters.FirstOrDefault(item =>
                    string.Equals(item.Name, addressRegisterParameter, StringComparison.OrdinalIgnoreCase));
                if (parameter == null) return null;
                List<Step7InterfaceMemberModel> matches = parameter.Members
                    .Where(member => member.ByteAddress == byteAddress && member.BitAddress == bitAddress &&
                                     string.Equals(member.ElementaryType, "BOOL", StringComparison.OrdinalIgnoreCase) == boolean &&
                                     !string.Equals(member.ElementaryType, "STRUCT", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(member.ElementaryType, "UDT", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return matches.Count == 1 ? "#" + parameter.Name + "." + matches[0].Path : null;
            }

            public bool TryAnonymousLocalBit(string operand, out string key)
            {
                key = null;
                Match match = LocalBit.Match(operand);
                if (!match.Success) return false;
                key = "L" + match.Groups["byte"].Value + "." + match.Groups["bit"].Value;
                return true;
            }

            public void StoreLocalBit(string key, ExtractedBooleanExpression value)
            {
                localBits[key] = value;
            }

            public bool TryReadLocalBit(string operand, out ExtractedBooleanExpression value)
            {
                value = null;
                string key;
                if (!TryAnonymousLocalBit(operand, out key)) return false;
                if (localBits.TryGetValue(key, out value)) return true;
                value = Expressions.Operand("L " + key.Substring(1), null);
                return true;
            }

            public void StoreLocalWord(string key)
            {
                localWords[key] = accumulator1 == null
                    ? new Step7TracedValue("?", null)
                    : new Step7TracedValue(accumulator1.Display, accumulator1.ResolvedPath);
            }

            public Step7CallTrace TraceForCall()
            {
                return new Step7CallTrace(localBits, localWords, TaintReason);
            }

            public void AfterCall(Step7StatementModel statement, Step7SymbolLookup symbols)
            {
                string called = symbols.SymbolOf(statement.CalledBlock ?? string.Empty) ?? statement.CalledBlock;
                foreach (Step7CallArgumentModel argument in statement.CallArguments)
                {
                    if (argument.Section != "OUT" && argument.Section != "IN_OUT") continue;
                    string output = "\"" + called + "\"." + argument.Name + " (call output)";
                    string key;
                    string value = Compact(argument.Value);
                    if (TryAnonymousLocalBit(value, out key))
                        localBits[key] = Expressions.Operand(output, null);
                    else if (LocalWord.IsMatch(value))
                        localWords[value.ToUpperInvariant()] = new Step7TracedValue(output, null);
                }
                accumulator1 = null;
                accumulator2 = null;
                addressRegisterParameter = null;
                EndString();
            }

            private void BeginIfNeeded()
            {
                if (!firstCheck) return;
                firstCheck = false;
                rlo = null;
                orAccumulator = null;
                // A timer, counter or call inside a bracket (LAD box in a branch) starts a new
                // logic string at that bracket level only; the open brackets remain.
                if (nesting.Count == 0) lines.Clear();
            }

            private sealed class AccumulatorValue
            {
                public AccumulatorValue(string display, string resolvedPath)
                {
                    Display = display;
                    ResolvedPath = resolvedPath;
                }

                public string Display { get; }
                public string ResolvedPath { get; }
            }

            private sealed class Frame
            {
                public Frame(ExtractedBooleanExpression rlo, ExtractedBooleanExpression orAccumulator, string command)
                {
                    Rlo = rlo;
                    OrAccumulator = orAccumulator;
                    Command = command;
                }

                public ExtractedBooleanExpression Rlo { get; }
                public ExtractedBooleanExpression OrAccumulator { get; }
                public string Command { get; }
            }
        }

        internal static class Expressions
        {
            public static ExtractedBooleanExpression Operand(string text, string resolvedPath)
            {
                return new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Operand, text, resolvedPath, null, null);
            }

            public static ExtractedBooleanExpression Constant(bool value)
            {
                return new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Constant,
                    value ? "TRUE" : "FALSE", null, value, null);
            }

            public static ExtractedBooleanExpression Unknown(string text)
            {
                return new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Unknown, text, null, null, null);
            }

            public static ExtractedBooleanExpression Not(ExtractedBooleanExpression value)
            {
                return new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Not,
                    "NOT " + Wrap(value), null, null, new[] { value });
            }

            public static ExtractedBooleanExpression And(ExtractedBooleanExpression left, ExtractedBooleanExpression right)
            {
                return Nary(ExtractedBooleanExpressionKind.And, " AND ", left, right);
            }

            public static ExtractedBooleanExpression Or(ExtractedBooleanExpression left, ExtractedBooleanExpression right)
            {
                return Nary(ExtractedBooleanExpressionKind.Or, " OR ", left, right);
            }

            public static ExtractedLogicResolutionStatus Status(ExtractedBooleanExpression expression)
            {
                if (Any(expression, item => item.Kind == ExtractedBooleanExpressionKind.Unknown))
                    return ExtractedLogicResolutionStatus.Unsupported;
                if (Any(expression, item => item.Kind == ExtractedBooleanExpressionKind.Operand &&
                                            string.IsNullOrWhiteSpace(item.ResolvedPath)))
                    return ExtractedLogicResolutionStatus.Partial;
                return ExtractedLogicResolutionStatus.Complete;
            }

            private static ExtractedBooleanExpression Nary(
                ExtractedBooleanExpressionKind kind, string separator,
                ExtractedBooleanExpression left, ExtractedBooleanExpression right)
            {
                var children = new List<ExtractedBooleanExpression>();
                foreach (ExtractedBooleanExpression item in new[] { left, right })
                {
                    if (item.Kind == kind) children.AddRange(item.Children);
                    else children.Add(item);
                }
                var text = new StringBuilder();
                for (var index = 0; index < children.Count; index++)
                {
                    if (index > 0) text.Append(separator);
                    text.Append(Wrap(children[index]));
                }
                return new ExtractedBooleanExpression(kind, text.ToString(), null, null, children);
            }

            private static string Wrap(ExtractedBooleanExpression value)
            {
                return value.Kind == ExtractedBooleanExpressionKind.And || value.Kind == ExtractedBooleanExpressionKind.Or
                    ? "(" + value.DisplayText + ")"
                    : value.DisplayText;
            }

            private static bool Any(ExtractedBooleanExpression expression, Func<ExtractedBooleanExpression, bool> predicate)
            {
                if (expression == null) return false;
                if (predicate(expression)) return true;
                return expression.Children.Any(child => Any(child, predicate));
            }
        }
    }
}
