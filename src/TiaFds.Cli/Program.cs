using System;
using TiaFds.Core;

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
    }
}
