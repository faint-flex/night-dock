using System.Collections.Generic;
using NightDock.Sim;
using UnityEngine;

namespace NightDock.Game
{
    /// <summary>
    /// Gray-box 3D view of one shift. Builds the terminal from boxes and mirrors the ShiftSim every frame.
    /// It never changes the simulation.
    /// </summary>
    public class TerminalView : MonoBehaviour
    {
        ShiftSim _sim;

        readonly List<GameObject> _outTrailers = new List<GameObject>();
        readonly List<TextMesh> _doorStatus = new List<TextMesh>();
        readonly List<GameObject> _doorHighlights = new List<GameObject>();
        readonly List<Transform> _inTrailers = new List<Transform>();
        readonly List<Transform> _pallets = new List<Transform>();
        readonly List<TextMesh> _palletLabels = new List<TextMesh>();
        readonly List<GameObject> _palletSelect = new List<GameObject>();
        readonly List<ForkliftVis> _forklifts = new List<ForkliftVis>();
        readonly List<TextMesh> _inboundDoorStatus = new List<TextMesh>();

        public int SelectedPallet = -1;
        public int SelectedDoor = -1;
        public int HoverPallet = -1;

        class ForkliftVis
        {
            public Transform Root;
            public int JobId = -1;
            public Vector3 Start;
            public Vector3[] PickupPath;
            public Vector3[] CarryPath;
        }

        public void Build(ShiftSim sim)
        {
            _sim = sim;
            var root = transform;

            // Ground, building floor and the yard.
            Look.Box("Yard", root, new Vector3(0f, -0.06f, 0f), new Vector3(220f, 0.1f, 180f), Look.Yard, true);
            Look.Box("Floor", root, new Vector3(0f, -0.02f, 0f), new Vector3(Layout.HalfWidth * 2f, 0.06f, Layout.HalfDepth * 2f), Look.Floor, true);

            // Aisle stripes.
            Look.Box("AisleN", root, new Vector3(0f, 0.02f, Layout.HalfDepth - 5f), new Vector3(Layout.HalfWidth * 2f - 4f, 0.02f, 0.15f), Look.SafetyYellow);
            Look.Box("AisleS", root, new Vector3(0f, 0.02f, -Layout.HalfDepth + 5f), new Vector3(Layout.HalfWidth * 2f - 4f, 0.02f, 0.15f), Look.SafetyYellow);

            BuildWalls(root);
            BuildStaging(root);
            BuildOutboundDoors(root);
            BuildInboundDoors(root);
            BuildLights(root);

            foreach (var t in sim.Inbound) _inTrailers.Add(BuildTrailer(root, "T" + (t.Id + 1), Look.TrailerInbound, true).transform);
            foreach (var p in sim.Pallets) BuildPallet(root, p);
            foreach (var f in sim.Forklifts) _forklifts.Add(BuildForklift(root, f.Index));
        }

        void BuildWalls(Transform root)
        {
            float h = Layout.WallHeight;
            float w = Layout.HalfWidth;
            float d = Layout.HalfDepth;
            Look.Box("WallW", root, new Vector3(-w, h / 2f, 0f), new Vector3(0.4f, h, d * 2f), Look.Wall);
            Look.Box("WallE", root, new Vector3(w, h / 2f, 0f), new Vector3(0.4f, h, d * 2f), Look.Wall);

            // North and south walls are built in pieces around the door openings, and kept low so the camera sees in.
            var northDoors = new List<float>();
            for (int i = 0; i < _sim.Doors.Length; i++) northDoors.Add(Layout.OutboundX(i));
            WallWithOpenings(root, "WallN", d, northDoors);
            var southDoors = new List<float>();
            for (int i = 0; i < _sim.B.InboundDoors; i++) southDoors.Add(Layout.InboundX(i));
            WallWithOpenings(root, "WallS", -d, southDoors);
        }

        void WallWithOpenings(Transform root, string name, float z, List<float> openings)
        {
            float wallH = 2.2f; // cut-away wall so the floor reads from above
            float x = -Layout.HalfWidth;
            openings.Sort();
            foreach (float o in openings)
            {
                float left = o - Layout.DoorWidth / 2f;
                if (left > x) Look.Box(name, root, new Vector3((x + left) / 2f, wallH / 2f, z), new Vector3(left - x, wallH, 0.4f), Look.Wall);
                // Door frame posts.
                Look.Box(name + "Post", root, new Vector3(left - 0.15f, 2f, z), new Vector3(0.3f, 4f, 0.5f), Look.SafetyYellow);
                Look.Box(name + "Post", root, new Vector3(o + Layout.DoorWidth / 2f + 0.15f, 2f, z), new Vector3(0.3f, 4f, 0.5f), Look.SafetyYellow);
                x = o + Layout.DoorWidth / 2f;
            }
            if (x < Layout.HalfWidth) Look.Box(name, root, new Vector3((x + Layout.HalfWidth) / 2f, wallH / 2f, z), new Vector3(Layout.HalfWidth - x, wallH, 0.4f), Look.Wall);
        }

        void BuildStaging(Transform root)
        {
            var zone = Look.Box("Staging", root, Layout.StagingCenter + new Vector3(0f, 0.01f, 0f), Layout.StagingSize, new Color(0.27f, 0.28f, 0.3f), true);
            zone.AddComponent<StagingTag>();
            // Painted lane lines.
            for (int c = 0; c <= Layout.StagingCols; c++)
            {
                float x = -13.2f - 1.2f + c * 2.4f;
                Look.Box("Lane", root, new Vector3(x, 0.04f, 0f), new Vector3(0.06f, 0.02f, Layout.StagingSize.z - 0.6f), new Color(0.9f, 0.9f, 0.9f));
            }
            Look.Label("STAGING  (key 0)", root, new Vector3(0f, 0.06f, -Layout.StagingSize.z / 2f - 1.2f), 1.4f, Color.white);
        }

        void BuildOutboundDoors(Transform root)
        {
            for (int i = 0; i < _sim.Doors.Length; i++)
            {
                var door = _sim.Doors[i];
                // Click zone covering the apron and the trailer.
                float z0 = Layout.HalfDepth - 4.5f;
                float z1 = Layout.HalfDepth + Layout.TrailerLength + 0.5f;
                var zone = new GameObject("DoorZone " + door.Name);
                zone.transform.SetParent(root, false);
                zone.transform.localPosition = new Vector3(Layout.OutboundX(i), 0.03f, (z0 + z1) / 2f);
                var col = zone.AddComponent<BoxCollider>();
                col.size = new Vector3(Layout.TrailerWidth + 1.6f, 0.05f, z1 - z0);
                zone.AddComponent<DoorTag>().Door = i;

                // Route-coloured apron with the door's letter and number painted on it.
                var apron = Look.Box("Apron " + door.Name, root, Layout.OutboundApron(i) + new Vector3(0f, 0.02f, 0f), new Vector3(Layout.DoorWidth, 0.02f, 3.6f), Look.RouteColors[door.Route]);
                Look.Label(door.Name + " " + Routes.Letters[door.Route], root, Layout.OutboundApron(i) + new Vector3(0f, 0.06f, -0.4f), 1.6f, Color.black);
                var status = Look.Label("", root, Layout.OutboundApron(i) + new Vector3(0f, 0.06f, -3.0f), 1.3f, Color.white);
                _doorStatus.Add(status);

                var hl = Look.Box("DoorSelect", root, Layout.OutboundApron(i) + new Vector3(0f, 0.01f, 3f), new Vector3(Layout.DoorWidth + 1.2f, 0.02f, 11f), Look.Select);
                hl.SetActive(false);
                _doorHighlights.Add(hl);

                var trailer = BuildTrailer(root, door.Name + "  " + Routes.Names[door.Route], Look.TrailerWhite, false);
                trailer.transform.localPosition = Layout.OutboundTrailerPos(i);
                _outTrailers.Add(trailer);
            }
        }

        void BuildInboundDoors(Transform root)
        {
            for (int i = 0; i < _sim.B.InboundDoors; i++)
            {
                Look.Box("InApron", root, Layout.InboundApron(i) + new Vector3(0f, 0.02f, 0f), new Vector3(Layout.DoorWidth, 0.02f, 3.6f), new Color(0.5f, 0.52f, 0.55f));
                Look.Label("I" + (i + 1), root, Layout.InboundApron(i) + new Vector3(0f, 0.06f, 0.3f), 1.6f, Color.black);
                _inboundDoorStatus.Add(Look.Label("", root, Layout.InboundApron(i) + new Vector3(0f, 0.06f, 3.0f), 1.2f, Color.white));
            }
            Look.Label("YARD", root, Layout.YardPos(0) + new Vector3(-5f, 0.06f, 0f), 2f, new Color(0.7f, 0.7f, 0.7f));
        }

        void BuildLights(Transform root)
        {
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.62f, 0.85f);
            moon.intensity = 0.35f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.23f, 0.27f);

            // Interior high-bays (cool white) and sodium yard lights (warm) over each dock.
            for (int x = -3; x <= 3; x++)
                for (int z = -1; z <= 1; z++)
                    PointLight(root, new Vector3(x * 12f, 8f, z * 11f), new Color(0.95f, 0.97f, 1f), 1.6f, 16f);
            for (int i = 0; i < _sim.Doors.Length; i++)
                PointLight(root, Layout.OutboundTrailerPos(i) + new Vector3(0f, 7f, 2f), new Color(1f, 0.7f, 0.35f), 1.2f, 14f);
            for (int i = 0; i < _sim.B.InboundDoors; i++)
                PointLight(root, Layout.InboundTrailerDocked(i) + new Vector3(0f, 7f, -2f), new Color(1f, 0.7f, 0.35f), 1.2f, 14f);
        }

        static void PointLight(Transform root, Vector3 pos, Color c, float intensity, float range)
        {
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.localPosition = pos;
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
        }

        /// <summary>Open-top trailer shell (so pallets inside are visible from above). Rear opening faces the door.</summary>
        GameObject BuildTrailer(Transform root, string label, Color c, bool inbound)
        {
            var t = new GameObject("Trailer " + label);
            t.transform.SetParent(root, false);
            float L = Layout.TrailerLength, W = Layout.TrailerWidth;
            Look.Box("Bed", t.transform, new Vector3(0f, 0.05f, 0f), new Vector3(W, 0.1f, L), new Color(0.3f, 0.3f, 0.32f));
            Look.Box("SideL", t.transform, new Vector3(-W / 2f, 1.4f, 0f), new Vector3(0.08f, 2.8f, L), c);
            Look.Box("SideR", t.transform, new Vector3(W / 2f, 1.4f, 0f), new Vector3(0.08f, 2.8f, L), c);
            float noseZ = inbound ? -L / 2f : L / 2f;
            Look.Box("Nose", t.transform, new Vector3(0f, 1.4f, noseZ), new Vector3(W, 2.8f, 0.08f), c);
            // Tractor at the nose end.
            float cabZ = inbound ? -L / 2f - 2f : L / 2f + 2f;
            Look.Box("Cab", t.transform, new Vector3(0f, 1.4f, cabZ), new Vector3(W, 2.6f, 2.8f), new Color(0.15f, 0.2f, 0.3f));
            Look.Label(label, t.transform, new Vector3(0f, 2.85f, noseZ * 0.92f), 1.0f, Color.black, true, false);
            return t;
        }

        void BuildPallet(Transform root, Pallet p)
        {
            var go = new GameObject("Pallet " + p.Label + " #" + p.Pro);
            go.transform.SetParent(root, false);
            var s = Layout.PalletSize;
            Look.Box("Skid", go.transform, new Vector3(0f, -s.y / 2f + 0.07f, 0f), new Vector3(s.x, 0.14f, s.z), Look.Wood);
            // Freight height hints at weight.
            float hgt = Mathf.Lerp(0.6f, 0.95f, Mathf.InverseLerp(400f, 2600f, p.Weight));
            var load = Look.Box("Freight", go.transform, new Vector3(0f, -s.y / 2f + 0.14f + hgt / 2f, 0f), new Vector3(s.x * 0.96f, hgt, s.z * 0.96f), Look.RouteColors[p.Route]);
            var col = go.AddComponent<BoxCollider>();
            col.size = s;
            go.AddComponent<PalletTag>().Id = p.Id;
            var label = Look.Label(p.Label, go.transform, new Vector3(0f, -s.y / 2f + 0.16f + hgt, 0f), 1.15f, Color.black);
            var sel = Look.Box("Select", go.transform, new Vector3(0f, -s.y / 2f + 0.03f, 0f), new Vector3(s.x + 0.5f, 0.04f, s.z + 0.5f), Look.Select);
            sel.SetActive(false);
            go.SetActive(false);
            _pallets.Add(go.transform);
            _palletLabels.Add(label);
            _palletSelect.Add(sel);
        }

        ForkliftVis BuildForklift(Transform root, int index)
        {
            var go = new GameObject("Forklift F" + (index + 1));
            go.transform.SetParent(root, false);
            Look.Box("Body", go.transform, new Vector3(0f, 0.7f, -0.2f), new Vector3(1.2f, 1.1f, 2.0f), Look.Forklift);
            Look.Box("Cage", go.transform, new Vector3(0f, 1.75f, -0.3f), new Vector3(1.1f, 1.0f, 1.2f), new Color(0.15f, 0.15f, 0.15f));
            Look.Box("Mast", go.transform, new Vector3(0f, 1.3f, 0.85f), new Vector3(0.9f, 2.4f, 0.12f), new Color(0.2f, 0.2f, 0.2f));
            Look.Box("ForkL", go.transform, new Vector3(-0.3f, 0.12f, 1.5f), new Vector3(0.12f, 0.06f, 1.2f), new Color(0.4f, 0.4f, 0.4f));
            Look.Box("ForkR", go.transform, new Vector3(0.3f, 0.12f, 1.5f), new Vector3(0.12f, 0.06f, 1.2f), new Color(0.4f, 0.4f, 0.4f));
            Look.Label("F" + (index + 1), go.transform, new Vector3(0f, 2.3f, -0.3f), 0.9f, Color.white);
            go.transform.localPosition = Layout.ForkliftPark(index);
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            return new ForkliftVis { Root = go.transform };
        }

        // ------------------------------------------------------------------ per-frame mirror

        Vector3 InboundTrailerPos(int id)
        {
            var t = _sim.Inbound[id];
            switch (t.State)
            {
                case InboundState.Yard: return Layout.YardPos(YardRank(id));
                case InboundState.Docking:
                    float k = 1f - Mathf.Clamp01(t.Timer / _sim.B.DockDelay);
                    return Vector3.Lerp(Layout.YardPos(0), Layout.InboundTrailerDocked(t.Door), Mathf.SmoothStep(0f, 1f, k));
                case InboundState.Docked: return Layout.InboundTrailerDocked(t.Door);
                case InboundState.Departing:
                    float d = 1f - Mathf.Clamp01(t.Timer / _sim.B.EmptyDepartDelay);
                    return Vector3.Lerp(Layout.InboundTrailerDocked(t.Door), Layout.OffMapSouth(t.Door), d);
                default: return new Vector3(0f, -100f, 0f);
            }
        }

        int YardRank(int id)
        {
            int r = 0;
            foreach (var t in _sim.Inbound)
            {
                if (t.Id == id) break;
                if (t.State == InboundState.Yard) r++;
            }
            return r;
        }

        void LateUpdate()
        {
            if (_sim == null) return;

            for (int i = 0; i < _sim.Inbound.Count; i++)
            {
                var t = _sim.Inbound[i];
                bool visible = t.State != InboundState.Scheduled && t.State != InboundState.Gone;
                _inTrailers[i].gameObject.SetActive(visible);
                if (visible) _inTrailers[i].localPosition = InboundTrailerPos(i);
            }
            for (int i = 0; i < _sim.B.InboundDoors; i++)
            {
                int tid = _sim.InboundDoorTrailer[i];
                string s = "";
                if (tid >= 0)
                {
                    var t = _sim.Inbound[tid];
                    s = "T" + (tid + 1) + (t.State == InboundState.Docked ? "  " + _sim.InboundRemaining(t) + " left" : t.State == InboundState.Docking ? "  backing in" : "  leaving");
                }
                _inboundDoorStatus[i].text = s;
            }

            for (int i = 0; i < _sim.Doors.Length; i++)
            {
                var d = _sim.Doors[i];
                _outTrailers[i].SetActive(d.TrailerPresent);
                if (d.TrailerPresent)
                {
                    int w = _sim.DoorWeight(i);
                    _doorStatus[i].text = d.Load.Count + "/" + _sim.B.TrailerSpots + "   " + (w / 1000f).ToString("0.0") + "k lb" + (w > _sim.B.TrailerWeightLimit ? "  OVER" : "");
                }
                else _doorStatus[i].text = "gone  new trailer " + Mathf.CeilToInt(d.NewTrailerTimer) + " min";
                // A selected pallet lights up the doors for its route, and the door text says so.
                bool match = SelectedPallet >= 0 && _sim.Pallets[SelectedPallet].Route == d.Route && d.TrailerPresent;
                _doorHighlights[i].SetActive(i == SelectedDoor || match);
                if (match) _doorStatus[i].text = ">> " + _sim.Pallets[SelectedPallet].Label + " goes here  (key " + (i + 1) + ")";
            }

            UpdateForklifts();
            var cam = Camera.main;
            if (cam != null) Look.TurnFlatLabels(cam.transform.eulerAngles.y);

            for (int i = 0; i < _sim.Pallets.Count; i++)
            {
                var p = _sim.Pallets[i];
                var tr = _pallets[i];
                bool visible;
                Vector3 target;
                if (p.Loc == PalletLoc.OnForklift)
                {
                    var f = _forklifts[p.LocIndex].Root;
                    target = f.localPosition + f.localRotation * new Vector3(0f, 0.75f, 1.6f);
                    tr.localRotation = f.localRotation;
                    visible = true;
                    tr.localPosition = target;
                }
                else if (p.Loc == PalletLoc.InboundTrailer)
                {
                    var t = _sim.Inbound[p.LocIndex];
                    visible = t.State != InboundState.Scheduled && t.State != InboundState.Gone;
                    target = Layout.PalletRest(_sim, p, InboundTrailerPos);
                    tr.localPosition = target;
                    tr.localRotation = Quaternion.identity;
                }
                else if (p.Loc == PalletLoc.Staging || p.Loc == PalletLoc.Outbound)
                {
                    visible = true;
                    target = Layout.PalletRest(_sim, p, InboundTrailerPos);
                    tr.localPosition = Vector3.Lerp(tr.localPosition, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                    tr.localRotation = Quaternion.identity;
                }
                else visible = false;

                if (tr.gameObject.activeSelf != visible) tr.gameObject.SetActive(visible);
                if (!visible) continue;

                string job = "";
                if (p.JobId >= 0 && _sim.Jobs.TryGetValue(p.JobId, out var j))
                    job = j.Dest == DestKind.Door ? " > " + _sim.Doors[j.DestDoor].Name : " > S";
                _palletLabels[i].text = p.Label + job;
                _palletSelect[i].SetActive(i == SelectedPallet || i == HoverPallet);
            }
        }

        void UpdateForklifts()
        {
            foreach (var f in _sim.Forklifts)
            {
                var vis = _forklifts[f.Index];
                if (!f.Busy) { vis.JobId = -1; continue; }
                var job = _sim.Jobs[f.JobId];
                if (vis.JobId != job.Id) StartJob(vis, job);

                float t = job.Duration > 0f ? Mathf.Clamp01(job.Elapsed / job.Duration) : 1f;
                float pf = _sim.B.PickupFraction;
                Vector3 pos = t < pf ? Along(vis.PickupPath, t / pf) : Along(vis.CarryPath, (t - pf) / (1f - pf));
                Vector3 delta = pos - vis.Root.localPosition;
                vis.Root.localPosition = pos;
                delta.y = 0f;
                if (delta.sqrMagnitude > 1e-5f)
                    vis.Root.localRotation = Quaternion.Slerp(vis.Root.localRotation, Quaternion.LookRotation(delta.normalized), 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            }
        }

        void StartJob(ForkliftVis vis, ForkliftJob job)
        {
            vis.JobId = job.Id;
            vis.Start = vis.Root.localPosition;
            var p = _sim.Pallets[job.PalletId];
            Vector3 pickup = Flat(Layout.PalletRest(_sim, p, InboundTrailerPos)) - new Vector3(0f, 0f, 0f);
            Vector3 srcApron = pickup;
            if (p.Loc == PalletLoc.InboundTrailer) srcApron = Layout.InboundApron(_sim.Inbound[p.LocIndex].Door);
            else if (p.Loc == PalletLoc.Outbound) srcApron = Layout.OutboundApron(p.LocIndex);

            Vector3 dest, destApron;
            if (job.Dest == DestKind.Door)
            {
                dest = Flat(Layout.OutboundSlot(job.DestDoor, _sim.DoorCommitted(job.DestDoor) - 1));
                destApron = Layout.OutboundApron(job.DestDoor);
            }
            else
            {
                int slot = _sim.JobStagingSlot(job.Id);
                dest = Flat(Layout.StagingSlot(slot < 0 ? 0 : slot));
                destApron = dest + new Vector3(0f, 0f, -2f);
            }
            // Stop short of the pallet so the forks, not the body, reach it.
            vis.PickupPath = new[] { vis.Start, srcApron, Back(srcApron, pickup) };
            vis.CarryPath = new[] { Back(srcApron, pickup), srcApron, destApron, Back(destApron, dest) };
        }

        static Vector3 Back(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            if (d.magnitude < 1.7f) return Flat(to);
            return Flat(to - d.normalized * 1.6f);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 Along(Vector3[] path, float t)
        {
            if (path == null || path.Length == 0) return Vector3.zero;
            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += Vector3.Distance(path[i - 1], path[i]);
            if (total < 1e-4f) return path[path.Length - 1];
            float want = Mathf.Clamp01(t) * total;
            for (int i = 1; i < path.Length; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (want <= seg) return Vector3.Lerp(path[i - 1], path[i], seg < 1e-4f ? 1f : want / seg);
                want -= seg;
            }
            return path[path.Length - 1];
        }
    }
}
