using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;
using TiaFds.Step7Classic;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class Step7ClassicCallTests
    {
        private static readonly System.DateTimeOffset FixedTime =
            new System.DateTimeOffset(2026, 9, 23, 12, 0, 0, System.TimeSpan.Zero);
        private static readonly Step7ProgramIdentity Program =
            new Step7ProgramIdentity("S7 Program", "SIMATIC 300", "CPU 315-2 PN/DP");

        [TestMethod]
        public void Resolver_ChoosesMemberByExpectedDatatype()
        {
            var resolver = new Step7AddressResolver(new[] { DriveStructure() });

            Step7AddressResolution module = resolver.Resolve("P#DB50.DBX 50.0", "Udt.cm.Drv");
            Assert.IsTrue(module.IsResolved);
            Assert.AreEqual("db.cm.Drv.AM_M1020", module.MemberPath);

            Step7AddressResolution bit = resolver.Resolve("DB50.DBX 50.4", "BOOL");
            Assert.AreEqual("db.cm.Drv.AM_M1020.ILK", bit.MemberPath);

            Step7AddressResolution sameAddressBool = resolver.Resolve("DB50.DBX50.0", "BOOL");
            Assert.AreEqual("db.cm.Drv.AM_M1020.MO", sameAddressBool.MemberPath,
                "The UDT and its first BOOL share 50.0; the datatype decides.");
        }

        [TestMethod]
        public void Resolver_ReportsInsteadOfGuessing()
        {
            var resolver = new Step7AddressResolver(new[] { DriveStructure() });

            Step7AddressResolution untyped = resolver.Resolve("P#DB50.DBX 50.0", null);
            Assert.IsTrue(untyped.IsAddress);
            Assert.IsFalse(untyped.IsResolved);
            StringAssert.Contains(untyped.FailureReason, "More than one member");

            StringAssert.Contains(resolver.Resolve("P#DB50.DBX 51.0", "Udt.cm.Drv").FailureReason, "No member starts");
            StringAssert.Contains(resolver.Resolve("P#DB50.DBX 50.0", "Udt.cm.Vlv").FailureReason, "No member of type");
            StringAssert.Contains(resolver.Resolve("P#DB99.DBX 0.0", "BOOL").FailureReason, "not in an extracted");

            Assert.IsFalse(resolver.Resolve("L 0.0", "BOOL").IsAddress);
            Assert.IsFalse(resolver.Resolve("M0.1", "BOOL").IsAddress);
        }

        [TestMethod]
        public void Calls_MapCallerCalleeParametersAndResolvedModule()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(ProgramWith(
                Call("FC50", null,
                    Argument("RN", "IN", "BOOL", "L 0.0"),
                    Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0"))));

            Assert.IsTrue(inventory.BlockCallsIncluded);
            BlockCallInfo call = inventory.BlockCalls.Single();
            Assert.AreEqual("Call cm Drv Feed", call.CallingBlockName);
            Assert.AreEqual(506, call.CallingBlockNumber);
            Assert.AreEqual("Function", call.CallingBlockType);
            Assert.AreEqual("Blocks/Call cm Drv Feed", call.CallingBlockPath);
            Assert.AreEqual("FnBlk.cm.Drv", call.CalledBlockName);
            Assert.AreEqual(50, call.CalledBlockNumber);
            Assert.AreEqual("FC", call.CalledBlockType);
            Assert.AreEqual(3, call.NetworkNumber);
            Assert.AreEqual("AM_M1020 DRIVE FUNCTION BLOCK", call.NetworkTitle);
            Assert.AreEqual(1, call.CallOrdinal);

            CallParameterInfo rn = call.Parameters.Single(parameter => parameter.FormalName == "RN");
            Assert.AreEqual("Input", rn.Direction);
            Assert.AreEqual("BOOL", rn.FormalDataType);
            Assert.AreEqual("DB11.DBX 0.3", rn.ActualExpression, "Traced through '= L0.0' earlier in the network.");
            Assert.AreEqual("L 0.0", rn.RawActualExpression);
            Assert.AreEqual(CallParameterTraceStatus.Traced, rn.TraceStatus);
            Assert.IsNull(rn.ResolvedMemberPath, "DB11 is not in the fixture's DB layouts.");

            CallParameterInfo drv = call.Parameters.Single(parameter => parameter.FormalName == "Drv");
            Assert.AreEqual("InOut", drv.Direction);
            Assert.AreEqual("Udt.cm.Drv", drv.FormalDataType, "Formal type comes from the called FC interface.");
            Assert.AreEqual("\"db.cm.Drv\".AM_M1020", drv.ActualExpression, "Pointers are shown by symbol.");
            Assert.AreEqual("P#DB50.DBX 50.0", drv.RawActualExpression);
            Assert.AreEqual("db.cm.Drv.AM_M1020", drv.ResolvedMemberPath);
            Assert.IsNull(drv.TraceStatus);
            Assert.AreEqual(0, call.Diagnostics.Count);
        }

        [TestMethod]
        public void Calls_ResolveAgainstLayoutsEvenWhenStructuresAreNotEmitted()
        {
            Step7ProgramModel source = ProgramWith(Call("FC50", null, Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 0.0")));
            var program = new Step7ProgramModel(Program, source.Symbols, source.Blocks, source.DataBlocks, false,
                source.CodeBlocks, true, null);

            PlcInventory inventory = new Step7InventoryMapper().MapInventory(program);

            Assert.IsFalse(inventory.DataBlockStructuresIncluded);
            Assert.AreEqual("db.cm.Drv.AM_M1017", inventory.BlockCalls.Single().Parameters.Single().ResolvedMemberPath);
        }

        [TestMethod]
        public void Correlation_UsesClassicCatalogueAndResolvedPointers()
        {
            EngineeringSnapshot snapshot = Snapshot(ProgramWith(
                Call("FC50", null, Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 50.0"))));

            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            ControlModuleImplementationResult result = new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);

            ControlModuleImplementation called = result.Modules.Single(item => item.Declaration.Name == "AM_M1020");
            Assert.AreEqual(ControlModuleImplementationStatus.Correlated, called.Status);
            Assert.AreEqual("FnBlk.cm.Drv", called.CallSites.Single().ProcessingVariant);
            Assert.AreEqual("Call cm Drv Feed", called.CallSites.Single().CallingBlockName);
            Assert.AreEqual(ControlModuleImplementationStatus.Unreferenced,
                result.Modules.Single(item => item.Declaration.Name == "AM_M1017").Status);
            Assert.IsFalse(result.Diagnostics.Any(item => item.Code == "CM113_FUNCTION_NUMBER_MISMATCH"),
                "FnBlk.cm.Drv is FC50 but must not be checked against the TIA DrvType numbers.");
        }

        [TestMethod]
        public void Calls_UnresolvablePointerIsDiagnosedAndNotCorrelated()
        {
            EngineeringSnapshot snapshot = Snapshot(ProgramWith(
                Call("FC50", null, Argument("Drv", "IN_OUT", "UDT", "P#DB50.DBX 51.0"))));

            BlockCallInfo call = snapshot.Project.Inventory.BlockCalls.Single();
            InventoryDiagnostic pointer = call.Diagnostics.Single();
            Assert.AreEqual(Step7DiagnosticCodes.PointerNotResolved, pointer.Code);
            StringAssert.Contains(pointer.Message, "P#DB50.DBX 51.0");

            ControlModuleImplementationResult result = new ControlModuleCallAnalyzer().Analyze(
                snapshot, new ControlModuleContainerAnalyzer().Analyze(snapshot));
            Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "CM104_ACTUAL_PARAMETER_NOT_RESOLVED"));
            Assert.IsTrue(result.Modules.All(item => item.Status == ControlModuleImplementationStatus.Unreferenced));
        }

        [TestMethod]
        public void Calls_OnlyMandatoryFcInOutParametersMustBeAssigned()
        {
            PlcInventory fcInventory = new Step7InventoryMapper().MapInventory(ProgramWith(
                Call("FC50", null, Argument("Drv", "IN_OUT", "UDT", ""))));
            Assert.AreEqual("CM122_INOUT_CONNECTION_INCOMPLETE", fcInventory.BlockCalls.Single().Diagnostics.Single().Code);

            PlcInventory sfbInventory = new Step7InventoryMapper().MapInventory(ProgramWith(
                Call("SFB14", "DI5005", Argument("ADDR_2", "IN_OUT", "ANY", ""))));
            BlockCallInfo sfbCall = sfbInventory.BlockCalls.Single();
            Assert.AreEqual("SFB", sfbCall.CalledBlockType);
            Assert.IsFalse(sfbCall.Diagnostics.Any(item => item.Code == "CM122_INOUT_CONNECTION_INCOMPLETE"),
                "Unassigned FB/SFB parameters are optional (instance DB).");
        }

        [TestMethod]
        public void Calls_ReportDecodeFailuresAndMissingCalledBlocks()
        {
            Step7ProgramModel source = ProgramWith(Call("FC999", null, Argument("X", "IN", "BOOL", "M0.0")));
            var broken = new Step7CodeBlockModel(Step7BlockKind.Function, 700, null, null, "Synthetic MC7 failure");
            var program = new Step7ProgramModel(Program, source.Symbols, source.Blocks, source.DataBlocks, true,
                source.CodeBlocks.Concat(new[] { broken }).ToArray(), true, null);

            PlcInventory inventory = new Step7InventoryMapper().MapInventory(program);

            InventoryDiagnostic failure = inventory.Diagnostics.Single(item => item.Code == "CM111_BLOCK_CALL_EXTRACTION_FAILED");
            Assert.AreEqual("Blocks/FC700", failure.Source);
            StringAssert.Contains(failure.Message, "Synthetic MC7 failure");

            BlockCallInfo call = inventory.BlockCalls.Single();
            Assert.AreEqual("FC999", call.CalledBlockName);
            Assert.AreEqual(Step7DiagnosticCodes.CalledBlockNotInProject, call.Diagnostics.Single().Code);
        }

        [TestMethod]
        public void Catalogue_ClassicDriveDoesNotInheritTiaDrivePolarity()
        {
            ControlModuleFunctionDefinition classic = ControlModuleFunctionCatalogue.FindByFunctionName("FnBlk.cm.Drv");
            Assert.AreEqual("Drive", classic.ModuleFamily);
            Assert.IsNull(classic.ExpectedFunctionNumber);
            Assert.IsFalse(classic.DriveSemanticsConfirmed);
            Assert.IsTrue(ControlModuleFunctionCatalogue.FindByFunctionName("cm.DrvType1").DriveSemanticsConfirmed);
            CollectionAssert.Contains(ControlModuleFunctionCatalogue.FindByFunctionName("FnBlk.cm.DI")
                .CandidateInOutParameterNames.ToArray(), "Lim");

            BehaviourSemanticInterpretation classicIlk = DefaultBehaviourConditionSemantics.Instance.Interpret(
                "Drive", new[] { "FnBlk.cm.Drv" }, BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);
            Assert.AreEqual(BehaviourReviewClassification.RequiresManualInterpretation, classicIlk.Classification);

            BehaviourSemanticInterpretation tiaIlk = DefaultBehaviourConditionSemantics.Instance.Interpret(
                "Drive", new[] { "DrvType1" }, BehaviouralConditionKind.Interlock, "ILK",
                BehaviourConditionEffect.PermanentlyTrue, true);
            Assert.AreEqual(BehaviourReviewClassification.BridgedOrBypassed, tiaIlk.Classification);
        }

        [TestMethod]
        public void Catalogue_FlagsUnknownProcessingFunctionsOfBothGenerations()
        {
            Assert.IsTrue(ControlModuleFunctionCatalogue.LooksLikeProcessingFunction("FnBlk.cm.Drv.Test"));
            Assert.IsTrue(ControlModuleFunctionCatalogue.LooksLikeProcessingFunction("cm.DrvType9"));
            Assert.IsFalse(ControlModuleFunctionCatalogue.LooksLikeProcessingFunction("FnBlk.Setup.Drv"));
            Assert.IsFalse(ControlModuleFunctionCatalogue.LooksLikeProcessingFunction("cm.Drv.System"));
        }

        private static Step7StatementModel Call(string calledBlock, string instanceDb, params Step7CallArgumentModel[] arguments)
        {
            return new Step7StatementModel(null, "CALL", calledBlock, null, calledBlock, instanceDb, arguments);
        }

        private static Step7CallArgumentModel Argument(string name, string section, string type, string value)
        {
            return new Step7CallArgumentModel(name, section, type, value);
        }

        private static Step7ProgramModel ProgramWith(Step7StatementModel call)
        {
            var symbols = new[]
            {
                new Step7SymbolModel("FnBlk.cm.Drv", "FC 50", "FC 50", ""),
                new Step7SymbolModel("Call cm Drv Feed", "FC 506", "FC 506", ""),
                new Step7SymbolModel("db.cm.Drv", "DB 50", "DB 50", ""),
                new Step7SymbolModel("Udt.cm.Drv", "UDT 50", "UDT 50", "")
            };
            var blocks = new[]
            {
                new Step7BlockModel(Step7BlockKind.Function, 50, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 506, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.DataBlock, 50, null, "DB", false, false),
                new Step7BlockModel(Step7BlockKind.UserDataType, 50, null, "AWL", false, false)
            };
            var dataBlocks = new[]
            {
                new Step7DataBlockModel(50, new[]
                {
                    Drive("AM_M1017", "AM_M1017 A-Store Mixback Conveyor", 0),
                    Drive("AM_M1020", "AM_M1020 Industrial Vibrator", 50)
                }, null)
            };
            var fnBlk = new Step7CodeBlockModel(Step7BlockKind.Function, 50, new[]
            {
                new Step7InterfaceParameterModel("IN", "RN", "BOOL", null),
                new Step7InterfaceParameterModel("OUT", "MO", "BOOL", null),
                new Step7InterfaceParameterModel("IN_OUT", "Drv", "UDT", 50)
            }, new Step7NetworkModel[0], null);
            var sfb = new Step7CodeBlockModel(Step7BlockKind.SystemFunctionBlock, 14, new[]
            {
                new Step7InterfaceParameterModel("IN_OUT", "ADDR_2", "ANY", null)
            }, null, null);
            var caller = new Step7CodeBlockModel(Step7BlockKind.Function, 506, new Step7InterfaceParameterModel[0], new[]
            {
                new Step7NetworkModel(1, "AM_M1020 INTERLOCK", null, new Step7StatementModel[0]),
                new Step7NetworkModel(2, "AM_M1020 CONTROL RELAY", null, new Step7StatementModel[0]),
                new Step7NetworkModel(3, "AM_M1020 DRIVE FUNCTION BLOCK", null, new[]
                {
                    new Step7StatementModel(null, "A", "DB11.DBX 0.3", null, null, null, null),
                    new Step7StatementModel(null, "=", "L0.0", null, null, null, null),
                    call,
                    new Step7StatementModel(null, "NOP", "0", null, null, null, null)
                })
            }, null);
            return new Step7ProgramModel(Program, symbols, blocks, dataBlocks, true,
                new[] { fnBlk, sfb, caller }, true, null);
        }

        private static Step7DataRowModel Drive(string name, string comment, int offset)
        {
            return new Step7DataRowModel(name, "UDT", 50, comment, offset, 0, false, null, new[]
            {
                Row("MO", "BOOL", offset, 0),
                Row("ILK", "BOOL", offset, 4),
                Row("DrvType", "INT", offset + 6, 0)
            });
        }

        private static Step7DataRowModel Row(string name, string type, int byteAddress, int bit)
        {
            return new Step7DataRowModel(name, type, null, null, byteAddress, bit, false, null, null);
        }

        private static DataBlockStructureInfo DriveStructure()
        {
            return new Step7InventoryMapper().MapInventory(ProgramWith(Call("FC50", null))).DataBlockStructures.Single();
        }

        private static EngineeringSnapshot Snapshot(Step7ProgramModel program)
        {
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(program.Identity),
                    new Step7InventoryMapper().MapInventory(program)));
        }
    }
}
