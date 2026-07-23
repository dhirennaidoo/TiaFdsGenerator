using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;
using TiaFds.Openness.Xml;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class ControlModuleCallAnalyzerTests
    {
        [DataTestMethod]
        [DataRow("cm.DrvType0", 50, "DrvType0")]
        [DataRow("cm.DrvType1", 51, "DrvType1")]
        [DataRow("cm.DrvType2", 52, "DrvType2")]
        [DataRow("cm.DrvType3", 53, "DrvType3")]
        public void Analyze_CorrelatesAllKnownDriveVariants(string function, int number, string variant)
        {
            ControlModuleImplementationResult result = Analyze(
                Snapshot(true, true, new[] { Call(function, number, "\"db.cm.Drv\".BP_M16006", "db.cm.Drv.BP_M16006", 1) }));
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, result.Modules[0].Status);
            Assert.AreEqual(variant, result.Modules[0].CallSites[0].ProcessingVariant);
            Assert.AreEqual("Bran Storage Silo 1 Activator", result.Modules[0].Description);
        }

        [DataTestMethod]
        [DataRow("cm.VlvType0", "Valve")]
        [DataRow("cm.LimType1", "DigitalInput")]
        public void Analyze_CorrelatesSupportedValveAndLimitFamilies(string function, string family)
        {
            string db = family == "Valve" ? "db.cm.Vlv" : "db.cm.DI";
            string name = family == "Valve" ? "V100" : "LS100";
            string type = family == "Valve" ? "Udt.cm.Vlv" : "Udt.cm.DI";
            ControlModuleImplementationResult result = Analyze(
                Snapshot(true, true, new[] { Call(function, null, db + "." + name, db + "." + name, 1) },
                    Module(db, name, type)));
            Assert.AreEqual(family, result.Modules[0].ModuleFamily);
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, result.Modules[0].Status);
        }

        [TestMethod]
        public void Analyze_ReportsMissingInputsUnresolvedMissingParameterAndMissingPath()
        {
            ControlModuleImplementationResult missing = Analyze(Snapshot(false, false, new BlockCallInfo[0]));
            HasDiagnostic(missing, "CM100_BLOCK_CALLS_NOT_EXTRACTED");
            HasDiagnostic(missing, "CM101_DB_STRUCTURES_NOT_EXTRACTED");

            BlockCallInfo unresolved = Call("cm.DrvType2", 52, "#Local", null, 1);
            BlockCallInfo noParameter = new BlockCallInfo("Main", 100, "Function", "Blocks/Main",
                "cm.DrvType2", 52, "Function", 2, null, 2, new CallParameterInfo[0], null);
            BlockCallInfo missingPath = Call("cm.DrvType2", 52, "db.cm.Drv.Unknown", "db.cm.Drv.Unknown", 3);
            ControlModuleImplementationResult result = Analyze(Snapshot(true, true,
                new[] { unresolved, noParameter, missingPath }));
            HasDiagnostic(result, "CM104_ACTUAL_PARAMETER_NOT_RESOLVED");
            HasDiagnostic(result, "CM102_RECOGNISED_FC_PARAMETER_NOT_FOUND");
            HasDiagnostic(result, "CM105_MODULE_PATH_NOT_FOUND");
        }

        [TestMethod]
        public void Analyze_ReportsAmbiguousFamilyAndFunctionNumberMismatch()
        {
            var parameters = new[]
            {
                Parameter("A", "InOut", "Udt.cm.Drv", "db.cm.Drv.BP_M16006", "db.cm.Drv.BP_M16006"),
                Parameter("B", "InOut", "Udt.cm.Drv", "db.cm.Drv.Other", "db.cm.Drv.Other")
            };
            var ambiguous = new BlockCallInfo("Main", 100, "Function", "Blocks/Main",
                "cm.DrvType2", 99, "Function", 1, null, 1, parameters, null);
            ControlModuleImplementationResult result = Analyze(Snapshot(true, true, new[] { ambiguous }));
            HasDiagnostic(result, "CM103_AMBIGUOUS_INOUT_PARAMETER");
            HasDiagnostic(result, "CM113_FUNCTION_NUMBER_MISMATCH");

            ControlModuleImplementationResult mismatch = Analyze(
                Snapshot(true, true,
                    new[] { Call("cm.DrvType2", 52, "db.cm.Vlv.V100", "db.cm.Vlv.V100", 1) },
                    Module("db.cm.Vlv", "V100", "Udt.cm.Vlv")));
            Assert.AreEqual(ControlModuleImplementationStatus.FamilyMismatch, mismatch.Modules[0].Status);
            HasDiagnostic(mismatch, "CM106_MODULE_FAMILY_MISMATCH");
        }

        [TestMethod]
        public void Analyze_PreservesMultipleCallsDeduplicatesExactSitesAndLeavesNestedMembersUnmatched()
        {
            BlockCallInfo first = Call("cm.DrvType0", 50, "db.cm.Drv.BP_M16006", "db.cm.Drv.BP_M16006", 1);
            BlockCallInfo second = Call("cm.DrvType2", 52, "db.cm.Drv.BP_M16006", "db.cm.Drv.BP_M16006", 2);
            BlockCallInfo duplicate = Call("cm.DrvType0", 50, "db.cm.Drv.BP_M16006", "db.cm.Drv.BP_M16006", 1);
            BlockCallInfo nested = Call("cm.DrvType2", 52,
                "db.cm.Drv.BP_M16006.Status", "db.cm.Drv.BP_M16006.Status", 3);
            ControlModuleImplementationResult result = Analyze(
                Snapshot(true, true, new[] { second, first, duplicate, nested }));
            Assert.AreEqual(ControlModuleImplementationStatus.MultipleCalls, result.Modules[0].Status);
            Assert.AreEqual(2, result.Modules[0].CallSites.Count);
            Assert.AreEqual(1, result.Modules[0].CallSites[0].CallOrdinal);
            HasDiagnostic(result, "CM112_DUPLICATE_CALL_SITE");
            HasDiagnostic(result, "CM105_MODULE_PATH_NOT_FOUND");
        }

        [TestMethod]
        public void Analyze_UnreferencedAndRenderingAndFiltersAreDeterministic()
        {
            ControlModuleImplementationResult result = Analyze(Snapshot(true, true, new BlockCallInfo[0]));
            Assert.AreEqual(ControlModuleImplementationStatus.Unreferenced, result.Modules[0].Status);
            var writer = new StringWriter();
            var renderer = new ControlModuleImplementationConsoleRenderer();
            renderer.PrintSummary(writer, result);
            renderer.PrintDetails(writer, result, new ControlModuleImplementationFilter
            {
                ModuleFamily = "Drive",
                ModuleName = "BP_M16006",
                Status = ControlModuleImplementationStatus.Unreferenced
            });
            string output = writer.ToString();
            StringAssert.Contains(output, "Advansys control-module implementation analysis");
            StringAssert.Contains(output, "Bran Storage Silo 1 Activator");
            StringAssert.Contains(output, "Unreferenced");
            StringAssert.Contains(output, "db.cm.Drv.BP_M16006");
        }

        [TestMethod]
        public void LadXmlThroughSnapshot_CorrelatesQuotedDriveOperandEndToEnd()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "TestData", "sanitized-fc501-first-call.xml");
            BlockCallParseResult parsed = new BlockCallXmlParser().Parse(
                path, "cm.Drv.System", 501, "Function", "Blocks/cm.Drv.System", "LAD",
                new System.Collections.Generic.HashSet<string>(
                    new[] { "db.cm.Drv.BP_M16001" }, StringComparer.OrdinalIgnoreCase));
            CallParameterInfo parameter = parsed.Calls[0].Parameters[0];
            Assert.AreEqual(51, parsed.Calls[0].CalledBlockNumber);
            Assert.AreEqual("\"db.cm.Drv\".BP_M16001", parameter.ActualExpression);
            Assert.AreEqual("db.cm.Drv.BP_M16001", parameter.ResolvedMemberPath);

            ControlModuleImplementationResult result = Analyze(Snapshot(
                true, true, parsed.Calls.ToArray(),
                Module("db.cm.Drv", "BP_M16001", "Udt.cm.Drv")));
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, result.Modules[0].Status);
            Assert.AreEqual("cm.DrvType1", result.Modules[0].CallSites[0].ProcessingFunctionName);
            Assert.AreEqual("DrvType1", result.Modules[0].CallSites[0].ProcessingVariant);
            Assert.AreEqual("cm.Drv.System", result.Modules[0].CallSites[0].CallingBlockName);
        }

        private static ControlModuleImplementationResult Analyze(EngineeringSnapshot snapshot)
        {
            ControlModuleDiscoveryResult modules = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            return new ControlModuleCallAnalyzer().Analyze(snapshot, modules);
        }

        private static EngineeringSnapshot Snapshot(
            bool structuresIncluded, bool callsIncluded, BlockCallInfo[] calls,
            DataBlockMemberInfo custom = null)
        {
            DataBlockMemberInfo member = custom ?? Module("db.cm.Drv", "BP_M16006", "Udt.cm.Drv");
            var inventory = new PlcInventory("PLC", new ProgramBlockInfo[0], new PlcTagTableInfo[0],
                new PlcDataTypeInfo[0], new InventoryDiagnostic[0],
                new[] { new DataBlockStructureInfo(member.MemberPath.Substring(0, member.MemberPath.LastIndexOf('.')),
                    50, "Blocks", new[] { member }, null) }, structuresIncluded, calls, callsIncluded);
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current,
                DateTimeOffset.UtcNow, new ProjectSnapshot("P", "P.ap15_1", null,
                    new PlcInfo("PLC", "D", "CPU"), inventory));
        }

        private static DataBlockMemberInfo Module(string db, string name, string type)
        {
            return new DataBlockMemberInfo(name, db + "." + name, type,
                "Bran Storage Silo 1 Activator", 0, false, null, null);
        }

        private static BlockCallInfo Call(
            string function, int? number, string actual, string resolved, int ordinal)
        {
            return new BlockCallInfo("Main", 100, "Function", "Blocks/Main",
                function, number, "Function", 147, "Process", ordinal,
                new[] { Parameter("Module", "InOut", FunctionType(function), actual, resolved) }, null);
        }

        private static string FunctionType(string function)
        {
            if (function.IndexOf("Vlv", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.Vlv";
            if (function.IndexOf("Lim", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.DI";
            return "Udt.cm.Drv";
        }

        private static CallParameterInfo Parameter(
            string name, string direction, string type, string actual, string resolved)
        {
            return new CallParameterInfo(name, direction, type, actual, resolved);
        }

        private static void HasDiagnostic(ControlModuleImplementationResult result, string code)
        {
            Assert.IsTrue(result.Diagnostics.Any(item => item.Code == code), "Expected " + code);
        }
    }
}
