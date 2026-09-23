using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;
using TiaFds.Step7Classic;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class Step7ClassicTests
    {
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        private static readonly Step7ProgramIdentity Program =
            new Step7ProgramIdentity("S7 Program", "SIMATIC 300", "CPU 315-2 PN/DP");

        [TestMethod]
        public void Mapper_MapsBlocksDataTypesAndSymbolTable()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SyntheticProgram(true));

            Assert.AreEqual("S7 Program", inventory.PlcName);
            CollectionAssert.AreEquivalent(
                new[] { "OB1", "FnBlk.cm.Drv", "db.cm.Drv", "idb.Comms", "BLKMOV" },
                inventory.ProgramBlocks.Select(block => block.Name).ToArray(),
                "UDTs and variable tables are not program blocks.");

            ProgramBlockInfo fc = inventory.ProgramBlocks.Single(block => block.Number == 50 && block.BlockType == "Function");
            Assert.AreEqual("LAD", fc.ProgrammingLanguage);
            Assert.AreEqual(Step7InventoryMapper.BlocksGroupPath, fc.GroupPath);
            Assert.AreEqual("STL", inventory.ProgramBlocks.Single(block => block.Name == "OB1").ProgrammingLanguage);
            Assert.AreEqual("InstanceDataBlock", inventory.ProgramBlocks.Single(block => block.Name == "idb.Comms").BlockType);
            Assert.AreEqual("GlobalDataBlock", inventory.ProgramBlocks.Single(block => block.Name == "db.cm.Drv").BlockType);
            Assert.AreEqual("SystemFunction", inventory.ProgramBlocks.Single(block => block.Name == "BLKMOV").BlockType);

            Assert.AreEqual("Udt.cm.Drv", inventory.DataTypes.Single().Name);
            Assert.AreEqual(Step7InventoryMapper.SymbolTableName, inventory.TagTables.Single().Name);
            Assert.AreEqual(4, inventory.TagTables.Single().TagCount);
        }

        [TestMethod]
        public void Mapper_ResolvesUdtSymbolsMemberPathsAndAddresses()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SyntheticProgram(true));

            Assert.IsTrue(inventory.DataBlockStructuresIncluded);
            DataBlockStructureInfo db = inventory.DataBlockStructures.Single();
            Assert.AreEqual("db.cm.Drv", db.BlockName);
            Assert.AreEqual(50, db.BlockNumber);

            DataBlockMemberInfo second = db.Members.Single(member => member.Name == "AM_M1020");
            Assert.AreEqual("Udt.cm.Drv", second.DataTypeName);
            Assert.AreEqual("db.cm.Drv.AM_M1020", second.MemberPath);
            Assert.AreEqual("50.0", second.Address);
            Assert.AreEqual("AM_M1020 Industrial Vibrator", second.Comment);

            DataBlockMemberInfo ilk = second.Children.Single(member => member.Name == "ILK");
            Assert.AreEqual("db.cm.Drv.AM_M1020.ILK", ilk.MemberPath);
            Assert.AreEqual("BOOL", ilk.DataTypeName);
            Assert.AreEqual("50.4", ilk.Address);
            Assert.AreEqual(1, ilk.NestingLevel);
        }

        [TestMethod]
        public void Mapper_OnlyIncludesStructuresWhenRequested()
        {
            PlcInventory inventory = new Step7InventoryMapper().MapInventory(SyntheticProgram(false));

            Assert.IsFalse(inventory.DataBlockStructuresIncluded);
            Assert.AreEqual(0, inventory.DataBlockStructures.Count);
        }

        [TestMethod]
        public void Mapper_KeepsAbsoluteNameForUnnamedUdtAndWarnsOnce()
        {
            var rows = new[]
            {
                Row("A", "UDT", 77, 0, 0), Row("B", "UDT", 77, 10, 0)
            };
            var program = new Step7ProgramModel(Program, new Step7SymbolModel[0],
                new[] { new Step7BlockModel(Step7BlockKind.DataBlock, 9, null, "DB", false, false) },
                new[] { new Step7DataBlockModel(9, rows, null) }, true, null);

            PlcInventory inventory = new Step7InventoryMapper().MapInventory(program);

            DataBlockStructureInfo db = inventory.DataBlockStructures.Single();
            Assert.AreEqual("DB9", db.BlockName);
            Assert.IsTrue(db.Members.All(member => member.DataTypeName == "UDT77"));
            Assert.AreEqual(1, inventory.Diagnostics.Count(item => item.Code == Step7DiagnosticCodes.UserDataTypeSymbolNotFound));
        }

        [TestMethod]
        public void Mapper_ReportsKnowHowProtectionAndLoaderDiagnostics()
        {
            var program = new Step7ProgramModel(Program, new Step7SymbolModel[0],
                new[] { new Step7BlockModel(Step7BlockKind.FunctionBlock, 14, "GET", "AWL", false, true) },
                null, false,
                new[] { new Step7LoadDiagnostic("Warning", Step7DiagnosticCodes.SymbolTableNotFound, "S7 Program", "No symbols.") });

            PlcInventory inventory = new Step7InventoryMapper().MapInventory(program);

            InventoryDiagnostic protectedBlock = inventory.Diagnostics.Single(item => item.Code == Step7DiagnosticCodes.BlockKnowHowProtected);
            Assert.AreEqual("Blocks/GET", protectedBlock.Source);
            Assert.IsTrue(inventory.Diagnostics.Any(item => item.Code == Step7DiagnosticCodes.SymbolTableNotFound));
        }

        [TestMethod]
        public void Discovery_FindsModulesAndSurfacesDecodeFailures()
        {
            Step7ProgramModel source = SyntheticProgram(true);
            var failed = new Step7DataBlockModel(90, null, "Synthetic decode failure");
            var program = new Step7ProgramModel(Program,
                source.Symbols.Concat(new[] { new Step7SymbolModel("db.cm.DI", "DB 90", "DB 90", "") }).ToArray(),
                source.Blocks, source.DataBlocks.Concat(new[] { failed }).ToArray(), true, null);

            ControlModuleDiscoveryResult result = new ControlModuleContainerAnalyzer().Analyze(Snapshot(program));

            Assert.IsTrue(result.DataBlockStructuresAvailable);
            Assert.AreEqual(2, result.Modules.Count);
            Assert.IsTrue(result.Modules.All(module =>
                module.ModuleFamily == "Drive" && module.Status == ControlModuleDiscoveryStatus.Confirmed));
            Assert.AreEqual("AM_M1017 A-Store Mixback Conveyor",
                result.Modules.Single(module => module.Name == "AM_M1017").Description);
            ModuleDiscoveryDiagnostic decode = result.Diagnostics.Single(item => item.Code == "CM007_DB_STRUCTURE_EXTRACTION_FAILED");
            Assert.AreEqual("db.cm.DI", decode.BlockName);
            StringAssert.Contains(decode.Message, "Synthetic decode failure");
        }

        [TestMethod]
        public void Summary_MapsStationsCpusAndPrograms()
        {
            var project = new Step7ProjectModel("A-Mill", @"C:\P\A-Mill.s7p",
                new[] { new Step7StationModel("SIMATIC 300", new[] { "CPU 315-2 PN/DP" }) },
                new[] { Program, new Step7ProgramIdentity("Library Program", null, null) });

            TiaProjectSummary summary = new Step7InventoryMapper().MapSummary(project);

            CollectionAssert.AreEqual(new[] { "SIMATIC 300" }, summary.DeviceNames.ToArray());
            Assert.AreEqual(2, summary.Plcs.Count);
            Assert.AreEqual("CPU 315-2 PN/DP", summary.Plcs[0].DeviceItemName);
            Assert.AreEqual(string.Empty, summary.Plcs[1].DeviceName);
            HardwareItemInfo cpu = summary.HardwareDevices.Single().Items.Single();
            Assert.AreEqual("S7 Program", cpu.Software.Name);
            Assert.AreEqual(Step7InventoryMapper.ProgramSoftwareTypeName, cpu.Software.TypeName);
        }

        [TestMethod]
        public void Selector_MatchesPathNameOrCpuAndRejectsAmbiguity()
        {
            var second = new Step7ProgramIdentity("S7 Program", "SIMATIC 400", "CPU 416-2");
            var programs = new[] { Program, second };

            Assert.AreSame(second, Step7ProgramSelector.Select(programs, "SIMATIC 400/CPU 416-2/S7 Program"));
            Assert.AreSame(Program, Step7ProgramSelector.Select(programs, "cpu 315-2 pn/dp"));
            Assert.IsNull(Step7ProgramSelector.Select(programs, "Missing"));
            Step7ProgramSelectionException ambiguous = Assert.ThrowsException<Step7ProgramSelectionException>(
                () => Step7ProgramSelector.Select(programs, "S7 Program"));
            StringAssert.Contains(ambiguous.Message, "SIMATIC 400/CPU 416-2/S7 Program");

            Assert.AreSame(Program, Step7ProgramSelector.Select(new[] { Program }, "S7 Program"));
        }

        [TestMethod]
        public void LanguageName_UsesTiaNamesForGermanMnemonics()
        {
            Assert.AreEqual("STL", Step7InventoryMapper.LanguageName("AWL"));
            Assert.AreEqual("LAD", Step7InventoryMapper.LanguageName("KOP"));
            Assert.AreEqual("FBD", Step7InventoryMapper.LanguageName("FUP"));
            Assert.AreEqual("SCL", Step7InventoryMapper.LanguageName("SCL"));
            Assert.AreEqual("GRAPH", Step7InventoryMapper.LanguageName("GRAPH"));
            Assert.AreEqual("Unknown", Step7InventoryMapper.LanguageName(null));
        }

        [TestMethod]
        public void Options_ValidateClassicExtractionArguments()
        {
            Step7ExtractOptions options = Step7ExtractOptions.Parse(new[]
            {
                "--input", "A-Mill.s7p", "--plc", "S7 Program", "--include-db-structures",
                "--export-json", "out.json", "--overwrite"
            });
            Assert.AreEqual("A-Mill.s7p", options.Input);
            Assert.AreEqual("S7 Program", options.PlcName);
            Assert.IsTrue(options.IncludeDataBlockStructures);
            Assert.IsTrue(options.Overwrite);

            AssertInvalid("--input is required", "--plc", "P");
            AssertInvalid("require --plc", "--input", "A.s7p", "--export-json", "out.json");
            AssertInvalid("not yet supported", "--input", "A.s7p", "--plc", "P", "--include-block-calls");
            AssertInvalid("require --export-json", "--input", "A.s7p", "--overwrite");
            AssertInvalid("only be specified once", "--input", "A.s7p", "--input", "B.s7p");
            AssertInvalid("Unknown argument", "--input", "A.s7p", "--bogus");
        }

        [TestMethod]
        public void Snapshot_RoundTripsSourceKindAndMemberAddresses()
        {
            string path = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N") + ".json");
            try
            {
                new EngineeringSnapshotJsonWriter().Write(Snapshot(SyntheticProgram(true)), path, false);
                EngineeringSnapshot actual = new EngineeringSnapshotJsonReader().Read(path);

                Assert.AreEqual(SnapshotSchema.CurrentVersion, actual.SchemaVersion);
                Assert.AreEqual(SnapshotSourceKind.Step7Classic, actual.Project.SourceKind);
                DataBlockMemberInfo ilk = actual.Project.Inventory.DataBlockStructures.Single()
                    .Members.Single(member => member.Name == "AM_M1020")
                    .Children.Single(member => member.Name == "ILK");
                Assert.AreEqual("50.4", ilk.Address);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [TestMethod]
        public void Snapshot_WithoutSourceKindIsTreatedAsTiaPortal()
        {
            string path = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(path, "{\"schemaVersion\":\"1.3\",\"generatorVersion\":\"0.6.1\",\"exportedAtUtc\":\"2026-07-23T20:00:00Z\",\"project\":{\"name\":\"P\",\"sourceFileName\":\"P.ap15_1\",\"selectedPlc\":{\"name\":\"PLC\",\"deviceName\":\"D\",\"deviceItemName\":\"CPU\"},\"inventory\":{\"plcName\":\"PLC\",\"programBlocks\":[],\"tagTables\":[],\"dataTypes\":[],\"diagnostics\":[],\"dataBlockStructures\":[{\"blockName\":\"db.cm.Drv\",\"blockNumber\":50,\"groupPath\":\"\",\"members\":[{\"name\":\"M1\",\"memberPath\":\"db.cm.Drv.M1\",\"dataTypeName\":\"Udt.cm.Drv\",\"nestingLevel\":0,\"isArray\":false,\"children\":[]}],\"diagnostics\":[]}],\"dataBlockStructuresIncluded\":true,\"blockCalls\":[],\"blockCallsIncluded\":false}}}");

                EngineeringSnapshot actual = new EngineeringSnapshotJsonReader().Read(path);

                Assert.AreEqual(SnapshotSourceKind.TiaPortal, actual.Project.SourceKind);
                Assert.IsNull(actual.Project.Inventory.DataBlockStructures.Single().Members.Single().Address);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // Runs against a real project when TIAFDS_STEP7_SAMPLE_PROJECT points at an .s7p file.
        // Customer projects must never be committed, so this is inconclusive on other machines.
        [TestMethod]
        [TestCategory("LocalSample")]
        public void LocalSample_ExtractsProjectAndDiscoversDrives()
        {
            string sample = Environment.GetEnvironmentVariable("TIAFDS_STEP7_SAMPLE_PROJECT");
            if (string.IsNullOrWhiteSpace(sample) || !File.Exists(sample))
                Assert.Inconclusive("Set TIAFDS_STEP7_SAMPLE_PROJECT to a STEP 7 V5.x .s7p file to run this test.");

            Step7ClassicReadResult structure = new Step7ClassicProjectReader().Read(sample, null, false);
            Assert.IsTrue(structure.Summary.Plcs.Count > 0);

            Step7ClassicReadResult result = new Step7ClassicProjectReader().Read(
                sample, FirstProgramPath(structure), true);
            Assert.IsTrue(result.SelectedPlcInventory.DataBlockStructuresIncluded);
            EngineeringSnapshot snapshot = new EngineeringSnapshotFactory().Create(result.Summary, result.SelectedPlc,
                result.SelectedPlcInventory, sample, false, FixedTime, SnapshotSourceKind.Step7Classic);
            ControlModuleDiscoveryResult discovery = new ControlModuleContainerAnalyzer().Analyze(snapshot);
            Assert.IsTrue(discovery.Modules.Any(module => module.ModuleFamily == "Drive"));
        }

        private static string FirstProgramPath(Step7ClassicReadResult structure)
        {
            PlcInfo first = structure.Summary.Plcs[0];
            return first.DeviceItemName.Length == 0
                ? first.Name
                : first.DeviceName + "/" + first.DeviceItemName + "/" + first.Name;
        }

        private static Step7ProgramModel SyntheticProgram(bool includeDataBlocks)
        {
            var symbols = new[]
            {
                new Step7SymbolModel("FnBlk.cm.Drv", "FC 50", "FC 50", "Drive function"),
                new Step7SymbolModel("db.cm.Drv", "DB 50", "DB 50", ""),
                new Step7SymbolModel("Udt.cm.Drv", "UDT 50", "UDT 50", ""),
                new Step7SymbolModel("idb.Comms", "DB 5005", "FB 14", "")
            };
            var blocks = new[]
            {
                new Step7BlockModel(Step7BlockKind.OrganizationBlock, 1, "OB1", "AWL", false, false),
                new Step7BlockModel(Step7BlockKind.Function, 50, null, "KOP", false, false),
                new Step7BlockModel(Step7BlockKind.DataBlock, 50, null, "DB", false, false),
                new Step7BlockModel(Step7BlockKind.DataBlock, 5005, null, "DB", true, false),
                new Step7BlockModel(Step7BlockKind.UserDataType, 50, null, "AWL", false, false),
                new Step7BlockModel(Step7BlockKind.SystemFunction, 20, "BLKMOV", "AWL", false, false),
                new Step7BlockModel(Step7BlockKind.VariableTable, 1, "VAT_1", "", false, false)
            };
            var dataBlocks = includeDataBlocks
                ? new[]
                {
                    new Step7DataBlockModel(50, new[]
                    {
                        Drive("AM_M1017", "AM_M1017 A-Store Mixback Conveyor", 0),
                        Drive("AM_M1020", "AM_M1020 Industrial Vibrator", 50)
                    }, null)
                }
                : new Step7DataBlockModel[0];
            return new Step7ProgramModel(Program, symbols, blocks, dataBlocks, includeDataBlocks, null);
        }

        private static Step7DataRowModel Drive(string name, string comment, int offset)
        {
            return new Step7DataRowModel(name, "UDT", 50, comment, offset, 0, false, null, new[]
            {
                Row("MO", "BOOL", null, offset, 0),
                Row("ILK", "BOOL", null, offset, 4),
                Row("DrvType", "INT", null, offset + 6, 0)
            });
        }

        private static Step7DataRowModel Row(string name, string type, int? udt, int byteAddress, int bit)
        {
            return new Step7DataRowModel(name, type, udt, null, byteAddress, bit, false, null, null);
        }

        private static EngineeringSnapshot Snapshot(Step7ProgramModel program)
        {
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current, FixedTime,
                new ProjectSnapshot("A-Mill", "A-Mill.s7p", null, SnapshotSourceKind.Step7Classic,
                    Step7InventoryMapper.ToPlcInfo(program.Identity),
                    new Step7InventoryMapper().MapInventory(program)));
        }

        private static void AssertInvalid(string expected, params string[] args)
        {
            ArgumentException exception = Assert.ThrowsException<ArgumentException>(() => Step7ExtractOptions.Parse(args));
            StringAssert.Contains(exception.Message, expected);
        }
    }
}
