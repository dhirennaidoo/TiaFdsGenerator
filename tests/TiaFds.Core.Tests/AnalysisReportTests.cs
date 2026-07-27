using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TiaFds.Analysis;
using TiaFds.Reporting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class AnalysisReportTests
    {
        [TestMethod]
        public void Builder_RejectsNullArguments()
        {
            var builder = new AnalysisReportBuilder();
            EngineeringSnapshot snapshot = Snapshot();
            ControlModuleDiscoveryResult discovery = Discovery();
            ControlModuleImplementationResult implementation = Result(new ControlModuleImplementation[0]);
            Assert.ThrowsException<ArgumentNullException>(() => builder.Build(null, discovery, implementation));
            Assert.ThrowsException<ArgumentNullException>(() => builder.Build(snapshot, null, implementation));
            Assert.ThrowsException<ArgumentNullException>(() => builder.Build(snapshot, discovery, null));
        }

        [TestMethod]
        public void Builder_CalculatesStatusesFamiliesOrderingAndManualReview()
        {
            var implementations = new[]
            {
                Implementation("Z", "UnexpectedZ", ControlModuleImplementationStatus.FamilyMismatch),
                Implementation("A", "Drive", ControlModuleImplementationStatus.Correlated),
                Implementation("B", "Drive", ControlModuleImplementationStatus.Unreferenced),
                Implementation("C", "Valve", ControlModuleImplementationStatus.MultipleCalls),
                Implementation("D", "Speed", ControlModuleImplementationStatus.UnresolvedParameter),
                Implementation("E", "DigitalInput", ControlModuleImplementationStatus.UnsupportedCall),
                Implementation("A", "UnexpectedA", ControlModuleImplementationStatus.Unreferenced)
            };
            AnalysisReport report = Build(Result(implementations));

            Assert.AreEqual(7, report.Summary.TotalModules);
            Assert.AreEqual(1, report.Summary.CorrelatedModules);
            Assert.AreEqual(2, report.Summary.UnreferencedModules);
            Assert.AreEqual(1, report.Summary.MultipleCallModules);
            Assert.AreEqual(1, report.Summary.UnresolvedModules);
            Assert.AreEqual(1, report.Summary.UnsupportedCallModules);
            Assert.AreEqual(1, report.Summary.FamilyMismatchModules);
            CollectionAssert.AreEqual(new[]
            {
                "AnalogueInput", "AnalogueOutput", "DigitalInput", "DigitalOutput",
                "Drive", "Speed", "Valve",
                "UnexpectedA", "UnexpectedZ"
            }, report.Families.Select(item => item.ModuleFamily).ToArray());
            AnalysisFamilySummary drive = report.Families.Single(item => item.ModuleFamily == "Drive");
            Assert.AreEqual(2, drive.Total);
            Assert.AreEqual(1, drive.Correlated);
            Assert.AreEqual(1, drive.Unreferenced);
            Assert.AreEqual(6, report.ManualReview.Count);
            Assert.IsFalse(report.ManualReview.Any(item => item.ModuleName == "A" &&
                item.ModuleFamily == "Drive"));
            Assert.AreEqual("No recognised processing call was correlated.",
                report.ManualReview.Single(item => item.ModuleName == "B").Reason);
            CollectionAssert.AreEqual(new[]
            {
                "DigitalInput/E", "Drive/A", "Drive/B", "Speed/D", "Valve/C",
                "UnexpectedA/A", "UnexpectedZ/Z"
            }, report.Modules.Select(item => item.ModuleFamily + "/" + item.ModuleName).ToArray());
        }

        [TestMethod]
        public void Builder_CopiesAndSortsModulesCallSitesAndCountsVariantCallSites()
        {
            var sites = new[]
            {
                Site("DrvType1", 2, "Later", 20),
                Site("DrvType1", 1, "Earlier", 10),
                Site(null, 3, "Blank", 30)
            };
            var implementation = new ControlModuleImplementation(
                Declaration("M2", "Drive"), ControlModuleImplementationStatus.MultipleCalls, sites);
            AnalysisReport report = Build(Result(new[] { implementation }));

            Assert.AreEqual(1, report.ProcessingVariants.Count);
            Assert.AreEqual("Drive", report.ProcessingVariants[0].ModuleFamily);
            Assert.AreEqual("DrvType1", report.ProcessingVariants[0].ProcessingVariant);
            Assert.AreEqual(2, report.ProcessingVariants[0].Count);
            Assert.AreEqual("Earlier", report.Modules[0].CallSites[0].CallingBlockName);
            Assert.AreEqual("Later", report.Modules[0].CallSites[1].CallingBlockName);
            Assert.AreEqual("M2", report.Modules[0].ModuleName);
            Assert.AreEqual("db.cm.Drive.M2", report.Modules[0].MemberPath);
        }

        [TestMethod]
        public void Builder_PreservesAndGroupsDiagnosticsByCodeAndSeverity()
        {
            var diagnostics = new[]
            {
                new ControlModuleImplementationDiagnostic("Warning", "CM200", "second", "B"),
                new ControlModuleImplementationDiagnostic("warning", "CM200", "third", "C"),
                new ControlModuleImplementationDiagnostic("Warning", "CM200", "first", "A"),
                new ControlModuleImplementationDiagnostic("Error", "CM100", "error", "Z")
            };
            AnalysisReport report = Build(Result(new ControlModuleImplementation[0], diagnostics));

            Assert.AreEqual(4, report.Diagnostics.Count);
            Assert.AreEqual(3, report.Summary.WarningCount);
            Assert.AreEqual(1, report.Summary.ErrorCount);
            Assert.AreEqual(3, report.DiagnosticSummary.Count);
            Assert.AreEqual("CM100", report.DiagnosticSummary[0].Code);
            Assert.AreEqual("CM200", report.DiagnosticSummary[1].Code);
            Assert.AreEqual("Warning", report.DiagnosticSummary[1].Severity);
            Assert.AreEqual(2, report.DiagnosticSummary[1].Count);
            Assert.AreEqual("warning", report.DiagnosticSummary[2].Severity);
        }

        [TestMethod]
        public void Renderer_UsesCalculatedReportValuesAndPrintsDetailsAndDiagnostics()
        {
            var report = new AnalysisReport(
                new AnalysisReportSummary(99, 88, 7, 1, 1, 1, 1, 12, 3),
                new AnalysisFamilySummary[0],
                new[] { new AnalysisVariantSummary("Drive", "DrvType1", 42) },
                new[]
                {
                    new AnalysisModule("M1", "Drive", "Description", "db.cm.Drv", 50,
                        "db.cm.Drv.M1", "Udt.cm.Drv", "Confirmed",
                        AnalysisImplementationStatus.Correlated,
                        new[] { CopySite(Site("DrvType1", 1, "Caller", 10)) })
                },
                new[] { new AnalysisDiagnosticSummary("CM200", "Warning", 12) },
                new[] { new AnalysisDiagnostic("Warning", "CM200", "Source", "Message") },
                new ManualReviewItem[0]);
            var writer = new StringWriter();
            var renderer = new AnalysisReportConsoleRenderer();
            renderer.PrintSummary(writer, report);
            renderer.PrintDetails(writer, report, new AnalysisReportFilter
            {
                ModuleFamily = "drive",
                ModuleName = "m1",
                ImplementationStatus = AnalysisImplementationStatus.Correlated
            });
            string output = writer.ToString();
            StringAssert.Contains(output, "Advansys engineering analysis");
            StringAssert.Contains(output, "99");
            StringAssert.Contains(output, "Drive / DrvType1: 42");
            StringAssert.Contains(output, "CM200 / Warning: 12");
            StringAssert.Contains(output, "db.cm.Drv.M1");
            StringAssert.Contains(output, "Warning CM200 [Source] Message");
            Assert.ThrowsException<ArgumentNullException>(() => renderer.PrintSummary(null, report));
            Assert.ThrowsException<ArgumentNullException>(() => renderer.PrintSummary(writer, null));
            Assert.ThrowsException<ArgumentNullException>(() => renderer.PrintDetails(null, report, null));
            Assert.ThrowsException<ArgumentNullException>(() => renderer.PrintDetails(writer, null, null));
        }

        [TestMethod]
        public void Builder_KnownBpRegressionTotalsAreStable()
        {
            var implementations = new List<ControlModuleImplementation>();
            for (var index = 0; index < 157; index++)
                implementations.Add(Implementation("C" + index, "Drive",
                    ControlModuleImplementationStatus.Correlated));
            for (var index = 0; index < 118; index++)
                implementations.Add(Implementation("U" + index, "Drive",
                    ControlModuleImplementationStatus.Unreferenced));
            AnalysisReport report = Build(Result(implementations.ToArray()));
            Assert.AreEqual(275, report.Summary.TotalModules);
            Assert.AreEqual(157, report.Summary.CorrelatedModules);
            Assert.AreEqual(118, report.Summary.UnreferencedModules);
            Assert.AreEqual(0, report.Summary.MultipleCallModules);
            Assert.AreEqual(0, report.Summary.UnresolvedModules);
            Assert.AreEqual(0, report.Summary.FamilyMismatchModules);
        }

        private static AnalysisReport Build(ControlModuleImplementationResult result)
        {
            return new AnalysisReportBuilder().Build(Snapshot(), Discovery(), result);
        }

        private static ControlModuleImplementationResult Result(
            IReadOnlyList<ControlModuleImplementation> modules,
            IReadOnlyList<ControlModuleImplementationDiagnostic> diagnostics = null)
        {
            return new ControlModuleImplementationResult(modules, diagnostics, true, true);
        }

        private static ControlModuleImplementation Implementation(
            string name, string family, ControlModuleImplementationStatus status)
        {
            return new ControlModuleImplementation(Declaration(name, family), status,
                status == ControlModuleImplementationStatus.Correlated
                    ? new[] { Site("Variant", 1, "Caller", 1) }
                    : new ControlModuleCallSite[0]);
        }

        private static ControlModuleInfo Declaration(string name, string family)
        {
            return new ControlModuleInfo(name, family, "db.cm." + family, 50,
                "db.cm." + family + "." + name, "Udt.cm." + family,
                "Description " + name, false, null, ControlModuleDiscoveryStatus.Confirmed);
        }

        private static ControlModuleCallSite Site(
            string variant, int ordinal, string caller, int callerNumber)
        {
            return new ControlModuleCallSite(
                "cm.Function", null, variant, caller, callerNumber, "Function",
                ordinal, "Network", ordinal, "Module", "\"db\".M");
        }

        private static AnalysisCallSite CopySite(ControlModuleCallSite site)
        {
            return new AnalysisCallSite(
                site.ProcessingFunctionName, site.ProcessingFunctionNumber,
                site.ProcessingVariant, site.CallingBlockName, site.CallingBlockNumber,
                site.CallingBlockType, site.NetworkNumber, site.NetworkTitle,
                site.CallOrdinal, site.InOutFormalParameterName, site.InOutActualExpression);
        }

        private static EngineeringSnapshot Snapshot()
        {
            var inventory = new PlcInventory("PLC", new ProgramBlockInfo[0],
                new PlcTagTableInfo[0], new PlcDataTypeInfo[0], new InventoryDiagnostic[0]);
            return new EngineeringSnapshot(SnapshotSchema.CurrentVersion, ProductVersion.Current,
                DateTimeOffset.UtcNow, new ProjectSnapshot("P", "P.ap15_1", null,
                    new PlcInfo("PLC", "D", "CPU"), inventory));
        }

        private static ControlModuleDiscoveryResult Discovery()
        {
            return new ControlModuleDiscoveryResult(
                new ControlModuleContainerInfo[0],
                new ControlModuleInfo[0],
                new ModuleDiscoveryDiagnostic[0], true);
        }
    }
}
