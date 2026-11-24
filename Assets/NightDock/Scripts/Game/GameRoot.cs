using System.Collections.Generic;
using System.IO;
using System.Text;
using NightDock.Sim;
using UnityEngine;

namespace NightDock.Game
{
    /// <summary>
    /// The whole game loop: title -> planning -> shift -> report/shop -> next shift.
    /// Creates itself in any scene, so the build only needs one empty scene.
    /// Gray-box UI is Unity IMGUI; it gets replaced by the UI Builder in the art pass.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        enum State { Title, Planning, Running, Report, Bankrupt }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameRoot>() != null) return;
            new GameObject("NightDock").AddComponent<GameRoot>();
        }

        readonly Balance B = new Balance();
        Career C;
        ShiftSim Sim;
        TerminalView View;
        CameraRig Rig;
        State _state = State.Title;

        bool _paused;
        bool _menu;
        int _speed = 1;
        string _toast;
        float _toastUntil;
        ShiftReport _report;
        readonly List<Rect> _uiRects = new List<Rect>();
        readonly List<Rect> _uiRectsLast = new List<Rect>();
        float _uiScale = 1f;

        // Screenshot run for QA and remote checks: NightDock.exe -ndshots <folder>
        string _shotsDir;
        float _nextShotMinute;
        int _shotIndex;
        readonly float[] _shotMinutes = { 20f, 90f, 200f, 330f, 460f };
        float _botTimer;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 120;
            Rig = CameraRig.Create();
            Rig.transform.SetParent(transform, false);

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-ndshots") _shotsDir = args[i + 1];

            if (_shotsDir != null)
            {
                Directory.CreateDirectory(_shotsDir);
                SaveSystem.PathOverride = Path.Combine(_shotsDir, "career-shots.json");
                C = Career.New(B, 4242);
                StartPlanning();
                StartCoroutine(ShotsRun());
            }
            else
            {
                BuildBackdrop();
            }
        }

        // ------------------------------------------------------------------ flow

        Career _titleSave;

        void BuildBackdrop()
        {
            _titleSave = SaveSystem.Load();
            // Title screen shows an empty terminal behind the menu.
            var preview = Career.New(B, 1);
            SetupShift(new ShiftSim(B, preview));
            Rig.Frame(new Vector3(0f, 0f, 0f), -20f, 40f, 75f);
            Rig.InputEnabled = false;
        }

        void SetupShift(ShiftSim sim)
        {
            if (View != null) Destroy(View.gameObject);
            Sim = sim;
            var go = new GameObject("Terminal");
            go.transform.SetParent(transform, false);
            View = go.AddComponent<TerminalView>();
            View.Build(sim);
        }

        void NewGame()
        {
            C = Career.New(B, Random.Range(1, 1000000));
            SaveSystem.Save(C);
            StartPlanning();
        }

        void ContinueGame()
        {
            C = SaveSystem.Load();
            if (C == null) { NewGame(); return; }
            if (C.Bankrupt) { _state = State.Bankrupt; return; }
            StartPlanning();
        }

        void StartPlanning()
        {
            SetupShift(new ShiftSim(B, C));
            Rig.Frame(new Vector3(0f, 0f, -4f), 0f, 58f, 66f);
            Rig.InputEnabled = true;
            _paused = false;
            _menu = false;
            _speed = 1;
            _state = State.Planning;
        }

        void StartShift()
        {
            _state = State.Running;
            Toast("Shift started. Click a pallet, then a door.");
        }

        void FinishShift()
        {
            _report = Sim.Report;
            C.Apply(B, _report);
            SaveSystem.Save(C);
            View.SelectedPallet = -1;
            View.SelectedDoor = -1;
            _state = C.Bankrupt ? State.Bankrupt : State.Report;
        }

        void Toast(string s)
        {
            _toast = s;
            _toastUntil = Time.unscaledTime + 3.5f;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            _uiRectsLast.Clear();
            _uiRectsLast.AddRange(_uiRects);

            if (_shotsDir != null) { UpdateShots(); return; }
            if (_state != State.Running) return;

            if (GameInput.Down(GameKey.Escape)) { _menu = !_menu; }
            Rig.InputEnabled = !_menu;
            if (_menu) return;

            if (GameInput.Down(GameKey.Space)) _paused = !_paused;
            if (GameInput.Down(GameKey.F)) _speed = _speed == 1 ? 2 : 1;

            if (!_paused)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                Sim.Tick(dt / B.RealSecondsPerGameMinute * _speed);
            }

            HandleWorldInput();

            if (Sim.Ended) FinishShift();
        }

        bool MouseOverUI()
        {
            Vector2 m = GameInput.MousePosition;
            var gui = new Vector2(m.x / _uiScale, (Screen.height - m.y) / _uiScale);
            foreach (var r in _uiRectsLast) if (r.Contains(gui)) return true;
            return false;
        }

        void HandleWorldInput()
        {
            View.HoverPallet = -1;
            bool overUI = MouseOverUI();
            RaycastHit hit = default;
            bool gotHit = false;
            if (!overUI)
            {
                var ray = Rig.Cam.ScreenPointToRay(GameInput.MousePosition);
                gotHit = Physics.Raycast(ray, out hit, 1000f);
                if (gotHit)
                {
                    var pt = hit.collider.GetComponent<PalletTag>();
                    if (pt != null) View.HoverPallet = pt.Id;
                }
            }

            if (GameInput.LeftDown && !overUI)
            {
                if (!gotHit) { Deselect(); }
                else
                {
                    var pallet = hit.collider.GetComponent<PalletTag>();
                    var door = hit.collider.GetComponent<DoorTag>();
                    var staging = hit.collider.GetComponent<StagingTag>();
                    if (pallet != null)
                    {
                        var p = Sim.Pallets[pallet.Id];
                        // Clicking a pallet on a trailer while another pallet is selected = route to that door.
                        if (View.SelectedPallet >= 0 && View.SelectedPallet != p.Id && p.Loc == PalletLoc.Outbound)
                            RouteSelectedToDoor(p.LocIndex);
                        else
                        {
                            View.SelectedPallet = p.Id;
                            View.SelectedDoor = -1;
                        }
                    }
                    else if (door != null)
                    {
                        if (View.SelectedPallet >= 0) RouteSelectedToDoor(door.Door);
                        else { View.SelectedDoor = door.Door; }
                    }
                    else if (staging != null && View.SelectedPallet >= 0) RouteSelectedToStaging();
                    else Deselect();
                }
            }
            if (GameInput.RightDown && !overUI) Deselect();

            if (View.SelectedPallet >= 0)
            {
                for (int i = 0; i < 7; i++)
                    if (GameInput.Down(GameKey.Alpha1 + i) && i < Sim.Doors.Length) RouteSelectedToDoor(i);
                if (GameInput.Down(GameKey.Alpha0)) RouteSelectedToStaging();
                if (GameInput.Down(GameKey.X)) CancelSelected();
            }
            else if (View.SelectedDoor >= 0 && GameInput.Down(GameKey.Enter)) DispatchSelected();
        }

        void Deselect()
        {
            View.SelectedPallet = -1;
            View.SelectedDoor = -1;
        }

        void RouteSelectedToDoor(int door)
        {
            int id = View.SelectedPallet;
            if (id < 0) return;
            var p = Sim.Pallets[id];
            string err = Sim.RouteToDoor(id, door);
            if (err != null) { Toast(err); return; }
            if (Sim.Doors[door].Route != p.Route)
                Toast("Heads up: " + p.Label + " is a " + Routes.Names[p.Route] + " pallet going to a " + Routes.Names[Sim.Doors[door].Route] + " door");
            View.SelectedPallet = -1;
        }

        void RouteSelectedToStaging()
        {
            int id = View.SelectedPallet;
            if (id < 0) return;
            string err = Sim.RouteToStaging(id);
            if (err != null) { Toast(err); return; }
            View.SelectedPallet = -1;
        }

        void CancelSelected()
        {
            int id = View.SelectedPallet;
            if (id < 0) return;
            if (!Sim.CancelJob(id)) Toast("Nothing to cancel, or the forklift already has it");
        }

        void DispatchSelected()
        {
            int door = View.SelectedDoor;
            if (door < 0) return;
            string err = Sim.Dispatch(door);
            Toast(err ?? Sim.Doors[door].Name + " dispatched");
        }

        // ------------------------------------------------------------------ screenshot run

        System.Collections.IEnumerator ShotsRun()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("01-planning");
            yield return new WaitForSecondsRealtime(0.5f);
            StartShift();
            _speed = 8;
            _nextShotMinute = _shotMinutes[0];
        }

        void UpdateShots()
        {
            if (_state != State.Running) return;
            _botTimer += Time.unscaledDeltaTime;
            if (_botTimer > 0.05f) { _botTimer = 0f; ShiftBot.Think(Sim, true); }
            Sim.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f) / B.RealSecondsPerGameMinute * _speed);
            if (_shotIndex < _shotMinutes.Length && Sim.Minute >= _nextShotMinute)
            {
                // Point the camera at something interesting for each shot.
                if (_shotIndex == 1) Rig.Frame(new Vector3(-10f, 0f, -14f), 20f, 45f, 30f);
                else if (_shotIndex == 2) Rig.Frame(new Vector3(-5f, 0f, 14f), 200f, 42f, 34f);
                else Rig.Frame(new Vector3(0f, 0f, -4f), 0f, 58f, 66f);
                Shot((_shotIndex + 2).ToString("00") + "-shift-" + ShiftSim.ClockText(Sim.Minute).Replace(":", "").Replace(" ", ""));
                _shotIndex++;
                if (_shotIndex < _shotMinutes.Length) _nextShotMinute = _shotMinutes[_shotIndex];
            }
            if (Sim.Ended)
            {
                FinishShift();
                StartCoroutine(ShotsFinish());
            }
        }

        System.Collections.IEnumerator ShotsFinish()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            Shot("09-report");
            var r = _report;
            var sb = new StringBuilder();
            sb.AppendLine("Night Dock screenshot run, shift " + r.ShiftNumber);
            sb.AppendLine("pallets " + r.TotalPallets + ", on time " + r.OnTime + ", misroutes " + r.Misroutes + ", load order " + r.LoadOrderViolations + ", left behind " + r.LeftBehind);
            sb.AppendLine("revenue " + r.Revenue + ", penalties " + r.Penalties + ", costs " + r.Costs + ", profit " + r.Profit + ", stars " + r.Stars);
            sb.AppendLine("fps (last frame) " + (1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)).ToString("0"));
            File.WriteAllText(Path.Combine(_shotsDir, "shots-result.txt"), sb.ToString());
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit();
        }

        void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_shotsDir, name + ".png"));
        }

        // ------------------------------------------------------------------ UI (gray-box IMGUI)

        GUIStyle _h1, _h2, _body, _small, _panel, _btn, _toastStyle;

        void Styles()
        {
            if (_h1 != null) return;
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _h1.normal.textColor = new Color(1f, 0.82f, 0.25f);
            _h2 = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            _h2.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true, richText = true };
            _body.normal.textColor = new Color(0.92f, 0.93f, 0.95f);
            _small = new GUIStyle(_body) { fontSize = 16 };
            _panel = new GUIStyle(GUI.skin.box);
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.1f, 0.88f));
            bg.Apply();
            _panel.normal.background = bg;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 18, padding = new RectOffset(10, 10, 6, 6) };
            _toastStyle = new GUIStyle(_body) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _toastStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);
        }

        Rect Panel(Rect r)
        {
            GUI.Box(r, GUIContent.none, _panel);
            _uiRects.Add(r);
            return r;
        }

        void OnGUI()
        {
            Styles();
            if (Event.current.type == EventType.Layout) _uiRects.Clear();
            _uiScale = Mathf.Max(0.6f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(_uiScale, _uiScale, 1f));
            float W = Screen.width / _uiScale, H = Screen.height / _uiScale;

            switch (_state)
            {
                case State.Title: DrawTitle(W, H); break;
                case State.Planning: DrawPlanning(W, H); break;
                case State.Running: DrawHud(W, H); if (_menu) DrawMenu(W, H); break;
                case State.Report: DrawReport(W, H); break;
                case State.Bankrupt: DrawBankrupt(W, H); break;
            }

            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                var r = new Rect(W / 2f - 420f, H - 170f, 840f, 50f);
                GUI.Box(r, GUIContent.none, _panel);
                GUI.Label(r, _toast, _toastStyle);
            }
        }

        void DrawTitle(float W, float H)
        {
            var r = Panel(new Rect(W / 2f - 330f, H / 2f - 260f, 660f, 520f));
            GUILayout.BeginArea(new Rect(r.x + 30f, r.y + 20f, r.width - 60f, r.height - 40f));
            GUILayout.Label("NIGHT DOCK", _h1, GUILayout.Height(90f));
            GUILayout.Label("Run the overnight freight terminal. Every truck out by 6:00 AM.", _body);
            GUILayout.Space(20f);
            var saved = _titleSave;
            if (saved != null && !saved.Bankrupt)
            {
                if (GUILayout.Button("Continue   (shift " + saved.ShiftNumber + ", $" + saved.Cash.ToString("N0") + ")", _btn, GUILayout.Height(54f))) ContinueGame();
                GUILayout.Space(8f);
            }
            if (GUILayout.Button(saved != null ? "New game (replaces your save)" : "New game", _btn, GUILayout.Height(54f))) NewGame();
            GUILayout.Space(8f);
            if (GUILayout.Button("Quit", _btn, GUILayout.Height(44f))) Application.Quit();
            GUILayout.Space(18f);
            GUILayout.Label("Gray-box build: plain boxes on purpose. Art comes after the shift is fun.", _small);
            GUILayout.EndArea();
        }

        void DrawPlanning(float W, float H)
        {
            var r = Panel(new Rect(W / 2f - 380f, 70f, 760f, 620f));
            GUILayout.BeginArea(new Rect(r.x + 28f, r.y + 18f, r.width - 56f, r.height - 36f));
            GUILayout.Label("Shift " + Sim.ShiftNumber + "  ·  Tonight's freight", _h2);
            int last = 0;
            foreach (var t in Sim.Inbound) last = Mathf.Max(last, (int)t.ArrivalMinute);
            GUILayout.Label(Sim.Pallets.Count + " pallets on " + Sim.Inbound.Count + " inbound trailers, arriving 10:00 PM to about " + ShiftSim.ClockText(last) + ".", _body);
            for (int route = 0; route < Routes.Count; route++)
            {
                int n = 0, w = 0;
                foreach (var p in Sim.Pallets) if (p.Route == route) { n++; w += p.Weight; }
                int doors = 0;
                foreach (var d in Sim.Doors) if (d.Route == route) doors++;
                GUILayout.Label("<b>" + Routes.Letters[route] + "  " + Routes.Names[route] + "</b>: " + n + " pallets, " + w.ToString("N0") + " lb, " + doors + " doors", _body);
            }
            GUILayout.Space(8f);
            GUILayout.Label("Crew: " + C.Forklifts + " forklifts" + (C.FastScanner ? ", fast scanner" : "") + ".  Crew cost tonight $" + C.ShiftCost(B).ToString("N0") + ".  Cash $" + C.Cash.ToString("N0") + ".", _body);
            GUILayout.Space(10f);
            GUILayout.Label("<b>How it works</b>", _body);
            GUILayout.Label("1. Click a pallet in a docked trailer, then click a door (or press 1-" + Sim.Doors.Length + "). 0 sends it to staging.", _small);
            GUILayout.Label("2. Pallet labels are route letter + stop: N3 = North Loop, stop 3. Doors show their route letter.", _small);
            GUILayout.Label("3. Load the last stop first: a pallet loaded after a lower stop blocks it (-$" + B.PenaltyLoadOrder + "). Stage pallets to fix the order.", _small);
            GUILayout.Label("4. Trailers hold " + B.TrailerSpots + " pallets and " + B.TrailerWeightLimit.ToString("N0") + " lb. Click a door to dispatch it early; a fresh trailer arrives " + (int)B.NewTrailerDelay + " min later.", _small);
            GUILayout.Label("5. Everything leaves at 6:00 AM. Pallets still on the floor are left behind (-$" + B.PenaltyLeftBehind + ").", _small);
            GUILayout.Label("Space pause · F fast · WASD/edge pan · scroll zoom · Q/E rotate · Esc menu", _small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Start shift  (10:00 PM)", _btn, GUILayout.Height(56f))) StartShift();
            GUILayout.EndArea();
        }

        void DrawHud(float W, float H)
        {
            // Top bar.
            var top = Panel(new Rect(10f, 10f, W - 20f, 54f));
            int busy = 0; foreach (var f in Sim.Forklifts) if (f.Busy) busy++;
            string status = (_paused ? "PAUSED" : (_speed == 1 ? "1x" : "2x FAST"));
            GUI.Label(new Rect(top.x + 16f, top.y + 8f, 900f, 40f),
                "<b>" + ShiftSim.ClockText(Sim.Minute) + "</b>   " + status + "    Shift " + Sim.ShiftNumber +
                "    Delivered " + Sim.PalletsDelivered + "/" + Sim.Pallets.Count +
                "    Forklifts busy " + busy + "/" + Sim.Forklifts.Length + "    Queue " + Sim.Queue.Count +
                "    Staging " + (B.StagingSlots - Sim.FreeStagingSlots()) + "/" + B.StagingSlots, _body);
            float bx = top.xMax - 360f;
            if (GUI.Button(new Rect(bx, top.y + 8f, 110f, 38f), _paused ? "Resume" : "Pause", _btn)) _paused = !_paused;
            if (GUI.Button(new Rect(bx + 118f, top.y + 8f, 110f, 38f), _speed == 1 ? "Fast" : "Normal", _btn)) _speed = _speed == 1 ? 2 : 1;
            if (GUI.Button(new Rect(bx + 236f, top.y + 8f, 110f, 38f), "Menu", _btn)) _menu = true;

            DrawDoorsPanel(W, H);
            DrawInboundPanel(W, H);
            if (View.SelectedPallet >= 0) DrawPalletPanel(W, H);
            else if (View.SelectedDoor >= 0) DrawDoorPanel(W, H);

            // Log.
            var log = Panel(new Rect(10f, H - 130f, 620f, 120f));
            int start = Mathf.Max(0, Sim.Log.Count - 4);
            var sb = new StringBuilder();
            for (int i = start; i < Sim.Log.Count; i++) sb.AppendLine(Sim.Log[i]);
            GUI.Label(new Rect(log.x + 12f, log.y + 8f, log.width - 24f, log.height - 16f), sb.ToString(), _small);

            if (AllFreightHandled())
            {
                var r = Panel(new Rect(W / 2f - 200f, 80f, 400f, 70f));
                if (GUI.Button(new Rect(r.x + 12f, r.y + 10f, r.width - 24f, 50f), "All freight handled: close out shift", _btn)) Sim.EndShift();
            }
        }

        bool AllFreightHandled()
        {
            if (Sim.Jobs.Count > 0) return false;
            foreach (var p in Sim.Pallets)
                if (p.Loc != PalletLoc.Delivered && p.Loc != PalletLoc.Outbound) return false;
            return true;
        }

        void DrawDoorsPanel(float W, float H)
        {
            float h = 44f + Sim.Doors.Length * 40f;
            var r = Panel(new Rect(W - 330f, 74f, 320f, h));
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, 300f, 30f), "<b>Outbound doors</b>", _body);
            for (int i = 0; i < Sim.Doors.Length; i++)
            {
                var d = Sim.Doors[i];
                string s = d.Name + " " + Routes.Letters[d.Route] + "   ";
                if (!d.TrailerPresent) s += "new trailer " + Mathf.CeilToInt(d.NewTrailerTimer) + "m";
                else
                {
                    int w = Sim.DoorWeight(i);
                    s += d.Load.Count + "/" + B.TrailerSpots + "   " + (w / 1000f).ToString("0.0") + "k lb" + (w > B.TrailerWeightLimit ? " OVER" : "");
                }
                if (GUI.Button(new Rect(r.x + 10f, r.y + 38f + i * 40f, 300f, 36f), (View.SelectedDoor == i ? "> " : "") + s, _btn))
                {
                    if (View.SelectedPallet >= 0) RouteSelectedToDoor(i);
                    else { View.SelectedDoor = i; }
                }
            }
        }

        void DrawInboundPanel(float W, float H)
        {
            var upcoming = new List<InboundTrailer>();
            int yard = 0;
            foreach (var t in Sim.Inbound)
            {
                if (t.State == InboundState.Scheduled) upcoming.Add(t);
                if (t.State == InboundState.Yard) yard++;
            }
            int rows = Mathf.Min(4, upcoming.Count);
            var r = Panel(new Rect(W - 330f, 84f + 44f + Sim.Doors.Length * 40f, 320f, 70f + rows * 26f));
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, 300f, 30f), "<b>Inbound</b>   " + yard + " waiting in yard", _body);
            for (int i = 0; i < rows; i++)
            {
                var t = upcoming[i];
                GUI.Label(new Rect(r.x + 12f, r.y + 36f + i * 26f, 300f, 26f), "T" + (t.Id + 1) + "  due " + ShiftSim.ClockText(t.ArrivalMinute) + "   " + t.PalletIds.Count + " pallets", _small);
            }
            if (rows == 0) GUI.Label(new Rect(r.x + 12f, r.y + 36f, 300f, 26f), "No more trucks tonight", _small);
        }

        void DrawPalletPanel(float W, float H)
        {
            var p = Sim.Pallets[View.SelectedPallet];
            var r = Panel(new Rect(10f, 74f, 360f, 420f));
            GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, r.height - 20f));
            GUILayout.Label("Pallet " + p.Label, _h2);
            GUILayout.Label(Routes.Names[p.Route] + ", stop " + p.Stop + "\n" + p.Consignee + "\nPro " + p.Pro + "    " + p.Weight.ToString("N0") + " lb", _body);
            GUILayout.Label("Now: " + Where(p), _small);
            GUILayout.Space(6f);
            if (p.JobId >= 0)
            {
                if (GUILayout.Button("Cancel move  (X)", _btn, GUILayout.Height(40f))) CancelSelected();
            }
            else if (Sim.IsAccessible(p))
            {
                for (int i = 0; i < Sim.Doors.Length; i++)
                {
                    var d = Sim.Doors[i];
                    if (d.Route != p.Route) continue;
                    string label = "Load on " + d.Name + "  (" + (i + 1) + ")   " + Sim.DoorCommitted(i) + "/" + B.TrailerSpots + (d.TrailerPresent ? "" : "  no trailer");
                    if (GUILayout.Button(label, _btn, GUILayout.Height(40f))) RouteSelectedToDoor(i);
                }
                if (p.Loc != PalletLoc.Staging && GUILayout.Button("To staging  (0)", _btn, GUILayout.Height(40f))) RouteSelectedToStaging();
            }
            else GUILayout.Label("A forklift can't reach this pallet yet.", _small);
            GUILayout.EndArea();
        }

        string Where(Pallet p)
        {
            switch (p.Loc)
            {
                case PalletLoc.InboundTrailer:
                    var t = Sim.Inbound[p.LocIndex];
                    return "in trailer T" + (t.Id + 1) + (t.State == InboundState.Docked ? " at I" + (t.Door + 1) : " (not docked)");
                case PalletLoc.Staging: return "staging";
                case PalletLoc.OnForklift: return "on forklift F" + (p.LocIndex + 1);
                case PalletLoc.Outbound: return "loaded on " + Sim.Doors[p.LocIndex].Name + ", position " + (Sim.Doors[p.LocIndex].Load.IndexOf(p.Id) + 1);
                case PalletLoc.Delivered: return "gone with its truck";
                default: return "left behind";
            }
        }

        void DrawDoorPanel(float W, float H)
        {
            int i = View.SelectedDoor;
            var d = Sim.Doors[i];
            var r = Panel(new Rect(10f, 74f, 380f, 480f));
            GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, r.height - 20f));
            GUILayout.Label("Door " + d.Name + "  ·  " + Routes.Letters[d.Route] + " " + Routes.Names[d.Route], _h2);
            if (!d.TrailerPresent)
            {
                GUILayout.Label("Trailer gone. A new one arrives in " + Mathf.CeilToInt(d.NewTrailerTimer) + " min.", _body);
                GUILayout.EndArea();
                return;
            }
            int w = Sim.DoorWeight(i);
            GUILayout.Label(d.Load.Count + "/" + B.TrailerSpots + " pallets   " + w.ToString("N0") + " / " + B.TrailerWeightLimit.ToString("N0") + " lb", _body);
            var preview = Sim.Evaluate(d);
            GUILayout.Label("If it left now: " + preview.OnTime + " on route, " + preview.Misroutes + " wrong route, " + preview.LoadOrderViolations + " out of order", _small);
            GUILayout.Label("Load (nose first): ", _small);
            var sb = new StringBuilder();
            foreach (int id in d.Load) sb.Append(Sim.Pallets[id].Label).Append("  ");
            GUILayout.Label(sb.Length > 0 ? sb.ToString() : "empty", _body);
            GUILayout.Space(8f);
            string why = Sim.CanDispatch(i);
            GUI.enabled = why == null;
            if (GUILayout.Button("Dispatch now  (Enter)", _btn, GUILayout.Height(46f))) DispatchSelected();
            GUI.enabled = true;
            if (why != null) GUILayout.Label(why, _small);
            if (d.Load.Count > 0 && GUILayout.Button("Pull last pallet off to staging", _btn, GUILayout.Height(40f)))
            {
                string err = Sim.RouteToStaging(d.Load[d.Load.Count - 1]);
                if (err != null) Toast(err);
            }
            GUILayout.EndArea();
        }

        void DrawMenu(float W, float H)
        {
            var r = Panel(new Rect(W / 2f - 220f, H / 2f - 210f, 440f, 420f));
            GUILayout.BeginArea(new Rect(r.x + 24f, r.y + 18f, r.width - 48f, r.height - 36f));
            GUILayout.Label("Paused", _h2);
            if (GUILayout.Button("Resume", _btn, GUILayout.Height(46f))) _menu = false;
            if (GUILayout.Button(Screen.fullScreen ? "Windowed" : "Fullscreen", _btn, GUILayout.Height(40f))) Screen.fullScreen = !Screen.fullScreen;
            var names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            if (names.Length > 0 && GUILayout.Button("Graphics: " + names[q], _btn, GUILayout.Height(40f))) QualitySettings.SetQualityLevel((q + 1) % names.Length, true);
            GUILayout.Label("Leaving mid-shift restarts this shift next time (saves happen after each shift).", _small);
            if (GUILayout.Button("Quit to title", _btn, GUILayout.Height(40f))) { _menu = false; _state = State.Title; BuildBackdrop(); }
            if (GUILayout.Button("Quit game", _btn, GUILayout.Height(40f))) Application.Quit();
            GUILayout.EndArea();
        }

        void DrawReport(float W, float H)
        {
            var r = _report;
            var p = Panel(new Rect(W / 2f - 400f, 60f, 800f, 700f));
            GUILayout.BeginArea(new Rect(p.x + 28f, p.y + 18f, p.width - 56f, p.height - 36f));
            GUILayout.Label("Shift " + r.ShiftNumber + " report  ·  " + r.Stars + " / 5 stars", _h2);
            GUILayout.Label(r.OnTime + " of " + r.TotalPallets + " pallets delivered on the right truck.", _body);
            GUILayout.Label("Revenue  +$" + r.Revenue.ToString("N0") + "   (" + r.OnTime + " x $" + B.PayPerPallet + ")", _body);
            if (r.LoadOrderViolations > 0) GUILayout.Label("Out of load order  -$" + (r.LoadOrderViolations * B.PenaltyLoadOrder).ToString("N0") + "   (" + r.LoadOrderViolations + ")", _body);
            if (r.Misroutes > 0) GUILayout.Label("Wrong route  -$" + (r.Misroutes * B.PenaltyMisroute).ToString("N0") + "   (" + r.Misroutes + ")", _body);
            if (r.LeftBehind > 0) GUILayout.Label("Left behind  -$" + (r.LeftBehind * B.PenaltyLeftBehind).ToString("N0") + "   (" + r.LeftBehind + ")", _body);
            GUILayout.Label("Crew and forklifts  -$" + r.Costs.ToString("N0"), _body);
            GUILayout.Label("<b>Profit " + (r.Profit >= 0 ? "+$" : "-$") + Mathf.Abs(r.Profit).ToString("N0") + "    Cash $" + C.Cash.ToString("N0") + "</b>", _body);
            GUILayout.Label("Next shift: " + B.PalletsForShift(C.ShiftNumber) + " pallets.", _small);

            GUILayout.Space(14f);
            GUILayout.Label("Upgrades", _h2);
            GUI.enabled = C.CanBuyForklift(B);
            if (GUILayout.Button("Extra forklift  $" + B.CostForklift.ToString("N0") + "   (" + C.Forklifts + "/" + B.MaxForklifts + ", +$" + B.ForkliftCostPerShift + " per shift)", _btn, GUILayout.Height(42f)))
            { C.BuyForklift(B); SaveSystem.Save(C); }
            GUI.enabled = C.CanBuyScanner(B);
            if (GUILayout.Button(C.FastScanner ? "Faster scanner  (owned)" : "Faster scanner  $" + B.CostScanner.ToString("N0") + "   (all moves 20% quicker)", _btn, GUILayout.Height(42f)))
            { C.BuyScanner(B); SaveSystem.Save(C); }
            GUI.enabled = C.CanBuyDoor(B);
            GUILayout.BeginHorizontal();
            GUILayout.Label(C.ExtraDoorRoutes.Count >= B.MaxExtraDoors ? "Extra door: owned (O7 " + Routes.Letters[C.ExtraDoorRoutes[0]] + ")" : "Extra door $" + B.CostExtraDoor.ToString("N0") + " for:", _body, GUILayout.Width(250f));
            for (int route = 0; route < Routes.Count; route++)
                if (GUILayout.Button(Routes.Letters[route] + " " + Routes.Names[route], _btn, GUILayout.Height(42f))) { C.BuyDoor(B, route); SaveSystem.Save(C); }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Next shift", _btn, GUILayout.Height(56f))) StartPlanning();
            if (GUILayout.Button("Title screen", _btn, GUILayout.Height(56f), GUILayout.Width(200f))) { _state = State.Title; BuildBackdrop(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawBankrupt(float W, float H)
        {
            var r = Panel(new Rect(W / 2f - 300f, H / 2f - 160f, 600f, 320f));
            GUILayout.BeginArea(new Rect(r.x + 28f, r.y + 20f, r.width - 56f, r.height - 40f));
            GUILayout.Label("Terminal closed", _h2);
            GUILayout.Label("Cash fell below -$" + Mathf.Abs(B.BankruptCash).ToString("N0") + ". The landlord changed the locks.", _body);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Start over", _btn, GUILayout.Height(54f))) NewGame();
            if (GUILayout.Button("Title screen", _btn, GUILayout.Height(44f))) { _state = State.Title; BuildBackdrop(); }
            GUILayout.EndArea();
        }
    }
}
