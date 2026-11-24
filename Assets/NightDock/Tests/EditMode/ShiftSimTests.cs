using System.Linq;
using NightDock.Sim;
using NUnit.Framework;

namespace NightDock.Tests
{
    public class ShiftSimTests
    {
        Balance B;
        Career C;

        [SetUp]
        public void SetUp()
        {
            B = new Balance();
            C = Career.New(B, 1234);
        }

        ShiftSim NewSim() => new ShiftSim(B, C);

        /// <summary>Runs until the first inbound trailer is docked.</summary>
        static void UntilDocked(ShiftSim s)
        {
            int guard = 0;
            while (s.Inbound[0].State != InboundState.Docked && guard++ < 1000) s.Tick(0.5f);
        }

        static void RunJobs(ShiftSim s)
        {
            int guard = 0;
            while (s.Jobs.Count > 0 && guard++ < 10000) s.Tick(0.5f);
        }

        [Test]
        public void Generates_the_right_amount_of_freight()
        {
            var s = NewSim();
            Assert.AreEqual(40, s.Pallets.Count);
            Assert.AreEqual(40, s.Inbound.Sum(t => t.PalletIds.Count));
            Assert.That(s.Inbound.All(t => t.PalletIds.Count >= 1 && t.PalletIds.Count <= B.MaxPalletsPerInbound));
            Assert.AreEqual(0f, s.Inbound[0].ArrivalMinute);
            Assert.That(s.Inbound.All(t => t.ArrivalMinute <= B.LastArrivalMinute));
        }

        [Test]
        public void Freight_grows_and_caps()
        {
            Assert.AreEqual(40, B.PalletsForShift(1));
            Assert.AreEqual(48, B.PalletsForShift(2));
            Assert.AreEqual(80, B.PalletsForShift(6));
            Assert.AreEqual(80, B.PalletsForShift(20));
        }

        [Test]
        public void Same_seed_and_shift_gives_same_freight()
        {
            var a = NewSim();
            var b = NewSim();
            Assert.AreEqual(a.Pallets.Select(p => p.Pro), b.Pallets.Select(p => p.Pro));
        }

        [Test]
        public void Pallets_cannot_be_moved_until_trailer_docks()
        {
            var s = NewSim();
            var p = s.Pallets[s.Inbound[0].PalletIds[0]];
            Assert.IsNotNull(s.RouteToDoor(p.Id, 0));
            UntilDocked(s);
            Assert.IsNull(s.RouteToDoor(p.Id, 0));
        }

        [Test]
        public void Forklift_moves_pallet_to_door_in_expected_time()
        {
            var s = NewSim();
            UntilDocked(s);
            var p = s.Pallets[s.Inbound[0].PalletIds[0]];
            float start = s.Minute;
            Assert.IsNull(s.RouteToDoor(p.Id, 2));
            RunJobs(s);
            Assert.AreEqual(PalletLoc.Outbound, p.Loc);
            Assert.AreEqual(2, p.LocIndex);
            Assert.AreEqual(B.MoveInboundToDoor, s.Minute - start, 0.6f);
            CollectionAssert.Contains(s.Doors[2].Load, p.Id);
        }

        [Test]
        public void Faster_scanner_cuts_move_time()
        {
            C.FastScanner = true;
            var s = NewSim();
            UntilDocked(s);
            var p = s.Pallets[s.Inbound[0].PalletIds[0]];
            float start = s.Minute;
            s.RouteToDoor(p.Id, 0);
            RunJobs(s);
            Assert.AreEqual(B.MoveInboundToDoor * B.ScannerSpeedup, s.Minute - start, 0.6f);
        }

        [Test]
        public void Staging_round_trip_works()
        {
            var s = NewSim();
            UntilDocked(s);
            var p = s.Pallets[s.Inbound[0].PalletIds[0]];
            Assert.IsNull(s.RouteToStaging(p.Id));
            RunJobs(s);
            Assert.AreEqual(PalletLoc.Staging, p.Loc);
            Assert.AreEqual(p.Id, s.Staging[p.LocIndex]);
            Assert.IsNull(s.RouteToDoor(p.Id, 4));
            RunJobs(s);
            Assert.AreEqual(PalletLoc.Outbound, p.Loc);
            Assert.That(s.Staging.All(x => x != p.Id));
        }

        [Test]
        public void Cannot_double_book_a_pallet()
        {
            var s = NewSim();
            UntilDocked(s);
            var id = s.Inbound[0].PalletIds[0];
            Assert.IsNull(s.RouteToDoor(id, 0));
            Assert.IsNotNull(s.RouteToDoor(id, 1));
            Assert.IsNotNull(s.RouteToStaging(id));
        }

        [Test]
        public void Cancel_before_pickup_frees_the_pallet()
        {
            var s = NewSim();
            UntilDocked(s);
            var id = s.Inbound[0].PalletIds[0];
            s.RouteToDoor(id, 0);
            Assert.IsTrue(s.CancelJob(id));
            Assert.AreEqual(-1, s.Pallets[id].JobId);
            Assert.AreEqual(0, s.Jobs.Count);
            Assert.IsNull(s.RouteToDoor(id, 1));
        }

        [Test]
        public void Load_order_last_stop_first_is_clean()
        {
            var s = NewSim();
            var d = s.Doors[0];
            int[] stops = { 5, 4, 4, 2, 1 };
            foreach (int stop in stops) d.Load.Add(MakePallet(s, d.Route, stop));
            var r = s.Evaluate(d);
            Assert.AreEqual(5, r.OnTime);
            Assert.AreEqual(0, r.LoadOrderViolations);
        }

        [Test]
        public void Load_order_violations_are_counted()
        {
            var s = NewSim();
            var d = s.Doors[0];
            int[] stops = { 1, 3, 2, 5 }; // 3 after 1, 2 after 1, 5 after 1 -> 3 violations
            foreach (int stop in stops) d.Load.Add(MakePallet(s, d.Route, stop));
            var r = s.Evaluate(d);
            Assert.AreEqual(3, r.LoadOrderViolations);
        }

        [Test]
        public void Misroutes_are_counted_and_not_paid()
        {
            var s = NewSim();
            var d = s.Doors[0]; // North
            d.Load.Add(MakePallet(s, 1, 3));
            d.Load.Add(MakePallet(s, 0, 3));
            var r = s.Evaluate(d);
            Assert.AreEqual(1, r.Misroutes);
            Assert.AreEqual(1, r.OnTime);
        }

        [Test]
        public void Door_capacity_is_enforced_including_queued_jobs()
        {
            var s = NewSim();
            for (int i = 0; i < B.TrailerSpots; i++) s.Doors[0].Load.Add(MakePallet(s, 0, 1));
            UntilDocked(s);
            var id = s.Inbound[0].PalletIds[0];
            StringAssert.Contains("full", s.RouteToDoor(id, 0));
        }

        [Test]
        public void Overweight_trailer_cannot_dispatch()
        {
            var s = NewSim();
            var d = s.Doors[0];
            for (int i = 0; i < 9; i++) { int id = MakePallet(s, 0, 1); s.Pallets[id].Weight = 2600; d.Load.Add(id); }
            StringAssert.Contains("overweight", s.CanDispatch(0));
        }

        [Test]
        public void Dispatch_delivers_and_brings_a_new_trailer_later()
        {
            var s = NewSim();
            var d = s.Doors[0];
            int id = MakePallet(s, 0, 2);
            s.Pallets[id].Loc = PalletLoc.Outbound;
            d.Load.Add(id);
            Assert.IsNull(s.Dispatch(0));
            Assert.AreEqual(PalletLoc.Delivered, s.Pallets[id].Loc);
            Assert.IsFalse(d.TrailerPresent);
            Assert.IsNotNull(s.RouteToDoor(s.Inbound[0].PalletIds[0], 0));
            s.Tick(B.NewTrailerDelay + 1f);
            Assert.IsTrue(d.TrailerPresent);
            Assert.AreEqual(0, d.Load.Count);
        }

        [Test]
        public void Doing_nothing_leaves_everything_behind()
        {
            var s = NewSim();
            s.Tick(B.ShiftMinutes + 1f);
            Assert.IsTrue(s.Ended);
            var r = s.Report;
            Assert.AreEqual(40, r.LeftBehind);
            Assert.AreEqual(0, r.Revenue);
            Assert.AreEqual(40 * B.PenaltyLeftBehind + 0, r.Penalties);
            Assert.AreEqual(B.CrewCostPerShift + 2 * B.ForkliftCostPerShift, r.Costs);
            Assert.AreEqual(1, r.Stars);
        }

        [Test]
        public void Overweight_trailer_at_six_drops_last_loaded_pallets()
        {
            var s = NewSim();
            var d = s.Doors[0];
            for (int i = 0; i < 10; i++)
            {
                int id = MakePallet(s, 0, 1);
                s.Pallets[id].Weight = 2500;
                s.Pallets[id].Loc = PalletLoc.Outbound;
                d.Load.Add(id);
            }
            s.EndShift();
            Assert.AreEqual(8, s.Report.Dispatches[0].OnTime); // 8 x 2,500 = 20,000 lb fits; 2 dropped
        }

        [Test]
        public void Clock_text_reads_like_a_real_shift()
        {
            Assert.AreEqual("10:00 PM", ShiftSim.ClockText(0));
            Assert.AreEqual("12:00 AM", ShiftSim.ClockText(120));
            Assert.AreEqual("6:00 AM", ShiftSim.ClockText(480));
        }

        [Test]
        public void Career_upgrades_cost_money_and_cap()
        {
            C.Cash = 100000;
            Assert.IsTrue(C.BuyForklift(B));
            Assert.IsTrue(C.BuyForklift(B));
            Assert.IsFalse(C.BuyForklift(B)); // max 4
            Assert.IsTrue(C.BuyScanner(B));
            Assert.IsFalse(C.BuyScanner(B));
            Assert.IsTrue(C.BuyDoor(B, 2));
            Assert.IsFalse(C.BuyDoor(B, 1));
            Assert.AreEqual(7, C.OutboundDoors(B));
            Assert.AreEqual(2, C.DoorRoute(B, 6));
            Assert.AreEqual(100000 - 2 * B.CostForklift - B.CostScanner - B.CostExtraDoor, C.Cash);
        }

        [Test]
        public void Bankrupt_below_floor()
        {
            C.Cash = -1500;
            C.Apply(B, new ShiftReport { Costs = 1000 });
            Assert.IsTrue(C.Bankrupt);
        }

        /// <summary>Adds a test pallet that isn't part of tonight's freight.</summary>
        static int MakePallet(ShiftSim s, int route, int stop)
        {
            var p = new Pallet { Id = s.Pallets.Count, Pro = "T", Route = route, Stop = stop, Weight = 1000, Loc = PalletLoc.Outbound, LocIndex = 0 };
            s.Pallets.Add(p);
            return p.Id;
        }
    }
}
