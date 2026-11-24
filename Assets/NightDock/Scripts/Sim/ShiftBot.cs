using System;
using System.Linq;

namespace NightDock.Sim
{
    /// <summary>
    /// Simple autopilot used for balance checks and the build's screenshot run.
    /// careful: a pallet goes on a trailer only when nothing with a higher stop for its route is still to come;
    /// everything else waits in staging, and late in the shift everything is flushed onto trailers.
    /// sloppy: everything straight to its route's door. It sees all of tonight's freight (a human can't).
    /// </summary>
    public static class ShiftBot
    {
        public static void Think(ShiftSim s, bool careful)
        {
            var b = s.B;
            // Dispatch full trailers, or everything near the end.
            foreach (var d in s.Doors)
            {
                if (!d.TrailerPresent || d.Load.Count == 0 || s.DoorBusy(d.Index)) continue;
                bool full = d.Load.Count >= b.TrailerSpots || s.DoorWeight(d.Index) > b.TrailerWeightLimit - 1500;
                bool late = s.Minute > b.ShiftMinutes - 12;
                if (full || late)
                {
                    if (s.DoorWeight(d.Index) > b.TrailerWeightLimit)
                    {
                        s.RouteToStaging(d.Load[d.Load.Count - 1]);
                        continue;
                    }
                    s.Dispatch(d.Index);
                }
            }

            bool noMoreArrivals = s.Inbound.All(t => t.State != InboundState.Scheduled && t.State != InboundState.Yard);
            // Don't flood the queue: keep it about one job ahead per forklift.
            int budget = s.Forklifts.Length * 2 - s.Queue.Count;

            var candidates = s.Pallets.Where(p => p.JobId < 0 && s.IsAccessible(p) && p.Loc != PalletLoc.Outbound)
                .OrderByDescending(p => p.Stop).ToList();

            foreach (var p in candidates)
            {
                if (budget <= 0) break;
                int door = BestDoor(s, p, careful, out bool clean);
                if (!careful)
                {
                    if (door >= 0 && p.Loc == PalletLoc.InboundTrailer) { if (s.RouteToDoor(p.Id, door) == null) budget--; }
                    continue;
                }
                // Careful: a pallet goes on a trailer only when nothing with a higher stop for its route is still
                // to come (so it can never block). Everything else waits in staging. Late in the shift, flush.
                bool safe = clean && p.Stop >= HighestUnloadedStop(s, p.Route, p.Id);
                bool flush = s.Minute > b.ShiftMinutes - 75;
                if (door >= 0 && (safe || flush))
                {
                    if (s.RouteToDoor(p.Id, door) == null) budget--;
                }
                else if (p.Loc == PalletLoc.InboundTrailer)
                {
                    if (s.RouteToStaging(p.Id) == null) budget--;
                    else if (door >= 0 && s.RouteToDoor(p.Id, door) == null) budget--;
                }
            }
        }

        /// <summary>Highest stop on this route among pallets not yet on a trailer (or headed to one), excluding one pallet.</summary>
        static int HighestUnloadedStop(ShiftSim s, int route, int exclude)
        {
            int hi = 0;
            foreach (var p in s.Pallets)
            {
                if (p.Id == exclude || p.Route != route) continue;
                if (p.Loc == PalletLoc.Outbound || p.Loc == PalletLoc.Delivered || p.Loc == PalletLoc.LeftBehind) continue;
                if (p.JobId >= 0 && s.Jobs[p.JobId].Dest == DestKind.Door) continue;
                hi = Math.Max(hi, p.Stop);
            }
            return hi;
        }

        /// <summary>The door for this pallet's route with room and weight to spare; prefers one where it loads in order.</summary>
        static int BestDoor(ShiftSim s, Pallet p, bool careful, out bool clean)
        {
            clean = false;
            int best = -1;
            foreach (var d in s.Doors)
            {
                if (d.Route != p.Route || !d.TrailerPresent) continue;
                if (s.DoorCommitted(d.Index) >= s.B.TrailerSpots) continue;
                if (s.DoorWeight(d.Index) + CommittedWeight(s, d.Index) + p.Weight > s.B.TrailerWeightLimit) continue;
                int lowest = LowestCommittedStop(s, d.Index);
                bool ok = p.Stop <= lowest;
                if (ok && !clean) { best = d.Index; clean = true; }
                else if (best < 0) best = d.Index;
            }
            return best;
        }

        static int CommittedWeight(ShiftSim s, int door)
        {
            int w = 0;
            foreach (var j in s.Jobs.Values)
                if (j.Dest == DestKind.Door && j.DestDoor == door) w += s.Pallets[j.PalletId].Weight;
            return w;
        }

        static int LowestCommittedStop(ShiftSim s, int door)
        {
            int lowest = int.MaxValue;
            foreach (int id in s.Doors[door].Load) lowest = Math.Min(lowest, s.Pallets[id].Stop);
            foreach (var j in s.Jobs.Values)
                if (j.Dest == DestKind.Door && j.DestDoor == door) lowest = Math.Min(lowest, s.Pallets[j.PalletId].Stop);
            return lowest;
        }
    }
}
