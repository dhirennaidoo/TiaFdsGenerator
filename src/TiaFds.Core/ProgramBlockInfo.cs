namespace TiaFds.Core
{
    public sealed class ProgramBlockInfo
    {
        public ProgramBlockInfo(
            string name,
            string blockType,
            int? number,
            string programmingLanguage,
            string groupPath,
            bool isConsistent)
        {
            Name = name;
            BlockType = blockType;
            Number = number;
            ProgrammingLanguage = programmingLanguage;
            GroupPath = groupPath;
            IsConsistent = isConsistent;
        }

        public string Name { get; }

        public string BlockType { get; }

        public int? Number { get; }

        public string ProgrammingLanguage { get; }

        public string GroupPath { get; }

        public bool IsConsistent { get; }
    }
}
