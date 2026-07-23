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

        [DataTestMethod]
        [DataRow("cm.AI", "AnalogueInput", "db.cm.AI", "BP_JT16014", "Udt.cm.AI", "AI", "Module")]
        [DataRow("cm.AO", "AnalogueOutput", "db.cm.AO", "BP_OP16008", "Udt.cm.AO", "AO", "AOut")]
        [DataRow("cm.DOType0", "DigitalOutput", "db.cm.DO", "BP_WE16134", "Udt.cm.DO", "DOType0", "Ctrl")]
        [DataRow("cm.DOType1", "DigitalOutput", "db.cm.DO", "BP_WE16134", "Udt.cm.DO", "DOType1", "Ctrl")]
        [DataRow("cm.SpdType0", "Speed", "db.cm.Spd", "BP_SS16008", "Udt.cm.Spd", "SpdType0", "Module")]
        [DataRow("cm.SpdType1", "Speed", "db.cm.Spd", "BP_SS16008", "Udt.cm.Spd", "SpdType1", "Module")]
        public void Analyze_CorrelatesNewFamiliesByDatatypeAndResolvedPath(
            string function, string family, string db, string name,
            string dataType, string variant, string formalName)
        {
            string path = db + "." + name;
            BlockCallInfo call = FamilyCall(function, formalName, dataType, path, 1, "Unrelated title");
            ControlModuleImplementationResult result = Analyze(
                Snapshot(true, true, new[] { call }, Module(db, name, dataType)));
            Assert.AreEqual(family, result.Modules[0].ModuleFamily);
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, result.Modules[0].Status);
            Assert.AreEqual(variant, result.Modules[0].CallSites[0].ProcessingVariant);
            Assert.AreEqual(path, result.Modules[0].MemberPath);
        }

        [TestMethod]
        public void Analyze_NewFamilyMismatchAndMissingOperandNeverInferFromNetworkTitle()
        {
            BlockCallInfo mismatch = FamilyCall(
                "cm.DOType1", "Ctrl", "Udt.cm.DO",
                "db.cm.Drv.BP_M16006", 1, "BP_M16006");
            ControlModuleImplementationResult mismatchResult = Analyze(
                Snapshot(true, true, new[] { mismatch },
                    Module("db.cm.Drv", "BP_M16006", "Udt.cm.Drv")));
            Assert.AreEqual(ControlModuleImplementationStatus.FamilyMismatch,
                mismatchResult.Modules[0].Status);
            HasDiagnostic(mismatchResult, "CM106_MODULE_FAMILY_MISMATCH");

            var unresolved = new BlockCallInfo("Main", 100, "Function", "Blocks/Main",
                "cm.AI", null, "Function", null, "BP_JT16014", 1,
                new[] { Parameter("Module", "InOut", "Udt.cm.AI", null, null) }, null);
            ControlModuleImplementationResult unresolvedResult = Analyze(
                Snapshot(true, true, new[] { unresolved },
                    Module("db.cm.AI", "BP_JT16014", "Udt.cm.AI")));
            Assert.AreEqual(ControlModuleImplementationStatus.Unreferenced,
                unresolvedResult.Modules[0].Status);
            Assert.AreEqual(0, unresolvedResult.Modules[0].CallSites.Count);
            HasDiagnostic(unresolvedResult, "CM104_ACTUAL_PARAMETER_NOT_RESOLVED");
        }

        [TestMethod]
        public void Analyze_SevenFamilyIntegrationPreservesBpM6019AndSpareAsUnreferenced()
        {
            var members = new[]
            {
                Module("db.cm.AI", "BP_JT16014", "Udt.cm.AI"),
                Module("db.cm.AO", "BP_OP16008", "Udt.cm.AO"),
                Module("db.cm.DO", "BP_WE16134", "Udt.cm.DO"),
                Module("db.cm.DI", "BP_FS16008a", "Udt.cm.DI"),
                Module("db.cm.Drv", "BP_M16001", "Udt.cm.Drv"),
                Module("db.cm.Drv", "BP_M6019", "Udt.cm.Drv"),
                Module("db.cm.Spd", "BP_SS16008", "Udt.cm.Spd"),
                Module("db.cm.Vlv", "BP_V16101", "Udt.cm.Vlv"),
                Module("db.cm.Vlv", "Spare041", "Udt.cm.Vlv")
            };
            var calls = new[]
            {
                FamilyCall("cm.AI", "Module", "Udt.cm.AI", "db.cm.AI.BP_JT16014", 1, null),
                FamilyCall("cm.AO", "AOut", "Udt.cm.AO", "db.cm.AO.BP_OP16008", 2, null),
                FamilyCall("cm.DOType1", "Ctrl", "Udt.cm.DO", "db.cm.DO.BP_WE16134", 3, null),
                FamilyCall("cm.LimType1", "Module", "Udt.cm.DI", "db.cm.DI.BP_FS16008A", 4, null),
                FamilyCall("cm.DrvType1", "Drv", "Udt.cm.Drv", "db.cm.Drv.BP_M16001", 5, null),
                FamilyCall("cm.SpdType0", "Module", "Udt.cm.Spd", "db.cm.Spd.BP_SS16008", 6, null),
                FamilyCall("cm.VlvType1", "Module", "Udt.cm.Vlv", "db.cm.Vlv.BP_V16101", 7, null)
            };
            ControlModuleImplementationResult result = Analyze(MultiFamilySnapshot(members, calls));

            foreach (string family in new[]
                { "AnalogueInput", "AnalogueOutput", "DigitalInput", "DigitalOutput", "Drive", "Speed", "Valve" })
                Assert.IsTrue(result.Modules.Any(module =>
                    module.ModuleFamily == family &&
                    module.Status == ControlModuleImplementationStatus.Correlated), family);
            Assert.AreEqual(ControlModuleImplementationStatus.Unreferenced,
                result.Modules.Single(module => module.MemberPath == "db.cm.Drv.BP_M6019").Status);
            Assert.AreEqual(ControlModuleImplementationStatus.Unreferenced,
                result.Modules.Single(module => module.MemberPath == "db.cm.Vlv.Spare041").Status);

            var output = new StringWriter();
            new ControlModuleImplementationConsoleRenderer().PrintSummary(output, result);
            string text = output.ToString();
            foreach (string variant in new[]
                { "AnalogueInput / AI", "AnalogueOutput / AO", "DigitalOutput / DOType1", "Speed / SpdType0" })
                StringAssert.Contains(text, variant);
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

        private static BlockCallInfo FamilyCall(
            string function, string formalName, string dataType,
            string resolvedPath, int ordinal, string networkTitle)
        {
            return new BlockCallInfo("Main", 100, "Function", "Blocks/Main",
                function, null, "Function", null, networkTitle, ordinal,
                new[] { Parameter(formalName, "InOut", dataType,
                    "\"" + resolvedPath.Substring(0, resolvedPath.LastIndexOf('.')) + "\"." +
                    resolvedPath.Substring(resolvedPath.LastIndexOf('.') + 1),
                    resolvedPath) }, null);
        }

        private static EngineeringSnapshot MultiFamilySnapshot(
            DataBlockMemberInfo[] members,
            BlockCallInfo[] calls)
        {
            var groups = members.GroupBy(member =>
                member.MemberPath.Substring(0, member.MemberPath.LastIndexOf('.')),
                StringComparer.OrdinalIgnoreCase);
            var structures = groups.Select(group =>
                new DataBlockStructureInfo(group.Key, null, "Blocks", group.ToArray(), null)).ToArray();
            var inventory = new PlcInventory("PLC", new ProgramBlockInfo[0],
                new PlcTagTableInfo[0], new PlcDataTypeInfo[0], new InventoryDiagnostic[0],
                structures, true, calls, true);
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current,
                DateTimeOffset.UtcNow, new ProjectSnapshot("P", "P.ap15_1", null,
                    new PlcInfo("PLC", "D", "CPU"), inventory));
        }

        private static string FunctionType(string function)
        {
            if (function.IndexOf("Vlv", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.Vlv";
            if (function.IndexOf("Lim", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.DI";
            if (string.Equals(function, "cm.AI", StringComparison.OrdinalIgnoreCase)) return "Udt.cm.AI";
            if (string.Equals(function, "cm.AO", StringComparison.OrdinalIgnoreCase)) return "Udt.cm.AO";
            if (function.IndexOf("DOType", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.DO";
            if (function.IndexOf("SpdType", StringComparison.OrdinalIgnoreCase) >= 0) return "Udt.cm.Spd";
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
