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
                ParseArguments(args, out input, out retrieveTo, out plcName, out verbose);
                TiaOpennessRuntime.Initialize();
                return Run(input, retrieveTo, plcName, verbose);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string input, string retrieveTo, string plcName, bool verbose)
        {
            TiaProjectSummary summary = new TiaProjectReader().Read(input, retrieveTo);
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
                PlcInfo selectedPlc = FindPlc(summary.Plcs, plcName);
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

        private static PlcInfo FindPlc(IReadOnlyList<PlcInfo> plcs, string name)
        {
            foreach (PlcInfo plc in plcs)
            {
                if (string.Equals(plc.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return plc;
                }
            }

            return null;
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
            out bool verbose)
        {
            input = null;
            retrieveTo = null;
            plcName = null;
            verbose = false;

            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--verbose")
                {
                    if (verbose)
                    {
                        throw new ArgumentException("--verbose may only be specified once." + Environment.NewLine + Usage());
                    }

                    verbose = true;
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
        }

        private static string Usage()
        {
            return "Usage: TiaFds.Cli.exe --input <path> [--retrieve-to <folder>] [--plc <name>] [--verbose]";
        }
    }
}
