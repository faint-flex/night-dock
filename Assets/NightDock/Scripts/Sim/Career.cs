using System;
using System.Collections.Generic;

namespace NightDock.Sim
{
    /// <summary>Everything that carries over between shifts. Saved as JSON after each shift report.</summary>
    [Serializable]
    public class Career
    {
        public int Version = 1;
        public int Seed;
        public int ShiftNumber = 1;      // the next shift to play
        public int Cash;
        public int Forklifts;
        public bool FastScanner;
        public List<int> ExtraDoorRoutes = new List<int>();
        public bool Bankrupt;

        public int LastProfit;
        public int LastStars;

        public static Career New(Balance b, int seed)
        {
            return new Career { Seed = seed, Cash = b.StartCash, Forklifts = b.StartForklifts };
        }

        public int OutboundDoors(Balance b) => b.BaseOutboundDoors + ExtraDoorRoutes.Count;

        public int DoorRoute(Balance b, int door)
        {
            if (door < b.BaseOutboundDoors) return door / 2; // O1-O2 North, O3-O4 Valley, O5-O6 Airport
            return ExtraDoorRoutes[door - b.BaseOutboundDoors];
        }

        public int ShiftCost(Balance b) => b.CrewCostPerShift + b.ForkliftCostPerShift * Forklifts;

        public bool CanBuyForklift(Balance b) => !Bankrupt && Forklifts < b.MaxForklifts && Cash >= b.CostForklift;
        public bool CanBuyScanner(Balance b) => !Bankrupt && !FastScanner && Cash >= b.CostScanner;
        public bool CanBuyDoor(Balance b) => !Bankrupt && ExtraDoorRoutes.Count < b.MaxExtraDoors && Cash >= b.CostExtraDoor;

        public bool BuyForklift(Balance b)
        {
            if (!CanBuyForklift(b)) return false;
            Cash -= b.CostForklift; Forklifts++; return true;
        }

        public bool BuyScanner(Balance b)
        {
            if (!CanBuyScanner(b)) return false;
            Cash -= b.CostScanner; FastScanner = true; return true;
        }

        public bool BuyDoor(Balance b, int route)
        {
            if (!CanBuyDoor(b) || route < 0 || route >= Routes.Count) return false;
            Cash -= b.CostExtraDoor; ExtraDoorRoutes.Add(route); return true;
        }

        /// <summary>Applies a finished shift's report to the career.</summary>
        public void Apply(Balance b, ShiftReport r)
        {
            Cash += r.Profit;
            LastProfit = r.Profit;
            LastStars = r.Stars;
            ShiftNumber++;
            if (Cash < b.BankruptCash) Bankrupt = true;
        }
    }
}
