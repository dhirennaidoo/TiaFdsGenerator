using System;
using TiaFds.Core;
using TiaFds.Analysis;

namespace TiaFds.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                SnapshotCliOptions options = SnapshotCliOptions.Parse(args);
                EngineeringSnapshot snapshot = new EngineeringSnapshotJsonReader().Read(options.ImportJson);
                var renderer = new PlcInventoryConsoleRenderer();
                renderer.PrintSummary(Console.Out, snapshot);
                if (options.Inventory)
                {
                    renderer.PrintDetailedInventory(Console.Out, snapshot.Project.Inventory, options.Verbose);
                }

                if (options.DiscoverModules)
                {
                    if (options.ModuleFamily != null &&
                        ControlModuleCatalogue.FindByFamily(options.ModuleFamily) == null)
                    {
                        Console.Error.WriteLine(
                            "Error: Unknown module family '{0}'. Known families: {1}",
                            options.ModuleFamily,
                            KnownFamilies());
                        return 4;
                    }

                    ControlModuleDiscoveryResult result = new ControlModuleContainerAnalyzer().Analyze(snapshot);
                    if (!result.DataBlockStructuresAvailable)
                    {
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Control-module discovery cannot run because the snapshot does not contain data-block structures.");
                        Console.Error.WriteLine("Re-export the snapshot with:");
                        Console.Error.WriteLine("--include-db-structures");
                        return 6;
                    }

                    var moduleRenderer = new ControlModuleConsoleRenderer();
                    Console.WriteLine();
                    moduleRenderer.PrintSummary(Console.Out, result);
                    moduleRenderer.PrintDetails(Console.Out, result, options.ModuleFamily);
                }
                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 4;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 1;
            }
        }

        private static string KnownFamilies()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
                names.Add(definition.ModuleFamily);
            return string.Join(", ", names);
        }
    }
}
