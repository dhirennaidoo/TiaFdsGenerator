using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace TiaFds.Reporting
{
    public sealed class AnalysisReportExcelWriter
    {
        private const string HeaderColour = "1F4E78";
        private const string SectionColour = "D9EAF7";

        public void Write(AnalysisReport report, string outputPath)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An Excel report output path is required.", nameof(outputPath));

            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var workbook = new XLWorkbook())
                {
                    WriteSummary(workbook.Worksheets.Add("Summary"), report);
                    WriteModules(workbook.Worksheets.Add("Modules"), report);
                    WriteProcessingCalls(workbook.Worksheets.Add("Processing Calls"), report);
                    WriteBehaviouralConditions(
                        workbook.Worksheets.Add("Behavioural Conditions"), report);
                    WriteDiagnostics(workbook.Worksheets.Add("Diagnostics"), report);
                    WriteManualReview(workbook.Worksheets.Add("Manual Review"), report);
                    workbook.SaveAs(fullPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException ||
                exception is System.Security.SecurityException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                throw new AnalysisReportWriteException(outputPath, exception);
            }
        }

        private static void WriteSummary(IXLWorksheet sheet, AnalysisReport report)
        {
            Text(sheet.Cell(1, 1), "Advansys Engineering Analysis");
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 16;

            var row = 3;
            Section(sheet, row++, "Project");
            LabelValue(sheet, row++, "Project name", report.Project.ProjectName);
            LabelValue(sheet, row++, "PLC name", report.Project.PlcName);
            LabelValue(sheet, row++, "Report schema version", report.SchemaVersion);

            row++;
            Section(sheet, row++, "PLC inventory");
            LabelValue(sheet, row++, "Program blocks", report.PlcInventory.ProgramBlockCount);
            LabelValue(sheet, row++, "Functions", report.PlcInventory.FunctionCount);
            LabelValue(sheet, row++, "Function blocks", report.PlcInventory.FunctionBlockCount);
            LabelValue(sheet, row++, "Global data blocks", report.PlcInventory.GlobalDataBlockCount);
            LabelValue(sheet, row++, "Instance data blocks", report.PlcInventory.InstanceDataBlockCount);
            LabelValue(sheet, row++, "Organization blocks", report.PlcInventory.OrganizationBlockCount);
            LabelValue(sheet, row++, "Tag tables", report.PlcInventory.TagTableCount);
            LabelValue(sheet, row++, "PLC data types", report.PlcInventory.PlcDataTypeCount);
            LabelValue(sheet, row++, "Extraction diagnostics", report.PlcInventory.DiagnosticCount);
            LabelValue(sheet, row++, "Data-block structures", report.PlcInventory.DataBlockStructureCount);

            row++;
            Section(sheet, row++, "Module summary");
            LabelValue(sheet, row++, "Total", report.Summary.TotalModules);
            LabelValue(sheet, row++, "Correlated", report.Summary.CorrelatedModules);
            LabelValue(sheet, row++, "Unreferenced", report.Summary.UnreferencedModules);
            LabelValue(sheet, row++, "Multiple calls", report.Summary.MultipleCallModules);
            LabelValue(sheet, row++, "Unresolved", report.Summary.UnresolvedModules);
            LabelValue(sheet, row++, "Unsupported calls", report.Summary.UnsupportedCallModules);
            LabelValue(sheet, row++, "Family mismatch", report.Summary.FamilyMismatchModules);

            row++;
            Section(sheet, row++, "Behaviour summary");
            LabelValue(sheet, row++, "Total behavioural conditions",
                report.BehaviourSummary.TotalConditionCount);
            LabelValue(sheet, row++, "Start commands",
                report.BehaviourSummary.StartCommandCount);
            LabelValue(sheet, row++, "Control requests",
                report.BehaviourSummary.ControlRequestCount);
            LabelValue(sheet, row++, "Interlocks",
                report.BehaviourSummary.InterlockCount);
            LabelValue(sheet, row++, "Complete",
                report.BehaviourSummary.CompleteCount);
            LabelValue(sheet, row++, "Partial",
                report.BehaviourSummary.PartialCount);
            LabelValue(sheet, row++, "Unsupported",
                report.BehaviourSummary.UnsupportedCount);
            LabelValue(sheet, row++, "Unresolved",
                report.BehaviourSummary.UnresolvedCount);
            LabelValue(sheet, row++, "Ambiguous",
                report.BehaviourSummary.AmbiguousCount);

            row++;
            Section(sheet, row++, "Family summary");
            int familyHeader = row++;
            string[] familyHeaders =
            {
                "Family", "Total", "Correlated", "Unreferenced", "Multiple Calls",
                "Unresolved", "Unsupported", "Family Mismatch"
            };
            Header(sheet, familyHeader, familyHeaders);
            foreach (AnalysisFamilySummary family in report.Families)
            {
                Text(sheet.Cell(row, 1), family.ModuleFamily);
                Number(sheet.Cell(row, 2), family.Total);
                Number(sheet.Cell(row, 3), family.Correlated);
                Number(sheet.Cell(row, 4), family.Unreferenced);
                Number(sheet.Cell(row, 5), family.MultipleCalls);
                Number(sheet.Cell(row, 6), family.Unresolved);
                Number(sheet.Cell(row, 7), family.UnsupportedCalls);
                Number(sheet.Cell(row, 8), family.FamilyMismatch);
                row++;
            }
            Table(sheet, familyHeader, row - 1, familyHeaders.Length, "FamilySummary");

            row++;
            Section(sheet, row++, "Processing variants");
            int variantHeader = row++;
            Header(sheet, variantHeader, "Family", "Variant", "Count");
            foreach (AnalysisVariantSummary variant in report.ProcessingVariants)
            {
                Text(sheet.Cell(row, 1), variant.ModuleFamily);
                Text(sheet.Cell(row, 2), variant.ProcessingVariant);
                Number(sheet.Cell(row, 3), variant.Count);
                row++;
            }
            Table(sheet, variantHeader, row - 1, 3, "ProcessingVariants");

            row++;
            Section(sheet, row++, "Diagnostic summary");
            LabelValue(sheet, row++, "Warnings", report.DiagnosticCounts.WarningCount);
            LabelValue(sheet, row++, "Errors", report.DiagnosticCounts.ErrorCount);
            row++;
            int diagnosticHeader = row++;
            Header(sheet, diagnosticHeader, "Severity", "Code", "Count");
            foreach (AnalysisDiagnosticSummary diagnostic in report.DiagnosticSummary)
            {
                Text(sheet.Cell(row, 1), diagnostic.Severity);
                Text(sheet.Cell(row, 2), diagnostic.Code);
                Number(sheet.Cell(row, 3), diagnostic.Count);
                row++;
            }
            Table(sheet, diagnosticHeader, row - 1, 3, "DiagnosticsByCode");

            sheet.SheetView.FreezeRows(1);
            sheet.Columns(1, 8).AdjustToContents();
            LimitWidth(sheet.Column(1), 35);
            LimitWidth(sheet.Column(2), 35);
        }

        private static void WriteModules(IXLWorksheet sheet, AnalysisReport report)
        {
            string[] headers =
            {
                "Family", "Name", "Description", "Status", "Container",
                "Container Number", "Datatype", "Member Path",
                "Processing Call Count", "Processing Variants", "Caller Blocks",
                "Start Command Count", "Control Request Count", "Interlock Count",
                "Incomplete Behaviour Count"
            };
            Header(sheet, 1, headers);
            var row = 2;
            foreach (AnalysisModule module in report.Modules)
            {
                Text(sheet.Cell(row, 1), module.ModuleFamily);
                Text(sheet.Cell(row, 2), module.ModuleName);
                Text(sheet.Cell(row, 3), module.Description);
                Text(sheet.Cell(row, 4), module.ImplementationStatus.ToString());
                Text(sheet.Cell(row, 5), module.ContainerDbName);
                NullableNumber(sheet.Cell(row, 6), module.ContainerDbNumber);
                Text(sheet.Cell(row, 7), module.DataTypeName);
                Text(sheet.Cell(row, 8), module.MemberPath);
                Number(sheet.Cell(row, 9), module.CallSites.Count);
                Text(sheet.Cell(row, 10), JoinDistinct(module.CallSites, true));
                Text(sheet.Cell(row, 11), JoinDistinct(module.CallSites, false));
                Number(sheet.Cell(row, 12), Count(module,
                    AnalysisBehaviouralConditionKind.StartCommand));
                Number(sheet.Cell(row, 13), Count(module,
                    AnalysisBehaviouralConditionKind.ControlRequest));
                Number(sheet.Cell(row, 14), Count(module,
                    AnalysisBehaviouralConditionKind.Interlock));
                Number(sheet.Cell(row, 15), CountIncomplete(module));
                row++;
            }
            FinishTable(sheet, row - 1, headers.Length, "ModulesTable",
                new[] { 3, 8 });
        }

        private static void WriteBehaviouralConditions(
            IXLWorksheet sheet, AnalysisReport report)
        {
            string[] headers =
            {
                "Module Family", "Module Name", "Module Description", "Module Member Path",
                "Condition Type", "Condition Member", "Condition Index", "Resolution Status",
                "Description", "Source Expression", "Normalized Expression", "Source Operands",
                "Resolved Operand Paths", "Block Number", "Block Name", "Block Language",
                "Network Number", "Network Title", "Network Comment", "Diagnostic Count"
            };
            Header(sheet, 1, headers);
            var row = 2;
            foreach (AnalysisBehaviouralCondition condition in report.BehaviouralConditions)
            {
                AnalysisModule module = FindModule(report.Modules, condition.ModuleMemberPath);
                Text(sheet.Cell(row, 1), condition.ModuleFamily);
                Text(sheet.Cell(row, 2), condition.ModuleName);
                Text(sheet.Cell(row, 3), module == null ? null : module.Description);
                Text(sheet.Cell(row, 4), condition.ModuleMemberPath);
                Text(sheet.Cell(row, 5), condition.Kind.ToString());
                Text(sheet.Cell(row, 6), condition.Member);
                NullableNumber(sheet.Cell(row, 7), condition.Index);
                Text(sheet.Cell(row, 8), condition.ResolutionStatus.ToString());
                Text(sheet.Cell(row, 9), condition.Description);
                Text(sheet.Cell(row, 10), condition.SourceExpression);
                Text(sheet.Cell(row, 11),
                    condition.Expression == null ? null : condition.Expression.DisplayText);
                Text(sheet.Cell(row, 12), string.Join("; ", condition.SourceOperands));
                Text(sheet.Cell(row, 13), string.Join("; ", condition.ResolvedOperandPaths));
                NullableNumber(sheet.Cell(row, 14), condition.BlockNumber);
                Text(sheet.Cell(row, 15), condition.BlockName);
                Text(sheet.Cell(row, 16), condition.BlockLanguage);
                NullableNumber(sheet.Cell(row, 17), condition.NetworkNumber);
                Text(sheet.Cell(row, 18), condition.NetworkTitle);
                Text(sheet.Cell(row, 19), condition.NetworkComment);
                Number(sheet.Cell(row, 20), condition.DiagnosticCount);
                row++;
            }
            FinishTable(sheet, row - 1, headers.Length, "BehaviouralConditionsTable",
                new[] { 3, 4, 9, 10, 11, 12, 13, 18, 19 });
        }

        private static void WriteProcessingCalls(IXLWorksheet sheet, AnalysisReport report)
        {
            string[] headers =
            {
                "Module Family", "Module Name", "Module Description", "Module Member Path",
                "Module Status", "Processing FC", "Variant", "Caller Block Number",
                "Caller Block Name", "Network Number", "Network Title", "Parameter Name",
                "Actual Expression", "Resolved Member Path"
            };
            Header(sheet, 1, headers);
            var row = 2;
            foreach (AnalysisModule module in report.Modules)
                foreach (AnalysisCallSite call in module.CallSites)
                {
                    Text(sheet.Cell(row, 1), module.ModuleFamily);
                    Text(sheet.Cell(row, 2), module.ModuleName);
                    Text(sheet.Cell(row, 3), module.Description);
                    Text(sheet.Cell(row, 4), module.MemberPath);
                    Text(sheet.Cell(row, 5), module.ImplementationStatus.ToString());
                    Text(sheet.Cell(row, 6), ProcessingFunction(call));
                    Text(sheet.Cell(row, 7), call.ProcessingVariant);
                    NullableNumber(sheet.Cell(row, 8), call.CallingBlockNumber);
                    Text(sheet.Cell(row, 9), call.CallingBlockName);
                    NullableNumber(sheet.Cell(row, 10), call.NetworkNumber);
                    Text(sheet.Cell(row, 11), call.NetworkTitle);
                    Text(sheet.Cell(row, 12), call.InOutFormalParameterName);
                    Text(sheet.Cell(row, 13), call.InOutActualExpression);
                    Text(sheet.Cell(row, 14), call.ResolvedMemberPath);
                    row++;
                }
            FinishTable(sheet, row - 1, headers.Length, "ProcessingCallsTable",
                new[] { 3, 4, 11, 13, 14 });
        }

        private static void WriteDiagnostics(IXLWorksheet sheet, AnalysisReport report)
        {
            string[] headers = { "Severity", "Code", "Message", "Subject" };
            Header(sheet, 1, headers);
            var row = 2;
            foreach (AnalysisDiagnostic diagnostic in report.Diagnostics)
            {
                Text(sheet.Cell(row, 1), diagnostic.Severity);
                Text(sheet.Cell(row, 2), diagnostic.Code);
                Text(sheet.Cell(row, 3), diagnostic.Message);
                Text(sheet.Cell(row, 4), diagnostic.Source);
                row++;
            }
            FinishTable(sheet, row - 1, headers.Length, "DiagnosticsTable",
                new[] { 3, 4 });
        }

        private static void WriteManualReview(IXLWorksheet sheet, AnalysisReport report)
        {
            string[] headers =
            {
                "Category", "Code", "Family", "Module", "Member Path",
                "Condition Type", "Condition Member", "Block", "Network",
                "Expression", "Reason"
            };
            Header(sheet, 1, headers);
            var row = 2;
            foreach (ManualReviewItem item in report.ManualReview)
            {
                Text(sheet.Cell(row, 1), item.Status.ToString());
                Text(sheet.Cell(row, 3), item.ModuleFamily);
                Text(sheet.Cell(row, 4), item.ModuleName);
                Text(sheet.Cell(row, 5), item.MemberPath);
                Text(sheet.Cell(row, 11), item.Reason);
                row++;
            }
            foreach (AnalysisBehaviourManualReviewItem item in report.BehaviourManualReview)
            {
                Text(sheet.Cell(row, 1), "Behaviour");
                Text(sheet.Cell(row, 2), item.Code);
                Text(sheet.Cell(row, 3), item.ModuleFamily);
                Text(sheet.Cell(row, 4), item.ModuleName);
                Text(sheet.Cell(row, 5), item.MemberPath);
                Text(sheet.Cell(row, 6), item.ConditionKind);
                Text(sheet.Cell(row, 7), item.ConditionMember);
                Text(sheet.Cell(row, 8), item.BlockNumber.HasValue
                    ? "FC" + item.BlockNumber.Value + " " + item.BlockName
                    : item.BlockName);
                NullableNumber(sheet.Cell(row, 9), item.NetworkNumber);
                Text(sheet.Cell(row, 10), item.Expression);
                Text(sheet.Cell(row, 11), item.Reason);
                row++;
            }
            FinishTable(sheet, row - 1, headers.Length, "ManualReviewTable",
                new[] { 5, 10, 11 });
        }

        private static AnalysisModule FindModule(
            IReadOnlyList<AnalysisModule> modules, string memberPath)
        {
            foreach (AnalysisModule module in modules)
                if (string.Equals(module.MemberPath, memberPath,
                    StringComparison.OrdinalIgnoreCase)) return module;
            return null;
        }

        private static int Count(
            AnalysisModule module, AnalysisBehaviouralConditionKind kind)
        {
            var count = 0;
            foreach (AnalysisBehaviouralCondition condition in module.BehaviouralConditions)
                if (condition.Kind == kind) count++;
            return count;
        }

        private static int CountIncomplete(AnalysisModule module)
        {
            var count = 0;
            foreach (AnalysisBehaviouralCondition condition in module.BehaviouralConditions)
                if (condition.ResolutionStatus !=
                    AnalysisBehaviouralResolutionStatus.Complete) count++;
            return count;
        }

        private static string ProcessingFunction(AnalysisCallSite call)
        {
            return call.ProcessingFunctionNumber.HasValue
                ? "FC" + call.ProcessingFunctionNumber.Value + " " + call.ProcessingFunctionName
                : call.ProcessingFunctionName;
        }

        private static string JoinDistinct(
            IReadOnlyList<AnalysisCallSite> sites, bool variants)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var values = new List<string>();
            foreach (AnalysisCallSite site in sites)
            {
                string value = variants
                    ? site.ProcessingVariant
                    : (site.CallingBlockNumber.HasValue
                        ? site.CallingBlockName + " (" + site.CallingBlockNumber.Value + ")"
                        : site.CallingBlockName);
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value)) values.Add(value);
            }
            return string.Join("; ", values);
        }

        private static void FinishTable(
            IXLWorksheet sheet, int lastDataRow, int columnCount, string tableName,
            int[] wrappedColumns)
        {
            Table(sheet, 1, lastDataRow, columnCount, tableName);
            sheet.SheetView.FreezeRows(1);
            sheet.Columns(1, columnCount).AdjustToContents();
            for (var column = 1; column <= columnCount; column++)
                LimitWidth(sheet.Column(column),
                    Array.IndexOf(wrappedColumns, column) >= 0
                        ? (column == 3 ? 70 : 90)
                        : 35);
            foreach (int column in wrappedColumns)
                sheet.Column(column).Style.Alignment.WrapText = true;
        }

        private static void Table(
            IXLWorksheet sheet, int headerRow, int lastDataRow, int columnCount,
            string tableName)
        {
            if (lastDataRow >= headerRow + 1)
                sheet.Range(headerRow, 1, lastDataRow, columnCount).CreateTable(tableName);
            else
                sheet.Range(headerRow, 1, headerRow, columnCount).SetAutoFilter();
        }

        private static void Header(IXLWorksheet sheet, int row, params string[] headers)
        {
            for (var column = 0; column < headers.Length; column++)
                Text(sheet.Cell(row, column + 1), headers[column]);
            IXLRange range = sheet.Range(row, 1, row, headers.Length);
            range.Style.Font.Bold = true;
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderColour);
        }

        private static void Section(IXLWorksheet sheet, int row, string title)
        {
            Text(sheet.Cell(row, 1), title);
            IXLRange range = sheet.Range(row, 1, row, 2);
            range.Style.Font.Bold = true;
            range.Style.Fill.BackgroundColor = XLColor.FromHtml(SectionColour);
        }

        private static void LabelValue(IXLWorksheet sheet, int row, string label, string value)
        {
            Text(sheet.Cell(row, 1), label);
            Text(sheet.Cell(row, 2), value);
        }

        private static void LabelValue(IXLWorksheet sheet, int row, string label, int value)
        {
            Text(sheet.Cell(row, 1), label);
            Number(sheet.Cell(row, 2), value);
        }

        private static void Text(IXLCell cell, string value)
        {
            if (!string.IsNullOrEmpty(value)) cell.SetValue(value);
        }

        private static void Number(IXLCell cell, int value)
        {
            cell.SetValue(value);
            cell.Style.NumberFormat.Format = "0";
        }

        private static void NullableNumber(IXLCell cell, int? value)
        {
            if (value.HasValue) Number(cell, value.Value);
        }

        private static void LimitWidth(IXLColumn column, double maximum)
        {
            if (column.Width > maximum) column.Width = maximum;
        }
    }
}
