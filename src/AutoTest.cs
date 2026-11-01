using System;
using System.IO;
using UnityEngine;

namespace RealTracks
{
    /// <summary>Auto-test and feedback markers (WP4). Every behaviour has a Test* setting (Ctrl+F10 panel).</summary>
    public partial class Settings
    {
        public bool TestEnabled = true;
        public bool TestQuitWhenDone = true;
        public string TestRequestFolder = "";
        public int TestTimeoutMinutes = 30;
        public bool TestScreenshots = true;
        public bool TestMarkerKey = true;
        public int TestMaxSimScale = 20;
        public bool TestForceStops = true;
        public bool TestUnattendedMode = true;
        public bool TestOverlay = true;
        public int TestGateEvery = 5;
        public int TestRaceShots = 3;
        public float TestStuckSeconds = 10f;
        public float TestTeleportMeters = 30f;
        public bool TestSkipMovies = true;
        public bool TestAiDrivers = true;
        public bool TestClampSessionTime = true;
        /// <summary>Log visual car-body clearance from the authored road edge in every pack Quick Race; diagnostic only.</summary>
        public bool TestRoadEdgeMeasure = true;
        public int TestFpsWindowSeconds = 0;   // superseded by the 1x smoothness window (TestSmoothSeconds)
        public int TestSmoothSeconds = 20;
        public int TestVideoSeconds = 12;
        public float TestClipZoom = 0f;        // turn clips: camera distance, 0 = closest the race camera allows, 1 = farthest
        public bool TestTurnClips = true;      // request-driven clips at named turns ("clips" in an item), after the default clip
        public int TestVideoWidth = 1280;
        public int TestVideoQuality = 80;
        public int TestPhaseStuckSeconds = 240;

        public bool TestDetLoaded = true;
        public bool TestDetExceptions = true;
        public bool TestDetGrid = true;
        public bool TestDetLapped = true;
        public bool TestDetStuck = true;
        public bool TestDetTeleport = true;
        public bool TestDetPit = true;
        public bool TestDetIncidents = true;
        public bool TestDetMinimap = true;
        public bool TestDetScreens = true;
        public bool TestDetFps = true;
        public bool TestDetGateSpeeds = true;
        public bool TestDetMarkerSelf = true;
    }

    public static class AutoTest
    {
        private static GameObject sHost;

        public static bool Running { get { return AtRun.Current != null; } }

        public static void Init()
        {
            if (sHost != null) return;
            AtSim.CheckEnumNumbering();
            sHost = new GameObject("RealTracksAutoTest");
            UnityEngine.Object.DontDestroyOnLoad(sHost);
            sHost.AddComponent<AtBehaviour>();
            Application.logMessageReceived += AtRun.OnLog;
            Main.Log("Real Tracks auto-test ready, watching " + AtRun.RequestFolder + " (Ctrl+Shift+M = feedback marker)");
        }

        public static AtCfg Cfg()
        {
            Settings s = Main.Settings;
            AtCfg c = new AtCfg();
            c.Loaded = s.TestDetLoaded; c.Exceptions = s.TestDetExceptions; c.Grid = s.TestDetGrid; c.Lapped = s.TestDetLapped;
            c.Stuck = s.TestDetStuck; c.Teleport = s.TestDetTeleport; c.Pit = s.TestDetPit; c.Incidents = s.TestDetIncidents;
            c.Minimap = s.TestDetMinimap; c.Screens = s.TestDetScreens; c.Fps = s.TestDetFps; c.GateSpeeds = s.TestDetGateSpeeds;
            c.MarkerSelf = s.TestDetMarkerSelf; c.UiFallbacks = s.TestDetUiFallbacks;
            c.StuckSeconds = Mathf.Max(2f, s.TestStuckSeconds);
            c.TeleportMeters = Mathf.Max(5f, s.TestTeleportMeters);
            c.GateEvery = Mathf.Max(1, s.TestGateEvery);
            return c;
        }

        public static void DrawGui(Settings s)
        {
            GUILayout.Label("Auto-test runs Quick Races unattended from autotest\\request.json and writes logs, screenshots and summary.json to the logs folder. It never runs in a career and never saves.");
            s.TestEnabled = GUILayout.Toggle(s.TestEnabled, " Watch for request.json when the main menu is reached");
            s.TestQuitWhenDone = GUILayout.Toggle(s.TestQuitWhenDone, " Quit the game when a run asks for it (the game never saves anything)");
            s.TestScreenshots = GUILayout.Toggle(s.TestScreenshots, " Take screenshots during runs");
            s.TestMarkerKey = GUILayout.Toggle(s.TestMarkerKey, " Ctrl+Shift+M feedback marker in any race (pauses, screenshot, note box)");
            s.TestForceStops = GUILayout.Toggle(s.TestForceStops, " Force one pit stop per team if the AI would finish without stopping");
            s.TestSmoothSeconds = IntSlider("Smoothness window at 1x from lap 2 (seconds, 0 = off)", s.TestSmoothSeconds, 0, 120);
            s.TestVideoSeconds = IntSlider("Video clip after it (seconds of race time, 0 = off)", s.TestVideoSeconds, 0, 60);
            s.TestArtTour = GUILayout.Toggle(s.TestArtTour, " Take the art-tour close-ups at the pre-race hub when a request asks (\"artTour\": true; pack venues only)");
            s.TestResolutions = GUILayout.Toggle(s.TestResolutions, " Resolution matrix when a request asks (\"resolutions\": [\"3840x1080\", ...]): switches the screen resolution at the first item's Quick Race setup screen, takes a screenshot at each and puts the original resolution back");
            s.TestArtTourManifestViews = GUILayout.Toggle(s.TestArtTourManifestViews, " Art tour: use the pack manifest's \"views\" (off = always the generic default set)");
            s.TestTurnClips = GUILayout.Toggle(s.TestTurnClips, " Record the turn clips an auto-test request asks for (\"clips\": turn, lead metres, seconds)");
            s.TestClipZoom = FloatSlider("Turn clips: camera distance (0 = closest, 1 = farthest)", s.TestClipZoom, 0f, 1f);
            s.TestVideoWidth = IntSlider("Video width (pixels)", s.TestVideoWidth, 640, 1920);
            s.TestClampSessionTime = GUILayout.Toggle(s.TestClampSessionTime, " Work around the game's short-race form bug at the flag (auto-test only)");
            s.TestRoadEdgeMeasure = GUILayout.Toggle(s.TestRoadEdgeMeasure, " Measure rendered car body against authored road edge (pack-race diagnostic; no handling changes)");
            s.TestUnattendedMode = GUILayout.Toggle(s.TestUnattendedMode, " During runs: keep running in the background and switch vsync off");
            s.TestSkipMovies = GUILayout.Toggle(s.TestSkipMovies, " During runs: skip the intro movies");
            s.TestAiDrivers = GUILayout.Toggle(s.TestAiDrivers, " During runs: the AI drives the player's cars too (hidden game switch)");
            s.TestOverlay = GUILayout.Toggle(s.TestOverlay, " Small status line on screen during runs");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Request folder (empty = Mods\\RealTracks\\autotest):", GUILayout.ExpandWidth(false));
            s.TestRequestFolder = GUILayout.TextField(s.TestRequestFolder ?? "", GUILayout.Width(420));
            GUILayout.EndHorizontal();
            s.TestTimeoutMinutes = IntSlider("Timeout per race (minutes)", s.TestTimeoutMinutes, 2, 120);
            s.TestMaxSimScale = IntSlider("Max simulation steps per frame (\"max\" speed)", s.TestMaxSimScale, 1, 60);
            s.TestGateEvery = IntSlider("Gate speed sample every N gates", s.TestGateEvery, 1, 50);
            s.TestRaceShots = IntSlider("Race screenshots per item", s.TestRaceShots, 0, 10);
            s.TestFpsWindowSeconds = IntSlider("fps measured at normal speed for the first N seconds of the race (0 = whole race at max)", s.TestFpsWindowSeconds, 0, 60);
            s.TestPhaseStuckSeconds = IntSlider("Fail when a menu step makes no progress for N seconds", s.TestPhaseStuckSeconds, 60, 900);
            s.TestStuckSeconds = FloatSlider("Stuck: seconds under 1 m", s.TestStuckSeconds, 3f, 60f);
            s.TestTeleportMeters = FloatSlider("Teleport: metres in one step", s.TestTeleportMeters, 5f, 200f);

            GUILayout.Label("<b>Detectors</b> (a detector that is off is left out of summary.json)");
            s.TestDetLoaded = GUILayout.Toggle(s.TestDetLoaded, " loaded: scene and circuit");
            s.TestDetExceptions = GUILayout.Toggle(s.TestDetExceptions, " exceptions: Error and Exception log lines");
            s.TestDetGrid = GUILayout.Toggle(s.TestDetGrid, " gridFormed: all cars on grid boxes");
            s.TestDetLapped = GUILayout.Toggle(s.TestDetLapped, " allCarsLapped");
            s.TestDetStuck = GUILayout.Toggle(s.TestDetStuck, " noStuck");
            s.TestDetTeleport = GUILayout.Toggle(s.TestDetTeleport, " noTeleports");
            s.TestDetPit = GUILayout.Toggle(s.TestDetPit, " pitRoundTrips");
            s.TestDetIncidents = GUILayout.Toggle(s.TestDetIncidents, " incidentsClean (crash and run-wide paths)");
            s.TestDetMinimap = GUILayout.Toggle(s.TestDetMinimap, " minimapOnOutline");
            s.TestDetScreens = GUILayout.Toggle(s.TestDetScreens, " screensShowData (location name on screens)");
            s.TestDetFps = GUILayout.Toggle(s.TestDetFps, " fps (against a stock item in the same run)");
            s.TestDetGateSpeeds = GUILayout.Toggle(s.TestDetGateSpeeds, " gate_speeds.csv");
            s.TestDetMarkerSelf = GUILayout.Toggle(s.TestDetMarkerSelf, " markerSelfTest (when the request asks)");
            s.TestDetUiFallbacks = GUILayout.Toggle(s.TestDetUiFallbacks, " uiFallbacks (informational: every fallback the mod took during the item, e.g. column -> stock grid)");

            GUILayout.Space(4);
            GUILayout.Label("State: " + (AtRun.Current == null ? "idle" : AtRun.Current.StatusLine()));
            string reqPath = Path.Combine(AtRun.RequestFolder, "request.json");
            GUILayout.Label("Request file: " + reqPath + (File.Exists(reqPath) ? "  (waiting: it runs the next time the main menu is reached)" : "  (none)"));
            GUILayout.Label("Last run: " + AtRun.LastRunText());
            if (GUILayout.Button("Write a sample request.json (Austin A 5 laps, Phoenix B 3 laps)", GUILayout.Width(460)))
            {
                try { AtRun.WriteSampleRequest(); }
                catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: could not write the sample request: " + e.Message); }
            }
        }

        private static int IntSlider(string label, int v, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + v, GUILayout.Width(340));
            v = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(220)));
            GUILayout.EndHorizontal();
            return v;
        }

        private static float FloatSlider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + v.ToString("0.#"), GUILayout.Width(340));
            v = Mathf.Round(GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(220)) * 2f) / 2f;
            GUILayout.EndHorizontal();
            return v;
        }
    }
}
