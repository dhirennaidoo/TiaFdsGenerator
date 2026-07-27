using System;
using System.IO;
using TiaFds.Core;
using TiaFds.Analysis;
using TiaFds.Reporting;

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
                ControlModuleDiscoveryResult sharedDiscovery = null;
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

                    ControlModuleDiscoveryResult result =
                        sharedDiscovery ?? (sharedDiscovery = new ControlModuleContainerAnalyzer().Analyze(snapshot));
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

                if (options.AnalyzeModuleCalls)
                {
                    AnalysisImplementationStatus? status = ParseStatus(options.ImplementationStatus);
                    if (options.ModuleFamily != null &&
                        ControlModuleCatalogue.FindByFamily(options.ModuleFamily) == null)
                    {
                        Console.Error.WriteLine("Error: Unknown module family '{0}'. Known families: {1}",
                            options.ModuleFamily, KnownFamilies());
                        return 4;
                    }

                    ControlModuleDiscoveryResult discovery =
                        sharedDiscovery ?? (sharedDiscovery = new ControlModuleContainerAnalyzer().Analyze(snapshot));
                    ControlModuleImplementationResult implementation =
                        new ControlModuleCallAnalyzer().Analyze(snapshot, discovery);
                    if (!implementation.DataBlockStructuresAvailable || !implementation.BlockCallsAvailable)
                    {
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Control-module call analysis requires both data-block structures and block calls.");
                        Console.Error.WriteLine("Re-export the snapshot with:");
                        Console.Error.WriteLine("--include-db-structures --include-block-calls");
                        foreach (ControlModuleImplementationDiagnostic diagnostic in implementation.Diagnostics)
                            if (diagnostic.Code == "CM100_BLOCK_CALLS_NOT_EXTRACTED" ||
                                diagnostic.Code == "CM101_DB_STRUCTURES_NOT_EXTRACTED")
                                Console.Error.WriteLine("{0}: {1}", diagnostic.Code, diagnostic.Message);
                        return 6;
                    }

                    ControlModuleBehaviourResult behaviour =
                        new ControlModuleBehaviourAnalyzer().Analyze(
                            snapshot, discovery, implementation);
                    AnalysisReport report = new AnalysisReportBuilder().Build(
                        snapshot, discovery, implementation, behaviour);
                    var implementationRenderer = new AnalysisReportConsoleRenderer();
                    Console.WriteLine();
                    implementationRenderer.PrintSummary(Console.Out, report);
                    implementationRenderer.PrintDetails(Console.Out, report,
                        new AnalysisReportFilter
                        {
                            ModuleFamily = options.ModuleFamily,
                            ModuleName = options.ModuleName,
                            ImplementationStatus = status
                        });

                    if (options.ReportJson != null)
                    {
                        new AnalysisReportJsonWriter().Write(report, options.ReportJson);
                        Console.WriteLine("JSON report: {0}", Path.GetFullPath(options.ReportJson));
                    }
                    if (options.ReportExcel != null)
                    {
                        new AnalysisReportExcelWriter().Write(report, options.ReportExcel);
                        Console.WriteLine("Excel report: {0}", Path.GetFullPath(options.ReportExcel));
                    }
                }
                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 4;
            }
            catch (AnalysisReportWriteException exception)
            {
                Console.Error.WriteLine("Error: {0}", exception.Message);
                return 7;
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

        private static AnalysisImplementationStatus? ParseStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            AnalysisImplementationStatus status;
            if (!Enum.TryParse(value, true, out status))
                throw new ArgumentException("Unknown implementation status '" + value +
                    "'. Valid values: " + string.Join(", ", Enum.GetNames(typeof(AnalysisImplementationStatus))));
            return status;
        }
    }
}
