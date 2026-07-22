namespace TiaFds.Core
{
    public sealed class ProgramBlockCategoryCount
    {
        public ProgramBlockCategoryCount(string blockType, int count)
        {
            BlockType = blockType;
            Count = count;
        }

        public string BlockType { get; }

        public int Count { get; }
    }
}
