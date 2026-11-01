using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Ctrl+Shift+M feedback marker: pauses (the UI pause, same as the game's own), takes a screenshot, writes marker-N.json,
    /// shows a note box (Enter = save the note, Esc = no note) and resumes. During an auto-test the files go to the run folder's
    /// markers\ folder, otherwise to the log folder's markers\ folder.
    /// </summary>
    public static class AtMarkers
    {
        /// <summary>True while the note box is up. AtPatches blocks the game's own Esc handling meanwhile.</summary>
        public static bool NoteOpen;

        private static bool busy;
        private static bool pausedByUs;
        private static AtMarker marker;
        private static string jsonPath = "", pngPath = "", source = "";
        private static bool pngOk;
        private static string noteText = "";
        private static bool focusSet;

        public static void Reset()
        {
            try
            {
                if (pausedByUs && Game.instance != null) Game.instance.time.UnPause(GameTimer.PauseType.UI);
            }
            catch { }
            NoteOpen = false; busy = false; pausedByUs = false; marker = null;
        }

        public static string MarkerDir()
        {
            AtRunCtx r = AtRun.Current;
            if (r != null && r.ItemActive) return Path.Combine(r.RunDir, "markers");
            return Path.Combine(Main.LogDir, "markers");
        }

        public static void Tick(AtBehaviour host)
        {
            Settings s = Main.Settings;
            if (s == null || !s.TestMarkerKey || !Main.Enabled || busy) return;
            if (!Input.GetKeyDown(KeyCode.M)) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl || !shift) return;
            Begin(host, "hotkey", true);
        }

        /// <summary>The self test of a request ("markerSelfTest": true): the same path as the hotkey, without the note box.</summary>
        public static void SelfTest(AtBehaviour host)
        {
            Begin(host, "selftest", false);
        }

        private static void Begin(AtBehaviour host, string src, bool interactive)
        {
            if (busy) return;
            Game g = Game.instance;
            if (g == null || g.sessionManager == null || !g.sessionManager.isCircuitLoaded)
            {
                Main.Log("Real Tracks marker: only available while a race or session is on screen");
                return;
            }
            busy = true;
            source = src;
            try
            {
                string dir = MarkerDir();
                int n = NextNumber(dir);
                marker = Collect(g, n, src);
                string baseName = "marker-" + n;
                jsonPath = Path.Combine(dir, baseName + ".json");
                pngPath = Path.Combine(dir, baseName + ".png");
                marker.Screenshot = baseName + ".png";
                pausedByUs = false;
                if (!g.time.IsPauseTypeActive(GameTimer.PauseType.UI))
                {
                    g.time.Pause(GameTimer.PauseType.UI);
                    pausedByUs = true;
                }
                pngOk = false;
                bool wantNote = interactive;
                host.Capture(pngPath, ok =>
                {
                    pngOk = ok;
                    if (wantNote) { noteText = ""; focusSet = false; NoteOpen = true; }
                    else Finish("");
                });
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks marker failed: " + e);
                Reset();
            }
        }

        private static void Finish(string note)
        {
            try
            {
                if (marker != null)
                {
                    marker.Note = note ?? "";
                    AtIo.WriteText(jsonPath, marker.ToJson());
                    Main.Log("Real Tracks marker " + marker.Number + " saved: " + jsonPath + (string.IsNullOrEmpty(note) ? "" : " (" + note + ")"));
                    AtRunCtx r = AtRun.Current;
                    if (r != null && r.ItemActive)
                    {
                        AtItemData d = r.Cur;
                        if (source == "selftest")
                        {
                            d.MarkerJson = File.Exists(jsonPath) && new FileInfo(jsonPath).Length > 0;
                            d.MarkerPng = pngOk && File.Exists(pngPath);
                            d.MarkerNote = "marker " + marker.Number + " at lap " + marker.Lap + ", nearest corner " + marker.NearestCorner + ", " + marker.Cars.Count + " cars";
                        }
                        AtRun.Event("marker", "number", marker.Number, "source", source, "note", marker.Note, "png", pngOk, "lap", marker.Lap, "corner", marker.NearestCorner);
                    }
                }
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks marker: writing failed: " + e); }
            NoteOpen = false;
            try
            {
                if (pausedByUs && Game.instance != null) Game.instance.time.UnPause(GameTimer.PauseType.UI);
            }
            catch { }
            pausedByUs = false; busy = false; marker = null;
        }

        public static void DrawNoteBox(AtBehaviour host)
        {
            if (!NoteOpen) return;
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); Finish(noteText); return; }
                if (e.keyCode == KeyCode.Escape) { e.Use(); Finish(""); return; }
            }
            float w = Mathf.Min(640f, Screen.width - 20f);
            Rect r = new Rect((Screen.width - w) / 2f, Screen.height * 0.35f, w, 96f);
            GUI.Box(r, "Marker " + (marker != null ? marker.Number.ToString() : "") + " — what did you see?  (Enter = save note, Esc = no note)");
            GUI.SetNextControlName("rtMarkerNote");
            noteText = GUI.TextField(new Rect(r.x + 10, r.y + 40, r.width - 20, 26), noteText ?? "", 400);
            if (!focusSet) { GUI.FocusControl("rtMarkerNote"); focusSet = true; }
            GUI.Label(new Rect(r.x + 10, r.y + 70, r.width - 20, 20), "The game is paused until you press Enter or Esc.");
        }

        private static int NextNumber(string dir)
        {
            int max = 0;
            try
            {
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "marker-*.json");
                    for (int i = 0; i < files.Length; i++)
                    {
                        string n = Path.GetFileNameWithoutExtension(files[i]).Substring("marker-".Length);
                        int v;
                        if (int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v > max) max = v;
                    }
                }
            }
            catch { }
            return max + 1;
        }

        private static AtMarker Collect(Game g, int number, string src)
        {
            SessionManager sm = g.sessionManager;
            AtMarker m = new AtMarker();
            m.Number = number;
            m.Source = src;
            m.UtcTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            AtRunCtx r = AtRun.Current;
            m.RealSeconds = r != null ? Time.realtimeSinceStartup - r.T0 : Time.realtimeSinceStartup;
            m.SimSeconds = AtSim.SimT;
            try
            {
                Circuit c = sm.eventDetails.circuit;
                m.Location = c.locationName; m.Layout = c.GetTrackVariation();
            }
            catch { }
            try { m.Session = sm.sessionType.ToString(); m.Flag = sm.flag.ToString(); m.Lap = sm.lap; m.LapCount = sm.lapCount; } catch { }

            Vector3 cam = Vector3.zero;
            try
            {
                Camera c = App.instance.cameraManager.GetCamera();
                if (c != null)
                {
                    cam = c.transform.position;
                    m.CamPos = new[] { cam.x, cam.y, cam.z };
                    Vector3 e = c.transform.eulerAngles;
                    m.CamRot = new[] { e.x, e.y, e.z };
                    m.CamFov = c.fieldOfView;
                }
            }
            catch { }

            try
            {
                PathData td = sm.circuit.GetTrackPath().data;
                m.TrackLengthM = td.length;
                float bestG = float.MaxValue;
                for (int i = 0; i < td.gates.Count; i++)
                {
                    Vector3 p = td.gates[i].position;
                    float d2 = (p.x - cam.x) * (p.x - cam.x) + (p.z - cam.z) * (p.z - cam.z);
                    if (d2 < bestG) { bestG = d2; m.LapDistanceM = td.gates[i].startDistance; }
                }
                float bestC = float.MaxValue;
                for (int i = 0; i < td.corners.Count; i++)
                {
                    PathData.Corner c = td.corners[i];
                    int mid = Mathf.Clamp((c.startGateID + c.endGateID) / 2, 0, td.gates.Count - 1);
                    Vector3 p = td.gates[mid].position;
                    float d2 = (p.x - cam.x) * (p.x - cam.x) + (p.z - cam.z) * (p.z - cam.z);
                    if (d2 < bestC) { bestC = d2; m.NearestCorner = i + 1; m.NearestCornerDistanceM = Mathf.Sqrt(d2); }
                }
            }
            catch { }

            try
            {
                VehicleManager vm = g.vehicleManager;
                for (int i = 0; i < vm.vehicleCount; i++)
                {
                    RacingVehicle v = vm.GetVehicle(i);
                    AtMarkerCar mc = new AtMarkerCar();
                    mc.Name = AtSim.CarName(v); mc.Team = AtSim.TeamName(v);
                    mc.Position = v.standingsPosition; mc.Lap = v.timer.lap + 1;
                    Vector3 p = v.transform.position;
                    mc.X = p.x; mc.Y = p.y; mc.Z = p.z;
                    mc.SpeedKph = v.speed * 3.6f;
                    mc.OnTrack = v.pathState.pathStateGroup == PathStateManager.PathStateGroup.OnTrack;
                    mc.State = v.pathState.stateType + "/" + v.pathController.currentPathType + (v.behaviourManager.isOutOfRace ? "/out" : "");
                    m.Cars.Add(mc);
                }
                m.Cars.Sort((a, b) => a.Position.CompareTo(b.Position));
            }
            catch { }
            return m;
        }
    }
}
