using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using TiaFds.Analysis;
using TiaFds.Openness.Xml;
using TiaFds.Reporting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class BehaviouralAnalysisTests
    {
        [TestMethod]
        public void XmlParser_ExtractsRealLadCoilShapeConstantsAndUnsupportedNodes()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "TestData", "sanitized-behaviour-lad.xml");
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "db.Auto.Enabled", "db.Sequence.Request",
                "db.cm.Drv.M1.SA1", "db.cm.Drv.M1.CR2", "db.cm.Drv.M1.ILK3"
            };
            BlockCallParseResult result = new BlockCallXmlParser().Parse(
                path, "FC_Test", 123, "Function", "Blocks/FC_Test", "LAD", known);

            Assert.AreEqual(0, result.Calls.Count);
            Assert.AreEqual(3, result.Assignments.Count);
            ExtractedLogicAssignment sa = result.Assignments[0];
            Assert.AreEqual("db.cm.Drv.M1.SA1", sa.ResolvedDestinationPath);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Or, sa.SourceExpression.Kind);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Not,
                sa.SourceExpression.Children[0].Kind);
            Assert.AreEqual("db.Auto.Enabled",
                sa.SourceExpression.Children[0].Children[0].ResolvedPath);
            Assert.AreEqual("Automatic start", sa.NetworkTitle);
            StringAssert.Contains(sa.NetworkComment, "sanitized");
            Assert.AreEqual(1, sa.StatementOrder);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Constant,
                result.Assignments[1].SourceExpression.Kind);
            Assert.AreEqual(true, result.Assignments[1].SourceExpression.ConstantValue);
            Assert.AreEqual(ExtractedLogicResolutionStatus.Unsupported,
                result.Assignments[2].ResolutionStatus);
        }

        [TestMethod]
        public void Analyzer_ClassifiesCorrelatesTracesAndDiagnosesWithoutGuessing()
        {
            EngineeringSnapshot snapshot = Snapshot(Assignments());
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation = Implementation(discovery);
            ControlModuleBehaviourResult result = new ControlModuleBehaviourAnalyzer().Analyze(
                snapshot, discovery, implementation);

            Assert.IsTrue(result.LogicAssignmentsAvailable);
            Assert.AreEqual(8, result.Conditions.Count);
            Assert.AreEqual(3, result.Conditions.Count(item =>
                item.Kind == BehaviouralConditionKind.StartCommand));
            Assert.AreEqual(3, result.Conditions.Count(item =>
                item.Kind == BehaviouralConditionKind.ControlRequest));
            Assert.AreEqual(2, result.Conditions.Count(item =>
                item.Kind == BehaviouralConditionKind.Interlock));
            Assert.AreEqual(4, result.Conditions.Count(item =>
                item.ResolutionStatus == BehaviouralConditionResolutionStatus.Complete));
            Assert.AreEqual(1, result.Conditions.Count(item =>
                item.ResolutionStatus == BehaviouralConditionResolutionStatus.Partial));
            Assert.AreEqual(1, result.Conditions.Count(item =>
                item.ResolutionStatus == BehaviouralConditionResolutionStatus.Unsupported));
            Assert.AreEqual(2, result.Conditions.Count(item =>
                item.ResolutionStatus == BehaviouralConditionResolutionStatus.Ambiguous));
            Assert.AreEqual("db.Auto.Enabled",
                result.Conditions.Single(item => item.ModuleName == "V1").Expression.ResolvedPath);
            Assert.IsTrue(result.Diagnostics.Any(item =>
                item.Code == "BEH104_MULTIPLE_ASSIGNMENTS"));
            Assert.IsTrue(result.Diagnostics.Any(item =>
                item.Code == "BEH102_EXPRESSION_NOT_SUPPORTED"));
            Assert.IsTrue(result.Diagnostics.Any(item =>
                item.Code == "BEH103_OPERAND_NOT_RESOLVED"));
            Assert.AreEqual(result.Diagnostics.Count, result.ManualReview.Count);
            Assert.IsFalse(result.Conditions.Any(item =>
                item.DestinationExpression == "#StartCondition"));
        }

        [TestMethod]
        public void Reporting_JsonExcelAndConsoleAgreeOnBehaviour()
        {
            EngineeringSnapshot snapshot = Snapshot(Assignments());
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation = Implementation(discovery);
            ControlModuleBehaviourResult behaviour =
                new ControlModuleBehaviourAnalyzer().Analyze(
                    snapshot, discovery, implementation);
            AnalysisReport report = new AnalysisReportBuilder().Build(
                snapshot, discovery, implementation, behaviour);

            Assert.AreEqual(8, report.BehaviourSummary.TotalConditionCount);
            Assert.AreEqual(3, report.BehaviourSummary.StartCommandCount);
            Assert.AreEqual(3, report.BehaviourSummary.ControlRequestCount);
            Assert.AreEqual(2, report.BehaviourSummary.InterlockCount);
            Assert.AreEqual(8, report.BehaviouralConditions.Count);
            Assert.AreEqual(1, report.Modules.Single(item =>
                item.ModuleName == "V1").StartCommands.Count);
            Assert.AreEqual(0, report.BaselineDiagnosticCounts.WarningCount);
            Assert.AreEqual(behaviour.Diagnostics.Count,
                report.DiagnosticCounts.WarningCount);

            string directory = Path.Combine(Path.GetTempPath(), "TiaFds.Behaviour.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string jsonPath = Path.Combine(directory, "report.json");
                string excelPath = Path.Combine(directory, "report.xlsx");
                new AnalysisReportJsonWriter().Write(report, jsonPath);
                new AnalysisReportExcelWriter().Write(report, excelPath);
                JObject json = JObject.Parse(File.ReadAllText(jsonPath));
                Assert.AreEqual("1.1", (string)json["schemaVersion"]);
                Assert.AreEqual(8, (int)json["behaviourSummary"]["totalConditionCount"]);
                Assert.AreEqual("And",
                    (string)json["modules"][0]["interlocks"][0]["expression"]["kind"]);

                using (var workbook = new XLWorkbook(excelPath))
                {
                    CollectionAssert.AreEqual(new[]
                    {
                        "Summary", "Modules", "Processing Calls",
                        "Behavioural Conditions", "Diagnostics", "Manual Review"
                    }, workbook.Worksheets.Select(item => item.Name).ToArray());
                    IXLWorksheet conditions = workbook.Worksheet("Behavioural Conditions");
                    Assert.AreEqual(9, conditions.LastRowUsed().RowNumber());
                    Assert.IsTrue(conditions.Tables.Any());
                    Assert.AreEqual(1, conditions.SheetView.SplitRow);
                    Assert.AreEqual(8, FindSummaryValue(
                        workbook.Worksheet("Summary"),
                        "Total behavioural conditions"));
                    Assert.AreEqual("Start Command Count",
                        workbook.Worksheet("Modules").Cell(1, 12).GetString());
                    IXLCell formulaLike = conditions.CellsUsed().First(item =>
                        item.GetString() == "@Indirect");
                    Assert.AreEqual(XLDataType.Text, formulaLike.DataType);
                    Assert.IsFalse(formulaLike.HasFormula);
                    Assert.IsTrue(conditions.Column(10).Style.Alignment.WrapText);
                }

                var console = new StringWriter();
                new AnalysisReportConsoleRenderer().PrintSummary(console, report);
                StringAssert.Contains(console.ToString(), "Behavioural conditions:");
                StringAssert.Contains(console.ToString(), "Start commands:");
            }
            finally { Directory.Delete(directory, true); }
        }

        [TestMethod]
        public void Analyzer_EmptyOrUnavailableLogicProducesNoConditions()
        {
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation = Implementation(discovery);
            EngineeringSnapshot unavailable = Snapshot(null, false);
            ControlModuleBehaviourResult result =
                new ControlModuleBehaviourAnalyzer().Analyze(
                    unavailable, discovery, implementation);
            Assert.IsFalse(result.LogicAssignmentsAvailable);
            Assert.AreEqual(0, result.Conditions.Count);
            Assert.AreEqual(0, result.Diagnostics.Count);
        }

        [TestMethod]
        public void Analyzer_BoundsTemporaryTracingAndKeepsAmbiguityDeterministic()
        {
            var assignments = new List<ExtractedLogicAssignment>();
            assignments.Add(Assignment("#T1", Operand("Input", "db.Input"), 1));
            for (int index = 2; index <= 18; index++)
                assignments.Add(Assignment("#T" + index,
                    Operand("#T" + (index - 1), null), index));
            assignments.Add(Assignment("#Ambiguous", Operand("A", "db.A"), 19));
            assignments.Add(Assignment("#Ambiguous", Operand("B", "db.B"), 20));
            assignments.Add(Assignment("db.cm.Drv.M1.SA3",
                Operand("#Ambiguous", null), 21));
            assignments.Add(Assignment("db.cm.Drv.M1.CR4",
                Operand("#T18", null), 22));

            var analyzer = new ControlModuleBehaviourAnalyzer();
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation = Implementation(discovery);
            ControlModuleBehaviourResult first = analyzer.Analyze(
                Snapshot(assignments), discovery, implementation);
            ControlModuleBehaviourResult second = analyzer.Analyze(
                Snapshot(assignments), discovery, implementation);

            Assert.AreEqual(2, first.Conditions.Count);
            Assert.AreEqual(BehaviouralConditionResolutionStatus.Ambiguous,
                first.Conditions.Single(item => item.Member == "SA3").ResolutionStatus);
            Assert.AreEqual(BehaviouralConditionResolutionStatus.Unsupported,
                first.Conditions.Single(item => item.Member == "CR4").ResolutionStatus);
            Assert.IsTrue(first.Diagnostics.Any(item =>
                item.Code == "BEH105_ASSIGNMENT_AMBIGUOUS"));
            Assert.IsTrue(first.Diagnostics.Any(item =>
                item.Code == "BEH106_TEMPORARY_TRACE_INCOMPLETE"));
            CollectionAssert.AreEqual(
                first.Conditions.Select(item => item.ModuleMemberPath + "|" +
                    item.Member + "|" + item.ResolutionStatus).ToArray(),
                second.Conditions.Select(item => item.ModuleMemberPath + "|" +
                    item.Member + "|" + item.ResolutionStatus).ToArray());
        }

        [TestMethod]
        public void SnapshotRoundTrip_PreservesLogicTreesEnumsAndMetadata()
        {
            string directory = Path.Combine(Path.GetTempPath(), "TiaFds.Logic.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "snapshot.json");
                new EngineeringSnapshotJsonWriter().Write(
                    Snapshot(Assignments()), path, false);
                string json = File.ReadAllText(path);
                StringAssert.Contains(json, "\"logicAssignmentsIncluded\": true");
                StringAssert.Contains(json, "\"kind\": \"And\"");
                EngineeringSnapshot actual =
                    new EngineeringSnapshotJsonReader().Read(path);
                Assert.AreEqual(10,
                    actual.Project.Inventory.LogicAssignments.Count);
                Assert.AreEqual(ExtractedBooleanExpressionKind.And,
                    actual.Project.Inventory.LogicAssignments[2].SourceExpression.Kind);
                Assert.AreEqual("Network 3",
                    actual.Project.Inventory.LogicAssignments[2].NetworkTitle);
                Assert.AreEqual(3,
                    actual.Project.Inventory.LogicAssignments[2].StatementOrder);
            }
            finally { Directory.Delete(directory, true); }
        }

        private static IReadOnlyList<ExtractedLogicAssignment> Assignments()
        {
            return new[]
            {
                Assignment("db.cm.Drv.M1.SA1", Operand("Auto", "db.Auto.Enabled"), 1),
                Assignment("db.cm.Drv.M1.CR[2]", Not(Operand("Request", "db.Sequence.Request")), 2),
                Assignment("db.cm.Drv.M1.ILK3", And(
                    Operand("Healthy", "db.IO.Healthy"),
                    Or(Operand("Auto", "db.Auto.Enabled"), Constant(false))), 3),
                Assignment("#StartCondition", Operand("Auto", "db.Auto.Enabled"), 4),
                Assignment("db.cm.Vlv.V1.SA", Operand("#StartCondition", null), 5),
                Assignment("db.cm.Drv.M1.ILK4", Operand("@Indirect", null), 6),
                Assignment("db.cm.Drv.M1.SA2", Unknown("Timer graph"), 7),
                Assignment("db.cm.Drv.M1.CR3", Operand("A", "db.A"), 8),
                Assignment("db.cm.Drv.M1.CR3", Operand("B", "db.B"), 9),
                Assignment("db.cm.Drv.M1.SAIL", Operand("False positive", "db.X"), 10)
            };
        }

        private static ExtractedLogicAssignment Assignment(
            string destination, ExtractedBooleanExpression expression, int order)
        {
            return new ExtractedLogicAssignment(
                destination, destination.StartsWith("#", StringComparison.Ordinal)
                    ? null : destination,
                expression, expression.DisplayText, "FC_Test", 123, "Function", "LAD",
                order, "Network " + order, "Comment", order,
                expression.Kind == ExtractedBooleanExpressionKind.Unknown
                    ? ExtractedLogicResolutionStatus.Unsupported
                    : ExtractedLogicResolutionStatus.Complete);
        }

        private static ExtractedBooleanExpression Operand(string text, string path)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Operand, text, path, null, null);
        }

        private static ExtractedBooleanExpression Constant(bool value)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Constant,
                value ? "TRUE" : "FALSE", null, value, null);
        }

        private static ExtractedBooleanExpression Not(ExtractedBooleanExpression child)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Not, "NOT (" + child.DisplayText + ")",
                null, null, new[] { child });
        }

        private static ExtractedBooleanExpression And(params ExtractedBooleanExpression[] children)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.And, "AND expression", null, null, children);
        }

        private static ExtractedBooleanExpression Or(params ExtractedBooleanExpression[] children)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Or, "OR expression", null, null, children);
        }

        private static ExtractedBooleanExpression Unknown(string text)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Unknown, text, null, null, null);
        }

        private static EngineeringSnapshot Snapshot(
            IReadOnlyList<ExtractedLogicAssignment> assignments,
            bool included = true)
        {
            var inventory = new PlcInventory(
                "PLC", new ProgramBlockInfo[0], new PlcTagTableInfo[0],
                new PlcDataTypeInfo[0], new InventoryDiagnostic[0],
                new DataBlockStructureInfo[0], true,
                new BlockCallInfo[0], true, assignments, included);
            return new EngineeringSnapshot(
                SnapshotSchema.CurrentVersion, ProductVersion.Current,
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                new ProjectSnapshot("Behaviour", "Behaviour.ap15_1", null,
                    new PlcInfo("PLC", "Station", "CPU"), inventory));
        }

        private static ControlModuleDiscoveryResult Discovery()
        {
            return new ControlModuleDiscoveryResult(
                new ControlModuleContainerInfo[0],
                new[]
                {
                    Module("M1", "Drive", "db.cm.Drv.M1", "Udt.cm.Drv"),
                    Module("V1", "Valve", "db.cm.Vlv.V1", "Udt.cm.Vlv")
                },
                new ModuleDiscoveryDiagnostic[0], true);
        }

        private static ControlModuleInfo Module(
            string name, string family, string path, string type)
        {
            int dot = path.LastIndexOf('.');
            return new ControlModuleInfo(name, family, path.Substring(0, dot), null,
                path, type, name + " description", false, null,
                ControlModuleDiscoveryStatus.Confirmed);
        }

        private static ControlModuleImplementationResult Implementation(
            ControlModuleDiscoveryResult discovery)
        {
            return new ControlModuleImplementationResult(
                discovery.Modules.Select(module => new ControlModuleImplementation(
                    module, ControlModuleImplementationStatus.Correlated,
                    new ControlModuleCallSite[0])).ToArray(),
                new ControlModuleImplementationDiagnostic[0], true, true);
        }

        private static int FindSummaryValue(IXLWorksheet sheet, string label)
        {
            IXLCell cell = sheet.CellsUsed().First(item => item.GetString() == label);
            return cell.CellRight().GetValue<int>();
        }
    }
}
