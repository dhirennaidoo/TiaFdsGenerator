using System;
using System.Collections.Generic;

namespace TiaFds.Step7Classic
{
    public sealed class Step7ExtractOptions
    {
        private Step7ExtractOptions() { }

        public string Input { get; private set; }
        public string PlcName { get; private set; }
        public string ExportJson { get; private set; }
        public bool Verbose { get; private set; }
        public bool Inventory { get; private set; }
        public bool Overwrite { get; private set; }
        public bool IncludeSourcePath { get; private set; }
        public bool IncludeDataBlockStructures { get; private set; }
        public bool IncludeBlockCalls { get; private set; }

        public static Step7ExtractOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var result = new Step7ExtractOptions();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (!seen.Add(option)) throw Invalid(option + " may only be specified once.");
                switch (option)
                {
                    case "--verbose": result.Verbose = true; continue;
                    case "--inventory": result.Inventory = true; continue;
                    case "--overwrite": result.Overwrite = true; continue;
                    case "--include-source-path": result.IncludeSourcePath = true; continue;
                    case "--include-db-structures": result.IncludeDataBlockStructures = true; continue;
                    case "--include-block-calls": result.IncludeBlockCalls = true; continue;
                    case "--retrieve-to":
                        throw Invalid("--retrieve-to is not used for STEP 7 Classic; zipped projects are read in place.");
                    case "--input":
                    case "--plc":
                    case "--export-json":
                        break;
                    default:
                        throw Invalid("Unknown argument: " + option);
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    throw Invalid("Missing value for " + option + ".");
                if (option == "--input") result.Input = args[index];
                else if (option == "--plc") result.PlcName = args[index];
                else result.ExportJson = args[index];
            }

            if (string.IsNullOrWhiteSpace(result.Input)) throw Invalid("--input is required.");
            if ((result.Inventory || result.ExportJson != null || result.IncludeDataBlockStructures ||
                 result.IncludeBlockCalls) &&
                string.IsNullOrWhiteSpace(result.PlcName))
                throw Invalid("--inventory, --include-db-structures, --include-block-calls, and --export-json require --plc <name>.");
            if ((result.Overwrite || result.IncludeSourcePath) && result.ExportJson == null)
                throw Invalid("--overwrite and --include-source-path require --export-json <path>.");
            return result;
        }

        public static string Usage()
        {
            return "Usage: TiaFds.Step7.Extract.Cli.exe --input <project.s7p|project.zip> [--plc <program|CPU|Station/CPU/Program>] " +
                   "[--inventory] [--verbose] [--include-db-structures] [--include-block-calls] [--export-json <path>] [--overwrite] [--include-source-path]";
        }

        private static ArgumentException Invalid(string message)
        {
            return new ArgumentException(message + Environment.NewLine + Usage());
        }
    }
}
