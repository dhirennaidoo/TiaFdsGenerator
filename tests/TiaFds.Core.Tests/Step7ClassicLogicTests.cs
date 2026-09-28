using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using TiaFds.Analysis;
using TiaFds.Reporting;
using TiaFds.Step7Classic;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class Step7ClassicLogicTests
    {
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        private static readonly Step7ProgramIdentity Program =
            new Step7ProgramIdentity("S7 Program", "SIMATIC 300", "CPU 315-2 PN/DP");

        [TestMethod]
        public void Parser_ResolvesDbBitsAndBuildsAndNotTrees()
        {
            ExtractedLogicAssignment ilk = Single(Map(Network(
                S("AN", "DB90.DBX 0.4"),
                S("A", "DB50.DBX 50.5"),
                S("=", "DB50.DBX50.4"))));

            Assert.AreEqual("\"db.cm.Drv\".AM_M1020.ILK", ilk.DestinationExpression);
            Assert.AreEqual("db.cm.Drv.AM_M1020.ILK", ilk.ResolvedDestinationPath);
            Assert.AreEqual(ExtractedLogicOperation.Assign, ilk.Operation);
            Assert.AreEqual(ExtractedBooleanExpressionKind.And, ilk.SourceExpression.Kind);
            Assert.AreEqual("NOT \"db.cm.DI\".AM_LS1035.FLT AND \"db.cm.Drv\".AM_M1020.ME",
                ilk.SourceExpression.DisplayText);
            Assert.AreEqual("db.cm.DI.AM_LS1035.FLT", ilk.SourceExpression.Children[0].Children[0].ResolvedPath);
            Assert.AreEqual(ExtractedLogicResolutionStatus.Complete, ilk.ResolutionStatus);
            Assert.AreEqual("Call cm Drv Feed", ilk.BlockName);
            Assert.AreEqual("LAD", ilk.BlockLanguage);
            Assert.AreEqual(1, ilk.NetworkNumber);
        }

        [TestMethod]
        public void Parser_AppliesS7OrPrecedence()
        {
            // A a; A b; ON c; O; A d; = x  ->  ((a AND b) OR NOT c) OR d
            ExtractedLogicAssignment result = Single(Map(Network(
                S("A", "M0.5"), S("A", "M0.6"), S("ON", "M0.7"), S("O", ""), S("A", "M1.0"),
                S("=", "DB50.DBX 50.6"))));

            Assert.AreEqual("(M0.5 AND M0.6) OR NOT M0.7 OR M1.0", result.SourceExpression.DisplayText);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Or, result.SourceExpression.Kind);
            Assert.AreEqual(3, result.SourceExpression.Children.Count);
        }

        [TestMethod]
        public void Parser_HandlesBracketsAndTimerBoxesInsideBranches()
        {
            // Regression: a timer inside a bracket starts a new logic string at that bracket
            // level only; the enclosing bracket must survive.
            ExtractedLogicAssignment result = Single(Map(Network(
                S("A", "M0.5"),
                S("A(", ""),
                S("A", "M0.6"), S("L", "S5T#2M"), S("SD", "T 2"), S("A", "T 2"),
                S(")", ""),
                S("A(", ""), S("O", "M0.7"), S("O", "M1.0"), S(")", ""),
                S("=", "DB50.DBX 50.6"))));

            Assert.AreEqual("M0.5 AND T 2 AND (M0.7 OR M1.0)", result.SourceExpression.DisplayText);
            Assert.AreNotEqual(ExtractedLogicResolutionStatus.Unsupported, result.ResolutionStatus);
        }

        [TestMethod]
        public void Parser_RecordsSetResetAndChainedAssignments()
        {
            var assignments = Map(Network(
                S("SET", ""), S("=", "DB50.DBX 0.6"), S("=", "DB50.DBX 50.6"),
                S("A", "M0.5"), S("S", "DB50.DBX 54.0"),
                S("A", "M0.6"), S("R", "DB50.DBX 54.0"),
                S("A", "M0.7"), S("S", "T 3"))).LogicAssignments;

            Assert.AreEqual(4, assignments.Count, "Timer set (S T 3) is not a boolean write.");
            Assert.IsTrue(assignments.Take(2).All(item =>
                item.SourceExpression.Kind == ExtractedBooleanExpressionKind.Constant &&
                item.SourceExpression.ConstantValue == true));
            Assert.AreEqual(ExtractedLogicOperation.Set, assignments[2].Operation);
            Assert.AreEqual("db.cm.Drv.AM_M1020.CmdSA", assignments[2].ResolvedDestinationPath);
            Assert.AreEqual(ExtractedLogicOperation.Reset, assignments[3].Operation);
            Assert.AreEqual("M0.6", assignments[3].SourceExpression.DisplayText);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, assignments.Select(item => item.StatementOrder).ToArray());
        }

        [TestMethod]
        public void Parser_SubstitutesLadLocalBitsWithinTheNetworkOnly()
        {
            PlcInventory inventory = Map(
                Network(S("A", "M0.5"), S("A", "M0.6"), S("=", "L0.0"), S("A", "L 0.0"), S("=", "DB50.DBX 50.6")),
                Network(S("A", "L 0.0"), S("=", "DB50.DBX 0.6")));

            Assert.AreEqual(2, inventory.LogicAssignments.Count, "Anonymous local bits are not emitted.");
            Assert.AreEqual("M0.5 AND M0.6", inventory.LogicAssignments[0].SourceExpression.DisplayText);
            ExtractedLogicAssignment otherNetwork = inventory.LogicAssignments[1];
            Assert.AreEqual("L 0.0", otherNetwork.SourceExpression.DisplayText);
            Assert.AreEqual(ExtractedLogicResolutionStatus.Partial, otherNetwork.ResolutionStatus);
        }

        [TestMethod]
        public void Parser_FoldsParameterPointerPreambleAndComparisons()
        {
            ExtractedLogicAssignment start = Single(Map(SequenceBlock(), 0));

            Assert.AreEqual(ExtractedLogicOperation.Set, start.Operation);
            Assert.AreEqual("db.cm.Drv.AM_M1020.CmdSA", start.ResolvedDestinationPath);
            Assert.AreEqual("#Seq.SG AND #Seq.StepNo == 110", start.SourceExpression.DisplayText,
                "Without a unique caller the interface references stay unbound.");
            Assert.AreEqual(ExtractedLogicResolutionStatus.Partial, start.ResolutionStatus);
        }

        [TestMethod]
        public void Binder_BindsInterfaceReferencesToTheOnlyCallSite()
        {
            ExtractedLogicAssignment start = Single(Map(SequenceBlock(), 1));

            Assert.AreEqual("\"db.seq\".SeqFeed.SG AND \"db.seq\".SeqFeed.StepNo == 110", start.SourceExpression.DisplayText);
            Assert.AreEqual("db.seq.SeqFeed.SG", start.SourceExpression.Children[0].ResolvedPath);
            Assert.AreEqual("db.seq.SeqFeed.StepNo", start.SourceExpression.Children[1].ResolvedPath,
                "A comparison of a resolved member with a literal resolves to that member.");
            Assert.AreEqual(ExtractedLogicResolutionStatus.Complete, start.ResolutionStatus);
        }

        [TestMethod]
        public void Binder_LeavesReferencesUnboundWithSeveralCallSites()
        {
            ExtractedLogicAssignment start = Single(Map(SequenceBlock(), 2));

            Assert.AreEqual("#Seq.SG AND #Seq.StepNo == 110", start.SourceExpression.DisplayText);
        }

        [TestMethod]
        public void Parser_AllowsEnJumpsButNotJumpsOverWrites()
        {
            PlcInventory enPattern = Map(Network(
                S("A", "M0.5"), S("=", "L0.0"), S("A", "L 0.0"), S("JNB", "_001"),
                S("L", "1"), S("T", "MW 10"), Labelled("_001", "NOP", "0"),
                S("A", "L 0.0"), S("=", "DB50.DBX 50.6")));
            Assert.AreEqual("M0.5", Single(enPattern).SourceExpression.DisplayText);

            PlcInventory conditional = Map(Network(
                S("A", "M0.5"), S("JCN", "_002"), S("A", "M0.6"), S("=", "DB50.DBX 50.6"),
                Labelled("_002", "NOP", "0")));
            ExtractedLogicAssignment tainted = Single(conditional);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Unknown, tainted.SourceExpression.Kind);
            StringAssert.Contains(tainted.SourceExpression.DisplayText, "conditional");
            Assert.AreEqual(ExtractedLogicResolutionStatus.Unsupported, tainted.ResolutionStatus);
        }

        [TestMethod]
        public void Analyzer_ClassicStartLatchIsNotAmbiguous()
        {
            EngineeringSnapshot snapshot = Snapshot(Map(Network(
                S("A", "M0.5"), S("S", "DB50.DBX 54.0"),
                S("A", "M0.6"), S("R", "DB50.DBX 54.0"),
                S("A", "M0.7"), S("=", "DB50.DBX 50.4"))));

            ControlModuleBehaviourResult behaviour = Behaviour(snapshot);
            BehaviouralCondition[] starts = behaviour.Conditions
                .Where(item => item.Kind == BehaviouralConditionKind.StartCommand).ToArray();
            Assert.AreEqual(2, starts.Length);
            CollectionAssert.AreEquivalent(new[] { ExtractedLogicOperation.Set, ExtractedLogicOperation.Reset },
                starts.Select(item => item.Operation).ToArray());
            Assert.IsTrue(starts.All(item => item.Member == "CmdSA" && item.ModuleName == "AM_M1020"));
            Assert.IsFalse(behaviour.Diagnostics.Any(item => item.Code == "BEH104_MULTIPLE_ASSIGNMENTS"));
            Assert.AreEqual(BehaviouralConditionKind.Interlock,
                behaviour.Conditions.Single(item => item.Member == "ILK").Kind);
        }

        [TestMethod]
        public void Analyzer_MixedCoilAndLatchWritesAreAmbiguous()
        {
            EngineeringSnapshot snapshot = Snapshot(Map(Network(
                S("A", "M0.5"), S("S", "DB50.DBX 54.0"),
                S("A", "M0.6"), S("=", "DB50.DBX 54.0"))));

            ControlModuleBehaviourResult behaviour = Behaviour(snapshot);
            Assert.IsTrue(behaviour.Diagnostics.Any(item => item.Code == "BEH104_MULTIPLE_ASSIGNMENTS"));
            Assert.IsTrue(behaviour.Conditions.All(item =>
                item.ResolutionStatus == BehaviouralConditionResolutionStatus.Ambiguous));
        }

        [TestMethod]
        public void Analyzer_ConstantLatchConditionRequiresReview()
        {
            EngineeringSnapshot snapshot = Snapshot(Map(Network(S("SET", ""), S("S", "DB50.DBX 54.0"))));

            BehaviouralCondition start = Behaviour(snapshot).Conditions.Single();
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyTrue, start.Effect);
            Assert.AreEqual(BehaviourReviewClassification.RequiresManualInterpretation, start.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.LatchConstantRule, start.SemanticRule);
        }

        [TestMethod]
        public void Report_CarriesOperationToJsonAndExcel()
        {
            EngineeringSnapshot snapshot = Snapshot(Map(Network(S("A", "M0.5"), S("S", "DB50.DBX 54.0"))));
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
            AnalysisReport report = new AnalysisReportBuilder().Build(snapshot, discovery, implementation,
                new ControlModuleBehaviourAnalyzer().Analyze(snapshot, discovery, implementation));

            Assert.AreEqual("1.3", report.SchemaVersion);
            Assert.AreEqual(AnalysisLogicOperation.Set, report.BehaviouralConditions.Single().Operation);

            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            try
            {
                string jsonPath = Path.Combine(directory, "report.json");
                new AnalysisReportJsonWriter().Write(report, jsonPath);
                JObject json = JObject.Parse(File.ReadAllText(jsonPath));
                Assert.AreEqual("Set", (string)json["behaviouralConditions"][0]["operation"]);

                string excelPath = Path.Combine(directory, "report.xlsx");
                new AnalysisReportExcelWriter().Write(report, excelPath);
                using (var workbook = new ClosedXML.Excel.XLWorkbook(excelPath))
                {
                    var sheet = workbook.Worksheet("Behavioural Conditions");
                    Assert.AreEqual("Operation", sheet.Cell(1, 30).GetString());
                    Assert.AreEqual("Set", sheet.Cell(2, 30).GetString());
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Snapshot_OperationDefaultsToAssignForOlderSnapshots()
        {
            var legacy = new ExtractedLogicAssignment("\"db\".X", "db.X",
                new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Constant, "TRUE", null, true, null),
                "TRUE", "FC1", 1, "Function", "LAD", 1, null, null, 1, ExtractedLogicResolutionStatus.Complete);
            Assert.AreEqual(ExtractedLogicOperation.Assign, legacy.Operation);

            ExtractedLogicAssignment parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<ExtractedLogicAssignment>(
                "{\"destinationExpression\":\"X\",\"statementOrder\":1,\"resolutionStatus\":\"Complete\"}");
            Assert.AreEqual(ExtractedLogicOperation.Assign, parsed.Operation);
        }

        [TestMethod]
        public void Analyzer_CountsBridgeFlagBranchesAsBypassed()
        {
            // ILK = NOT LS1035.FLT AND (NOT M0.5 OR glb.Bridge): the M0.5 check is bypassed.
            EngineeringSnapshot snapshot = Snapshot(Map(Network(
                S("AN", "DB90.DBX 0.4"), S("A(", ""), S("ON", "M0.5"), S("O", "M0.2"), S(")", ""),
                S("=", "DB50.DBX 50.4"))));

            BehaviouralCondition ilk = Behaviour(snapshot).Conditions.Single();
            Assert.AreEqual(BehaviourConditionEffect.PartiallySimplified, ilk.Effect);
            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed, ilk.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.BypassFlagRule, ilk.SemanticRule);
            Assert.AreEqual("NOT (\"db.cm.DI\".AM_LS1035.FLT)", ilk.SimplifiedExpression.DisplayText);
            BehaviourConstantFinding bridged = ilk.ConstantFindings.Single(item =>
                item.FindingKind == BehaviourConstantFindingKind.ConditionBridged);
            StringAssert.Contains(bridged.Message, "glb.Bridge");
            StringAssert.Contains(bridged.Message, "M0.5");
        }

        [TestMethod]
        public void Analyzer_BridgeFlagInSeriesIsNotABypass()
        {
            // glb.Bridge AND M0.5 removes nothing: TRUE AND x = x.
            EngineeringSnapshot snapshot = Snapshot(Map(Network(
                S("A", "M0.2"), S("A", "DB90.DBX 0.4"), S("=", "DB50.DBX 50.4"))));

            BehaviouralCondition ilk = Behaviour(snapshot).Conditions.Single();
            Assert.AreNotEqual(BehaviourReviewClassification.BridgedOrBypassed, ilk.ReviewClassification);
            Assert.AreEqual(BehaviourConditionEffect.Dynamic, ilk.Effect);
        }

        [TestMethod]
        public void Catalogue_BridgeIsATrueBypassFlagAndAnalogueIsAFamily()
        {
            ResolvedBooleanConstant constant;
            bool conflict;
            Assert.IsTrue(BooleanConstantCatalogue.Default.TryResolve("\"glb.Bridge\"", null, out constant, out conflict));
            Assert.IsTrue(constant.Value);
            string symbol;
            Assert.IsTrue(BooleanConstantCatalogue.Default.IsBypassEvidence(constant.Evidence, out symbol));
            Assert.AreEqual("glb.Bridge", symbol);
            Assert.IsTrue(BooleanConstantCatalogue.Default.TryResolve("glb.One", null, out constant, out conflict));
            Assert.IsFalse(BooleanConstantCatalogue.Default.IsBypassEvidence(constant.Evidence, out symbol),
                "glb.One is a neutral constant, not a bypass.");

            ControlModuleTypeDefinition analogue = ControlModuleCatalogue.FindByDataType("Udt.cm.An");
            Assert.AreEqual("Analogue", analogue.ModuleFamily);
            Assert.AreEqual("db.cm.An", analogue.ExpectedContainerDbName);
            Assert.AreEqual("Analogue", ControlModuleFunctionCatalogue.FindByFunctionName("FnBlk.RAWScaling").ModuleFamily);
            Assert.AreEqual("Udt.cm.An", ControlModuleFunctionCatalogue.FindByFunctionName("FnBlkRAW.cm.AO").ExpectedModuleDataType);
        }

        [TestMethod]
        public void Correlation_AnalogueModulesUseRawProcessingFunctions()
        {
            var module = new DataBlockMemberInfo("AM_WE0003", "db.cm.An.AM_WE0003", "Udt.cm.An", null, 0, false, null, "42.0", null);
            var structure = new DataBlockStructureInfo("db.cm.An", 82, "Blocks", new[] { module }, null);
            var call = new BlockCallInfo("VitFeeders", 300, "Function", "Blocks/VitFeeders", "FnBlk.RAWScaling", 83, "FC",
                4, "Scale", 1, new[] { new CallParameterInfo("AIn", "InOut", "Udt.cm.An", "P#DB82.DBX 42.0", "db.cm.An.AM_WE0003") },
                null);
            var inventory = new PlcInventory("S7 Program", null, null, null, null, new[] { structure }, true, new[] { call }, true);
            var snapshot = new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(Program), inventory));

            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            Assert.AreEqual("Analogue", discovery.Modules.Single().ModuleFamily);
            Assert.AreEqual(ControlModuleDiscoveryStatus.Confirmed, discovery.Modules.Single().Status);
            ControlModuleImplementation implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery).Modules.Single();
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, implementation.Status);
            Assert.AreEqual("FnBlk.RAWScaling", implementation.CallSites.Single().ProcessingVariant);
        }

        // --- Fixture -------------------------------------------------------------------------

        private static Step7StatementModel S(string command, string operand)
        {
            return new Step7StatementModel(null, command, operand, null, null, null, null);
        }

        private static Step7StatementModel Labelled(string label, string command, string operand)
        {
            return new Step7StatementModel(label, command, operand, null, null, null, null);
        }

        private static Step7NetworkModel[] Network(params Step7StatementModel[] statements)
        {
            return new[] { new Step7NetworkModel(1, "Network 1", null, statements) };
        }

        private static PlcInventory Map(params Step7NetworkModel[][] networks)
        {
            var numbered = networks.Select((items, index) =>
                new Step7NetworkModel(index + 1, items[0].Title, null, items[0].Statements)).ToArray();
            var caller = new Step7CodeBlockModel(Step7BlockKind.Function, 506, new Step7InterfaceParameterModel[0], numbered, null);
            return new Step7InventoryMapper().MapInventory(ProgramWith(new[] { caller }));
        }

        // A sequence FC reading #Seq through the compiler's pointer preamble, called by
        // callerCount call sites (0, 1 or 2) with Seq := P#DB200.DBX 20.0.
        private static PlcInventory Map(Step7CodeBlockModel[] sequence, int callerCount)
        {
            var blocks = sequence.ToList();
            var callStatements = Enumerable.Range(0, callerCount).Select(_ =>
                new Step7StatementModel(null, "CALL", "FC210", null, "FC210", null, new[]
                {
                    new Step7CallArgumentModel("Seq", "IN_OUT", "UDT", "P#DB200.DBX 20.0")
                })).ToArray();
            blocks.Add(new Step7CodeBlockModel(Step7BlockKind.Function, 200, new Step7InterfaceParameterModel[0],
                new[] { new Step7NetworkModel(1, "Call sequences", null, callStatements) }, null));
            return new Step7InventoryMapper().MapInventory(ProgramWith(blocks.ToArray()));
        }

        private static Step7CodeBlockModel[] SequenceBlock()
        {
            var seq = new Step7InterfaceParameterModel("IN_OUT", "Seq", "UDT", 200, new[]
            {
                new Step7InterfaceMemberModel("SG", "BOOL", 0, 0),
                new Step7InterfaceMemberModel("PG", "BOOL", 0, 1),
                new Step7InterfaceMemberModel("StepNo", "INT", 6, 0)
            });
            Step7StatementModel[] preamble =
            {
                S("BLD", "5"), S("T", "LD3"), S("TAK", ""), S("T", "LD7"), S("L", "P##Seq"), S("LAR1", ""),
                S("L", "W[AR1,P#0.0]"), S("T", "LW1"), S("OPN", "DB[LW1]"), S("L", "D[AR1,P#2.0]"),
                S("LAR1", ""), S("L", "LD7"), S("L", "LD3")
            };
            var statements = preamble
                .Concat(new[] { S("A", "[AR1,P#0.0]"), S("A(", "") })
                .Concat(preamble)
                .Concat(new[]
                {
                    S("L", "W[AR1,P#6.0]"), S("L", "110"), S("==I", ""), S(")", ""),
                    S("S", "DB50.DBX 54.0")
                }).ToArray();
            return new[]
            {
                new Step7CodeBlockModel(Step7BlockKind.Function, 210, new[] { seq },
                    new[] { new Step7NetworkModel(1, "Start AM_M1020", null, statements) }, null)
            };
        }

        private static Step7ProgramModel ProgramWith(Step7CodeBlockModel[] codeBlocks)
        {
            var symbols = new[]
            {
                new Step7SymbolModel("Call cm Drv Feed", "FC 506", "FC 506", ""),
                new Step7SymbolModel("Seq Feed", "FC 210", "FC 210", ""),
                new Step7SymbolModel("Call Sequences", "FC 200", "FC 200", ""),
                new Step7SymbolModel("db.cm.Drv", "DB 50", "DB 50", ""),
                new Step7SymbolModel("db.cm.DI", "DB 90", "DB 90", ""),
                new Step7SymbolModel("db.seq", "DB 200", "DB 200", ""),
                new Step7SymbolModel("Udt.cm.Drv", "UDT 50", "UDT 50", ""),
                new Step7SymbolModel("Udt.cm.DI", "UDT 90", "UDT 90", ""),
                new Step7SymbolModel("UDT Sequence", "UDT 200", "UDT 200", ""),
                new Step7SymbolModel("glb.Bridge", "M 0.2", "BOOL", "")
            };
            var blocks = new[]
            {
                new Step7BlockModel(Step7BlockKind.Function, 506, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 210, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 200, null, "AWL", false, false)
            };
            var dataBlocks = new[]
            {
                new Step7DataBlockModel(50, new[]
                {
                    Drive("AM_M1017", 0),
                    Drive("AM_M1020", 50)
                }, null),
                new Step7DataBlockModel(90, new[]
                {
                    new Step7DataRowModel("AM_LS1035", "UDT", 90, "AM_LS1035 Flap Switch", 0, 0, false, null, new[]
                    {
                        Row("FLT", "BOOL", 0, 4)
                    })
                }, null),
                new Step7DataBlockModel(200, new[]
                {
                    Sequence("SeqAirLifts", 0),
                    Sequence("SeqFeed", 20)
                }, null)
            };
            return new Step7ProgramModel(Program, symbols, blocks, dataBlocks, true, codeBlocks, true, null);
        }

        private static Step7DataRowModel Drive(string name, int offset)
        {
            return new Step7DataRowModel(name, "UDT", 50, name, offset, 0, false, null, new[]
            {
                Row("MO", "BOOL", offset, 0),
                Row("ILK", "BOOL", offset, 4),
                Row("ME", "BOOL", offset, 5),
                Row("CR", "BOOL", offset, 6),
                Row("CmdSA", "BOOL", offset + 4, 0)
            });
        }

        private static Step7DataRowModel Sequence(string name, int offset)
        {
            return new Step7DataRowModel(name, "UDT", 200, null, offset, 0, false, null, new[]
            {
                Row("SG", "BOOL", offset, 0),
                Row("PG", "BOOL", offset, 1),
                Row("StepNo", "INT", offset + 6, 0)
            });
        }

        private static Step7DataRowModel Row(string name, string type, int byteAddress, int bit)
        {
            return new Step7DataRowModel(name, type, null, null, byteAddress, bit, false, null, null);
        }

        private static ExtractedLogicAssignment Single(PlcInventory inventory)
        {
            Assert.IsTrue(inventory.LogicAssignmentsIncluded);
            return inventory.LogicAssignments.Single(item => item.ResolvedDestinationPath != null);
        }

        private static EngineeringSnapshot Snapshot(PlcInventory inventory)
        {
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(Program), inventory));
        }

        private static ControlModuleBehaviourResult Behaviour(EngineeringSnapshot snapshot)
        {
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
            return new ControlModuleBehaviourAnalyzer().Analyze(snapshot, discovery, implementation);
        }
    }
}
