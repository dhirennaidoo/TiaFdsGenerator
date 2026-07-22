using System.Collections.Generic;

namespace TiaFds.Core
{
    public sealed class TiaProjectSummary
    {
        public TiaProjectSummary(
            string name,
            string path,
            IReadOnlyList<string> deviceNames,
            IReadOnlyList<PlcInfo> plcs,
            IReadOnlyList<HardwareDeviceInfo> hardwareDevices)
        {
            Name = name;
            Path = path;
            DeviceNames = deviceNames;
            Plcs = plcs;
            HardwareDevices = hardwareDevices;
        }

        public string Name { get; }

        public string Path { get; }

        public IReadOnlyList<string> DeviceNames { get; }

        public IReadOnlyList<PlcInfo> Plcs { get; }

        public IReadOnlyList<HardwareDeviceInfo> HardwareDevices { get; }
    }
}
