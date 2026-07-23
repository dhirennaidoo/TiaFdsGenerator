using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class SnapshotTests
    {
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 7, 23, 20, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void RoundTrip_PreservesUnicodeContractAndDiagnostics()
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf("snapshot.json");
                new EngineeringSnapshotJsonWriter().Write(CreateSnapshot("项目", "控制器_Ä", true), path, false);
                EngineeringSnapshot actual = new EngineeringSnapshotJsonReader().Read(path);

                Assert.AreEqual(SnapshotSchema.CurrentVersion, actual.SchemaVersion);
                Assert.AreEqual(ProductVersion.Current, actual.GeneratorVersion);
                Assert.AreEqual(FixedTime, actual.ExportedAtUtc);
                Assert.AreEqual("项目", actual.Project.Name);
                Assert.AreEqual("控制器_Ä", actual.Project.SelectedPlc.Name);
                Assert.AreEqual("功能块_ß", actual.Project.Inventory.ProgramBlocks[0].Name);
                Assert.AreEqual("类型_日本語", actual.Project.Inventory.DataTypes[0].Name);
                Assert.AreEqual("Meldung_é", actual.Project.Inventory.Diagnostics[0].Message);
            }
        }

        [TestMethod]
        public void Writer_UsesIndentedCamelCaseAndFixedTimestamp()
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf("snapshot.json");
                new EngineeringSnapshotJsonWriter().Write(CreateSnapshot("Project", "PLC", false), path, false);
                string json = File.ReadAllText(path);
                StringAssert.Contains(json, "\n  \"schemaVersion\": \"1.0\"");
                StringAssert.Contains(json, "\"generatorVersion\": \"0.4.0\"");
                StringAssert.Contains(json, "\"exportedAtUtc\": \"2026-07-23T20:00:00+00:00\"");
                Assert.IsFalse(json.Contains("SourceFileName"));
            }
        }

        [TestMethod]
        public void Reader_RejectsUnsupportedSchema()
        {
            AssertReadFails(ValidJson().Replace("\"1.0\"", "\"2.0\""), "Unsupported snapshot schema version '2.0'");
        }

        [TestMethod]
        public void Reader_ReportsMalformedJson()
        {
            AssertReadFails("{ not json", "malformed");
        }

        [TestMethod]
        public void Reader_RequiresProjectSelectedPlcAndInventory()
        {
            AssertReadFails(ValidJson().Replace("\"project\":{", "\"projectMissing\":{"), "project object");
            AssertReadFails(ValidJson().Replace("\"selectedPlc\":{", "\"selectedPlcMissing\":{"), "selectedPlc");
            AssertReadFails(ValidJson().Replace("\"inventory\":{", "\"inventoryMissing\":{"), "inventory");
        }

        [TestMethod]
        public void Reader_ToleratesUnknownPropertiesAndNormalizesNullCollections()
        {
            string json = ValidJson()
                .Replace("\"generatorVersion\": \"0.4.0\",", "\"generatorVersion\": \"0.4.0\", \"futureRoot\": 42,")
                .Replace("\"programBlocks\": []", "\"programBlocks\": null")
                .Replace("\"tagTables\": []", "\"tagTables\": null")
                .Replace("\"dataTypes\": []", "\"dataTypes\": null")
                .Replace("\"diagnostics\": []", "\"diagnostics\": null");
            EngineeringSnapshot snapshot = ReadText(json);
            Assert.AreEqual(0, snapshot.Project.Inventory.ProgramBlocks.Count);
            Assert.AreEqual(0, snapshot.Project.Inventory.TagTables.Count);
            Assert.AreEqual(0, snapshot.Project.Inventory.DataTypes.Count);
            Assert.AreEqual(0, snapshot.Project.Inventory.Diagnostics.Count);
        }

        [TestMethod]
        public void Factory_ExtractsFilenameAndOmitsAbsolutePathByDefault()
        {
            EngineeringSnapshot snapshot = CreateSnapshot("Project", "PLC", false);
            Assert.AreEqual("Project.zap15_1", snapshot.Project.SourceFileName);
            Assert.IsNull(snapshot.Project.SourcePath);
            string json = WriteText(snapshot);
            Assert.IsFalse(json.Contains("sourcePath"));
            Assert.IsFalse(json.Contains("Client"));
        }

        [TestMethod]
        public void Factory_IncludesAbsolutePathOnlyWhenRequested()
        {
            EngineeringSnapshot snapshot = CreateSnapshot("Project", "PLC", true);
            Assert.IsTrue(System.IO.Path.IsPathRooted(snapshot.Project.SourcePath));
            StringAssert.Contains(WriteText(snapshot), "sourcePath");
        }

        [TestMethod]
        public void Writer_ProtectsAndThenOverwritesDestination()
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf("snapshot.json");
                File.WriteAllText(path, "original");
                Assert.ThrowsException<SnapshotFileExistsException>(() =>
                    new EngineeringSnapshotJsonWriter().Write(CreateSnapshot("One", "PLC", false), path, false));
                Assert.AreEqual("original", File.ReadAllText(path));
                Assert.AreEqual(0, Directory.GetFiles(files.Root, "*.tmp").Length);

                new EngineeringSnapshotJsonWriter().Write(CreateSnapshot("Two", "PLC", false), path, true);
                Assert.AreEqual("Two", new EngineeringSnapshotJsonReader().Read(path).Project.Name);
                Assert.AreEqual(0, Directory.GetFiles(files.Root, "*.tmp").Length);
                Assert.AreEqual(0, Directory.GetFiles(files.Root, "*.bak").Length);
            }
        }

        [TestMethod]
        public void Writer_CreatesParentDirectory()
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf(System.IO.Path.Combine("new", "nested", "snapshot.json"));
                new EngineeringSnapshotJsonWriter().Write(CreateSnapshot("Project", "PLC", false), path, false);
                Assert.IsTrue(File.Exists(path));
            }
        }

        [TestMethod]
        public void Builder_SortsEverySerializedCollectionDeterministically()
        {
            var builder = new PlcInventoryBuilder("PLC");
            builder.AddProgramBlock(new ProgramBlockInfo("Z", "Function", 2, "LAD", "Z", true));
            builder.AddProgramBlock(new ProgramBlockInfo("A", "Function", 1, "LAD", "A", true));
            builder.AddTagTable(new PlcTagTableInfo("Z", "Z", 1));
            builder.AddTagTable(new PlcTagTableInfo("A", "A", 1));
            builder.AddDataType(new PlcDataTypeInfo("Z", "Z"));
            builder.AddDataType(new PlcDataTypeInfo("A", "A"));
            builder.AddDiagnostic(new InventoryDiagnostic("Warning", "Z", "Z"));
            builder.AddDiagnostic(new InventoryDiagnostic("Error", "A", "A"));
            PlcInventory inventory = builder.Build();
            Assert.AreEqual("A", inventory.ProgramBlocks[0].Name);
            Assert.AreEqual("A", inventory.TagTables[0].Name);
            Assert.AreEqual("A", inventory.DataTypes[0].Name);
            Assert.AreEqual("Error", inventory.Diagnostics[0].Severity);
        }

        [TestMethod]
        public void Renderer_PrintsImportedSummaryAndDetails()
        {
            EngineeringSnapshot snapshot = ReadText(WriteText(CreateSnapshot("Project", "PLC", false)));
            var output = new StringWriter();
            var renderer = new PlcInventoryConsoleRenderer();
            renderer.PrintSummary(output, snapshot);
            renderer.PrintDetailedInventory(output, snapshot.Project.Inventory, false);
            string text = output.ToString();
            StringAssert.Contains(text, "Project: Project");
            StringAssert.Contains(text, "Selected PLC: PLC");
            StringAssert.Contains(text, "Program blocks: 1");
            StringAssert.Contains(text, "功能块_ß");
            StringAssert.Contains(text, "类型_日本語");
        }

        [TestMethod]
        public void SnapshotCliOptions_RejectsLiveAndMixedModes()
        {
            ArgumentException live = Assert.ThrowsException<ArgumentException>(() => SnapshotCliOptions.Parse(new[] { "--input", "project.ap15_1" }));
            StringAssert.Contains(live.Message, "TiaFds.Extract.Cli");
            Assert.ThrowsException<ArgumentException>(() => SnapshotCliOptions.Parse(new[] { "--import-json", "x.json", "--retrieve-to", "folder" }));
            SnapshotCliOptions valid = SnapshotCliOptions.Parse(new[] { "--import-json", "x.json", "--inventory", "--verbose" });
            Assert.IsTrue(valid.Inventory);
            Assert.IsTrue(valid.Verbose);
        }

        private static EngineeringSnapshot CreateSnapshot(string projectName, string plcName, bool includePath)
        {
            var builder = new PlcInventoryBuilder(plcName);
            builder.AddProgramBlock(new ProgramBlockInfo("功能块_ß", "FunctionBlock", 20, "SCL", "Program blocks/制御", true));
            builder.AddTagTable(new PlcTagTableInfo("标签_é", "PLC tag tables", 3));
            builder.AddDataType(new PlcDataTypeInfo("类型_日本語", "PLC data types"));
            builder.AddDiagnostic(new InventoryDiagnostic("Warning", "Source", "Meldung_é"));
            var plc = new PlcInfo(plcName, "Station", "CPU");
            var summary = new TiaProjectSummary(projectName, @"C:\Client\Project.ap15_1", new string[0], new[] { plc }, new HardwareDeviceInfo[0]);
            return new EngineeringSnapshotFactory().Create(summary, plc, builder.Build(), @"C:\Client\Project.zap15_1", includePath, FixedTime);
        }

        private static string WriteText(EngineeringSnapshot snapshot)
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf("snapshot.json");
                new EngineeringSnapshotJsonWriter().Write(snapshot, path, false);
                return File.ReadAllText(path, Encoding.UTF8);
            }
        }

        private static EngineeringSnapshot ReadText(string json)
        {
            using (var files = new TestDirectory())
            {
                string path = files.PathOf("snapshot.json");
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return new EngineeringSnapshotJsonReader().Read(path);
            }
        }

        private static void AssertReadFails(string json, string expected)
        {
            Exception exception;
            try { ReadText(json); Assert.Fail("Expected snapshot read to fail."); return; }
            catch (Exception caught) { exception = caught; }
            StringAssert.Contains(exception.Message.ToLowerInvariant(), expected.ToLowerInvariant());
        }

        private static string ValidJson()
        {
            return "{\"schemaVersion\":\"1.0\",\"generatorVersion\":\"0.4.0\",\"exportedAtUtc\":\"2026-07-23T20:00:00Z\",\"project\":{\"name\":\"P\",\"sourceFileName\":\"P.ap15_1\",\"selectedPlc\":{\"name\":\"PLC\",\"deviceName\":\"D\",\"deviceItemName\":\"CPU\"},\"inventory\":{\"plcName\":\"PLC\",\"programBlocks\":[],\"tagTables\":[],\"dataTypes\":[],\"diagnostics\":[]}}}";
        }

        private sealed class TestDirectory : IDisposable
        {
            public TestDirectory()
            {
                Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }
            public string Root { get; }
            public string PathOf(string relative) { return System.IO.Path.Combine(Root, relative); }
            public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        }
    }
}
