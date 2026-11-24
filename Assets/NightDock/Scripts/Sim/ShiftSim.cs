using System;
using System.Collections.Generic;

namespace NightDock.Sim
{
    /// <summary>
    /// One overnight shift, simulated in game minutes. Pure C# (no Unity) so the rules are unit-testable.
    /// The Unity layer reads this state every frame and calls the player actions below.
    /// </summary>
    public class ShiftSim
    {
        public readonly Balance B;
        public readonly Career Career;
        public readonly int ShiftNumber;

        public float Minute { get; private set; }
        public bool Ended { get; private set; }
        public ShiftReport Report { get; private set; }

        public readonly List<Pallet> Pallets = new List<Pallet>();          // index == id
        public readonly List<InboundTrailer> Inbound = new List<InboundTrailer>(); // index == id
        public readonly int[] InboundDoorTrailer;                            // trailer id or -1
        public readonly OutboundDoor[] Doors;
        public readonly Forklift[] Forklifts;
        public readonly int[] Staging;                                       // pallet id or -1
        public readonly Dictionary<int, ForkliftJob> Jobs = new Dictionary<int, ForkliftJob>();
        public readonly List<int> Queue = new List<int>();                   // queued job ids, oldest first
        public readonly List<DispatchResult> Dispatches = new List<DispatchResult>();
        public readonly List<string> Log = new List<string>();

        readonly HashSet<int> _reservedStaging = new HashSet<int>();
        readonly Dictionary<int, int> _jobStagingSlot = new Dictionary<int, int>();
        int _nextJobId;

        public ShiftSim(Balance b, Career career)
        {
            B = b;
            Career = career;
            ShiftNumber = career.ShiftNumber;

            InboundDoorTrailer = new int[b.InboundDoors];
            for (int i = 0; i < InboundDoorTrailer.Length; i++) InboundDoorTrailer[i] = -1;

            int doorCount = career.OutboundDoors(b);
            Doors = new OutboundDoor[doorCount];
            for (int i = 0; i < doorCount; i++)
                Doors[i] = new OutboundDoor { Index = i, Route = career.DoorRoute(b, i) };

            Forklifts = new Forklift[career.Forklifts];
            for (int i = 0; i < Forklifts.Length; i++) Forklifts[i] = new Forklift { Index = i };

            Staging = new int[b.StagingSlots];
            for (int i = 0; i < Staging.Length; i++) Staging[i] = -1;

            ShiftGenerator.Generate(this, new Random(career.Seed * 7919 + ShiftNumber * 104729));
        }

        // ------------------------------------------------------------------ clock

        public static string ClockText(float minute)
        {
            int total = 22 * 60 + (int)minute;
            int h = (total / 60) % 24;
            int m = total % 60;
            string ampm = h >= 12 ? "PM" : "AM";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return h12 + ":" + m.ToString("00") + " " + ampm;
        }

        public float MoveTimeScale => Career.FastScanner ? B.ScannerSpeedup : 1f;

        /// <summary>Advance the shift by dt game minutes.</summary>
        public void Tick(float dt)
        {
            if (Ended || dt <= 0f) return;
            while (dt > 0f && !Ended)
            {
                float step = dt > 0.25f ? 0.25f : dt;
                dt -= step;
                Step(step);
            }
        }

        void Step(float dt)
        {
            Minute += dt;
            if (Minute >= B.ShiftMinutes)
            {
                Minute = B.ShiftMinutes;
                EndShift();
                return;
            }
            StepInbound(dt);
            StepOutbound(dt);
            StepForklifts(dt);
        }

        void StepInbound(float dt)
        {
            foreach (var t in Inbound)
            {
                if (t.State == InboundState.Scheduled && Minute >= t.ArrivalMinute)
                {
                    t.State = InboundState.Yard;
                    Say("Inbound trailer T" + (t.Id + 1) + " arrived with " + t.PalletIds.Count + " pallets");
                }
            }
            // Dock yard trailers in arrival order.
            foreach (var t in Inbound)
            {
                if (t.State != InboundState.Yard) continue;
                int free = Array.IndexOf(InboundDoorTrailer, -1);
                if (free < 0) break;
                InboundDoorTrailer[free] = t.Id;
                t.Door = free;
                t.State = InboundState.Docking;
                t.Timer = B.DockDelay;
            }
            foreach (var t in Inbound)
            {
                switch (t.State)
                {
                    case InboundState.Docking:
                        t.Timer -= dt;
                        if (t.Timer <= 0f)
                        {
                            t.State = InboundState.Docked;
                            Say("T" + (t.Id + 1) + " docked at I" + (t.Door + 1));
                        }
                        break;
                    case InboundState.Docked:
                        if (InboundRemaining(t) == 0)
                        {
                            t.State = InboundState.Departing;
                            t.Timer = B.EmptyDepartDelay;
                        }
                        break;
                    case InboundState.Departing:
                        t.Timer -= dt;
                        if (t.Timer <= 0f)
                        {
                            t.State = InboundState.Gone;
                            InboundDoorTrailer[t.Door] = -1;
                        }
                        break;
                }
            }
        }

        public int InboundRemaining(InboundTrailer t)
        {
            int n = 0;
            foreach (int id in t.PalletIds)
                if (Pallets[id].Loc == PalletLoc.InboundTrailer) n++;
            return n;
        }

        void StepOutbound(float dt)
        {
            foreach (var d in Doors)
            {
                if (d.TrailerPresent) continue;
                d.NewTrailerTimer -= dt;
                if (d.NewTrailerTimer <= 0f)
                {
                    d.TrailerPresent = true;
                    d.Load.Clear();
                    Say("Empty trailer spotted at " + d.Name);
                }
            }
        }

        void StepForklifts(float dt)
        {
            // Hand queued jobs to idle forklifts, oldest job first.
            foreach (var f in Forklifts)
            {
                if (f.Busy || Queue.Count == 0) continue;
                int jobId = Queue[0];
                Queue.RemoveAt(0);
                var job = Jobs[jobId];
                job.Forklift = f.Index;
                job.Duration = MoveTime(Pallets[job.PalletId].Loc, job.Dest);
                f.JobId = jobId;
            }
            foreach (var f in Forklifts)
            {
                if (!f.Busy) continue;
                var job = Jobs[f.JobId];
                job.Elapsed += dt;
                var p = Pallets[job.PalletId];
                if (!job.PickedUp && job.Elapsed >= job.Duration * B.PickupFraction)
                {
                    job.PickedUp = true;
                    job.FromLoc = p.Loc;
                    job.FromIndex = p.LocIndex;
                    RemoveFromLocation(p);
                    p.Loc = PalletLoc.OnForklift;
                    p.LocIndex = f.Index;
                }
                if (job.Elapsed >= job.Duration)
                {
                    Complete(job);
                    f.JobId = -1;
                }
            }
        }

        float MoveTime(PalletLoc from, DestKind dest)
        {
            float t;
            switch (from)
            {
                case PalletLoc.InboundTrailer: t = dest == DestKind.Door ? B.MoveInboundToDoor : B.MoveInboundToStaging; break;
                case PalletLoc.Staging: t = B.MoveStagingToDoor; break;
                default: t = dest == DestKind.Door ? B.MoveOutboundToDoor : B.MoveOutboundToStaging; break;
            }
            return t * MoveTimeScale;
        }

        void RemoveFromLocation(Pallet p)
        {
            if (p.Loc == PalletLoc.Staging) Staging[p.LocIndex] = -1;
            else if (p.Loc == PalletLoc.Outbound) Doors[p.LocIndex].Load.Remove(p.Id);
        }

        void Complete(ForkliftJob job)
        {
            var p = Pallets[job.PalletId];
            Jobs.Remove(job.Id);
            p.JobId = -1;
            if (job.Dest == DestKind.Door)
            {
                var d = Doors[job.DestDoor];
                d.Load.Add(p.Id);
                p.Loc = PalletLoc.Outbound;
                p.LocIndex = d.Index;
            }
            else
            {
                int slot = _jobStagingSlot[job.Id];
                _jobStagingSlot.Remove(job.Id);
                _reservedStaging.Remove(slot);
                Staging[slot] = p.Id;
                p.Loc = PalletLoc.Staging;
                p.LocIndex = slot;
            }
        }

        // ------------------------------------------------------------------ queries

        public bool IsAccessible(Pallet p)
        {
            switch (p.Loc)
            {
                case PalletLoc.InboundTrailer: return Inbound[p.LocIndex].State == InboundState.Docked;
                case PalletLoc.Staging: return true;
                case PalletLoc.Outbound: return Doors[p.LocIndex].TrailerPresent;
                default: return false;
            }
        }

        public int DoorWeight(int door)
        {
            int w = 0;
            foreach (int id in Doors[door].Load) w += Pallets[id].Weight;
            return w;
        }

        /// <summary>Pallets on the trailer plus pallets on their way to it.</summary>
        public int DoorCommitted(int door)
        {
            int n = Doors[door].Load.Count;
            foreach (var j in Jobs.Values)
                if (j.Dest == DestKind.Door && j.DestDoor == door) n++;
            return n;
        }

        public bool DoorBusy(int door)
        {
            foreach (var j in Jobs.Values)
            {
                if (j.Dest == DestKind.Door && j.DestDoor == door) return true;
                var p = Pallets[j.PalletId];
                if (!j.PickedUp && p.Loc == PalletLoc.Outbound && p.LocIndex == door) return true;
            }
            return false;
        }

        /// <summary>The staging slot a staging job will drop into, or -1.</summary>
        public int JobStagingSlot(int jobId) => _jobStagingSlot.TryGetValue(jobId, out int slot) ? slot : -1;

        public int FreeStagingSlots()
        {
            int n = 0;
            for (int i = 0; i < Staging.Length; i++)
                if (Staging[i] < 0 && !_reservedStaging.Contains(i)) n++;
            return n;
        }

        public int PalletsDelivered
        {
            get
            {
                int n = 0;
                foreach (var p in Pallets) if (p.Loc == PalletLoc.Delivered) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ player actions (return null on success, else a reason)

        public string RouteToDoor(int palletId, int door)
        {
            if (Ended) return "The shift is over";
            if (door < 0 || door >= Doors.Length) return "No such door";
            var p = Pallets[palletId];
            var d = Doors[door];
            if (p.JobId >= 0) return p.Label + " already has a forklift job";
            if (!IsAccessible(p)) return p.Label + " can't be reached yet";
            if (!d.TrailerPresent) return d.Name + " has no trailer right now";
            if (p.Loc == PalletLoc.Outbound && p.LocIndex == door) return p.Label + " is already on " + d.Name;
            if (DoorCommitted(door) >= B.TrailerSpots) return d.Name + " is full (" + B.TrailerSpots + " spots)";
            Enqueue(p, DestKind.Door, door, -1);
            return null;
        }

        public string RouteToStaging(int palletId)
        {
            if (Ended) return "The shift is over";
            var p = Pallets[palletId];
            if (p.JobId >= 0) return p.Label + " already has a forklift job";
            if (!IsAccessible(p)) return p.Label + " can't be reached yet";
            if (p.Loc == PalletLoc.Staging) return p.Label + " is already in staging";
            int slot = -1;
            for (int i = 0; i < Staging.Length; i++)
                if (Staging[i] < 0 && !_reservedStaging.Contains(i)) { slot = i; break; }
            if (slot < 0) return "Staging is full";
            _reservedStaging.Add(slot);
            Enqueue(p, DestKind.Staging, -1, slot);
            return null;
        }

        void Enqueue(Pallet p, DestKind dest, int door, int slot)
        {
            var job = new ForkliftJob { Id = _nextJobId++, PalletId = p.Id, Dest = dest, DestDoor = door };
            Jobs[job.Id] = job;
            Queue.Add(job.Id);
            if (slot >= 0) _jobStagingSlot[job.Id] = slot;
            p.JobId = job.Id;
        }

        /// <summary>Cancel a pallet's move if the forklift hasn't picked it up yet.</summary>
        public bool CancelJob(int palletId)
        {
            var p = Pallets[palletId];
            if (p.JobId < 0) return false;
            var job = Jobs[p.JobId];
            if (job.PickedUp) return false;
            if (job.Forklift >= 0) Forklifts[job.Forklift].JobId = -1;
            Queue.Remove(job.Id);
            Jobs.Remove(job.Id);
            if (_jobStagingSlot.TryGetValue(job.Id, out int slot))
            {
                _reservedStaging.Remove(slot);
                _jobStagingSlot.Remove(job.Id);
            }
            p.JobId = -1;
            return true;
        }

        public string CanDispatch(int door)
        {
            if (Ended) return "The shift is over";
            var d = Doors[door];
            if (!d.TrailerPresent) return d.Name + " has no trailer";
            if (d.Load.Count == 0) return d.Name + " is empty";
            if (DoorBusy(door)) return "A forklift is still working " + d.Name;
            int w = DoorWeight(door);
            if (w > B.TrailerWeightLimit) return d.Name + " is overweight (" + w.ToString("N0") + " / " + B.TrailerWeightLimit.ToString("N0") + " lb). Pull a pallet off.";
            return null;
        }

        public string Dispatch(int door)
        {
            string why = CanDispatch(door);
            if (why != null) return why;
            DoDispatch(door);
            return null;
        }

        void DoDispatch(int door)
        {
            var d = Doors[door];
            var r = Evaluate(d);
            r.Minute = Minute;
            Dispatches.Add(r);
            foreach (int id in d.Load) Pallets[id].Loc = PalletLoc.Delivered;
            d.Load.Clear();
            d.TrailerPresent = false;
            d.NewTrailerTimer = B.NewTrailerDelay;
            d.Dispatches++;
            Say(d.Name + " (" + Routes.Names[d.Route] + ") dispatched: " + r.OnTime + " on time" +
                (r.Misroutes > 0 ? ", " + r.Misroutes + " misrouted" : "") +
                (r.LoadOrderViolations > 0 ? ", " + r.LoadOrderViolations + " out of order" : ""));
        }

        /// <summary>Scores a trailer's load: misroutes, and last-stop-first load order.</summary>
        public DispatchResult Evaluate(OutboundDoor d)
        {
            var r = new DispatchResult { Door = d.Index, Weight = 0 };
            int lowestStopSoFar = int.MaxValue;
            foreach (int id in d.Load)
            {
                var p = Pallets[id];
                r.Weight += p.Weight;
                if (p.Route != d.Route) { r.Misroutes++; continue; }
                r.OnTime++;
                // Loaded after a pallet that comes off earlier -> it blocks that pallet.
                if (p.Stop > lowestStopSoFar) r.LoadOrderViolations++;
                if (p.Stop < lowestStopSoFar) lowestStopSoFar = p.Stop;
            }
            return r;
        }

        // ------------------------------------------------------------------ end of shift

        /// <summary>Ends the shift now (called automatically at 6:00 AM).</summary>
        public void EndShift()
        {
            if (Ended) return;
            Minute = Math.Max(Minute, 0f);

            // Forklifts stop; anything not on a trailer stays behind.
            foreach (var j in new List<ForkliftJob>(Jobs.Values))
            {
                var p = Pallets[j.PalletId];
                p.JobId = -1;
                if (p.Loc == PalletLoc.OnForklift) p.Loc = PalletLoc.LeftBehind;
            }
            Jobs.Clear();
            Queue.Clear();
            foreach (var f in Forklifts) f.JobId = -1;

            // Every loaded trailer leaves at 6:00. Overweight ones drop their last-loaded pallets.
            foreach (var d in Doors)
            {
                if (!d.TrailerPresent || d.Load.Count == 0) continue;
                while (DoorWeight(d.Index) > B.TrailerWeightLimit && d.Load.Count > 0)
                {
                    int last = d.Load[d.Load.Count - 1];
                    d.Load.RemoveAt(d.Load.Count - 1);
                    Pallets[last].Loc = PalletLoc.LeftBehind;
                }
                if (d.Load.Count > 0) DoDispatch(d.Index);
            }
            foreach (var p in Pallets)
                if (p.Loc != PalletLoc.Delivered) p.Loc = PalletLoc.LeftBehind;

            Ended = true;
            Report = BuildReport();
            Say("6:00 AM. Shift over.");
        }

        ShiftReport BuildReport()
        {
            var r = new ShiftReport { ShiftNumber = ShiftNumber, TotalPallets = Pallets.Count };
            foreach (var d in Dispatches)
            {
                r.OnTime += d.OnTime;
                r.Misroutes += d.Misroutes;
                r.LoadOrderViolations += d.LoadOrderViolations;
                r.Dispatches.Add(d);
            }
            foreach (var p in Pallets) if (p.Loc == PalletLoc.LeftBehind) r.LeftBehind++;
            r.Revenue = r.OnTime * B.PayPerPallet;
            r.Penalties = r.LoadOrderViolations * B.PenaltyLoadOrder + r.Misroutes * B.PenaltyMisroute + r.LeftBehind * B.PenaltyLeftBehind;
            r.Costs = Career.ShiftCost(B);
            r.Stars = Stars(r);
            return r;
        }

        public static int Stars(ShiftReport r)
        {
            if (r.TotalPallets == 0) return 5;
            float q = (r.OnTime - 0.5f * r.LoadOrderViolations) / r.TotalPallets;
            if (q >= 0.97f) return 5;
            if (q >= 0.88f) return 4;
            if (q >= 0.75f) return 3;
            if (q >= 0.5f) return 2;
            return 1;
        }

        void Say(string text)
        {
            Log.Add(ClockText(Minute) + "  " + text);
            if (Log.Count > 200) Log.RemoveAt(0);
        }
    }
}
