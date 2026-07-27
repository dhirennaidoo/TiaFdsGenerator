using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using TiaFds.Reporting;

namespace TiaFds.Core.Tests
{
    [TestClass]
    public sealed class AnalysisReportWriterTests
    {
        [TestMethod]
        public void JsonWriter_WritesStableCompleteContractAndOverwrites()
        {
            string directory = TemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "nested", "AnalysisReport.json");
                File.WriteAllText(Path.Combine(directory, "placeholder.txt"), "old");
                AnalysisReport report = RepresentativeReport();
                var writer = new AnalysisReportJsonWriter();

                writer.Write(report, path);
                string first = File.ReadAllText(path);
                File.WriteAllText(path, "old");
                writer.Write(report, path);
                string second = File.ReadAllText(path);

                Assert.AreEqual(first, second);
                JObject json = JObject.Parse(second);
                Assert.AreEqual("1.1", (string)json["schemaVersion"]);
                Assert.AreEqual("Example", (string)json["project"]["projectName"]);
                Assert.AreEqual(3, (int)json["moduleSummary"]["totalCount"]);
                Assert.AreEqual(2, json["processingVariants"].Count());
                Assert.AreEqual(2, json["diagnosticsByCode"].Count());
                Assert.AreEqual(3, json["modules"].Count());
                Assert.AreEqual(2, json["modules"][1]["processingCalls"].Count());
                Assert.AreEqual("db.cm.Drv.M2",
                    (string)json["modules"][1]["processingCalls"][0]["resolvedMemberPath"]);
                Assert.AreEqual(2, json["diagnostics"].Count());
                Assert.AreEqual(2, json["manualReviewItems"].Count());
                Assert.IsFalse(first.StartsWith("\uFEFF", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void JsonWriter_ValidatesArguments()
        {
            var writer = new AnalysisReportJsonWriter();
            Assert.ThrowsException<ArgumentNullException>(() => writer.Write(null, "x.json"));
            Assert.ThrowsException<ArgumentException>(
                () => writer.Write(RepresentativeReport(), " "));
        }

        [TestMethod]
        public void ExcelWriter_WritesRequiredSheetsTablesCountsAndSafeText()
        {
            string directory = TemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "nested", "AnalysisReport.xlsx");
                var writer = new AnalysisReportExcelWriter();
                writer.Write(RepresentativeReport(), path);

                using (var workbook = new XLWorkbook(path))
                {
                    CollectionAssert.AreEqual(
                        new[] { "Summary", "Modules", "Processing Calls",
                            "Behavioural Conditions", "Diagnostics", "Manual Review" },
                        workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
                    IXLWorksheet summary = workbook.Worksheet("Summary");
                    Assert.AreEqual(3, FindSummaryValue(summary, "Total"));
                    Assert.AreEqual(2, FindSummaryValue(summary, "Warnings"));
                    Assert.AreEqual(0, FindSummaryValue(summary, "Errors"));

                    Assert.AreEqual(4, workbook.Worksheet("Modules").LastRowUsed().RowNumber());
                    Assert.AreEqual(4, workbook.Worksheet("Processing Calls").LastRowUsed().RowNumber());
                    Assert.AreEqual(3, workbook.Worksheet("Diagnostics").LastRowUsed().RowNumber());
                    Assert.AreEqual(3, workbook.Worksheet("Manual Review").LastRowUsed().RowNumber());
                    Assert.AreEqual("Family", workbook.Worksheet("Modules").Cell(1, 1).GetString());
                    Assert.IsTrue(workbook.Worksheet("Modules").Tables.Any());
                    Assert.IsTrue(workbook.Worksheet("Processing Calls").Tables.Any());
                    Assert.IsTrue(workbook.Worksheet("Diagnostics").Tables.Any());
                    Assert.IsTrue(workbook.Worksheet("Manual Review").Tables.Any());
                    Assert.AreEqual(1, workbook.Worksheet("Modules").SheetView.SplitRow);

                    IXLCell description = workbook.Worksheet("Modules").Cell(2, 3);
                    Assert.AreEqual("=SUM(A1:A2)", description.GetString());
                    Assert.AreEqual(XLDataType.Text, description.DataType);
                    Assert.IsFalse(description.HasFormula);
                    IXLCell expression = workbook.Worksheet("Processing Calls").Cell(2, 13);
                    Assert.AreEqual("+unsafe", expression.GetString());
                    Assert.AreEqual(XLDataType.Text, expression.DataType);
                    Assert.IsFalse(expression.HasFormula);
                }

                File.WriteAllText(path, "not a workbook");
                writer.Write(RepresentativeReport(), path);
                using (var overwritten = new XLWorkbook(path))
                    Assert.AreEqual(6, overwritten.Worksheets.Count);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExcelWriter_EmptyCollectionsStillProduceValidFilteredWorksheets()
        {
            string directory = TemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "empty.xlsx");
                var empty = new AnalysisReport(
                    new AnalysisProjectInfo("Empty", "PLC"),
                    new AnalysisPlcInventorySummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                    new AnalysisReportSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
                    new AnalysisFamilySummary[0],
                    new AnalysisVariantSummary[0],
                    new AnalysisModule[0],
                    new AnalysisDiagnosticSummary[0],
                    new AnalysisDiagnostic[0],
                    new ManualReviewItem[0]);
                new AnalysisReportExcelWriter().Write(empty, path);
                using (var workbook = new XLWorkbook(path))
                {
                    foreach (string name in new[]
                    {
                        "Modules", "Processing Calls", "Behavioural Conditions",
                        "Diagnostics", "Manual Review"
                    })
                    {
                        Assert.AreEqual(1, workbook.Worksheet(name).LastRowUsed().RowNumber());
                        Assert.IsTrue(workbook.Worksheet(name).AutoFilter.IsEnabled);
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void ExcelWriter_ValidatesArguments()
        {
            var writer = new AnalysisReportExcelWriter();
            Assert.ThrowsException<ArgumentNullException>(() => writer.Write(null, "x.xlsx"));
            Assert.ThrowsException<ArgumentException>(
                () => writer.Write(RepresentativeReport(), string.Empty));
        }

        [TestMethod]
        public void JsonAndExcel_RepresentTheSameReportInstance()
        {
            string directory = TemporaryDirectory();
            try
            {
                AnalysisReport report = RepresentativeReport();
                string jsonPath = Path.Combine(directory, "report.json");
                string excelPath = Path.Combine(directory, "report.xlsx");
                new AnalysisReportJsonWriter().Write(report, jsonPath);
                new AnalysisReportExcelWriter().Write(report, excelPath);

                JObject json = JObject.Parse(File.ReadAllText(jsonPath));
                using (var workbook = new XLWorkbook(excelPath))
                {
                    Assert.AreEqual(report.Summary.TotalModules,
                        (int)json["moduleSummary"]["totalCount"]);
                    Assert.AreEqual(report.Summary.TotalModules,
                        FindSummaryValue(workbook.Worksheet("Summary"), "Total"));
                    Assert.AreEqual(report.Modules.Count, json["modules"].Count());
                    Assert.AreEqual(report.Modules.Count,
                        workbook.Worksheet("Modules").LastRowUsed().RowNumber() - 1);
                    int callCount = report.Modules.Sum(module => module.CallSites.Count);
                    Assert.AreEqual(callCount,
                        json["modules"].Sum(module => module["processingCalls"].Count()));
                    Assert.AreEqual(callCount,
                        workbook.Worksheet("Processing Calls").LastRowUsed().RowNumber() - 1);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static AnalysisReport RepresentativeReport()
        {
            var aiCall = new AnalysisCallSite(
                "cm.AI", null, "AI", "FC100", 100, "Function", 1, "Input", 1,
                "AI", "+unsafe", "db.cm.AI.M1");
            var driveCalls = new[]
            {
                new AnalysisCallSite("cm.DrvType1", 51, "DrvType1", "FC501", 501,
                    "Function", 10, "Drive A", 1, "Drv", "\"db.cm.Drv\".M2",
                    "db.cm.Drv.M2"),
                new AnalysisCallSite("cm.DrvType3", 53, "DrvType3", "FC502", 502,
                    "Function", 20, "Drive B", 2, "Drv", "@operand",
                    "db.cm.Drv.M2")
            };
            return new AnalysisReport(
                new AnalysisProjectInfo("Example", "PLC_1"),
                new AnalysisPlcInventorySummary(10, 5, 1, 2, 1, 1, 2, 3, 4, 2),
                new AnalysisReportSummary(3, 1, 1, 1, 0, 0, 0, 2, 0),
                new[]
                {
                    new AnalysisFamilySummary("AnalogueInput", 1, 1, 0, 0, 0, 0, 0),
                    new AnalysisFamilySummary("Drive", 1, 0, 0, 1, 0, 0, 0),
                    new AnalysisFamilySummary("Valve", 1, 0, 1, 0, 0, 0, 0)
                },
                new[]
                {
                    new AnalysisVariantSummary("AnalogueInput", "AI", 1),
                    new AnalysisVariantSummary("Drive", "DrvType1", 1)
                },
                new[]
                {
                    new AnalysisModule("M1", "AnalogueInput", "=SUM(A1:A2)", "db.cm.AI",
                        40, "db.cm.AI.M1", "Udt.cm.AI", "Confirmed",
                        AnalysisImplementationStatus.Correlated, new[] { aiCall }),
                    new AnalysisModule("M2", "Drive", "Drive", "db.cm.Drv",
                        50, "db.cm.Drv.M2", "Udt.cm.Drv", "Confirmed",
                        AnalysisImplementationStatus.MultipleCalls, driveCalls),
                    new AnalysisModule("M3", "Valve", "Valve", "db.cm.Vlv",
                        60, "db.cm.Vlv.M3", "Udt.cm.Vlv", "Confirmed",
                        AnalysisImplementationStatus.Unreferenced, new AnalysisCallSite[0])
                },
                new[]
                {
                    new AnalysisDiagnosticSummary("CM100", "Warning", 1),
                    new AnalysisDiagnosticSummary("CM200", "Warning", 1)
                },
                new[]
                {
                    new AnalysisDiagnostic("Warning", "CM100", "db.cm.Drv.M2", "First"),
                    new AnalysisDiagnostic("Warning", "CM200", "db.cm.Vlv.M3", "Second")
                },
                new[]
                {
                    new ManualReviewItem("Drive", "M2", "db.cm.Drv.M2",
                        AnalysisImplementationStatus.MultipleCalls, "Review both calls."),
                    new ManualReviewItem("Valve", "M3", "db.cm.Vlv.M3",
                        AnalysisImplementationStatus.Unreferenced, "No call found.")
                });
        }

        private static int FindSummaryValue(IXLWorksheet sheet, string label)
        {
            IXLCell cell = sheet.CellsUsed().First(item => item.GetString() == label);
            return cell.CellRight().GetValue<int>();
        }

        private static string TemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "TiaFds.Reporting.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
