using TiaFds.Core;

namespace TiaFds.Step7Classic
{
    public sealed class Step7ClassicReadResult
    {
        public Step7ClassicReadResult(
            TiaProjectSummary summary,
            Step7ProgramIdentity selectedProgram,
            PlcInventory selectedPlcInventory)
        {
            Summary = summary;
            SelectedProgram = selectedProgram;
            SelectedPlc = selectedProgram == null ? null : Step7InventoryMapper.ToPlcInfo(selectedProgram);
            SelectedPlcInventory = selectedPlcInventory;
        }

        public TiaProjectSummary Summary { get; }
        public Step7ProgramIdentity SelectedProgram { get; }
        public PlcInfo SelectedPlc { get; }
        public PlcInventory SelectedPlcInventory { get; }
    }

    public sealed class Step7ClassicProjectReader
    {
        // Reads the project structure and, when plcName is given, the selected program's
        // inventory. Throws Step7ProgramNotFoundException when plcName matches no program.
        public Step7ClassicReadResult Read(string inputPath, string plcName, bool includeDataBlockStructures)
        {
            return Read(inputPath, plcName, includeDataBlockStructures, false);
        }

        public Step7ClassicReadResult Read(
            string inputPath, string plcName, bool includeDataBlockStructures, bool includeBlockCalls)
        {
            using (Step7LibraryProjectLoader loader = Step7LibraryProjectLoader.Open(inputPath))
            {
                var mapper = new Step7InventoryMapper();
                TiaProjectSummary summary = mapper.MapSummary(loader.Model);
                if (string.IsNullOrWhiteSpace(plcName))
                    return new Step7ClassicReadResult(summary, null, null);

                Step7ProgramIdentity program = Step7ProgramSelector.Select(loader.Model.Programs, plcName);
                if (program == null)
                    throw new Step7ProgramNotFoundException(plcName, Step7ProgramSelector.Describe(loader.Model.Programs));

                Step7ProgramModel model = loader.LoadProgram(program, includeDataBlockStructures, includeBlockCalls);
                return new Step7ClassicReadResult(summary, program, mapper.MapInventory(model));
            }
        }
    }

    public sealed class Step7ProgramNotFoundException : System.Exception
    {
        public Step7ProgramNotFoundException(string requested, string available)
            : base("S7 program '" + requested + "' was not found. Discovered programs: " + available)
        {
        }
    }
}
