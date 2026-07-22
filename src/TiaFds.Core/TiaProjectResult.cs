namespace TiaFds.Core
{
    public sealed class TiaProjectResult
    {
        public TiaProjectResult(TiaProjectSummary summary, PlcInventory selectedPlcInventory)
        {
            Summary = summary;
            SelectedPlcInventory = selectedPlcInventory;
        }

        public TiaProjectSummary Summary { get; }

        public PlcInventory SelectedPlcInventory { get; }
    }
}
