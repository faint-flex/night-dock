using System;
using System.Collections.Generic;

namespace NightDock.Sim
{
    public static class Routes
    {
        public static readonly string[] Names = { "North Loop", "Valley", "Airport" };
        public static readonly string[] Letters = { "N", "V", "A" };
        public static int Count => Names.Length;

        // Fictional consignees, stop 1..5 per route.
        public static readonly string[][] Consignees =
        {
            new[] { "Pinecrest Hardware", "Ridgeview Market", "Copper Hill Clinic", "Northgate Tile", "Summit Auto Parts" },
            new[] { "Valley Feed & Seed", "Mesa Floor Supply", "Dry Creek Brewing", "Sandstone Office", "Canyon Pet Center" },
            new[] { "Runway Electric", "Terminal Cafe Supply", "Jetway Uniforms", "Hangar 9 Tools", "Skyport Freight Desk" },
        };

        public static string Label(int route, int stop) => Letters[route] + stop;
    }

    public enum PalletLoc { InboundTrailer, Staging, OnForklift, Outbound, Delivered, LeftBehind }

    [Serializable]
    public class Pallet
    {
        public int Id;
        public string Pro;
        public int Route;
        public int Stop;          // 1 = first delivery
        public int Weight;        // lb
        public string Consignee;

        public PalletLoc Loc;
        public int LocIndex;      // inbound trailer id, staging slot, or outbound door index
        public int JobId = -1;    // queued or active forklift job, -1 if none

        public string Label => Routes.Label(Route, Stop);
    }

    public enum InboundState { Scheduled, Yard, Docking, Docked, Departing, Gone }

    public class InboundTrailer
    {
        public int Id;
        public float ArrivalMinute;
        public InboundState State;
        public int Door = -1;
        public float Timer;       // counts down Docking / Departing
        public List<int> PalletIds = new List<int>();
    }

    public class OutboundDoor
    {
        public int Index;
        public int Route;
        public bool TrailerPresent = true;
        public float NewTrailerTimer;
        public List<int> Load = new List<int>();   // pallet ids in load order (nose first)
        public int Dispatches;

        public string Name => "O" + (Index + 1);
    }

    public enum DestKind { Staging, Door }

    public class ForkliftJob
    {
        public int Id;
        public int PalletId;
        public DestKind Dest;
        public int DestDoor;      // when Dest == Door
        public float Duration;
        public float Elapsed;
        public int Forklift = -1; // -1 while queued
        public bool PickedUp;

        // Where the pallet was when the forklift reached it (for the view).
        public PalletLoc FromLoc;
        public int FromIndex;
    }

    public class Forklift
    {
        public int Index;
        public int JobId = -1;
        public bool Busy => JobId >= 0;
    }

    public class DispatchResult
    {
        public int Door;
        public float Minute;
        public int OnTime;
        public int Misroutes;
        public int LoadOrderViolations;
        public int Weight;
    }

    public class ShiftReport
    {
        public int ShiftNumber;
        public int TotalPallets;
        public int OnTime;
        public int Misroutes;
        public int LoadOrderViolations;
        public int LeftBehind;
        public int Revenue;
        public int Penalties;
        public int Costs;
        public int Profit => Revenue - Penalties - Costs;
        public int Stars;
        public List<DispatchResult> Dispatches = new List<DispatchResult>();
    }
}
