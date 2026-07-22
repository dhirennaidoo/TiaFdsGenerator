using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TiaFds.Core;
using TiaFds.Openness;

namespace TiaFds.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string input;
                string retrieveTo;
                string plcName;
                bool verbose;
                bool inventory;
                ParseArguments(
                    args,
                    out input,
                    out retrieveTo,
                    out plcName,
                    out verbose,
                    out inventory);
                TiaOpennessRuntime.Initialize();
                return Run(input, retrieveTo, plcName, verbose, inventory);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(
            string input,
            string retrieveTo,
            string plcName,
            bool verbose,
            bool printInventory)
        {
            TiaProjectResult result = new TiaProjectReader().Read(input, retrieveTo, plcName);
            TiaProjectSummary summary = result.Summary;
            Console.WriteLine("Project name: {0}", summary.Name);
            Console.WriteLine("Project path: {0}", summary.Path);
            Console.WriteLine("Top-level devices:");
            foreach (string deviceName in summary.DeviceNames)
            {
                Console.WriteLine("- {0}", deviceName);
            }

            PrintPlcs(summary.Plcs);

            if (!string.IsNullOrWhiteSpace(plcName))
            {
                PlcInfo selectedPlc = PlcSelection.FindByName(summary.Plcs, plcName);
                if (selectedPlc == null)
                {
                    Console.Error.WriteLine(
                        "Error: PLC '{0}' was not found. Discovered PLCs: {1}",
                        plcName,
                        GetDiscoveredPlcNames(summary.Plcs));
                    return 2;
                }

                Console.WriteLine("Selected PLC: {0}", selectedPlc.Name);
            }

            if (verbose)
            {
                PrintHardwareHierarchy(summary.HardwareDevices);
            }

            if (result.SelectedPlcInventory != null)
            {
                PrintInventorySummary(result.SelectedPlcInventory);
                if (printInventory)
                {
                    PrintDetailedInventory(result.SelectedPlcInventory);
                }
            }

            return 0;
        }

        private static void PrintPlcs(IReadOnlyList<PlcInfo> plcs)
        {
            Console.WriteLine("PLCs:");
            if (plcs.Count == 0)
            {
                Console.WriteLine("- None found");
                return;
            }

            foreach (PlcInfo plc in plcs)
            {
                Console.WriteLine("- {0}", plc.Name);
                Console.WriteLine("  Device: {0}", plc.DeviceName);
                Console.WriteLine("  Device item: {0}", plc.DeviceItemName);
            }
        }

        private static void PrintInventorySummary(PlcInventory inventory)
        {
            Console.WriteLine("PLC inventory:");
            Console.WriteLine("  Program blocks: {0}", inventory.ProgramBlocks.Count);
            foreach (ProgramBlockCategoryCount category in inventory.ProgramBlockCategories)
            {
                Console.WriteLine(
                    "    {0}: {1}",
                    GetBlockCategoryLabel(category.BlockType),
                    category.Count);
            }

            Console.WriteLine("  Tag tables: {0}", inventory.TagTables.Count);
            Console.WriteLine("  PLC data types: {0}", inventory.DataTypes.Count);
            Console.WriteLine("  Diagnostics: {0}", inventory.Diagnostics.Count);
        }

        private static string GetBlockCategoryLabel(string blockType)
        {
            switch (blockType)
            {
                case "OrganizationBlock":
                    return "Organization blocks";
                case "FunctionBlock":
                    return "Function blocks";
                case "Function":
                    return "Functions";
                case "GlobalDataBlock":
                    return "Global data blocks";
                case "InstanceDataBlock":
                    return "Instance data blocks";
                case "ArrayDataBlock":
                    return "Array data blocks";
                case "DataBlock":
                    return "Data blocks";
                default:
                    return "Other blocks";
            }
        }

        private static void PrintDetailedInventory(PlcInventory inventory)
        {
            Console.WriteLine("Program blocks:");
            Console.WriteLine("{0,-20} {1,6}  {2,-12} {3,-10} {4,-36} {5}",
                "Type",
                "Number",
                "Language",
                "Consistent",
                "Group",
                "Name");
            if (inventory.ProgramBlocks.Count == 0)
            {
                Console.WriteLine("- None found");
            }
            else
            {
                foreach (ProgramBlockInfo block in inventory.ProgramBlocks)
                {
                    Console.WriteLine("{0,-20} {1,6}  {2,-12} {3,-10} {4,-36} {5}",
                        block.BlockType,
                        block.Number.HasValue ? block.Number.Value.ToString() : string.Empty,
                        block.ProgrammingLanguage,
                        block.IsConsistent ? "Yes" : "No",
                        block.GroupPath,
                        block.Name);
                }
            }

            Console.WriteLine("Tag tables:");
            Console.WriteLine("{0,6}  {1,-36} {2}", "Tags", "Group", "Name");
            if (inventory.TagTables.Count == 0)
            {
                Console.WriteLine("- None found");
            }
            else
            {
                foreach (PlcTagTableInfo tagTable in inventory.TagTables)
                {
                    Console.WriteLine("{0,6}  {1,-36} {2}",
                        tagTable.TagCount,
                        tagTable.GroupPath,
                        tagTable.Name);
                }
            }

            Console.WriteLine("PLC data types:");
            Console.WriteLine("{0,-36} {1}", "Group", "Name");
            if (inventory.DataTypes.Count == 0)
            {
                Console.WriteLine("- None found");
            }
            else
            {
                foreach (PlcDataTypeInfo dataType in inventory.DataTypes)
                {
                    Console.WriteLine("{0,-36} {1}", dataType.GroupPath, dataType.Name);
                }
            }

            if (inventory.Diagnostics.Count > 0)
            {
                Console.WriteLine("Inventory diagnostics:");
                foreach (InventoryDiagnostic diagnostic in inventory.Diagnostics)
                {
                    Console.WriteLine("- {0} | {1} | {2}",
                        diagnostic.Severity,
                        diagnostic.Source,
                        diagnostic.Message);
                }
            }
        }

        private static string GetDiscoveredPlcNames(IReadOnlyList<PlcInfo> plcs)
        {
            if (plcs.Count == 0)
            {
                return "None";
            }

            var names = new List<string>();
            foreach (PlcInfo plc in plcs)
            {
                if (!names.Exists(name => string.Equals(name, plc.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Add(plc.Name);
                }
            }

            return string.Join(", ", names);
        }

        private static void PrintHardwareHierarchy(IReadOnlyList<HardwareDeviceInfo> devices)
        {
            Console.WriteLine("Hardware hierarchy:");
            foreach (HardwareDeviceInfo device in devices)
            {
                Console.WriteLine("Device: {0}", device.Name);
                foreach (HardwareItemInfo item in device.Items)
                {
                    PrintHardwareItem(item, 1);
                }
            }
        }

        private static void PrintHardwareItem(HardwareItemInfo item, int depth)
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine("{0}Item: {1}", indent, item.Name);
            if (item.Software != null)
            {
                Console.WriteLine(
                    "{0}  Software: {1} [{2}]",
                    indent,
                    item.Software.Name,
                    item.Software.TypeName);
            }

            foreach (HardwareItemInfo childItem in item.Items)
            {
                PrintHardwareItem(childItem, depth + 1);
            }
        }

        private static void ParseArguments(
            string[] args,
            out string input,
            out string retrieveTo,
            out string plcName,
            out bool verbose,
            out bool inventory)
        {
            input = null;
            retrieveTo = null;
            plcName = null;
            verbose = false;
            inventory = false;

            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--verbose" || option == "--inventory")
                {
                    if (option == "--verbose")
                    {
                        if (verbose)
                        {
                            throw new ArgumentException("--verbose may only be specified once." + Environment.NewLine + Usage());
                        }

                        verbose = true;
                    }
                    else
                    {
                        if (inventory)
                        {
                            throw new ArgumentException("--inventory may only be specified once." + Environment.NewLine + Usage());
                        }

                        inventory = true;
                    }

                    continue;
                }

                if (option != "--input" && option != "--retrieve-to" && option != "--plc")
                {
                    throw new ArgumentException("Unknown argument: " + option + Environment.NewLine + Usage());
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw new ArgumentException("Missing value for " + option + "." + Environment.NewLine + Usage());
                }

                if (option == "--input")
                {
                    if (input != null)
                    {
                        throw new ArgumentException("--input may only be specified once." + Environment.NewLine + Usage());
                    }

                    input = args[index];
                }
                else if (option == "--retrieve-to")
                {
                    if (retrieveTo != null)
                    {
                        throw new ArgumentException("--retrieve-to may only be specified once." + Environment.NewLine + Usage());
                    }

                    retrieveTo = args[index];
                }
                else
                {
                    if (plcName != null)
                    {
                        throw new ArgumentException("--plc may only be specified once." + Environment.NewLine + Usage());
                    }

                    plcName = args[index];
                }
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("--input is required." + Environment.NewLine + Usage());
            }

            if (inventory && string.IsNullOrWhiteSpace(plcName))
            {
                throw new ArgumentException("--inventory requires --plc <name>." + Environment.NewLine + Usage());
            }
        }

        private static string Usage()
        {
            return "Usage: TiaFds.Cli.exe --input <path> [--retrieve-to <folder>] " +
                   "[--plc <name>] [--inventory] [--verbose]";
        }
    }
}
