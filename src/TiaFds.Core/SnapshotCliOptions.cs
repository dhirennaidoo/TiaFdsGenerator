using System;

namespace TiaFds.Core
{
    public sealed class SnapshotCliOptions
    {
        private SnapshotCliOptions(
            string importJson,
            bool inventory,
            bool verbose,
            bool discoverModules,
            string moduleFamily,
            bool analyzeModuleCalls,
            string implementationStatus,
            string moduleName,
            string reportJson,
            string reportExcel)
        {
            ImportJson = importJson;
            Inventory = inventory;
            Verbose = verbose;
            DiscoverModules = discoverModules;
            ModuleFamily = moduleFamily;
            AnalyzeModuleCalls = analyzeModuleCalls;
            ImplementationStatus = implementationStatus;
            ModuleName = moduleName;
            ReportJson = reportJson;
            ReportExcel = reportExcel;
        }

        public string ImportJson { get; }
        public bool Inventory { get; }
        public bool Verbose { get; }
        public bool DiscoverModules { get; }
        public string ModuleFamily { get; }
        public bool AnalyzeModuleCalls { get; }
        public string ImplementationStatus { get; }
        public string ModuleName { get; }
        public string ReportJson { get; }
        public string ReportExcel { get; }

        public static SnapshotCliOptions Parse(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            string importJson = null;
            bool inventory = false;
            bool verbose = false;
            bool discoverModules = false;
            string moduleFamily = null;
            bool analyzeModuleCalls = false;
            string implementationStatus = null;
            string moduleName = null;
            string reportJson = null;
            string reportExcel = null;

            for (var index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--inventory" || option == "--verbose" ||
                    option == "--discover-modules" || option == "--analyze-module-calls")
                {
                    if (option == "--inventory")
                    {
                        if (inventory) throw Invalid("--inventory may only be specified once.");
                        inventory = true;
                    }
                    else if (option == "--verbose")
                    {
                        if (verbose) throw Invalid("--verbose may only be specified once.");
                        verbose = true;
                    }
                    else
                    {
                        if (option == "--discover-modules")
                        {
                            if (discoverModules) throw Invalid("--discover-modules may only be specified once.");
                            discoverModules = true;
                        }
                        else
                        {
                            if (analyzeModuleCalls) throw Invalid("--analyze-module-calls may only be specified once.");
                            analyzeModuleCalls = true;
                        }
                    }

                    continue;
                }

                if (option != "--import-json" && option != "--module-family" &&
                    option != "--implementation-status" && option != "--module" &&
                    option != "--report-json" && option != "--report-excel")
                {
                    if (option == "--input" || option == "--retrieve-to" || option == "--plc" ||
                        option == "--export-json" || option == "--overwrite" || option == "--include-source-path" ||
                        option == "--include-db-structures" || option == "--include-block-calls")
                    {
                        throw Invalid("Live-project argument '" + option + "' is not supported by TiaFds.Cli. Use TiaFds.Extract.Cli.");
                    }

                    throw Invalid("Unknown argument: " + option);
                }

                if (option == "--import-json" && importJson != null)
                    throw Invalid("--import-json may only be specified once.");
                if (option == "--module-family" && moduleFamily != null)
                    throw Invalid("--module-family may only be specified once.");
                if (option == "--implementation-status" && implementationStatus != null)
                    throw Invalid("--implementation-status may only be specified once.");
                if (option == "--module" && moduleName != null)
                    throw Invalid("--module may only be specified once.");
                if (option == "--report-json" && reportJson != null)
                    throw Invalid("--report-json may only be specified once.");
                if (option == "--report-excel" && reportExcel != null)
                    throw Invalid("--report-excel may only be specified once.");
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    throw Invalid("Missing value for " + option + ".");
                }

                if (option == "--import-json") importJson = args[index];
                else if (option == "--module-family") moduleFamily = args[index];
                else if (option == "--implementation-status") implementationStatus = args[index];
                else if (option == "--module") moduleName = args[index];
                else if (option == "--report-json") reportJson = args[index];
                else reportExcel = args[index];
            }

            if (string.IsNullOrWhiteSpace(importJson))
            {
                throw Invalid("--import-json is required.");
            }

            if (moduleFamily != null && !discoverModules && !analyzeModuleCalls)
            {
                throw Invalid("--module-family requires --discover-modules or --analyze-module-calls.");
            }
            if ((implementationStatus != null || moduleName != null) && !analyzeModuleCalls)
                throw Invalid("--implementation-status and --module require --analyze-module-calls.");
            if ((reportJson != null || reportExcel != null) && !analyzeModuleCalls)
                throw Invalid("--report-json and --report-excel require --analyze-module-calls.");

            return new SnapshotCliOptions(importJson, inventory, verbose, discoverModules, moduleFamily,
                analyzeModuleCalls, implementationStatus, moduleName, reportJson, reportExcel);
        }

        public static string Usage()
        {
            return "Usage: TiaFds.Cli.exe --import-json <path> [--inventory] [--verbose] " +
                   "[--discover-modules [--module-family <name>]]" +
                   " [--analyze-module-calls [--module-family <name>] [--implementation-status <status>]" +
                   " [--module <name>] [--report-json <path>] [--report-excel <path>]]";
        }

        private static ArgumentException Invalid(string message)
        {
            return new ArgumentException(message + Environment.NewLine + Usage());
        }
    }
}
