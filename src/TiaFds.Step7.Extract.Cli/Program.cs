using System;
using System.Collections.Generic;
using System.IO;
using TiaFds.Core;
using TiaFds.Step7Classic;

namespace TiaFds.Step7.Extract.Cli
{
    // Extracts an EngineeringSnapshot from a STEP 7 V5.x project by reading its files directly.
    // Needs no Siemens software; the project is opened read-only.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Step7ExtractOptions options;
            try { options = Step7ExtractOptions.Parse(args); }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 4;
            }

            try
            {
                return Run(options);
            }
            catch (Step7ProgramNotFoundException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 2;
            }
            catch (Step7ProgramSelectionException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 4;
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

        private static int Run(Step7ExtractOptions options)
        {
            Step7ClassicReadResult result = new Step7ClassicProjectReader().Read(
                options.Input, options.PlcName, options.IncludeDataBlockStructures, options.IncludeBlockCalls);
            TiaProjectSummary summary = result.Summary;
            Console.WriteLine("Project name: {0}", summary.Name);
            Console.WriteLine("Project path: {0}", summary.Path);
            Console.WriteLine("Project type: STEP 7 Classic (V5.x)");
            Console.WriteLine("Stations:");
            if (summary.DeviceNames.Count == 0) Console.WriteLine("- None found");
            foreach (string deviceName in summary.DeviceNames) Console.WriteLine("- {0}", deviceName);
            PrintPrograms(summary.Plcs);

            if (result.SelectedProgram != null)
                Console.WriteLine("Selected program: {0}", result.SelectedProgram.Path);

            if (options.Verbose) PrintHardware(summary.HardwareDevices);
            if (result.SelectedPlcInventory != null)
            {
                var renderer = new PlcInventoryConsoleRenderer();
                renderer.PrintInventorySummary(Console.Out, result.SelectedPlcInventory);
                if (options.Inventory) renderer.PrintDetailedInventory(Console.Out, result.SelectedPlcInventory, false);
            }

            if (options.ExportJson != null)
            {
                EngineeringSnapshot snapshot = new EngineeringSnapshotFactory().Create(
                    summary,
                    result.SelectedPlc,
                    result.SelectedPlcInventory,
                    options.Input,
                    options.IncludeSourcePath,
                    DateTimeOffset.UtcNow,
                    SnapshotSourceKind.Step7Classic);
                new EngineeringSnapshotJsonWriter().Write(snapshot, options.ExportJson, options.Overwrite);
                Console.WriteLine();
                Console.WriteLine("Snapshot exported:");
                Console.WriteLine(Path.GetFullPath(options.ExportJson));
            }
            return 0;
        }

        private static void PrintPrograms(IReadOnlyList<PlcInfo> programs)
        {
            Console.WriteLine("S7 programs:");
            if (programs.Count == 0) { Console.WriteLine("- None found"); return; }
            foreach (PlcInfo program in programs)
            {
                Console.WriteLine("- {0}", program.Name);
                if (program.DeviceName.Length > 0) Console.WriteLine("  Station: {0}", program.DeviceName);
                if (program.DeviceItemName.Length > 0) Console.WriteLine("  CPU: {0}", program.DeviceItemName);
            }
        }

        private static void PrintHardware(IReadOnlyList<HardwareDeviceInfo> devices)
        {
            Console.WriteLine("Hardware hierarchy:");
            foreach (HardwareDeviceInfo device in devices)
            {
                Console.WriteLine("Station: {0}", device.Name);
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
    }
}
