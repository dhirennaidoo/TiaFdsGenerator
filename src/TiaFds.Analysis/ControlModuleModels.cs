using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public enum ControlModuleDiscoveryStatus
    {
        Confirmed,
        UnexpectedContainer
    }

    public sealed class ControlModuleInfo
    {
        public ControlModuleInfo(
            string name,
            string moduleFamily,
            string containerDbName,
            int? containerDbNumber,
            string memberPath,
            string dataTypeName,
            string description,
            bool isArray,
            string arrayBounds,
            ControlModuleDiscoveryStatus status)
        {
            Name = name;
            ModuleFamily = moduleFamily;
            ContainerDbName = containerDbName;
            ContainerDbNumber = containerDbNumber;
            MemberPath = memberPath;
            DataTypeName = dataTypeName;
            Description = description;
            IsArray = isArray;
            ArrayBounds = arrayBounds;
            Status = status;
        }

        public string Name { get; }
        public string ModuleFamily { get; }
        public string ContainerDbName { get; }
        public int? ContainerDbNumber { get; }
        public string MemberPath { get; }
        public string DataTypeName { get; }
        public string Description { get; }
        public bool IsArray { get; }
        public string ArrayBounds { get; }
        public ControlModuleDiscoveryStatus Status { get; }
    }

    public sealed class ControlModuleContainerInfo
    {
        public ControlModuleContainerInfo(string moduleFamily, string blockName, int? blockNumber, bool expectedName)
        {
            ModuleFamily = moduleFamily;
            BlockName = blockName;
            BlockNumber = blockNumber;
            ExpectedName = expectedName;
        }

        public string ModuleFamily { get; }
        public string BlockName { get; }
        public int? BlockNumber { get; }
        public bool ExpectedName { get; }
    }

    public sealed class ModuleDiscoveryDiagnostic
    {
        public ModuleDiscoveryDiagnostic(
            string severity,
            string code,
            string message,
            string blockName,
            string memberPath)
        {
            Severity = severity;
            Code = code;
            Message = message;
            BlockName = blockName;
            MemberPath = memberPath;
        }

        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string BlockName { get; }
        public string MemberPath { get; }
    }

    public sealed class ControlModuleDiscoveryResult
    {
        public ControlModuleDiscoveryResult(
            IReadOnlyList<ControlModuleContainerInfo> containers,
            IReadOnlyList<ControlModuleInfo> modules,
            IReadOnlyList<ModuleDiscoveryDiagnostic> diagnostics,
            bool dataBlockStructuresAvailable)
        {
            Containers = SortContainers(containers);
            Modules = SortModules(modules);
            Diagnostics = SortDiagnostics(diagnostics);
            DataBlockStructuresAvailable = dataBlockStructuresAvailable;
        }

        public IReadOnlyList<ControlModuleContainerInfo> Containers { get; }
        public IReadOnlyList<ControlModuleInfo> Modules { get; }
        public IReadOnlyList<ModuleDiscoveryDiagnostic> Diagnostics { get; }
        public bool DataBlockStructuresAvailable { get; }

        public IReadOnlyList<ControlModuleInfo> GetModules(string moduleFamily)
        {
            var result = new List<ControlModuleInfo>();
            foreach (ControlModuleInfo module in Modules)
                if (string.IsNullOrWhiteSpace(moduleFamily) ||
                    string.Equals(module.ModuleFamily, moduleFamily, StringComparison.OrdinalIgnoreCase))
                    result.Add(module);
            return result.ToArray();
        }

        private static IReadOnlyList<ControlModuleContainerInfo> SortContainers(IReadOnlyList<ControlModuleContainerInfo> source)
        {
            var result = source == null ? new List<ControlModuleContainerInfo>() : new List<ControlModuleContainerInfo>(source);
            result.Sort((left, right) =>
            {
                int value = CompareText(left.ModuleFamily, right.ModuleFamily);
                if (value != 0) return value;
                value = CompareNullable(left.BlockNumber, right.BlockNumber);
                return value != 0 ? value : CompareText(left.BlockName, right.BlockName);
            });
            return result.ToArray();
        }

        private static IReadOnlyList<ControlModuleInfo> SortModules(IReadOnlyList<ControlModuleInfo> source)
        {
            var result = source == null ? new List<ControlModuleInfo>() : new List<ControlModuleInfo>(source);
            result.Sort((left, right) =>
            {
                int value = CompareText(left.ModuleFamily, right.ModuleFamily);
                if (value != 0) return value;
                value = CompareNullable(left.ContainerDbNumber, right.ContainerDbNumber);
                if (value != 0) return value;
                value = CompareText(left.ContainerDbName, right.ContainerDbName);
                return value != 0 ? value : CompareText(left.MemberPath, right.MemberPath);
            });
            return result.ToArray();
        }

        private static IReadOnlyList<ModuleDiscoveryDiagnostic> SortDiagnostics(IReadOnlyList<ModuleDiscoveryDiagnostic> source)
        {
            var result = source == null ? new List<ModuleDiscoveryDiagnostic>() : new List<ModuleDiscoveryDiagnostic>(source);
            result.Sort((left, right) =>
            {
                int value = CompareText(left.Code, right.Code);
                if (value != 0) return value;
                value = CompareText(left.BlockName, right.BlockName);
                if (value != 0) return value;
                value = CompareText(left.MemberPath, right.MemberPath);
                return value != 0 ? value : CompareText(left.Message, right.Message);
            });
            return result.ToArray();
        }

        private static int CompareNullable(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            return right.HasValue ? 1 : 0;
        }

        private static int CompareText(string left, string right)
        {
            int value = StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);
            return value != 0 ? value : StringComparer.Ordinal.Compare(left ?? string.Empty, right ?? string.Empty);
        }
    }
}
