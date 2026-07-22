using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class PlcInventoryTests
    {
        [TestMethod]
        public void Build_SortsProgramBlocksDeterministically()
        {
            var builder = new PlcInventoryBuilder("PLC");
            builder.AddProgramBlock(Block("Zulu", "Function", 20, "Program blocks/Z"));
            builder.AddProgramBlock(Block("NoNumber", "Function", null, "Program blocks/A"));
            builder.AddProgramBlock(Block("Second", "FunctionBlock", 2, "Program blocks/A"));
            builder.AddProgramBlock(Block("First", "FunctionBlock", 1, "Program blocks/A"));
            builder.AddProgramBlock(Block("NoNumberFb", "FunctionBlock", null, "Program blocks/A"));

            PlcInventory inventory = builder.Build();

            Assert.AreEqual("NoNumber", inventory.ProgramBlocks[0].Name);
            Assert.AreEqual("First", inventory.ProgramBlocks[1].Name);
            Assert.AreEqual("Second", inventory.ProgramBlocks[2].Name);
            Assert.AreEqual("NoNumberFb", inventory.ProgramBlocks[3].Name);
            Assert.AreEqual("Zulu", inventory.ProgramBlocks[4].Name);
        }

        [TestMethod]
        public void Build_CountsBlockCategories()
        {
            var builder = new PlcInventoryBuilder("PLC");
            builder.AddProgramBlock(Block("Main", "OrganizationBlock", 1, "Program blocks"));
            builder.AddProgramBlock(Block("Motor", "FunctionBlock", 10, "Program blocks"));
            builder.AddProgramBlock(Block("Valve", "FunctionBlock", 11, "Program blocks"));

            PlcInventory inventory = builder.Build();

            Assert.AreEqual(2, inventory.ProgramBlockCategories.Count);
            Assert.AreEqual("FunctionBlock", inventory.ProgramBlockCategories[0].BlockType);
            Assert.AreEqual(2, inventory.ProgramBlockCategories[0].Count);
            Assert.AreEqual("OrganizationBlock", inventory.ProgramBlockCategories[1].BlockType);
            Assert.AreEqual(1, inventory.ProgramBlockCategories[1].Count);
        }

        [TestMethod]
        public void Build_RemovesDuplicateEntriesAndAddsDiagnostics()
        {
            var builder = new PlcInventoryBuilder("PLC");
            var block = Block("Main", "OrganizationBlock", 1, "Program blocks");
            var tagTable = new PlcTagTableInfo("Inputs", "PLC tag tables", 10);
            var dataType = new PlcDataTypeInfo("MotorType", "PLC data types");

            Assert.IsTrue(builder.AddProgramBlock(block));
            Assert.IsFalse(builder.AddProgramBlock(block));
            Assert.IsTrue(builder.AddTagTable(tagTable));
            Assert.IsFalse(builder.AddTagTable(tagTable));
            Assert.IsTrue(builder.AddDataType(dataType));
            Assert.IsFalse(builder.AddDataType(dataType));

            PlcInventory inventory = builder.Build();
            Assert.AreEqual(1, inventory.ProgramBlocks.Count);
            Assert.AreEqual(1, inventory.TagTables.Count);
            Assert.AreEqual(1, inventory.DataTypes.Count);
            Assert.AreEqual(3, inventory.Diagnostics.Count);
        }

        [TestMethod]
        public void BuildGroupPath_NormalizesNestedSegments()
        {
            string path = PlcInventoryBuilder.BuildGroupPath(
                "Program blocks/Control Modules",
                " Motors ",
                "Speed\\Loops");

            Assert.AreEqual("Program blocks/Control Modules/Motors/Speed/Loops", path);
        }

        [TestMethod]
        public void FindByName_MatchesCaseInsensitively()
        {
            var plcs = new[]
            {
                new PlcInfo("BP_PLC", "Station", "CPU")
            };

            PlcInfo selected = PlcSelection.FindByName(plcs, "bp_plc");

            Assert.IsNotNull(selected);
            Assert.AreEqual("BP_PLC", selected.Name);
            Assert.IsNull(PlcSelection.FindByName(plcs, "missing"));
        }

        [TestMethod]
        public void Build_ProducesEmptyInventory()
        {
            PlcInventory inventory = new PlcInventoryBuilder("EmptyPLC").Build();

            Assert.AreEqual("EmptyPLC", inventory.PlcName);
            Assert.AreEqual(0, inventory.ProgramBlocks.Count);
            Assert.AreEqual(0, inventory.ProgramBlockCategories.Count);
            Assert.AreEqual(0, inventory.TagTables.Count);
            Assert.AreEqual(0, inventory.DataTypes.Count);
            Assert.AreEqual(0, inventory.Diagnostics.Count);
        }

        private static ProgramBlockInfo Block(
            string name,
            string blockType,
            int? number,
            string groupPath)
        {
            return new ProgramBlockInfo(
                name,
                blockType,
                number,
                "LAD",
                groupPath,
                true);
        }
    }
}
