using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;
using TiaFds.Reporting;
using TiaFds.Step7Classic;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class Step7ClassicDriveRuleTests
    {
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
        private static readonly Step7ProgramIdentity Program =
            new Step7ProgramIdentity("S7 Program", "SIMATIC 300", "CPU 315-2 PN/DP");

        // --- FnBlk.Setup.Drv startup model --------------------------------------------------

        [TestMethod]
        public void Setup_ProvidesDriveTypeAndIlkCrDefaultsByIndex()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SetupProgram(2, true));

            Assert.AreEqual("1", Startup(inventory, "db.cm.Drv.AM_M1017.DrvType"));
            Assert.AreEqual("0", Startup(inventory, "db.cm.Drv.AM_M1020.DrvType"),
                "Pairing is by index: AM_M1020 receives the second setup entry.");
            Assert.AreEqual("TRUE", Startup(inventory, "db.cm.Drv.AM_M1017.ILK"));
            Assert.AreEqual("TRUE", Startup(inventory, "db.cm.Drv.AM_M1020.CR"));
            StartupValueInfo evidence = inventory.StartupValues.First(item => item.MemberPath == "db.cm.Drv.AM_M1017.DrvType");
            Assert.AreEqual("CLASSIC_SETUP_DRV_STARTUP", evidence.Rule);
            Assert.AreEqual("FnBlk.Setup.Drv", evidence.SourceBlockName);
            StringAssert.Contains(evidence.Evidence, "db.Setup.Drv.AM_M1017 (index 0)");

            InventoryDiagnostic mismatch = inventory.Diagnostics.Single(item => item.Code == Step7DiagnosticCodes.SetupEntryNameMismatch);
            Assert.AreEqual("db.cm.Drv.AM_M1020", mismatch.Source);
            StringAssert.Contains(mismatch.Message, "db.Setup.Drv.OldName");

            Assert.IsFalse(inventory.StartupValues.Any(item => item.MemberPath.StartsWith("db.cm.Drv.AM_M1122.")));
            Assert.AreEqual("db.cm.Drv.AM_M1122",
                inventory.Diagnostics.Single(item => item.Code == Step7DiagnosticCodes.DriveNotInitialisedBySetup).Source);
        }

        [TestMethod]
        public void Setup_OnlyClaimsIlkCrDefaultsWhenTheMaskIsPresent()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SetupProgram(2, false));

            Assert.IsNotNull(Startup(inventory, "db.cm.Drv.AM_M1017.DrvType"));
            Assert.IsNull(Startup(inventory, "db.cm.Drv.AM_M1017.ILK"));
        }

        [TestMethod]
        public void Setup_WithoutRecognisedDriveCountIsDiagnosed()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SetupProgram(null, true));

            Assert.AreEqual(0, inventory.StartupValues.Count);
            Assert.IsTrue(inventory.Diagnostics.Any(item => item.Code == Step7DiagnosticCodes.SetupRoutineNotRecognised));
        }

        [TestMethod]
        public void Snapshot_RoundTripsActualValuesAndStartupValues()
        {
            EngineeringSnapshot snapshot = SnapshotOf(new Step7InventoryMapper().MapInventory(SetupProgram(2, true)));
            string path = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new EngineeringSnapshotJsonWriter().Write(snapshot, path, false);
                EngineeringSnapshot actual = new EngineeringSnapshotJsonReader().Read(path);
                DataBlockMemberInfo driveType = actual.Project.Inventory.DataBlockStructures
                    .Single(item => item.BlockName == "db.Setup.Drv").Members
                    .Single(item => item.Name == "AM_M1017").Children.Single(item => item.Name == "DrvType");
                Assert.AreEqual("1", driveType.ActualValue);
                Assert.AreEqual(snapshot.Project.Inventory.StartupValues.Count, actual.Project.Inventory.StartupValues.Count);
                Assert.AreEqual(1, actual.Project.Inventory.StartupValues
                    .Single(item => item.MemberPath == "db.cm.Drv.AM_M1017.DrvType").IntegerValue);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // --- Classic drive rules ------------------------------------------------------------

        [TestMethod]
        public void Rules_StartupOnlyIlkIsBridgedForClassicDrives()
        {
            BehaviouralCondition ilk = Conditions(driveType: 0, cyclicCr: null)
                .Single(item => item.Kind == BehaviouralConditionKind.Interlock);

            Assert.AreEqual(BehaviourConditionEffect.PermanentlyTrue, ilk.Effect);
            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed, ilk.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.ClassicIlkRule, ilk.SemanticRule);
            Assert.AreEqual("FnBlk.Setup.Drv", ilk.BlockName);
            StringAssert.Contains(ilk.ConstantFindings.Single(item =>
                item.FindingKind == BehaviourConstantFindingKind.ConditionBridged).Message, "No cyclic logic writes ILK");
        }

        [TestMethod]
        public void Rules_CrIsNotUsedUnlessDriveTypeIsOne()
        {
            BehaviouralCondition startupCr = Conditions(driveType: 0, cyclicCr: null)
                .Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreEqual(BehaviourReviewClassification.NotUsedByProcessing, startupCr.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.ClassicCrNotUsedRule, startupCr.SemanticRule);

            BehaviouralCondition dynamicCr = Conditions(driveType: 2, cyclicCr: "M0.5")
                .Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreEqual(BehaviourConditionEffect.Dynamic, dynamicCr.Effect);
            Assert.AreEqual(BehaviourReviewClassification.NotUsedByProcessing, dynamicCr.ReviewClassification,
                "Cyclic CR logic on a DrvType 2 drive has no effect and is reported.");
            StringAssert.Contains(dynamicCr.ConstantFindings.Single(item =>
                item.FindingKind == BehaviourConstantFindingKind.ChannelNotUsedByProcessing).Message, "DrvType 2");
        }

        [TestMethod]
        public void Rules_DriveTypeOneCrFollowsTheEnableRule()
        {
            BehaviouralCondition disabled = Conditions(driveType: 1, cyclicCr: "FALSE")
                .Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreEqual(BehaviourReviewClassification.PermanentlyDisabled, disabled.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.ClassicCrDriveType1Rule, disabled.SemanticRule);

            BehaviouralCondition dynamic = Conditions(driveType: 1, cyclicCr: "M0.5")
                .Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreEqual(BehaviourReviewClassification.NormalDynamicCondition, dynamic.ReviewClassification);
        }

        [TestMethod]
        public void Rules_UnknownDriveTypeConstantCrNeedsReview()
        {
            BehaviouralCondition cr = Conditions(driveType: null, cyclicCr: "TRUE")
                .Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);

            Assert.AreEqual(BehaviourReviewClassification.RequiresManualInterpretation, cr.ReviewClassification);
            Assert.AreEqual(ControlModuleBehaviourAnalyzer.ClassicCrDriveTypeUnknownRule, cr.SemanticRule);
        }

        [TestMethod]
        public void Rules_CyclicWriteSupersedesStartupValue()
        {
            var conditions = Conditions(driveType: 1, cyclicCr: "M0.5");

            Assert.AreEqual(1, conditions.Count(item => item.Kind == BehaviouralConditionKind.ControlRequest));
            BehaviouralCondition cr = conditions.Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreEqual("Call cm Drv Feed", cr.BlockName);
            Assert.AreNotEqual(BehaviouralConditionResolutionStatus.Ambiguous, cr.ResolutionStatus);
        }

        [TestMethod]
        public void Rules_TiaDrivesKeepTheirOwnRules()
        {
            var conditions = Conditions(driveType: 0, cyclicCr: null, processingFunction: "cm.DrvType1");
            BehaviouralCondition ilk = conditions.Single(item => item.Kind == BehaviouralConditionKind.Interlock);
            Assert.AreEqual("DRV_ILK_ACTIVE_HIGH_PERMISSIVE", ilk.SemanticRule);
            BehaviouralCondition cr = conditions.Single(item => item.Kind == BehaviouralConditionKind.ControlRequest);
            Assert.AreNotEqual(BehaviourReviewClassification.NotUsedByProcessing, cr.ReviewClassification);
        }

        [TestMethod]
        public void Report_ShowsDriveTypeAndNotUsedCount()
        {
            EngineeringSnapshot snapshot = DriveSnapshot(0, null, "FnBlk.cm.Drv");
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
            AnalysisReport report = new AnalysisReportBuilder().Build(snapshot, discovery, implementation,
                new ControlModuleBehaviourAnalyzer().Analyze(snapshot, discovery, implementation));

            Assert.AreEqual(0, report.Modules.Single().ConfiguredDriveType);
            Assert.AreEqual(1, report.BehaviourSummary.NotUsedByProcessingCount);
            Assert.AreEqual(1, report.BehaviourSummary.BridgedOrBypassedCount);

            string path = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"), "report.xlsx");
            try
            {
                new AnalysisReportExcelWriter().Write(report, path);
                using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
                {
                    var modules = workbook.Worksheet("Modules");
                    Assert.AreEqual("Drive Type", modules.Cell(1, 21).GetString());
                    Assert.AreEqual(0, modules.Cell(2, 21).GetValue<int>());
                }
            }
            finally
            {
                if (File.Exists(path)) Directory.Delete(Path.GetDirectoryName(path), true);
            }
        }

        // --- Fixtures -----------------------------------------------------------------------

        private static string Startup(PlcInventory inventory, string path)
        {
            StartupValueInfo value = inventory.StartupValues.FirstOrDefault(item => item.MemberPath == path);
            return value == null ? null : value.Value;
        }

        private static BehaviouralCondition[] Conditions(int? driveType, string cyclicCr, string processingFunction = "FnBlk.cm.Drv")
        {
            EngineeringSnapshot snapshot = DriveSnapshot(driveType, cyclicCr, processingFunction);
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult implementation = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
            return new ControlModuleBehaviourAnalyzer().Analyze(snapshot, discovery, implementation).Conditions.ToArray();
        }

        // One drive AM_M1017 processed by processingFunction, with startup ILK/CR TRUE and an
        // optional DrvType, plus an optional cyclic CR assignment (operand text or TRUE/FALSE).
        private static EngineeringSnapshot DriveSnapshot(int? driveType, string cyclicCr, string processingFunction)
        {
            var drive = new DataBlockMemberInfo("AM_M1017", "db.cm.Drv.AM_M1017", "Udt.cm.Drv", "AM_M1017 Conveyor", 0, false, null, "0.0",
                new[]
                {
                    Member("db.cm.Drv.AM_M1017", "ILK", "BOOL"),
                    Member("db.cm.Drv.AM_M1017", "CR", "BOOL"),
                    Member("db.cm.Drv.AM_M1017", "DrvType", "INT")
                });
            var structure = new DataBlockStructureInfo("db.cm.Drv", 50, "Blocks", new[] { drive }, null);
            var call = new BlockCallInfo("Call cm Drv Feed", 506, "Function", "Blocks/Call cm Drv Feed", processingFunction, 50, "FC",
                3, "AM_M1017 DRIVE", 1,
                new[] { new CallParameterInfo("Drv", "InOut", "Udt.cm.Drv", "P#DB50.DBX 0.0", "db.cm.Drv.AM_M1017") }, null);

            var startup = new System.Collections.Generic.List<StartupValueInfo>
            {
                new StartupValueInfo("db.cm.Drv.AM_M1017.ILK", "TRUE", "CLASSIC_SETUP_DRV_STARTUP", "FnBlk.Setup.Drv", "test"),
                new StartupValueInfo("db.cm.Drv.AM_M1017.CR", "TRUE", "CLASSIC_SETUP_DRV_STARTUP", "FnBlk.Setup.Drv", "test")
            };
            if (driveType.HasValue)
                startup.Add(new StartupValueInfo("db.cm.Drv.AM_M1017.DrvType",
                    driveType.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "CLASSIC_SETUP_DRV_STARTUP", "FnBlk.Setup.Drv", "test"));

            var logic = new System.Collections.Generic.List<ExtractedLogicAssignment>();
            if (cyclicCr != null)
            {
                bool constant;
                ExtractedBooleanExpression source = bool.TryParse(cyclicCr, out constant)
                    ? new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Constant, cyclicCr.ToUpperInvariant(), null, constant, null)
                    : new ExtractedBooleanExpression(ExtractedBooleanExpressionKind.Operand, cyclicCr, "db.x." + cyclicCr, null, null);
                logic.Add(new ExtractedLogicAssignment("\"db.cm.Drv\".AM_M1017.CR", "db.cm.Drv.AM_M1017.CR", source,
                    "A " + cyclicCr, "Call cm Drv Feed", 506, "Function", "LAD", 2, "AM_M1017 CONTROL RELAY", null, 1,
                    ExtractedLogicResolutionStatus.Complete, ExtractedLogicOperation.Assign));
            }

            var inventory = new PlcInventory("S7 Program", null, null, null, null, new[] { structure }, true,
                new[] { call }, true, logic, true, startup);
            return SnapshotOf(inventory);
        }

        private static DataBlockMemberInfo Member(string parent, string name, string type)
        {
            return new DataBlockMemberInfo(name, parent + "." + name, type, null, 1, false, null, null, null);
        }

        private static EngineeringSnapshot SnapshotOf(PlcInventory inventory)
        {
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(Program), inventory));
        }

        // FnBlk.Setup.Drv (FC51) called from Global Setup (FC20); db.cm.Drv holds three drives,
        // db.Setup.Drv three entries whose second is named differently. driveCount null leaves
        // out the "L n; T #NoDrives" pair.
        private static Step7ProgramModel SetupProgram(int? driveCount, bool setsDefaults)
        {
            var statements = new System.Collections.Generic.List<Step7StatementModel>();
            if (driveCount.HasValue)
            {
                statements.Add(S("L", driveCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                statements.Add(S("T", "#NoDrives"));
            }
            statements.Add(S("OPN", "DB50"));
            if (setsDefaults)
            {
                statements.Add(S("L", "DBB[#DBD_IndexReg]"));
                statements.Add(S("L", "W#16#50"));
                statements.Add(S("OW", ""));
                statements.Add(S("T", "DBB[#DBD_IndexReg]"));
            }
            var setup = new Step7CodeBlockModel(Step7BlockKind.Function, 51,
                new[] { new Step7InterfaceParameterModel("TEMP", "NoDrives", "INT", null, null, 28, 0) },
                new[] { new Step7NetworkModel(1, "Copy Out of Setup DB into Drive DB", null, statements) }, null);
            var globalSetup = new Step7CodeBlockModel(Step7BlockKind.Function, 20, new Step7InterfaceParameterModel[0],
                new[] { new Step7NetworkModel(5, "Setup All Drives", null, new[]
                {
                    new Step7StatementModel(null, "CALL", "FC51", null, "FC51", null, null)
                }) }, null);

            var symbols = new[]
            {
                new Step7SymbolModel("FnBlk.Setup.Drv", "FC 51", "FC 51", ""),
                new Step7SymbolModel("Global Setup", "FC 20", "FC 20", ""),
                new Step7SymbolModel("db.cm.Drv", "DB 50", "DB 50", ""),
                new Step7SymbolModel("db.Setup.Drv", "DB 51", "DB 51", ""),
                new Step7SymbolModel("Udt.cm.Drv", "UDT 50", "UDT 50", ""),
                new Step7SymbolModel("Udt.Setup.Drv", "UDT 51", "UDT 51", "")
            };
            var blocks = new[]
            {
                new Step7BlockModel(Step7BlockKind.Function, 51, null, "AWL", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 20, null, "AWL", false, false)
            };
            var dataBlocks = new[]
            {
                new Step7DataBlockModel(50, new[]
                {
                    Drive("AM_M1017", 0), Drive("AM_M1020", 50), Drive("AM_M1122", 100)
                }, null),
                new Step7DataBlockModel(51, new[]
                {
                    SetupEntry("AM_M1017", 0, "1"), SetupEntry("OldName", 20, "0"), SetupEntry("Spare109", 40, "3")
                }, null)
            };
            return new Step7ProgramModel(Program, symbols, blocks, dataBlocks, true, new[] { setup, globalSetup }, true, null);
        }

        private static Step7DataRowModel Drive(string name, int offset)
        {
            return new Step7DataRowModel(name, "UDT", 50, name, offset, 0, false, null, null, new[]
            {
                Row("ILK", "BOOL", offset, 4, null),
                Row("CR", "BOOL", offset, 6, null),
                Row("DrvType", "INT", offset + 6, 0, "0")
            });
        }

        private static Step7DataRowModel SetupEntry(string name, int offset, string driveType)
        {
            return new Step7DataRowModel(name, "UDT", 51, name, offset, 0, false, null, null, new[]
            {
                Row("DrvType", "INT", offset, 0, driveType)
            });
        }

        private static Step7DataRowModel Row(string name, string type, int byteAddress, int bit, string actual)
        {
            return new Step7DataRowModel(name, type, null, null, byteAddress, bit, false, null, actual, null);
        }

        private static Step7StatementModel S(string command, string operand)
        {
            return new Step7StatementModel(null, command, operand, null, null, null, null);
        }
    }
}
