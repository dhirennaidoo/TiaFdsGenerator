using System;
using System.IO;

namespace TiaFds.Core
{
    public sealed class PlcInventoryConsoleRenderer
    {
        public void PrintSummary(TextWriter writer, EngineeringSnapshot snapshot)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            writer.WriteLine("Project: {0}", snapshot.Project.Name);
            writer.WriteLine("Selected PLC: {0}", snapshot.Project.SelectedPlc.Name);
            writer.WriteLine();
            PrintInventorySummary(writer, snapshot.Project.Inventory);
        }

        public void PrintInventorySummary(TextWriter writer, PlcInventory inventory)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));

            writer.WriteLine("PLC inventory:");
            writer.WriteLine("  Program blocks: {0}", inventory.ProgramBlocks.Count);
            foreach (ProgramBlockCategoryCount category in inventory.ProgramBlockCategories)
            {
                writer.WriteLine("    {0}: {1}", CategoryLabel(category.BlockType), category.Count);
            }
            writer.WriteLine("  Tag tables: {0}", inventory.TagTables.Count);
            writer.WriteLine("  PLC data types: {0}", inventory.DataTypes.Count);
            writer.WriteLine("  Diagnostics: {0}", inventory.Diagnostics.Count);
        }

        public void PrintDetailedInventory(TextWriter writer, PlcInventory inventory, bool verbose)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));

            writer.WriteLine("Program blocks:");
            writer.WriteLine("{0,-20} {1,6}  {2,-12} {3,-10} {4,-36} {5}", "Type", "Number", "Language", "Consistent", "Group", "Name");
            if (inventory.ProgramBlocks.Count == 0) writer.WriteLine("- None found");
            foreach (ProgramBlockInfo block in inventory.ProgramBlocks)
            {
                writer.WriteLine("{0,-20} {1,6}  {2,-12} {3,-10} {4,-36} {5}", block.BlockType,
                    block.Number.HasValue ? block.Number.Value.ToString() : string.Empty,
                    block.ProgrammingLanguage, block.IsConsistent ? "Yes" : "No", block.GroupPath, block.Name);
            }

            writer.WriteLine("Tag tables:");
            writer.WriteLine("{0,6}  {1,-36} {2}", "Tags", "Group", "Name");
            if (inventory.TagTables.Count == 0) writer.WriteLine("- None found");
            foreach (PlcTagTableInfo table in inventory.TagTables)
                writer.WriteLine("{0,6}  {1,-36} {2}", table.TagCount, table.GroupPath, table.Name);

            writer.WriteLine("PLC data types:");
            writer.WriteLine("{0,-36} {1}", "Group", "Name");
            if (inventory.DataTypes.Count == 0) writer.WriteLine("- None found");
            foreach (PlcDataTypeInfo type in inventory.DataTypes)
                writer.WriteLine("{0,-36} {1}", type.GroupPath, type.Name);

            if (inventory.Diagnostics.Count > 0)
            {
                writer.WriteLine("Inventory diagnostics:");
                foreach (InventoryDiagnostic diagnostic in inventory.Diagnostics)
                    writer.WriteLine("- {0} | {1} | {2}", diagnostic.Severity, diagnostic.Source, diagnostic.Message);
            }

            // Reserved for additional Siemens-independent detail without changing callers.
        }

        private static string CategoryLabel(string blockType)
        {
            switch (blockType)
            {
                case "OrganizationBlock": return "Organization blocks";
                case "FunctionBlock": return "Function blocks";
                case "Function": return "Functions";
                case "GlobalDataBlock": return "Global data blocks";
                case "InstanceDataBlock": return "Instance data blocks";
                case "ArrayDataBlock": return "Array data blocks";
                case "DataBlock": return "Data blocks";
                default: return "Other blocks";
            }
        }
    }
}
