using System;

namespace TiaFds.Core
{
    public sealed class SnapshotCliOptions
    {
        private SnapshotCliOptions(string importJson, bool inventory, bool verbose)
        {
            ImportJson = importJson;
            Inventory = inventory;
            Verbose = verbose;
        }

        public string ImportJson { get; }
        public bool Inventory { get; }
        public bool Verbose { get; }

        public static SnapshotCliOptions Parse(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            string importJson = null;
            bool inventory = false;
            bool verbose = false;

            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--inventory" || option == "--verbose")
                {
                    if (option == "--inventory")
                    {
                        if (inventory) throw Invalid("--inventory may only be specified once.");
                        inventory = true;
                    }
                    else
                    {
                        if (verbose) throw Invalid("--verbose may only be specified once.");
                        verbose = true;
                    }

                    continue;
                }

                if (option != "--import-json")
                {
                    if (option == "--input" || option == "--retrieve-to" || option == "--plc" ||
                        option == "--export-json" || option == "--overwrite" || option == "--include-source-path")
                    {
                        throw Invalid("Live-project argument '" + option + "' is not supported by TiaFds.Cli. Use TiaFds.Extract.Cli.");
                    }

                    throw Invalid("Unknown argument: " + option);
                }

                if (importJson != null) throw Invalid("--import-json may only be specified once.");
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw Invalid("Missing value for --import-json.");
                }

                importJson = args[index];
            }

            if (string.IsNullOrWhiteSpace(importJson))
            {
                throw Invalid("--import-json is required.");
            }

            return new SnapshotCliOptions(importJson, inventory, verbose);
        }

        public static string Usage()
        {
            return "Usage: TiaFds.Cli.exe --import-json <path> [--inventory] [--verbose]";
        }

        private static ArgumentException Invalid(string message)
        {
            return new ArgumentException(message + Environment.NewLine + Usage());
        }
    }
}
