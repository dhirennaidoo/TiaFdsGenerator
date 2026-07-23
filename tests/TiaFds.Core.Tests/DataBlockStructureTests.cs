using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class DataBlockStructureTests
    {
        [TestMethod]
        public void SnapshotRoundTrip_PreservesNestedUnicodeArraysAndPaths()
        {
            var child = new DataBlockMemberInfo(
                "Running",
                "db.cm.Drv.M16006.Status.Running",
                "Bool",
                "运行中",
                2,
                false,
                null,
                null);
            var status = new DataBlockMemberInfo(
                "Status",
                "db.cm.Drv.M16006.Status",
                "Struct",
                null,
                1,
                false,
                null,
                new[] { child });
            var drive = new DataBlockMemberInfo(
                "M16006",
                "db.cm.Drv.M16006",
                "Udt.cm.Drv",
                "Dérive principale",
                0,
                false,
                null,
                new[] { status });
            var array = new DataBlockMemberInfo(
                "Drives",
                "db.cm.Drv.Drives",
                "Udt.cm.Drv",
                null,
                0,
                true,
                "1..20",
                null);
            var structure = new DataBlockStructureInfo(
                "db.cm.Drv",
                50,
                "Program blocks/Control Modules",
                new[] { drive, array },
                new[] { new InventoryDiagnostic("Warning", "db.cm.Drv", "Synthetic diagnostic") });

            EngineeringSnapshot actual = RoundTrip(Snapshot(true, structure));

            Assert.AreEqual("1.1", actual.SchemaVersion);
            Assert.AreEqual(1, actual.Project.Inventory.DataBlockStructures.Count);
            DataBlockStructureInfo block = actual.Project.Inventory.DataBlockStructures[0];
            Assert.AreEqual(2, block.Members.Count);
            Assert.AreEqual("Drives", block.Members[0].Name);
            Assert.IsTrue(block.Members[0].IsArray);
            Assert.AreEqual("1..20", block.Members[0].ArrayBounds);
            Assert.AreEqual("Dérive principale", block.Members[1].Comment);
            Assert.AreEqual("db.cm.Drv.M16006.Status.Running",
                block.Members[1].Children[0].Children[0].MemberPath);
            Assert.AreEqual("运行中", block.Members[1].Children[0].Children[0].Comment);
            Assert.AreEqual(1, block.Diagnostics.Count);
        }

        [TestMethod]
        public void Constructors_NormalizeNullCollectionsAndOptionalComments()
        {
            var member = new DataBlockMemberInfo("M1", "DB.M1", "Int", null, 0, false, null, null);
            var structure = new DataBlockStructureInfo("DB", null, null, new[] { member }, null);
            var inventory = new PlcInventory("PLC", null, null, null, null, null, true);

            Assert.IsNull(member.Comment);
            Assert.AreEqual(0, member.Children.Count);
            Assert.AreEqual(0, structure.Diagnostics.Count);
            Assert.AreEqual(0, inventory.DataBlockStructures.Count);
            Assert.IsTrue(inventory.DataBlockStructuresIncluded);
        }

        [TestMethod]
        public void Reader_ToleratesUnknownDbProperties()
        {
            string json = Write(Snapshot(true, new DataBlockStructureInfo(
                "DB", 1, "Blocks", null, null)));
            json = json.Replace("\"blockName\": \"DB\",", "\"blockName\": \"DB\", \"futureProperty\": 42,");

            EngineeringSnapshot snapshot = Read(json);

            Assert.AreEqual("DB", snapshot.Project.Inventory.DataBlockStructures[0].BlockName);
        }

        [TestMethod]
        public void Reader_AcceptsSchema10AndNormalizesMissingStructures()
        {
            const string json =
                "{\"schemaVersion\":\"1.0\",\"generatorVersion\":\"0.4.0\",\"exportedAtUtc\":\"2026-01-01T00:00:00Z\"," +
                "\"project\":{\"name\":\"P\",\"sourceFileName\":\"P.ap15_1\",\"selectedPlc\":{\"name\":\"PLC\",\"deviceName\":\"D\",\"deviceItemName\":\"CPU\"}," +
                "\"inventory\":{\"plcName\":\"PLC\",\"programBlocks\":[],\"tagTables\":[],\"dataTypes\":[],\"diagnostics\":[]}}}";

            EngineeringSnapshot snapshot = Read(json);

            Assert.AreEqual("1.0", snapshot.SchemaVersion);
            Assert.AreEqual(0, snapshot.Project.Inventory.DataBlockStructures.Count);
            Assert.IsFalse(snapshot.Project.Inventory.DataBlockStructuresIncluded);
        }

        [TestMethod]
        public void Reader_ReportsMalformedDbStructureJson()
        {
            string json = Write(Snapshot(true)).Replace(
                "\"dataBlockStructures\": []",
                "\"dataBlockStructures\": {}");

            Exception exception = Assert.ThrowsException<SnapshotSerializationException>(() => Read(json));
            StringAssert.Contains(exception.Message, "malformed");
        }

        private static EngineeringSnapshot Snapshot(bool included, params DataBlockStructureInfo[] structures)
        {
            var inventory = new PlcInventory(
                "PLC",
                new ProgramBlockInfo[0],
                new PlcTagTableInfo[0],
                new PlcDataTypeInfo[0],
                new InventoryDiagnostic[0],
                structures,
                included);
            return new EngineeringSnapshot(
                SnapshotSchema.CurrentVersion,
                ProductVersion.Current,
                new DateTimeOffset(2026, 7, 23, 20, 0, 0, TimeSpan.Zero),
                new ProjectSnapshot(
                    "Project",
                    "Project.ap15_1",
                    null,
                    new PlcInfo("PLC", "Station", "CPU"),
                    inventory));
        }

        private static EngineeringSnapshot RoundTrip(EngineeringSnapshot snapshot)
        {
            return Read(Write(snapshot));
        }

        private static string Write(EngineeringSnapshot snapshot)
        {
            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "snapshot.json");
            try
            {
                new EngineeringSnapshotJsonWriter().Write(snapshot, path, false);
                return File.ReadAllText(path, Encoding.UTF8);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static EngineeringSnapshot Read(string json)
        {
            string directory = Path.Combine(Path.GetTempPath(), "TiaFdsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "snapshot.json");
            try
            {
                File.WriteAllText(path, json, new UTF8Encoding(false));
                return new EngineeringSnapshotJsonReader().Read(path);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
