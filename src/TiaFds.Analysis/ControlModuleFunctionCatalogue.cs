using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleFunctionDefinition
    {
        public ControlModuleFunctionDefinition(
            string moduleFamily,
            string functionName,
            int? expectedFunctionNumber,
            string variantName,
            string expectedModuleDataType,
            IReadOnlyList<string> candidateInOutParameterNames)
        {
            ModuleFamily = moduleFamily;
            FunctionName = functionName;
            ExpectedFunctionNumber = expectedFunctionNumber;
            VariantName = variantName;
            ExpectedModuleDataType = expectedModuleDataType;
            CandidateInOutParameterNames = candidateInOutParameterNames ?? new string[0];
        }

        public string ModuleFamily { get; }
        public string FunctionName { get; }
        public int? ExpectedFunctionNumber { get; }
        public string VariantName { get; }
        public string ExpectedModuleDataType { get; }
        public IReadOnlyList<string> CandidateInOutParameterNames { get; }
    }

    public static class ControlModuleFunctionCatalogue
    {
        private static readonly IReadOnlyList<ControlModuleFunctionDefinition> Items =
            new[]
            {
                Definition("Drive", "cm.DrvType0", 50, "DrvType0", "Udt.cm.Drv"),
                Definition("Drive", "cm.DrvType1", 51, "DrvType1", "Udt.cm.Drv"),
                Definition("Drive", "cm.DrvType2", 52, "DrvType2", "Udt.cm.Drv"),
                Definition("Drive", "cm.DrvType3", 53, "DrvType3", "Udt.cm.Drv"),
                Definition("Valve", "cm.VlvType0", null, "VlvType0", "Udt.cm.Vlv"),
                Definition("Valve", "cm.VlvType1", null, "VlvType1", "Udt.cm.Vlv"),
                Definition("DigitalInput", "cm.LimType0", null, "LimType0", "Udt.cm.DI"),
                Definition("DigitalInput", "cm.LimType1", null, "LimType1", "Udt.cm.DI"),
                Definition("DigitalInput", "cm.LimType2", null, "LimType2", "Udt.cm.DI")
            };

        public static IReadOnlyList<ControlModuleFunctionDefinition> Definitions => Items;

        public static ControlModuleFunctionDefinition FindByFunctionName(string name)
        {
            foreach (ControlModuleFunctionDefinition item in Items)
                if (string.Equals(item.FunctionName, name, StringComparison.OrdinalIgnoreCase)) return item;
            return null;
        }

        private static ControlModuleFunctionDefinition Definition(
            string family, string name, int? number, string variant, string dataType)
        {
            return new ControlModuleFunctionDefinition(
                family, name, number, variant, dataType,
                new[] { "Module", "CM", "Drv", "Data", "Instance" });
        }
    }
}
