using System;
using System.Collections.Generic;

namespace NightDock.Sim
{
    /// <summary>Builds tonight's freight: pallets and the inbound trailers that carry them.</summary>
    public static class ShiftGenerator
    {
        public static void Generate(ShiftSim sim, Random rng)
        {
            var b = sim.B;
            int total = b.PalletsForShift(sim.ShiftNumber);

            for (int i = 0; i < total; i++)
            {
                int route = rng.Next(Routes.Count);
                int stop = 1 + rng.Next(b.StopsPerRoute);
                int weight = (b.MinWeight + rng.Next(b.MaxWeight - b.MinWeight + 1)) / 10 * 10;
                sim.Pallets.Add(new Pallet
                {
                    Id = i,
                    Pro = (100000000 + rng.Next(900000000)).ToString(),
                    Route = route,
                    Stop = stop,
                    Weight = weight,
                    Consignee = Routes.Consignees[route][(stop - 1) % Routes.Consignees[route].Length],
                    Loc = PalletLoc.InboundTrailer,
                });
            }

            // Split pallets into trailer loads of 4-8.
            var sizes = new List<int>();
            int left = total;
            while (left > 0)
            {
                int n = b.MinPalletsPerInbound + rng.Next(b.MaxPalletsPerInbound - b.MinPalletsPerInbound + 1);
                if (n > left) n = left;
                sizes.Add(n);
                left -= n;
            }

            // Arrivals: the first trailer is waiting at 10:00 PM, the rest spread out to about 4:00 AM.
            int k = sizes.Count;
            int next = 0;
            for (int t = 0; t < k; t++)
            {
                float arrival;
                if (t == 0) arrival = 0f;
                else
                {
                    float frac = (float)t / Math.Max(1, k - 1);
                    arrival = b.LastArrivalMinute * frac + (float)(rng.NextDouble() * 20.0 - 10.0);
                    if (arrival < 1f) arrival = 1f;
                    if (arrival > b.LastArrivalMinute) arrival = b.LastArrivalMinute;
                }
                var trailer = new InboundTrailer { Id = t, ArrivalMinute = arrival, State = InboundState.Scheduled };
                for (int i = 0; i < sizes[t]; i++)
                {
                    var p = sim.Pallets[next++];
                    p.LocIndex = t;
                    trailer.PalletIds.Add(p.Id);
                }
                sim.Inbound.Add(trailer);
            }

            // Keep trailer ids in arrival order (jitter can swap neighbours).
            sim.Inbound.Sort((x, y) => x.ArrivalMinute.CompareTo(y.ArrivalMinute));
            for (int t = 0; t < sim.Inbound.Count; t++)
            {
                sim.Inbound[t].Id = t;
                foreach (int id in sim.Inbound[t].PalletIds) sim.Pallets[id].LocIndex = t;
            }
        }
    }
}
