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
            : this(moduleFamily, functionName, expectedFunctionNumber, variantName,
                expectedModuleDataType, candidateInOutParameterNames, false)
        {
        }

        public ControlModuleFunctionDefinition(
            string moduleFamily,
            string functionName,
            int? expectedFunctionNumber,
            string variantName,
            string expectedModuleDataType,
            IReadOnlyList<string> candidateInOutParameterNames,
            bool driveSemanticsConfirmed)
        {
            ModuleFamily = moduleFamily;
            FunctionName = functionName;
            ExpectedFunctionNumber = expectedFunctionNumber;
            VariantName = variantName;
            ExpectedModuleDataType = expectedModuleDataType;
            CandidateInOutParameterNames = candidateInOutParameterNames ?? new string[0];
            DriveSemanticsConfirmed = driveSemanticsConfirmed;
        }

        public string ModuleFamily { get; }
        public string FunctionName { get; }
        public int? ExpectedFunctionNumber { get; }
        public string VariantName { get; }
        public string ExpectedModuleDataType { get; }
        public IReadOnlyList<string> CandidateInOutParameterNames { get; }

        // True when the Drive ILK/CR polarity rules (DRV_ILK_ACTIVE_HIGH_PERMISSIVE,
        // DRV_CR_ACTIVE_HIGH_ENABLE) have been confirmed for this processing FC.
        public bool DriveSemanticsConfirmed { get; }
    }

    public static class ControlModuleFunctionCatalogue
    {
        public const string ClassicFunctionPrefix = "FnBlk.cm.";

        private static readonly IReadOnlyList<ControlModuleFunctionDefinition> Items =
            new[]
            {
                // TIA Portal library generation: one FC per processing variant.
                ConfirmedDrive("cm.DrvType0", 50, "DrvType0"),
                ConfirmedDrive("cm.DrvType1", 51, "DrvType1"),
                ConfirmedDrive("cm.DrvType2", 52, "DrvType2"),
                ConfirmedDrive("cm.DrvType3", 53, "DrvType3"),
                Definition("Valve", "cm.VlvType0", null, "VlvType0", "Udt.cm.Vlv"),
                Definition("Valve", "cm.VlvType1", null, "VlvType1", "Udt.cm.Vlv"),
                Definition("DigitalInput", "cm.LimType0", null, "LimType0", "Udt.cm.DI"),
                Definition("DigitalInput", "cm.LimType1", null, "LimType1", "Udt.cm.DI"),
                Definition("DigitalInput", "cm.LimType2", null, "LimType2", "Udt.cm.DI"),
                Definition("AnalogueInput", "cm.AI", null, "AI", "Udt.cm.AI"),
                Definition("AnalogueOutput", "cm.AO", null, "AO", "Udt.cm.AO", "AOut"),
                Definition("DigitalOutput", "cm.DOType0", null, "DOType0", "Udt.cm.DO", "Ctrl"),
                Definition("DigitalOutput", "cm.DOType1", null, "DOType1", "Udt.cm.DO", "Ctrl"),
                Definition("Speed", "cm.SpdType0", null, "SpdType0", "Udt.cm.Spd"),
                Definition("Speed", "cm.SpdType1", null, "SpdType1", "Udt.cm.Spd"),

                // STEP 7 Classic library generation: one FC per family. FC numbers are not
                // validated (FnBlk.cm.Drv is FC50 in A-Mill, the number TIA expects for
                // cm.DrvType0). The drive type is a runtime INT (Drv.DrvType), not a separate
                // FC, and FnBlk.cm.Drv gates CR differently from DrvType*, so the Drive
                // polarity rules are not applied until Classic rules are confirmed.
                Definition("Drive", "FnBlk.cm.Drv", null, "FnBlk.cm.Drv", "Udt.cm.Drv"),
                Definition("Valve", "FnBlk.cm.Vlv", null, "FnBlk.cm.Vlv", "Udt.cm.Vlv", "Vlv"),
                Definition("DigitalInput", "FnBlk.cm.DI", null, "FnBlk.cm.DI", "Udt.cm.DI", "Lim"),
                Definition("AnalogueInput", "FnBlk.cm.AI", null, "FnBlk.cm.AI", "Udt.cm.AI"),
                Definition("AnalogueOutput", "FnBlk.cm.AO", null, "FnBlk.cm.AO", "Udt.cm.AO", "AOut"),
                Definition("DigitalOutput", "FnBlk.cm.DO", null, "FnBlk.cm.DO", "Udt.cm.DO", "Ctrl"),
                Definition("Speed", "FnBlk.cm.Spd", null, "FnBlk.cm.Spd", "Udt.cm.Spd", "Spd"),
                // Raw-value processing of the generic analogue module.
                Definition("Analogue", "FnBlkRAW.cm.AO", null, "FnBlkRAW.cm.AO", "Udt.cm.An", "AOut"),
                Definition("Analogue", "FnBlk.RAWScaling", null, "FnBlk.RAWScaling", "Udt.cm.An", "AIn")
            };

        public static IReadOnlyList<ControlModuleFunctionDefinition> Definitions => Items;

        public static ControlModuleFunctionDefinition FindByFunctionName(string name)
        {
            foreach (ControlModuleFunctionDefinition item in Items)
                if (string.Equals(item.FunctionName, name, StringComparison.OrdinalIgnoreCase)) return item;
            return null;
        }

        // Names that look like a processing FC of either library generation but have no
        // catalogue entry (reported as CM109 rather than silently ignored).
        public static bool LooksLikeProcessingFunction(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.StartsWith("cm.", StringComparison.OrdinalIgnoreCase) &&
                name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return name.StartsWith(ClassicFunctionPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static ControlModuleFunctionDefinition ConfirmedDrive(string name, int number, string variant)
        {
            return new ControlModuleFunctionDefinition(
                "Drive", name, number, variant, "Udt.cm.Drv", CandidateNames(null), true);
        }

        private static ControlModuleFunctionDefinition Definition(
            string family, string name, int? number, string variant, string dataType)
        {
            return Definition(family, name, number, variant, dataType, null);
        }

        private static ControlModuleFunctionDefinition Definition(
            string family, string name, int? number, string variant, string dataType,
            string additionalCandidateName)
        {
            return new ControlModuleFunctionDefinition(
                family, name, number, variant, dataType,
                CandidateNames(additionalCandidateName));
        }

        private static string[] CandidateNames(string additionalCandidateName)
        {
            var names = new List<string> { "Module", "CM", "Drv", "Data", "Instance" };
            if (!string.IsNullOrWhiteSpace(additionalCandidateName)) names.Add(additionalCandidateName);
            return names.ToArray();
        }
    }
}
