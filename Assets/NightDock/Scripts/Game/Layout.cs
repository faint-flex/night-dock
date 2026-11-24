using NightDock.Sim;
using UnityEngine;

namespace NightDock.Game
{
    /// <summary>Where everything sits in the terminal (metres). Inbound doors on the south wall, outbound on the north.</summary>
    public static class Layout
    {
        public const float HalfWidth = 45f;
        public const float HalfDepth = 18f;
        public const float WallHeight = 9f;
        public const float TrailerLength = 9f;
        public const float TrailerWidth = 2.6f;
        public const float DoorWidth = 3.4f;

        public static readonly Vector3 PalletSize = new Vector3(1.1f, 1.1f, 1.15f);

        public static float OutboundX(int door) => -33f + 11f * door;
        public static float InboundX(int door) => -24f + 16f * door;

        public static Vector3 OutboundDoorPos(int door) => new Vector3(OutboundX(door), 0f, HalfDepth);
        public static Vector3 InboundDoorPos(int door) => new Vector3(InboundX(door), 0f, -HalfDepth);

        /// <summary>Trailer centre when docked at an outbound door.</summary>
        public static Vector3 OutboundTrailerPos(int door) => new Vector3(OutboundX(door), 0f, HalfDepth + TrailerLength / 2f + 0.2f);

        public static Vector3 InboundTrailerDocked(int door) => new Vector3(InboundX(door), 0f, -HalfDepth - TrailerLength / 2f - 0.2f);

        public static Vector3 YardPos(int queueIndex) => new Vector3(-36f + 6f * queueIndex, 0f, -HalfDepth - 24f);

        public static Vector3 OffMapSouth(int door) => new Vector3(InboundX(door) + 30f, 0f, -HalfDepth - 60f);

        /// <summary>Load slot k on an outbound trailer: first-loaded pallets sit at the nose (far end).</summary>
        public static Vector3 OutboundSlot(int door, int k)
        {
            int row = k / 2;
            int side = k % 2;
            var c = OutboundTrailerPos(door);
            float z = c.z + TrailerLength / 2f - 0.9f - row * 1.32f;
            float x = c.x + (side == 0 ? -0.6f : 0.6f);
            return new Vector3(x, PalletSize.y / 2f + 0.1f, z);
        }

        /// <summary>Pallet k inside an inbound trailer whose centre is at trailerPos (rear faces north, toward the door).</summary>
        public static Vector3 InboundSlot(Vector3 trailerPos, int k)
        {
            int row = k / 2;
            int side = k % 2;
            float z = trailerPos.z + TrailerLength / 2f - 0.9f - row * 1.32f;
            float x = trailerPos.x + (side == 0 ? -0.6f : 0.6f);
            return new Vector3(x, PalletSize.y / 2f + 0.1f, z);
        }

        public const int StagingCols = 12;
        public static Vector3 StagingSlot(int slot)
        {
            int col = slot % StagingCols;
            int row = slot / StagingCols;
            return new Vector3(-13.2f + col * 2.4f, PalletSize.y / 2f, -4.2f + row * 2.8f);
        }

        public static Vector3 StagingCenter => new Vector3(0f, 0f, 0f);
        public static Vector3 StagingSize => new Vector3(StagingCols * 2.4f + 1.5f, 0.05f, 4 * 2.8f + 1.5f);

        public static Vector3 ForkliftPark(int i) => new Vector3(-HalfWidth + 4f, 0f, -6f + 4f * i);

        /// <summary>Spot just inside an outbound door where a forklift turns in.</summary>
        public static Vector3 OutboundApron(int door) => new Vector3(OutboundX(door), 0f, HalfDepth - 2.5f);
        public static Vector3 InboundApron(int door) => new Vector3(InboundX(door), 0f, -HalfDepth + 2.5f);

        /// <summary>World position for a pallet that is sitting somewhere (not on a forklift).</summary>
        public static Vector3 PalletRest(ShiftSim sim, Pallet p, System.Func<int, Vector3> inboundTrailerPos)
        {
            switch (p.Loc)
            {
                case PalletLoc.InboundTrailer:
                    var t = sim.Inbound[p.LocIndex];
                    return InboundSlot(inboundTrailerPos(t.Id), t.PalletIds.IndexOf(p.Id));
                case PalletLoc.Staging:
                    return StagingSlot(p.LocIndex);
                case PalletLoc.Outbound:
                    return OutboundSlot(p.LocIndex, sim.Doors[p.LocIndex].Load.IndexOf(p.Id));
                default:
                    return new Vector3(0f, -50f, 0f);
            }
        }
    }
}
