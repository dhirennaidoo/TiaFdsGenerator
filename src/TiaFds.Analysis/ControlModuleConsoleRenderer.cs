using System;
using System.IO;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleConsoleRenderer
    {
        public void PrintSummary(TextWriter writer, ControlModuleDiscoveryResult result)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (result == null) throw new ArgumentNullException(nameof(result));

            writer.WriteLine("Advansys control-module discovery");
            writer.WriteLine();
            writer.WriteLine("Containers:");
            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
            {
                ControlModuleContainerInfo container = FindContainer(result, definition.ModuleFamily);
                writer.WriteLine("  {0,-16} {1}", definition.ModuleFamily + ":",
                    container == null ? "Not found" : FormatBlock(container));
            }

            writer.WriteLine();
            writer.WriteLine("Modules:");
            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
                writer.WriteLine("  {0,-16} {1,5}", definition.ModuleFamily + ":", CountModules(result, definition.ModuleFamily));

            int warnings = 0;
            int errors = 0;
            foreach (ModuleDiscoveryDiagnostic diagnostic in result.Diagnostics)
            {
                if (string.Equals(diagnostic.Severity, "Error", StringComparison.OrdinalIgnoreCase)) errors++;
                else warnings++;
            }
            writer.WriteLine();
            writer.WriteLine("Diagnostics:");
            writer.WriteLine("  Warnings:        {0,5}", warnings);
            writer.WriteLine("  Errors:          {0,5}", errors);
        }

        public void PrintDetails(TextWriter writer, ControlModuleDiscoveryResult result, string moduleFamily)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (result == null) throw new ArgumentNullException(nameof(result));

            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
            {
                if (!string.IsNullOrWhiteSpace(moduleFamily) &&
                    !string.Equals(moduleFamily, definition.ModuleFamily, StringComparison.OrdinalIgnoreCase))
                    continue;

                writer.WriteLine();
                writer.WriteLine("{0} modules", definition.ModuleFamily);
                var modules = result.GetModules(definition.ModuleFamily);
                int nameWidth = CalculateWidth(modules, module => module.Name, 12, 24);
                int descriptionWidth = CalculateWidth(modules, module => module.Description, 20, 60);
                int containerWidth = CalculateWidth(modules, module => module.ContainerDbName, 12, 20);
                int dataTypeWidth = CalculateWidth(modules, module => module.DataTypeName, 12, 20);
                writer.WriteLine(
                    "{0,-" + nameWidth + "} {1,-" + descriptionWidth + "} {2,-" + containerWidth +
                    "} {3,-" + dataTypeWidth + "} {4}",
                    "Name",
                    "Description",
                    "Container",
                    "Datatype",
                    "Member path");
                if (modules.Count == 0) writer.WriteLine("- None found");
                foreach (ControlModuleInfo module in modules)
                {
                    string[] descriptionLines = Wrap(module.Description, descriptionWidth);
                    writer.WriteLine(
                        "{0,-" + nameWidth + "} {1,-" + descriptionWidth + "} {2,-" + containerWidth +
                        "} {3,-" + dataTypeWidth + "} {4}",
                        module.Name,
                        descriptionLines[0],
                        module.ContainerDbName,
                        module.DataTypeName,
                        module.MemberPath);
                    for (var index = 1; index < descriptionLines.Length; index++)
                    {
                        writer.WriteLine(
                            "{0,-" + nameWidth + "} {1,-" + descriptionWidth + "} {2,-" + containerWidth +
                            "} {3,-" + dataTypeWidth + "}",
                            string.Empty,
                            descriptionLines[index],
                            string.Empty,
                            string.Empty);
                    }
                }
            }

            if (result.Diagnostics.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("Module discovery diagnostics:");
                foreach (ModuleDiscoveryDiagnostic diagnostic in result.Diagnostics)
                    writer.WriteLine("- {0} | {1} | {2}", diagnostic.Severity, diagnostic.Code, diagnostic.Message);
            }
        }

        private static string FormatBlock(ControlModuleContainerInfo container)
        {
            return container.BlockNumber.HasValue
                ? string.Format("{0} (DB{1})", container.BlockName, container.BlockNumber.Value)
                : container.BlockName;
        }

        private static ControlModuleContainerInfo FindContainer(ControlModuleDiscoveryResult result, string family)
        {
            foreach (ControlModuleContainerInfo container in result.Containers)
                if (string.Equals(container.ModuleFamily, family, StringComparison.OrdinalIgnoreCase) && container.ExpectedName)
                    return container;
            foreach (ControlModuleContainerInfo container in result.Containers)
                if (string.Equals(container.ModuleFamily, family, StringComparison.OrdinalIgnoreCase)) return container;
            return null;
        }

        private static int CountModules(ControlModuleDiscoveryResult result, string family)
        {
            int count = 0;
            foreach (ControlModuleInfo module in result.Modules)
                if (string.Equals(module.ModuleFamily, family, StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }

        private static int CalculateWidth(
            System.Collections.Generic.IReadOnlyList<ControlModuleInfo> modules,
            Func<ControlModuleInfo, string> select,
            int minimum,
            int maximum)
        {
            int width = minimum;
            foreach (ControlModuleInfo module in modules)
            {
                string value = select(module) ?? string.Empty;
                if (value.Length > width) width = value.Length;
            }
            return Math.Min(width, maximum);
        }

        private static string[] Wrap(string value, int width)
        {
            string text = value ?? string.Empty;
            if (text.Length == 0) return new[] { string.Empty };

            int lineCount = (text.Length + width - 1) / width;
            var lines = new string[lineCount];
            for (var index = 0; index < lineCount; index++)
            {
                int start = index * width;
                lines[index] = text.Substring(start, Math.Min(width, text.Length - start));
            }
            return lines;
        }
    }
}
