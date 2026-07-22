using System.Collections.Generic;

namespace TiaFds.Core
{
    public sealed class HardwareDeviceInfo
    {
        public HardwareDeviceInfo(string name, IReadOnlyList<HardwareItemInfo> items)
        {
            Name = name;
            Items = items;
        }

        public string Name { get; }

        public IReadOnlyList<HardwareItemInfo> Items { get; }
    }

    public sealed class HardwareItemInfo
    {
        public HardwareItemInfo(
            string name,
            HardwareSoftwareInfo software,
            IReadOnlyList<HardwareItemInfo> items)
        {
            Name = name;
            Software = software;
            Items = items;
        }

        public string Name { get; }

        public HardwareSoftwareInfo Software { get; }

        public IReadOnlyList<HardwareItemInfo> Items { get; }
    }

    public sealed class HardwareSoftwareInfo
    {
        public HardwareSoftwareInfo(string name, string typeName)
        {
            Name = name;
            TypeName = typeName;
        }

        public string Name { get; }

        public string TypeName { get; }
    }
}
