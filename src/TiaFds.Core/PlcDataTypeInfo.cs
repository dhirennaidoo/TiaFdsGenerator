namespace TiaFds.Core
{
    public sealed class PlcDataTypeInfo
    {
        public PlcDataTypeInfo(string name, string groupPath)
        {
            Name = name;
            GroupPath = groupPath;
        }

        public string Name { get; }

        public string GroupPath { get; }
    }
}
