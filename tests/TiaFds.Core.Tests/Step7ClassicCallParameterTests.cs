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
    // LAD passes boolean actuals through anonymous local bits: inputs are assigned before the
    // call (A "ioDI".X; = L 0.0 ... RN := L 0.0) and outputs copied after it, in the CALL row's
    // post-call statements (UC FC50; A L 1.1; OPN DB10; = DBX16.1). These tests pin the trace.
    [TestClass]
    public sealed class Step7ClassicCallParameterTests
    {
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        private static readonly Step7ProgramIdentity Program =
            new Step7ProgramIdentity("S7 Program", "SIMATIC 300", "CPU 315-2 PN/DP");

        [TestMethod]
        public void Inputs_AreTracedToTheWiredOperandOrSymbol()
        {
            BlockCallInfo call = SingleCall(Map(StandardNetwork()));

            CallParameterInfo rn = Parameter(call, "RN");
            Assert.AreEqual("\"ioDI\".AM_M1020RN", rn.ActualExpression);
            Assert.AreEqual("L 0.0", rn.RawActualExpression);
            Assert.AreEqual("ioDI.AM_M1020RN", rn.ResolvedMemberPath);
            Assert.AreEqual(CallParameterTraceStatus.Traced, rn.TraceStatus);
            Assert.AreEqual(ExtractedBooleanExpressionKind.Operand, rn.ActualExpressionTree.Kind);

            CallParameterInfo tr = Parameter(call, "TR");
            Assert.AreEqual("\"glb.One\"", tr.ActualExpression);
            Assert.AreEqual("L 0.1", tr.RawActualExpression);
            Assert.IsNull(tr.ResolvedMemberPath, "Global symbols are not DB members.");

            Assert.AreEqual(0, call.Diagnostics.Count);
        }

        [TestMethod]
        public void Inputs_KeepTheWholeExpressionWhenSeveralOperandsDriveThem()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                S("A", "M0.5"), S("AN", "M0.6"), S("=", "L0.0"),
                Call(Argument("RN", "IN", "BOOL", "L 0.0"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo rn = Parameter(call, "RN");
            Assert.AreEqual("M0.5 AND NOT M0.6", rn.ActualExpression);
            Assert.AreEqual(ExtractedBooleanExpressionKind.And, rn.ActualExpressionTree.Kind);
            Assert.IsNull(rn.ResolvedMemberPath);
        }

        [TestMethod]
        public void Outputs_AreTracedThroughThePostCallCopies()
        {
            BlockCallInfo call = SingleCall(Map(StandardNetwork()));

            CallParameterInfo mo = Parameter(call, "MO");
            Assert.AreEqual("\"ioDO\".AM_M1020MO", mo.ActualExpression);
            Assert.AreEqual("L 1.1", mo.RawActualExpression);
            Assert.AreEqual("ioDO.AM_M1020MO", mo.ResolvedMemberPath);
            Assert.AreEqual(CallParameterTraceStatus.Traced, mo.TraceStatus);

            CallParameterInfo rawOut = Parameter(call, "RawOut");
            Assert.AreEqual("\"ioDO\".Speed", rawOut.ActualExpression, "Word outputs: L LW2; OPN DB10; T DBW20.");
            Assert.AreEqual("LW2", rawOut.RawActualExpression);
        }

        [TestMethod]
        public void WordInputs_AreTracedThroughLocalWords()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                S("L", "DB10.DBW 20"), S("T", "LW4"),
                Call(Argument("RawIn", "IN", "INT", "LW4"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo rawIn = Parameter(call, "RawIn");
            Assert.AreEqual("\"ioDO\".Speed", rawIn.ActualExpression);
            Assert.AreEqual("ioDO.Speed", rawIn.ResolvedMemberPath);
        }

        [TestMethod]
        public void DirectGlobalActuals_AreShownBySymbol()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                Call(Argument("MO", "OUT", "BOOL", "M 0.4"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo mo = Parameter(call, "MO");
            Assert.AreEqual("\"glb.DontCare\"", mo.ActualExpression);
            Assert.AreEqual("M 0.4", mo.RawActualExpression);
            Assert.IsNull(mo.TraceStatus, "A direct actual needs no trace.");
            Assert.AreEqual(0, call.Diagnostics.Count, "Not connected is deliberate and not diagnosed.");
        }

        [TestMethod]
        public void UntraceableInput_KeepsTheRawAddressAndIsDiagnosed()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                Call(Argument("RN", "IN", "BOOL", "L 0.3"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo rn = Parameter(call, "RN");
            Assert.AreEqual("L 0.3", rn.ActualExpression);
            Assert.IsNull(rn.RawActualExpression);
            Assert.AreEqual(CallParameterTraceStatus.NotTraced, rn.TraceStatus);
            InventoryDiagnostic diagnostic = call.Diagnostics.Single();
            Assert.AreEqual(Step7DiagnosticCodes.CallInputNotTraced, diagnostic.Code);
            StringAssert.Contains(diagnostic.Message, "no assignment to L0.3");
        }

        [TestMethod]
        public void InputAfterAConditionalJump_IsNotGuessed()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                S("A", "M0.5"), S("=", "L0.0"),
                S("A", "M0.6"), S("JCN", "_001"), S("A", "M0.7"), S("=", "L0.0"), Labelled("_001", "NOP", "0"),
                Call(Argument("RN", "IN", "BOOL", "L 0.0"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo rn = Parameter(call, "RN");
            Assert.AreEqual(CallParameterTraceStatus.NotTraced, rn.TraceStatus);
            Assert.AreEqual("L 0.0", rn.ActualExpression);
            StringAssert.Contains(call.Diagnostics.Single().Message, "conditional");
        }

        [TestMethod]
        public void OutputCopyWithExtraLogic_IsDiagnosed()
        {
            BlockCallInfo call = SingleCall(Map(Network(
                Call(new[] { S("A", "L 1.1"), S("A", "M0.5"), S("OPN", "DB10"), S("=", "DBX16.1") },
                    Argument("MO", "OUT", "BOOL", "L 1.1"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            CallParameterInfo mo = Parameter(call, "MO");
            Assert.AreEqual(CallParameterTraceStatus.NotTraced, mo.TraceStatus);
            InventoryDiagnostic diagnostic = call.Diagnostics.Single();
            Assert.AreEqual(Step7DiagnosticCodes.CallOutputNotTraced, diagnostic.Code);
            StringAssert.Contains(diagnostic.Message, "'A M0.5'");
        }

        [TestMethod]
        public void PostCallCopies_BecomeLogicAssignments()
        {
            PlcInventory inventory = Map(StandardNetwork());

            ExtractedLogicAssignment output = inventory.LogicAssignments.Single(item =>
                item.ResolvedDestinationPath == "ioDO.AM_M1020MO");
            Assert.AreEqual("\"FnBlk.cm.Drv\".MO (call output)", output.SourceExpression.DisplayText);
        }

        [TestMethod]
        public void Report_ExposesEveryParameterInJsonAndTheCallParametersSheet()
        {
            AnalysisReport report = Report(Map(StandardNetwork()));
            Assert.AreEqual("1.4", report.SchemaVersion);

            AnalysisCallSite site = report.Modules.Single(item => item.ModuleName == "AM_M1020").CallSites.Single();
            CollectionAssert.AreEqual(new[] { "Drv", "MO", "RawOut", "RN", "TR" },
                site.Parameters.Select(item => item.FormalName).ToArray(), "Sorted by formal name.");
            AnalysisCallParameter tr = site.Parameters.Single(item => item.FormalName == "TR");
            Assert.AreEqual(true, tr.ConstantValue);
            Assert.AreEqual("BP_GLB_ONE", tr.ConstantRule);
            Assert.IsFalse(tr.NotConnected);

            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            try
            {
                string jsonPath = Path.Combine(directory, "report.json");
                new AnalysisReportJsonWriter().Write(report, jsonPath);
                JToken rn = JObject.Parse(File.ReadAllText(jsonPath))["modules"]
                    .Single(item => (string)item["name"] == "AM_M1020")["processingCalls"][0]["parameters"]
                    .Single(item => (string)item["formalName"] == "RN");
                Assert.AreEqual("\"ioDI\".AM_M1020RN", (string)rn["actualExpression"]);
                Assert.AreEqual("L 0.0", (string)rn["rawActualExpression"]);
                Assert.AreEqual("Traced", (string)rn["traceStatus"]);

                string excelPath = Path.Combine(directory, "report.xlsx");
                new AnalysisReportExcelWriter().Write(report, excelPath);
                using (var workbook = new ClosedXML.Excel.XLWorkbook(excelPath))
                {
                    Assert.AreEqual("Call Parameters", workbook.Worksheets.Last().Name);
                    var sheet = workbook.Worksheet("Call Parameters");
                    Assert.AreEqual("Formal Name", sheet.Cell(1, 9).GetString());
                    Assert.AreEqual("Trace Status", sheet.Cell(1, 17).GetString());
                    var row = sheet.RowsUsed().Single(item => item.Cell(9).GetString() == "MO");
                    Assert.AreEqual("\"ioDO\".AM_M1020MO", row.Cell(12).GetString());
                    Assert.AreEqual("L 1.1", row.Cell(13).GetString());
                    Assert.AreEqual("ioDO.AM_M1020MO", row.Cell(14).GetString());
                }
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void Report_FlagsTheNotConnectedPlaceholder()
        {
            AnalysisReport report = Report(Map(Network(
                Call(Argument("MO", "OUT", "BOOL", "M 0.4"), Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")))));

            AnalysisCallParameter mo = report.Modules.Single(item => item.ModuleName == "AM_M1020")
                .CallSites.Single().Parameters.Single(item => item.FormalName == "MO");
            Assert.IsTrue(mo.NotConnected);
            Assert.IsNull(mo.ConstantValue, "Outputs are not classified as constants.");
        }

        // --- Fixture -------------------------------------------------------------------------

        // FC511 network 3 as the A-Mill decompiler emits it (see 08-evidence), plus a word output.
        private static Step7StatementModel[] StandardNetwork()
        {
            return new[]
            {
                S("A", "DB11.DBX 24.4"), S("=", "L0.0"), S("BLD", "103"),
                Symbolised("A", "M0.1", "glb.One"), S("=", "L0.1"), S("BLD", "103"),
                Call(new[]
                    {
                        S("A", "L 1.1"), S("OPN", "DB10"), S("=", "DBX16.1"), S("BLD", "2"),
                        S("L", "LW2"), S("OPN", "DB10"), S("T", "DBW20")
                    },
                    Argument("RN", "IN", "BOOL", "L 0.0"),
                    Argument("TR", "IN", "BOOL", "L 0.1"),
                    Argument("MO", "OUT", "BOOL", "L 1.1"),
                    Argument("RawOut", "OUT", "INT", "LW2"),
                    Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0")),
                S("NOP", "0")
            };
        }

        private static Step7StatementModel[] Network(params Step7StatementModel[] statements)
        {
            return statements;
        }

        private static Step7StatementModel Call(params Step7CallArgumentModel[] arguments)
        {
            return Call(new Step7StatementModel[0], arguments);
        }

        private static Step7StatementModel Call(Step7StatementModel[] postCall, params Step7CallArgumentModel[] arguments)
        {
            return new Step7StatementModel(null, "CALL", "FC50", "FnBlk.cm.Drv", "FC50", null, arguments, postCall);
        }

        private static Step7CallArgumentModel Argument(string name, string section, string type, string value)
        {
            return new Step7CallArgumentModel(name, section, type, value);
        }

        private static Step7StatementModel S(string command, string operand)
        {
            return new Step7StatementModel(null, command, operand, null, null, null, null);
        }

        private static Step7StatementModel Symbolised(string command, string operand, string symbol)
        {
            return new Step7StatementModel(null, command, operand, symbol, null, null, null);
        }

        private static Step7StatementModel Labelled(string label, string command, string operand)
        {
            return new Step7StatementModel(label, command, operand, null, null, null, null);
        }

        private static PlcInventory Map(Step7StatementModel[] statements)
        {
            var processing = new Step7CodeBlockModel(Step7BlockKind.Function, 50, new[]
            {
                new Step7InterfaceParameterModel("IN", "RN", "BOOL", null),
                new Step7InterfaceParameterModel("IN", "TR", "BOOL", null),
                new Step7InterfaceParameterModel("IN", "RawIn", "INT", null),
                new Step7InterfaceParameterModel("OUT", "MO", "BOOL", null),
                new Step7InterfaceParameterModel("OUT", "RawOut", "INT", null),
                new Step7InterfaceParameterModel("IN_OUT", "Drv", "UDT", 50)
            }, new Step7NetworkModel[0], null);
            var caller = new Step7CodeBlockModel(Step7BlockKind.Function, 511, new Step7InterfaceParameterModel[0],
                new[] { new Step7NetworkModel(3, "AM_M1020 INDUSTRIAL VIBRATOR DRIVE FUNCTION BLOCK", null, statements) },
                null);

            var symbols = new[]
            {
                new Step7SymbolModel("FnBlk.cm.Drv", "FC 50", "FC 50", ""),
                new Step7SymbolModel("Call cm Drv Transfer", "FC 511", "FC 511", ""),
                new Step7SymbolModel("db.cm.Drv", "DB 50", "DB 50", ""),
                new Step7SymbolModel("ioDO", "DB 10", "DB 10", ""),
                new Step7SymbolModel("ioDI", "DB 11", "DB 11", ""),
                new Step7SymbolModel("Udt.cm.Drv", "UDT 50", "UDT 50", ""),
                new Step7SymbolModel("glb.One", "M 0.1", "BOOL", ""),
                new Step7SymbolModel("glb.DontCare", "M 0.4", "BOOL", "")
            };
            var blocks = new[]
            {
                new Step7BlockModel(Step7BlockKind.Function, 50, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 511, null, "KOP", false, false)
            };
            var dataBlocks = new[]
            {
                new Step7DataBlockModel(50, new[]
                {
                    new Step7DataRowModel("AM_M1020", "UDT", 50, "AM_M1020 Industrial Vibrator", 50, 0, false, null, new[]
                    {
                        Row("MO", "BOOL", 50, 0), Row("ILK", "BOOL", 50, 4)
                    })
                }, null),
                new Step7DataBlockModel(10, new[] { Row("AM_M1020MO", "BOOL", 16, 1), Row("Speed", "INT", 20, 0) }, null),
                new Step7DataBlockModel(11, new[] { Row("AM_M1020RN", "BOOL", 24, 4) }, null)
            };
            return new Step7InventoryMapper().MapInventory(new Step7ProgramModel(Program, symbols, blocks, dataBlocks, true,
                new[] { processing, caller }, true, null));
        }

        private static Step7DataRowModel Row(string name, string type, int byteAddress, int bit)
        {
            return new Step7DataRowModel(name, type, null, null, byteAddress, bit, false, null, null);
        }

        private static BlockCallInfo SingleCall(PlcInventory inventory)
        {
            return inventory.BlockCalls.Single(call => call.CalledBlockName == "FnBlk.cm.Drv");
        }

        private static CallParameterInfo Parameter(BlockCallInfo call, string name)
        {
            return call.Parameters.Single(parameter => parameter.FormalName == name);
        }

        private static AnalysisReport Report(PlcInventory inventory)
        {
            var snapshot = new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(Program), inventory));
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
            return new AnalysisReportBuilder().Build(snapshot, discovery, implementation,
                new ControlModuleBehaviourAnalyzer().Analyze(snapshot, discovery, implementation));
        }
    }
}
