using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using TiaFds.Analysis;
using TiaFds.Reporting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class BooleanConstantAnalysisTests
    {
        [TestMethod]
        public void Catalogue_ResolvesOnlyLiteralsAndExactConfiguredSymbols()
        {
            ResolvedBooleanConstant constant;
            bool conflict;
            BooleanConstantCatalogue catalogue = BooleanConstantCatalogue.Default;

            Assert.IsTrue(catalogue.TryResolve("TRUE", null,
                out constant, out conflict));
            Assert.AreEqual(BooleanConstantSource.Literal, constant.Source);
            Assert.IsTrue(constant.Value);
            Assert.IsTrue(catalogue.TryResolve("\"glb.Zero\"", null,
                out constant, out conflict));
            Assert.AreEqual(BooleanConstantSource.KnownProjectSymbol,
                constant.Source);
            Assert.IsFalse(constant.Value);
            Assert.IsTrue(catalogue.TryResolve("anything", "\"glb\".One",
                out constant, out conflict));
            Assert.IsTrue(constant.Value);
            Assert.IsTrue(catalogue.TryResolve("GLB.ONE", null,
                out constant, out conflict));
            Assert.IsFalse(catalogue.TryResolve("DB1.OneAlarm", null,
                out constant, out conflict));
            Assert.IsFalse(catalogue.TryResolve("SomeTagNamedZero", null,
                out constant, out conflict));
            Assert.IsFalse(catalogue.TryResolve("1", null,
                out constant, out conflict));
        }

        [TestMethod]
        public void Analyzer_SimplifiesConstantsWithoutNameOrOwnershipFalsePositives()
        {
            Scenario scenario = CreateScenario(BooleanConstantCatalogue.Default);
            ControlModuleBehaviourResult result = scenario.Result;

            Assert.AreEqual(13, result.Conditions.Count);
            Assert.IsFalse(result.Conditions.Any(item =>
                item.ResolvedDestinationPath == "Seq_Header.ILK"));
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyTrue,
                Find(result, "M1", "SA1").Effect);
            Assert.AreEqual(BehaviourReviewClassification.PermanentlyEnabled,
                Find(result, "M1", "SA1").ReviewClassification);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyFalse,
                Find(result, "M1", "SA2").Effect);
            Assert.AreEqual(BehaviourReviewClassification.PermanentlyDisabled,
                Find(result, "M1", "SA2").ReviewClassification);
            Assert.AreEqual("Auto",
                Find(result, "M1", "SA3").SimplifiedExpression.DisplayText);
            Assert.AreEqual(BehaviourConditionEffect.Dynamic,
                Find(result, "M1", "SA3").Effect);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyTrue,
                Find(result, "M1", "CR1").Effect);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyFalse,
                Find(result, "M1", "CR2").Effect);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyTrue,
                Find(result, "M1", "CR3").Effect);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyFalse,
                Find(result, "M1", "CR4").Effect);
            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed,
                Find(result, "M1", "ILK1").ReviewClassification);
            Assert.AreEqual("DRV_ILK_ACTIVE_HIGH_PERMISSIVE",
                Find(result, "M1", "ILK1").SemanticRule);
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyFalse,
                Find(result, "M1", "ILK2").Effect);
            Assert.AreEqual(BehaviourReviewClassification.PermanentlyAsserted,
                Find(result, "M1", "ILK2").ReviewClassification);
            Assert.AreEqual(BehaviourConditionEffect.Dynamic,
                Find(result, "M1", "ILK3").Effect);
            Assert.AreEqual(BehaviourConditionEffect.Unknown,
                Find(result, "M1", "ILK4").Effect);
            Assert.AreEqual(BehaviouralConditionResolutionStatus.Partial,
                Find(result, "M1", "ILK4").ResolutionStatus);
            Assert.AreEqual(BehaviouralConditionResolutionStatus.Partial,
                Find(result, "M1", "ILK5").ResolutionStatus);
            Assert.IsTrue(result.Diagnostics.Any(item =>
                item.Code == "BEH118_BOOLEAN_LITERAL_DATATYPE_AMBIGUOUS"));
            Assert.IsFalse(result.Diagnostics.Any(item =>
                item.Code == "BEH115_CONSTANT_SEMANTICS_UNKNOWN" &&
                item.Member == "ILK1"));
            Assert.IsTrue(Find(result, "V1", "CR1")
                .OriginalExpression.Children.Any(item =>
                    item.ResolvedConstant != null &&
                    item.ResolvedConstant.Source ==
                    BooleanConstantSource.TemporaryTrace));
            Assert.AreEqual(BehaviourConditionEffect.PermanentlyFalse,
                Find(result, "V1", "CR1").Effect);
        }

        [TestMethod]
        public void Analyzer_ReportsCatalogueConflictsInsteadOfGuessing()
        {
            var catalogue = new BooleanConstantCatalogue(new[]
            {
                new BooleanConstantMapping("glb.One", true, "RULE_TRUE"),
                new BooleanConstantMapping("glb.One", false, "RULE_FALSE")
            });
            Scenario scenario = CreateScenario(catalogue);
            BehaviouralCondition condition = Find(scenario.Result, "M1", "ILK1");
            Assert.AreEqual(BehaviouralConditionResolutionStatus.Partial,
                condition.ResolutionStatus);
            Assert.IsNull(condition.EffectiveConstantValue);
            Assert.IsTrue(scenario.Result.Diagnostics.Any(item =>
                item.Code == "BEH119_CONSTANT_CATALOGUE_CONFLICT"));
        }

        [TestMethod]
        public void Semantics_UsesConfirmedPolarityForAllDriveVariants()
        {
            IBehaviourConditionSemantics policy =
                DefaultBehaviourConditionSemantics.Instance;
            BehaviourSemanticInterpretation healthy = policy.Interpret(
                "Drive", new[] { "DrvType1" },
                BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);
            BehaviourSemanticInterpretation active = policy.Interpret(
                "Drive", new[] { "DrvType1" },
                BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyFalse, false);
            BehaviourSemanticInterpretation mixedDrive = policy.Interpret(
                "Drive", new[] { "DrvType0", "DrvType1", "DrvType3" },
                BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);
            BehaviourSemanticInterpretation unknownDrive = policy.Interpret(
                "Drive", new[] { "DrvType99" },
                BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);
            BehaviourSemanticInterpretation unknown = policy.Interpret(
                "Valve", new[] { "VlvType1" },
                BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);

            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed,
                healthy.Classification);
            Assert.AreEqual(BehaviourReviewClassification.PermanentlyAsserted,
                active.Classification);
            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed,
                mixedDrive.Classification);
            Assert.AreEqual(
                BehaviourReviewClassification.RequiresManualInterpretation,
                unknownDrive.Classification);
            Assert.AreEqual(
                BehaviourReviewClassification.RequiresManualInterpretation,
                unknown.Classification);
        }

        [TestMethod]
        public void Reporting_PreservesOriginalSimplifiedAndEffectiveContracts()
        {
            Scenario scenario = CreateScenario(BooleanConstantCatalogue.Default);
            AnalysisReport report = new AnalysisReportBuilder().Build(
                scenario.Snapshot, scenario.Discovery,
                scenario.Implementation, scenario.Result);

            Assert.AreEqual("1.2", report.SchemaVersion);
            Assert.AreEqual(13, report.BehaviourSummary.TotalConditionCount);
            Assert.AreEqual(4, report.BehaviourSummary.PermanentlyTrueCount);
            Assert.AreEqual(5, report.BehaviourSummary.PermanentlyFalseCount);
            Assert.AreEqual(3, report.BehaviourSummary.PermanentlyEnabledCount);
            Assert.AreEqual(4, report.BehaviourSummary.PermanentlyDisabledCount);
            Assert.AreEqual(2,
                report.BehaviourSummary.SemanticReviewRequiredCount);
            Assert.AreEqual(1,
                report.BehaviourSummary.BridgedOrBypassedCount);
            Assert.AreEqual(1,
                report.BehaviourSummary.PermanentlyAssertedCount);

            string directory = Path.Combine(Path.GetTempPath(),
                "TiaFds.Constants.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string jsonPath = Path.Combine(directory, "report.json");
                string excelPath = Path.Combine(directory, "report.xlsx");
                new AnalysisReportJsonWriter().Write(report, jsonPath);
                new AnalysisReportExcelWriter().Write(report, excelPath);

                JObject json = JObject.Parse(File.ReadAllText(jsonPath));
                JToken condition = json["behaviouralConditions"]
                    .First(item => (string)item["moduleName"] == "M1" &&
                                   (string)item["member"] == "CR2");
                Assert.IsNotNull(condition["originalExpression"]);
                Assert.AreEqual("FALSE",
                    (string)condition["simplifiedDisplayText"]);
                Assert.AreEqual(false,
                    (bool)condition["effectiveConstantValue"]);
                Assert.AreEqual("PermanentlyFalse",
                    (string)condition["effect"]);
                Assert.IsTrue(condition["constantFindings"].Any());

                using (var workbook = new XLWorkbook(excelPath))
                {
                    IXLWorksheet conditions =
                        workbook.Worksheet("Behavioural Conditions");
                    Assert.AreEqual("Effective Constant Value",
                        conditions.Cell(1, 12).GetString());
                    Assert.AreEqual("Condition Effect",
                        conditions.Cell(1, 13).GetString());
                    Assert.AreEqual("Review Classification",
                        conditions.Cell(1, 14).GetString());
                    Assert.IsTrue(conditions.CellsUsed().Any(item =>
                        item.GetString() == "TRUE"));
                    Assert.IsTrue(conditions.CellsUsed().Any(item =>
                        item.GetString() == "FALSE"));
                    Assert.IsFalse(conditions.CellsUsed().Any(item =>
                        item.HasFormula));
                    Assert.AreEqual("Permanently TRUE",
                        workbook.Worksheet("Modules").Cell(1, 16).GetString());
                }

                var console = new StringWriter();
                new AnalysisReportConsoleRenderer().PrintSummary(console, report);
                StringAssert.Contains(console.ToString(),
                    "Constant and bypass analysis:");
                StringAssert.Contains(console.ToString(),
                    "Permanently FALSE:");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static BehaviouralCondition Find(
            ControlModuleBehaviourResult result, string module, string member)
        {
            return result.Conditions.Single(item =>
                item.ModuleName == module && item.Member == member);
        }

        private static Scenario CreateScenario(BooleanConstantCatalogue catalogue)
        {
            IReadOnlyList<ExtractedLogicAssignment> assignments = new[]
            {
                Assignment("db.cm.Drv.M1.SA1", Constant(true), 1),
                Assignment("db.cm.Drv.M1.SA2", Constant(false), 2),
                Assignment("db.cm.Drv.M1.SA3", And(
                    Operand("Auto", "db.Auto"), Operand("glb.One", null)), 3),
                Assignment("db.cm.Drv.M1.CR1", Or(
                    Operand("@Unknown", null), Operand("glb.One", null)), 4),
                Assignment("db.cm.Drv.M1.CR2", And(
                    Operand("@Unknown", null), Operand("glb.Zero", null)), 5),
                Assignment("db.cm.Drv.M1.CR3",
                    Not(Operand("glb.Zero", null)), 6),
                Assignment("db.cm.Drv.M1.CR4",
                    Not(Operand("glb.One", null)), 7),
                Assignment("db.cm.Drv.M1.ILK1",
                    Operand("glb.One", null), 8),
                Assignment("db.cm.Drv.M1.ILK2", And(
                    Operand("Healthy", "db.Healthy"),
                    Operand("glb.Zero", null)), 9),
                Assignment("db.cm.Drv.M1.ILK3",
                    Operand("DB1.OneAlarm", "DB1.OneAlarm"), 10),
                Assignment("db.cm.Drv.M1.ILK4",
                    Operand("SomeTagNamedZero", null), 11),
                Assignment("db.cm.Drv.M1.ILK5", Operand("1", null), 12),
                Assignment("#AlwaysOff", Operand("glb.Zero", null), 13),
                Assignment("db.cm.Vlv.V1.CR1", And(
                    Operand("OpenRequest", "db.OpenRequest"),
                    Operand("#AlwaysOff", null)), 14),
                Assignment("Seq_Header.ILK", Operand("glb.One", null), 15)
            };
            EngineeringSnapshot snapshot = Snapshot(assignments);
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation =
                Implementation(discovery);
            var analyzer = new ControlModuleBehaviourAnalyzer(
                catalogue, DefaultBehaviourConditionSemantics.Instance);
            return new Scenario(snapshot, discovery, implementation,
                analyzer.Analyze(snapshot, discovery, implementation));
        }

        private static ExtractedLogicAssignment Assignment(
            string destination, ExtractedBooleanExpression expression, int order)
        {
            return new ExtractedLogicAssignment(
                destination, destination.StartsWith("#",
                    StringComparison.Ordinal) ? null : destination,
                expression, expression.DisplayText, "FC_Constants", 200,
                "Function", "LAD", order, "Constants", null, order,
                ExtractedLogicResolutionStatus.Complete);
        }

        private static ExtractedBooleanExpression Operand(
            string text, string path)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Operand,
                text, path, null, null);
        }

        private static ExtractedBooleanExpression Constant(bool value)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Constant,
                value ? "TRUE" : "FALSE", null, value, null);
        }

        private static ExtractedBooleanExpression Not(
            ExtractedBooleanExpression child)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Not,
                "NOT (" + child.DisplayText + ")", null, null,
                new[] { child });
        }

        private static ExtractedBooleanExpression And(
            params ExtractedBooleanExpression[] children)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.And,
                string.Join(" AND ", children.Select(item => item.DisplayText)),
                null, null, children);
        }

        private static ExtractedBooleanExpression Or(
            params ExtractedBooleanExpression[] children)
        {
            return new ExtractedBooleanExpression(
                ExtractedBooleanExpressionKind.Or,
                string.Join(" OR ", children.Select(item => item.DisplayText)),
                null, null, children);
        }

        private static EngineeringSnapshot Snapshot(
            IReadOnlyList<ExtractedLogicAssignment> assignments)
        {
            var inventory = new PlcInventory(
                "PLC", new ProgramBlockInfo[0], new PlcTagTableInfo[0],
                new PlcDataTypeInfo[0], new InventoryDiagnostic[0],
                new DataBlockStructureInfo[0], true,
                new BlockCallInfo[0], true, assignments, true);
            return new EngineeringSnapshot(
                SnapshotSchema.CurrentVersion, ProductVersion.Current,
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
                new ProjectSnapshot("Constants", "Constants.ap15_1", null,
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
                }, new ModuleDiscoveryDiagnostic[0], true);
        }

        private static ControlModuleInfo Module(
            string name, string family, string path, string type)
        {
            int dot = path.LastIndexOf('.');
            return new ControlModuleInfo(
                name, family, path.Substring(0, dot), null, path, type,
                name, false, null, ControlModuleDiscoveryStatus.Confirmed);
        }

        private static ControlModuleImplementationResult Implementation(
            ControlModuleDiscoveryResult discovery)
        {
            var modules = new List<ControlModuleImplementation>();
            foreach (ControlModuleInfo module in discovery.Modules)
                modules.Add(new ControlModuleImplementation(
                    module, ControlModuleImplementationStatus.Correlated,
                    new[]
                    {
                        new ControlModuleCallSite(
                            module.ModuleFamily == "Drive"
                                ? "cm.DrvType1"
                                : "cm.VlvType0",
                            module.ModuleFamily == "Drive" ? 501 : 601,
                            module.ModuleFamily == "Drive"
                                ? "DrvType1"
                                : "VlvType0",
                            "System", 100, "Function", 1, "Module", 1,
                            "Module", module.MemberPath)
                    }));
            return new ControlModuleImplementationResult(
                modules, new ControlModuleImplementationDiagnostic[0],
                true, true);
        }

        private sealed class Scenario
        {
            public Scenario(
                EngineeringSnapshot snapshot,
                ControlModuleDiscoveryResult discovery,
                ControlModuleImplementationResult implementation,
                ControlModuleBehaviourResult result)
            {
                Snapshot = snapshot;
                Discovery = discovery;
                Implementation = implementation;
                Result = result;
            }
            public EngineeringSnapshot Snapshot { get; }
            public ControlModuleDiscoveryResult Discovery { get; }
            public ControlModuleImplementationResult Implementation { get; }
            public ControlModuleBehaviourResult Result { get; }
        }
    }
}
