using System;
using System.Collections.Generic;

namespace TiaFds.Analysis
{
    public sealed class ControlModuleTypeDefinition
    {
        public ControlModuleTypeDefinition(string moduleFamily, string dataTypeName, string expectedContainerDbName)
        {
            ModuleFamily = moduleFamily;
            DataTypeName = dataTypeName;
            ExpectedContainerDbName = expectedContainerDbName;
        }

        public string ModuleFamily { get; }
        public string DataTypeName { get; }
        public string ExpectedContainerDbName { get; }
    }

    public static class ControlModuleCatalogue
    {
        private static readonly IReadOnlyList<ControlModuleTypeDefinition> DefinitionsValue =
            new[]
            {
                new ControlModuleTypeDefinition("Drive", "Udt.cm.Drv", "db.cm.Drv"),
                new ControlModuleTypeDefinition("Valve", "Udt.cm.Vlv", "db.cm.Vlv"),
                new ControlModuleTypeDefinition("Speed", "Udt.cm.Spd", "db.cm.Spd"),
                new ControlModuleTypeDefinition("DigitalInput", "Udt.cm.DI", "db.cm.DI"),
                new ControlModuleTypeDefinition("AnalogueInput", "Udt.cm.AI", "db.cm.AI"),
                new ControlModuleTypeDefinition("AnalogueOutput", "Udt.cm.AO", "db.cm.AO"),
                new ControlModuleTypeDefinition("DigitalOutput", "Udt.cm.DO", "db.cm.DO")
            };

        public static IReadOnlyList<ControlModuleTypeDefinition> Definitions => DefinitionsValue;

        public static ControlModuleTypeDefinition FindByDataType(string dataTypeName)
        {
            foreach (ControlModuleTypeDefinition definition in DefinitionsValue)
                if (string.Equals(definition.DataTypeName, dataTypeName, StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }

        public static ControlModuleTypeDefinition FindByFamily(string moduleFamily)
        {
            foreach (ControlModuleTypeDefinition definition in DefinitionsValue)
                if (string.Equals(definition.ModuleFamily, moduleFamily, StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }

        public static ControlModuleTypeDefinition FindByExpectedContainer(string blockName)
        {
            foreach (ControlModuleTypeDefinition definition in DefinitionsValue)
                if (string.Equals(definition.ExpectedContainerDbName, blockName, StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }
    }
}
