namespace TiaFds.Core
{
    public sealed class PlcInfo
    {
        public PlcInfo(string name, string deviceName, string deviceItemName)
        {
            Name = name;
            DeviceName = deviceName;
            DeviceItemName = deviceItemName;
        }

        public string Name { get; }

        public string DeviceName { get; }

        public string DeviceItemName { get; }
    }
}
