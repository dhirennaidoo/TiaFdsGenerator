namespace TiaFds.Core
{
    public sealed class PlcTagTableInfo
    {
        public PlcTagTableInfo(string name, string groupPath, int tagCount)
        {
            Name = name;
            GroupPath = groupPath;
            TagCount = tagCount;
        }

        public string Name { get; }

        public string GroupPath { get; }

        public int TagCount { get; }
    }
}
