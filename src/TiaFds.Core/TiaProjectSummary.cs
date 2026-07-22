using System.Collections.Generic;

namespace TiaFds.Core
{
    public sealed class TiaProjectSummary
    {
        public TiaProjectSummary(string name, string path, IReadOnlyList<string> deviceNames)
        {
            Name = name;
            Path = path;
            DeviceNames = deviceNames;
        }

        public string Name { get; }

        public string Path { get; }

        public IReadOnlyList<string> DeviceNames { get; }
    }
}
