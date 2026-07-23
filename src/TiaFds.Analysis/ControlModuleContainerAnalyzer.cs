using System;
using System.Collections.Generic;
using TiaFds.Core;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleContainerAnalyzer
    {
        public ControlModuleDiscoveryResult Analyze(EngineeringSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            PlcInventory inventory = snapshot.Project.Inventory;
            var containers = new List<ControlModuleContainerInfo>();
            var modules = new List<ControlModuleInfo>();
            var diagnostics = new List<ModuleDiscoveryDiagnostic>();

            if (!inventory.DataBlockStructuresIncluded)
            {
                diagnostics.Add(new ModuleDiscoveryDiagnostic(
                    "Error",
                    "CM001_DB_STRUCTURES_NOT_EXTRACTED",
                    "Data-block structures were not included in this snapshot.",
                    null,
                    null));
                return new ControlModuleDiscoveryResult(containers, modules, diagnostics, false);
            }

            var containerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var memberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expectedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataBlockStructureInfo structure in inventory.DataBlockStructures)
            {
                ControlModuleTypeDefinition namedDefinition = ControlModuleCatalogue.FindByExpectedContainer(structure.BlockName);
                if (namedDefinition != null)
                {
                    expectedContainers.Add(namedDefinition.ModuleFamily);
                    AddContainer(containers, containerKeys, namedDefinition, structure, true);
                }

                foreach (InventoryDiagnostic extractionDiagnostic in structure.Diagnostics)
                {
                    diagnostics.Add(new ModuleDiscoveryDiagnostic(
                        extractionDiagnostic.Severity,
                        "CM007_DB_STRUCTURE_EXTRACTION_FAILED",
                        extractionDiagnostic.Message,
                        structure.BlockName,
                        null));
                }

                foreach (DataBlockMemberInfo member in structure.Members)
                {
                    bool found = AnalyzeMember(
                        structure,
                        member,
                        containers,
                        modules,
                        diagnostics,
                        containerKeys,
                        memberPaths);
                    if (namedDefinition != null && !found)
                    {
                        diagnostics.Add(new ModuleDiscoveryDiagnostic(
                            "Warning",
                            "CM003_UNRECOGNISED_MEMBER_TYPE",
                            "Member datatype '" + member.DataTypeName + "' is not a recognised Advansys control-module type.",
                            structure.BlockName,
                            member.MemberPath));
                    }
                }
            }

            foreach (ControlModuleTypeDefinition definition in ControlModuleCatalogue.Definitions)
            {
                if (!expectedContainers.Contains(definition.ModuleFamily))
                {
                    diagnostics.Add(new ModuleDiscoveryDiagnostic(
                        "Warning",
                        "CM002_EXPECTED_CONTAINER_NOT_FOUND",
                        "Expected container '" + definition.ExpectedContainerDbName + "' was not found.",
                        definition.ExpectedContainerDbName,
                        null));
                }
            }

            return new ControlModuleDiscoveryResult(containers, modules, diagnostics, true);
        }

        private static bool AnalyzeMember(
            DataBlockStructureInfo structure,
            DataBlockMemberInfo member,
            ICollection<ControlModuleContainerInfo> containers,
            ICollection<ControlModuleInfo> modules,
            ICollection<ModuleDiscoveryDiagnostic> diagnostics,
            ISet<string> containerKeys,
            ISet<string> memberPaths)
        {
            if (!memberPaths.Add(member.MemberPath ?? string.Empty))
            {
                diagnostics.Add(new ModuleDiscoveryDiagnostic(
                    "Warning",
                    "CM005_DUPLICATE_MEMBER_PATH",
                    "Duplicate module member path was ignored.",
                    structure.BlockName,
                    member.MemberPath));
                return false;
            }

            ControlModuleTypeDefinition definition = ControlModuleCatalogue.FindByDataType(member.DataTypeName);
            if (definition != null)
            {
                bool expected = string.Equals(
                    structure.BlockName,
                    definition.ExpectedContainerDbName,
                    StringComparison.OrdinalIgnoreCase);
                AddContainer(containers, containerKeys, definition, structure, expected);
                modules.Add(new ControlModuleInfo(
                    member.Name,
                    definition.ModuleFamily,
                    structure.BlockName,
                    structure.BlockNumber,
                    member.MemberPath,
                    member.DataTypeName,
                    member.Comment,
                    member.IsArray,
                    member.ArrayBounds,
                    expected ? ControlModuleDiscoveryStatus.Confirmed : ControlModuleDiscoveryStatus.UnexpectedContainer));

                if (!expected)
                {
                    diagnostics.Add(new ModuleDiscoveryDiagnostic(
                        "Warning",
                        "CM004_MODULE_IN_UNEXPECTED_CONTAINER",
                        "Module datatype '" + member.DataTypeName + "' was found outside expected container '" + definition.ExpectedContainerDbName + "'.",
                        structure.BlockName,
                        member.MemberPath));
                }

                if (member.IsArray)
                {
                    diagnostics.Add(new ModuleDiscoveryDiagnostic(
                        "Warning",
                        "CM006_ARRAY_NOT_EXPANDED",
                        "Array declaration was retained as one module collection and was not expanded into indexes.",
                        structure.BlockName,
                        member.MemberPath));
                }

                return true;
            }

            bool descendantFound = false;
            foreach (DataBlockMemberInfo child in member.Children)
            {
                if (AnalyzeMember(
                    structure,
                    child,
                    containers,
                    modules,
                    diagnostics,
                    containerKeys,
                    memberPaths))
                    descendantFound = true;
            }
            return descendantFound;
        }

        private static void AddContainer(
            ICollection<ControlModuleContainerInfo> containers,
            ISet<string> keys,
            ControlModuleTypeDefinition definition,
            DataBlockStructureInfo structure,
            bool expected)
        {
            string key = definition.ModuleFamily + "\u001f" + structure.GroupPath + "\u001f" + structure.BlockName;
            if (keys.Add(key))
                containers.Add(new ControlModuleContainerInfo(
                    definition.ModuleFamily,
                    structure.BlockName,
                    structure.BlockNumber,
                    expected));
        }
    }
}
