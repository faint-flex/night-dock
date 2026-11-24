namespace NightDock.Sim
{
    /// <summary>
    /// Every tunable number for Milestone 1. Owned by the Systems Designer.
    /// Mirrors /mnt/project-files/night-dock/design-m1.md; change both together.
    /// </summary>
    public class Balance
    {
        // Clock
        public float ShiftMinutes = 480f;          // 10:00 PM -> 6:00 AM
        public float RealSecondsPerGameMinute = 1.5f;

        // Terminal
        public int InboundDoors = 4;
        public int BaseOutboundDoors = 6;
        public int MaxExtraDoors = 1;
        public int StagingSlots = 48;

        // Outbound trailers (28 ft pups)
        public int TrailerSpots = 12;
        public int TrailerWeightLimit = 22000;      // lb
        public float NewTrailerDelay = 30f;         // minutes after a dispatch until an empty trailer is spotted

        // Inbound trailers
        public float LastArrivalMinute = 390f;      // about 4:30 AM
        public int MinPalletsPerInbound = 4;
        public int MaxPalletsPerInbound = 8;
        public float DockDelay = 5f;                // backing a yard trailer into a free door
        public float EmptyDepartDelay = 3f;         // empty inbound trailer pulls away

        // Pallets
        public int MinWeight = 400;
        public int MaxWeight = 2600;
        public int StopsPerRoute = 5;

        // Forklift move times (game minutes)
        public float MoveInboundToDoor = 10f;
        public float MoveInboundToStaging = 6f;
        public float MoveStagingToDoor = 7f;
        public float MoveOutboundToStaging = 7f;
        public float MoveOutboundToDoor = 10f;
        public float PickupFraction = 0.4f;          // share of the move spent driving to the pallet
        public float ScannerSpeedup = 0.8f;          // move times x this with the faster scanner

        // Freight volume
        public int BasePallets = 40;
        public int PalletsPerShiftGrowth = 8;
        public int MaxPallets = 80;

        // Money
        public int PayPerPallet = 45;
        public int PenaltyLoadOrder = 40;
        public int PenaltyMisroute = 120;
        public int PenaltyLeftBehind = 80;
        public int CrewCostPerShift = 600;
        public int ForkliftCostPerShift = 150;
        public int StartCash = 2000;
        public int BankruptCash = -2000;

        // Upgrades
        public int StartForklifts = 2;
        public int MaxForklifts = 4;
        public int CostForklift = 2500;
        public int CostScanner = 1500;
        public int CostExtraDoor = 4000;

        public int PalletsForShift(int shiftNumber)
        {
            int n = BasePallets + PalletsPerShiftGrowth * (shiftNumber - 1);
            return n > MaxPallets ? MaxPallets : n;
        }
    }
}
