using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TiaFds.Core;
using TiaFds.Openness;

namespace TiaFds.Extract.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            ExtractOptions options;
            try { options = ExtractOptions.Parse(args); }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 4;
            }

            try
            {
                TiaOpennessRuntime.Initialize();
                return Run(options);
            }
            catch (SnapshotFileExistsException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 3;
            }
            catch (SnapshotSerializationException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 5;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(ExtractOptions options)
        {
            TiaProjectResult result = new TiaProjectReader().Read(
                options.Input,
                options.RetrieveTo,
                options.PlcName,
                options.IncludeDataBlockStructures);
            TiaProjectSummary summary = result.Summary;
            Console.WriteLine("Project name: {0}", summary.Name);
            Console.WriteLine("Project path: {0}", summary.Path);
            Console.WriteLine("Top-level devices:");
            foreach (string deviceName in summary.DeviceNames) Console.WriteLine("- {0}", deviceName);
            PrintPlcs(summary.Plcs);

            PlcInfo selectedPlc = null;
            if (!string.IsNullOrWhiteSpace(options.PlcName))
            {
                selectedPlc = PlcSelection.FindByName(summary.Plcs, options.PlcName);
                if (selectedPlc == null)
                {
                    Console.Error.WriteLine("Error: PLC '{0}' was not found. Discovered PLCs: {1}", options.PlcName, PlcNames(summary.Plcs));
                    return 2;
                }
                Console.WriteLine("Selected PLC: {0}", selectedPlc.Name);
            }

            if (options.Verbose) PrintHardware(summary.HardwareDevices);
            if (result.SelectedPlcInventory != null)
            {
                var renderer = new PlcInventoryConsoleRenderer();
                renderer.PrintInventorySummary(Console.Out, result.SelectedPlcInventory);
                if (options.Inventory) renderer.PrintDetailedInventory(Console.Out, result.SelectedPlcInventory, false);
            }

            if (options.ExportJson != null)
            {
                EngineeringSnapshot snapshot = new EngineeringSnapshotFactory().Create(summary, selectedPlc,
                    result.SelectedPlcInventory, options.Input, options.IncludeSourcePath, DateTimeOffset.UtcNow);
                new EngineeringSnapshotJsonWriter().Write(snapshot, options.ExportJson, options.Overwrite);
                Console.WriteLine();
                Console.WriteLine("Snapshot exported:");
                Console.WriteLine(System.IO.Path.GetFullPath(options.ExportJson));
            }
            return 0;
        }

        private static void PrintPlcs(IReadOnlyList<PlcInfo> plcs)
        {
            Console.WriteLine("PLCs:");
            if (plcs.Count == 0) { Console.WriteLine("- None found"); return; }
            foreach (PlcInfo plc in plcs)
            {
                Console.WriteLine("- {0}", plc.Name);
                Console.WriteLine("  Device: {0}", plc.DeviceName);
                Console.WriteLine("  Device item: {0}", plc.DeviceItemName);
            }
        }

        private static string PlcNames(IReadOnlyList<PlcInfo> plcs)
        {
            var names = new List<string>();
            foreach (PlcInfo plc in plcs)
                if (!names.Exists(x => string.Equals(x, plc.Name, StringComparison.OrdinalIgnoreCase))) names.Add(plc.Name);
            return names.Count == 0 ? "None" : string.Join(", ", names);
        }

        private static void PrintHardware(IReadOnlyList<HardwareDeviceInfo> devices)
        {
            Console.WriteLine("Hardware hierarchy:");
            foreach (HardwareDeviceInfo device in devices)
            {
                Console.WriteLine("Device: {0}", device.Name);
                foreach (HardwareItemInfo item in device.Items) PrintItem(item, 1);
            }
        }

        private static void PrintItem(HardwareItemInfo item, int depth)
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine("{0}Item: {1}", indent, item.Name);
            if (item.Software != null) Console.WriteLine("{0}  Software: {1} [{2}]", indent, item.Software.Name, item.Software.TypeName);
            foreach (HardwareItemInfo child in item.Items) PrintItem(child, depth + 1);
        }

        private sealed class ExtractOptions
        {
            public string Input, RetrieveTo, PlcName, ExportJson;
            public bool Verbose, Inventory, Overwrite, IncludeSourcePath, IncludeDataBlockStructures;

            public static ExtractOptions Parse(string[] args)
            {
                var result = new ExtractOptions();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < args.Length; i++)
                {
                    string option = args[i];
                    if (!seen.Add(option)) throw Invalid(option + " may only be specified once.");
                    if (option == "--verbose") { result.Verbose = true; continue; }
                    if (option == "--inventory") { result.Inventory = true; continue; }
                    if (option == "--overwrite") { result.Overwrite = true; continue; }
                    if (option == "--include-source-path") { result.IncludeSourcePath = true; continue; }
                    if (option == "--include-db-structures") { result.IncludeDataBlockStructures = true; continue; }
                    if (option != "--input" && option != "--retrieve-to" && option != "--plc" && option != "--export-json")
                        throw Invalid("Unknown argument: " + option);
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) throw Invalid("Missing value for " + option + ".");
                    if (option == "--input") result.Input = args[i];
                    else if (option == "--retrieve-to") result.RetrieveTo = args[i];
                    else if (option == "--plc") result.PlcName = args[i];
                    else result.ExportJson = args[i];
                }
                if (string.IsNullOrWhiteSpace(result.Input)) throw Invalid("--input is required.");
                if ((result.Inventory || result.ExportJson != null || result.IncludeDataBlockStructures) && string.IsNullOrWhiteSpace(result.PlcName))
                    throw Invalid("--inventory, --include-db-structures, and --export-json require --plc <name>.");
                if ((result.Overwrite || result.IncludeSourcePath) && result.ExportJson == null) throw Invalid("--overwrite and --include-source-path require --export-json <path>.");
                return result;
            }

            private static ArgumentException Invalid(string text) { return new ArgumentException(text + Environment.NewLine + Usage()); }
            private static string Usage() { return "Usage: TiaFds.Extract.Cli.exe --input <path> [--retrieve-to <folder>] [--plc <name>] [--inventory] [--verbose] [--include-db-structures] [--export-json <path>] [--overwrite] [--include-source-path]"; }
        }
    }
}
