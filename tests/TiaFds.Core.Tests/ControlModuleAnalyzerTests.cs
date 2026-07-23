using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class ControlModuleAnalyzerTests
    {
        [TestMethod]
        public void Analyze_ClassifiesAllCatalogueFamiliesFromDatatype()
        {
            EngineeringSnapshot snapshot = Snapshot(true,
                Structure("db.cm.Drv", 150, Member("D1", "Udt.cm.Drv", "db.cm.Drv.D1")),
                Structure("db.cm.Vlv", 160, Member("V1", "Udt.cm.Vlv", "db.cm.Vlv.V1")),
                Structure("db.cm.Spd", 180, Member("S1", "Udt.cm.Spd", "db.cm.Spd.S1")),
                Structure("db.cm.DI", 190, Member("DI1", "Udt.cm.DI", "db.cm.DI.DI1")),
                Structure("db.cm.AI", 195, Member("AI1", "Udt.cm.AI", "db.cm.AI.AI1")),
                Structure("db.cm.AO", 196, Member("AO1", "Udt.cm.AO", "db.cm.AO.AO1")),
                Structure("db.cm.DO", 197, Member("DO1", "Udt.cm.DO", "db.cm.DO.DO1")));

            ControlModuleDiscoveryResult result = Analyze(snapshot);

            CollectionAssert.AreEquivalent(
                new[] { "Drive", "Valve", "Speed", "DigitalInput", "AnalogueInput", "AnalogueOutput", "DigitalOutput" },
                result.Modules.Select(module => module.ModuleFamily).ToArray());
            Assert.IsTrue(result.Modules.All(module => module.Status == ControlModuleDiscoveryStatus.Confirmed));
            Assert.AreEqual(150, result.Modules.Single(module => module.ModuleFamily == "Drive").ContainerDbNumber);
        }

        [TestMethod]
        public void Analyze_ClassifiesByDatatypeInUnexpectedContainerAndDoesNotRequireDbNumber()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("SomeOtherDb", null, Member("MotorA", "udt.cm.drv", "SomeOtherDb.MotorA"))));

            Assert.AreEqual(1, result.Modules.Count);
            Assert.AreEqual("Drive", result.Modules[0].ModuleFamily);
            Assert.AreEqual(ControlModuleDiscoveryStatus.UnexpectedContainer, result.Modules[0].Status);
            Assert.IsNull(result.Modules[0].ContainerDbNumber);
            HasDiagnostic(result, "CM004_MODULE_IN_UNEXPECTED_CONTAINER");
        }

        [TestMethod]
        public void Analyze_RecognizesEmptyExpectedContainerAndUnrecognisedMember()
        {
            ControlModuleDiscoveryResult empty = Analyze(Snapshot(true, Structure("db.cm.Drv", 50)));
            Assert.IsTrue(empty.Containers.Any(container => container.ModuleFamily == "Drive"));
            Assert.AreEqual(0, empty.Modules.Count);

            ControlModuleDiscoveryResult unknown = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50, Member("M16006", "Int", "db.cm.Drv.M16006"))));
            Assert.AreEqual(0, unknown.Modules.Count);
            HasDiagnostic(unknown, "CM003_UNRECOGNISED_MEMBER_TYPE");
        }

        [TestMethod]
        public void Analyze_DoesNotClassifyByMemberNameAndReportsMissingExpectedContainers()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("Other", 999, Member("Drv", "Bool", "Other.Drv"))));

            Assert.AreEqual(0, result.Modules.Count);
            HasDiagnostic(result, "CM002_EXPECTED_CONTAINER_NOT_FOUND");
        }

        [TestMethod]
        public void Analyze_DoesNotReturnNestedFieldsInsideKnownModule()
        {
            var misleadingNestedField = Member("NestedValve", "Udt.cm.Vlv", "db.cm.Drv.M1.NestedValve");
            var drive = new DataBlockMemberInfo(
                "M1", "db.cm.Drv.M1", "Udt.cm.Drv", null, 0, false, null,
                new[] { misleadingNestedField });

            ControlModuleDiscoveryResult result = Analyze(Snapshot(true, Structure("db.cm.Drv", 50, drive)));

            Assert.AreEqual(1, result.Modules.Count);
            Assert.AreEqual("Drive", result.Modules[0].ModuleFamily);
        }

        [TestMethod]
        public void Analyze_FindsModuleNestedInStructure()
        {
            var nestedDrive = new DataBlockMemberInfo(
                "Drives", "Other.Area1.Drives", "Udt.cm.Drv", null, 1, true, "0..15", null);
            var area = new DataBlockMemberInfo(
                "Area1", "Other.Area1", "Struct", null, 0, false, null, new[] { nestedDrive });

            ControlModuleDiscoveryResult result = Analyze(Snapshot(true, Structure("Other", 701, area)));

            Assert.AreEqual(1, result.Modules.Count);
            Assert.AreEqual("Other.Area1.Drives", result.Modules[0].MemberPath);
            Assert.IsTrue(result.Modules[0].IsArray);
            HasDiagnostic(result, "CM006_ARRAY_NOT_EXPANDED");
        }

        [TestMethod]
        public void Analyze_IgnoresDuplicateMemberPathAndOrdersModulesDeterministically()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50,
                    Member("Z", "Udt.cm.Drv", "db.cm.Drv.Z"),
                    Member("A", "Udt.cm.Drv", "db.cm.Drv.A"),
                    Member("Duplicate", "Udt.cm.Drv", "db.cm.Drv.A"))));

            Assert.AreEqual(2, result.Modules.Count);
            Assert.AreEqual("db.cm.Drv.A", result.Modules[0].MemberPath);
            Assert.AreEqual("db.cm.Drv.Z", result.Modules[1].MemberPath);
            HasDiagnostic(result, "CM005_DUPLICATE_MEMBER_PATH");
        }

        [TestMethod]
        public void Analyze_FiltersModulesByFamily()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50, Member("D", "Udt.cm.Drv", "db.cm.Drv.D")),
                Structure("db.cm.Vlv", 60, Member("V", "Udt.cm.Vlv", "db.cm.Vlv.V"))));

            Assert.AreEqual(1, result.GetModules("drive").Count);
            Assert.AreEqual("Drive", result.GetModules("drive")[0].ModuleFamily);
            Assert.AreEqual(2, result.GetModules(null).Count);
        }

        [TestMethod]
        public void Analyze_MissingStructuresReturnsClearDiagnostic()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(false));

            Assert.IsFalse(result.DataBlockStructuresAvailable);
            HasDiagnostic(result, "CM001_DB_STRUCTURES_NOT_EXTRACTED");
        }

        [TestMethod]
        public void Analyze_ConvertsDbExtractionDiagnostics()
        {
            var structure = new DataBlockStructureInfo(
                "BrokenDb", 12, "Blocks", null,
                new[] { new InventoryDiagnostic("Error", "BrokenDb", "Export failed.") });

            ControlModuleDiscoveryResult result = Analyze(Snapshot(true, structure));

            HasDiagnostic(result, "CM007_DB_STRUCTURE_EXTRACTION_FAILED");
        }

        [TestMethod]
        public void Renderer_PrintsSummaryAndFilteredDetails()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 150, Member(
                    "BP_M16006",
                    "Udt.cm.Drv",
                    "db.cm.Drv.BP_M16006",
                    "Bran Storage Silo 1 Activator"))));
            var writer = new StringWriter();
            var renderer = new ControlModuleConsoleRenderer();

            Assert.AreEqual("Bran Storage Silo 1 Activator", result.Modules[0].Description);
            renderer.PrintSummary(writer, result);
            renderer.PrintDetails(writer, result, "Drive");
            string output = writer.ToString();

            StringAssert.Contains(output, "Advansys control-module discovery");
            StringAssert.Contains(output, "db.cm.Drv (DB150)");
            StringAssert.Contains(output, "Drive modules");
            StringAssert.Contains(output, "BP_M16006");
            StringAssert.Contains(output, "Bran Storage Silo 1 Activator");
            StringAssert.Contains(output, "Description");
            Assert.IsFalse(output.Contains("Valve modules"));
        }

        [TestMethod]
        public void Renderer_RendersEmptyAndNullDescriptions()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50,
                    Member("EmptyComment", "Udt.cm.Drv", "db.cm.Drv.EmptyComment", string.Empty),
                    Member("NullComment", "Udt.cm.Drv", "db.cm.Drv.NullComment", null))));
            var writer = new StringWriter();

            new ControlModuleConsoleRenderer().PrintDetails(writer, result, "Drive");
            string output = writer.ToString();

            StringAssert.Contains(output, "EmptyComment");
            StringAssert.Contains(output, "NullComment");
            Assert.IsFalse(output.Contains("System.String"));
        }

        [TestMethod]
        public void Renderer_WrapsLongDescriptionWithoutTruncatingIt()
        {
            string description =
                "This deliberately long module description crosses the bounded console column " +
                "without losing any characters from the original engineering comment.";
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50,
                    Member("LongComment", "Udt.cm.Drv", "db.cm.Drv.LongComment", description))));
            var writer = new StringWriter();

            new ControlModuleConsoleRenderer().PrintDetails(writer, result, "Drive");
            string output = writer.ToString();

            StringAssert.Contains(output, description.Substring(0, 60));
            StringAssert.Contains(output, description.Substring(60, 60));
            StringAssert.Contains(output, description.Substring(120));
        }

        [TestMethod]
        public void Renderer_PreservesUnicodeDescriptionFilteringCountsAndOrdering()
        {
            ControlModuleDiscoveryResult result = Analyze(Snapshot(true,
                Structure("db.cm.Drv", 50,
                    Member("Z_Module", "Udt.cm.Drv", "db.cm.Drv.Z_Module", "驱动装置 – Départ"),
                    Member("A_Module", "Udt.cm.Drv", "db.cm.Drv.A_Module", "Bran Storage")),
                Structure("db.cm.Vlv", 60,
                    Member("Valve", "Udt.cm.Vlv", "db.cm.Vlv.Valve", "Filtered valve"))));
            var writer = new StringWriter();

            Assert.AreEqual(3, result.Modules.Count);
            Assert.AreEqual("A_Module", result.GetModules("Drive")[0].Name);
            Assert.AreEqual("Z_Module", result.GetModules("Drive")[1].Name);
            new ControlModuleConsoleRenderer().PrintDetails(writer, result, "Drive");
            string output = writer.ToString();

            StringAssert.Contains(output, "驱动装置 – Départ");
            StringAssert.Contains(output, "Bran Storage");
            Assert.IsFalse(output.Contains("Filtered valve"));
            Assert.AreEqual(3, result.Modules.Count);
            Assert.AreEqual("A_Module", result.GetModules("Drive")[0].Name);
        }

        private static ControlModuleDiscoveryResult Analyze(EngineeringSnapshot snapshot)
        {
            return new ControlModuleContainerAnalyzer().Analyze(snapshot);
        }

        private static void HasDiagnostic(ControlModuleDiscoveryResult result, string code)
        {
            Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == code),
                "Expected diagnostic " + code);
        }

        private static DataBlockMemberInfo Member(string name, string type, string path)
        {
            return Member(name, type, path, null);
        }

        private static DataBlockMemberInfo Member(
            string name,
            string type,
            string path,
            string comment)
        {
            return new DataBlockMemberInfo(name, path, type, comment, 0, false, null, null);
        }

        private static DataBlockStructureInfo Structure(
            string name,
            int? number,
            params DataBlockMemberInfo[] members)
        {
            return new DataBlockStructureInfo(name, number, "Program blocks", members, null);
        }

        private static EngineeringSnapshot Snapshot(
            bool structuresIncluded,
            params DataBlockStructureInfo[] structures)
        {
            var inventory = new PlcInventory(
                "PLC",
                new ProgramBlockInfo[0],
                new PlcTagTableInfo[0],
                new[]
                {
                    new PlcDataTypeInfo("Udt.cm.Drv", "PLC data types"),
                    new PlcDataTypeInfo("Udt.cm.Vlv", "PLC data types")
                },
                new InventoryDiagnostic[0],
                structures,
                structuresIncluded);
            return new EngineeringSnapshot(
                SnapshotSchema.CurrentVersion,
                ProductVersion.Current,
                DateTimeOffset.UtcNow,
                new ProjectSnapshot(
                    "Project",
                    "Project.ap15_1",
                    null,
                    new PlcInfo("PLC", "Station", "CPU"),
                    inventory));
        }
    }
}
