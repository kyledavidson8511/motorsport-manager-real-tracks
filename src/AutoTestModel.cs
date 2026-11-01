using System;
using System.Collections.Generic;
using System.Globalization;

// Unity-free (see AutoTestJson.cs).
namespace RealTracks
{
    // ------------------------------------------------------------------ request

    public class AtItemRequest
    {
        public string Location = "";
        public string Layout = "A";
        public int Laps = 5;
        /// <summary>Make the game crash this many cars (CrashDirector.AddCrash while its crash window is open). 0 = leave it to the game/Chaos.</summary>
        public int ForceCrashes = 0;
        public bool SteerPitchAlign = false;   // test only: force the pitch-alignment steering fix on at a stock venue (A/B pace measurement)
        /// <summary>Optional turn-targeted clips (AutoTestClips.cs); empty = none, nothing changes.</summary>
        public List<AtClipRequest> Clips = new List<AtClipRequest>();
        /// <summary>Close-up art screenshots at the pre-race hub (ArtTour.cs); the run-level flag is the default, an item can override it. Pack venues only.</summary>
        public bool ArtTour = false;
    }

    /// <summary>autotest\request.json (see the plan's "Auto-test request").</summary>
    public class AtRequest
    {
        public string RunId = "";
        public List<AtItemRequest> Items = new List<AtItemRequest>();
        /// <summary>"max" (default), "normal" (the game's own fast speed) or a number (sim steps per frame).</summary>
        public string SimSpeed = "max";
        public bool Screenshots = true;
        public bool MarkerSelfTest = false;
        public bool Quit = true;
        /// <summary>Default of every item's "artTour" (request top level "artTour": true turns the tour on for all pack items).</summary>
        public bool ArtTour = false;
        /// <summary>Run option "resolutions": at the Quick Race setup screen of the first item, re-shoot the screen at each of these (AutoTestResolutions.cs). Empty = none.</summary>
        public List<AtResolution> Resolutions = new List<AtResolution>();
        /// <summary>Problems found in the "resolutions" value (entries dropped); logged, never fatal.</summary>
        public List<string> ResolutionErrors = new List<string>();

        public static AtRequest Parse(string json, out string error)
        {
            AtRequest r = new AtRequest();
            Dictionary<string, object> o = AtJson.Parse(json, out error) as Dictionary<string, object>;
            if (o == null)
            {
                if (error == null) error = "request.json is not a JSON object";
                return null;
            }
            r.RunId = Sanitize(AtJson.Str(o, "runId", ""));
            r.SimSpeed = AtJson.Str(o, "simSpeed", "max");
            r.Screenshots = AtJson.Bool(o, "screenshots", true);
            r.MarkerSelfTest = AtJson.Bool(o, "markerSelfTest", false);
            r.Quit = AtJson.Bool(o, "quit", true);
            r.ArtTour = AtJson.Bool(o, "artTour", false);
            object rawRes;
            o.TryGetValue("resolutions", out rawRes);
            r.Resolutions = AtResolution.ParseList(rawRes, r.ResolutionErrors);
            List<object> items = AtJson.Arr(o, "items");
            if (items == null || items.Count == 0) { error = "request.json needs a non-empty \"items\" array"; return null; }
            for (int i = 0; i < items.Count; i++)
            {
                Dictionary<string, object> it = items[i] as Dictionary<string, object>;
                if (it == null) { error = "items[" + i + "] is not an object"; return null; }
                AtItemRequest ir = new AtItemRequest();
                ir.Location = (AtJson.Str(it, "location", "") ?? "").Trim();
                ir.Layout = (AtJson.Str(it, "layout", "A") ?? "A").Trim().ToUpperInvariant();
                ir.Laps = (int)AtJson.Num(it, "laps", 5);
                ir.ForceCrashes = (int)AtJson.Num(it, "forceCrashes", 0);
                ir.SteerPitchAlign = AtJson.Num(it, "steerPitchAlign", 0) > 0.5;
                ir.Clips = AtClips.Parse(it);
                ir.ArtTour = AtJson.Bool(it, "artTour", r.ArtTour);
                if (ir.Location.Length == 0) { error = "items[" + i + "] has no location"; return null; }
                if (ir.Layout.Length != 1 || ir.Layout[0] < 'A' || ir.Layout[0] > 'F') { error = "items[" + i + "] layout must be A to F"; return null; }
                if (ir.Laps < 1) ir.Laps = 1;
                if (ir.Laps > 99) ir.Laps = 99;
                r.Items.Add(ir);
            }
            error = null;
            return r;
        }

        /// <summary>Sim steps per frame. 0 = leave the game's own speed.</summary>
        public float SimScale(float maxScale)
        {
            string s = (SimSpeed ?? "max").Trim().ToLowerInvariant();
            if (s == "" || s == "max") return maxScale;
            if (s == "normal" || s == "default" || s == "game") return 0f;
            float f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return Math.Max(1f, Math.Min(maxScale, f));
            return maxScale;
        }

        /// <summary>File-system safe single path segment.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            char[] bad = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            string t = s.Trim();
            for (int i = 0; i < bad.Length; i++) t = t.Replace(bad[i], '_');
            t = t.Replace("..", "_");
            return t;
        }
    }

    // ------------------------------------------------------------------ settings for the pure code

    /// <summary>Detector switches and thresholds (copied from the Settings panel by the mod, set by hand in the test).</summary>
    public class AtCfg
    {
        public bool UiFallbacks = true;
        public bool Loaded = true, Exceptions = true, Grid = true, Lapped = true, Stuck = true, Teleport = true, Pit = true,
            Incidents = true, Minimap = true, Screens = true, Fps = true, GateSpeeds = true, MarkerSelf = true;
        public float StuckSeconds = 10f;
        public float StuckMeters = 1f;
        public float PitLaneStuckSeconds = 30f;
        public float TeleportMeters = 30f;
        public float IncidentGlitchWindowSeconds = 10f;
        public float MinimapRequiredRatio = 0.95f;
        public int MinimapMinSamples = 20;
        public float FpsMaxLossRatio = 0.20f;
        public int GateEvery = 5;
    }

    // ------------------------------------------------------------------ per item data (filled by the game side, judged by AtChecks)

    public class AtCarData
    {
        public int CarId;
        public string Name = "", Team = "";
        public int TeamIndex;
        public int Laps;
        public int PitIns, PitOuts, PitBoxes, PitTrips;
        public bool PitOpen;           // went into the pit lane and had not come out when the race ended
        public int Stuck, Teleports;
        public bool Finished, Retired;
        public string RetireReason = "";
        public bool ForcedStop;
    }

    public class AtIncident
    {
        public double SimT;
        public string Kind = "";       // crash | runwide | cutcorner
        public string Car = "";
        public int Corner;             // 1-based, 0 = unknown
        public int Lap;
        public bool Glitch;            // a stuck or teleport glitch followed within the window
    }

    public class AtGlitch
    {
        public double SimT;
        public string Kind = "";       // stuck | teleport
        public string Car = "";
        public int Lap;
        public double Meters;
    }

    public class AtScreenScan
    {
        public string Screen = "";     // setup | loading | preRace | hud | results ...
        public string Expected = "";
        public bool Found;
        public int MatchCount;
        public int TextCount;
        public string Sample = "";
        public string DonorLeak = "";  // donor location name found where it should not be
        public string Shot = "";
    }

    public class AtErr
    {
        public string Type = "";       // Error | Exception | Assert
        public string Sig = "";
        public string Message = "";
        public string Stack = "";
        public double T;
    }

    public class AtShot
    {
        public string File = "";
        public string What = "";
    }

    public class AtItemData
    {
        // request
        public int Index;
        public string Location = "", Layout = "A";
        public int LapsTarget;
        public int ForceCrashes, CrashesAdded;
        public bool SteerPitchAlign;

        // what the game loaded
        public bool CircuitLoaded;
        public int CircuitId;
        public string SceneName = "", CircuitLocation = "", CircuitLayout = "", LoadNote = "";
        public bool IsPack, VenueBuilt, InQuickRaceList;
        public int LapsActual = -1;

        // exceptions
        public List<AtErr> Errors = new List<AtErr>();       // first 20 only
        public int ErrorCount, ExceptionCount;
        public Dictionary<string, int> ErrorSigs = new Dictionary<string, int>();   // signature -> count (all)

        // grid
        public bool GridChecked;
        public int GridCars, GridOk;
        public string GridDetail = "";

        // race
        public List<AtCarData> Cars = new List<AtCarData>();
        public List<AtIncident> Incidents = new List<AtIncident>();
        public List<AtGlitch> Glitches = new List<AtGlitch>();
        public int CornerCount;
        public Dictionary<int, int> PerCorner = new Dictionary<int, int>();
        public bool RaceStarted, RaceFinished;
        public double RaceSimSeconds;
        public string Abort = "";       // non-empty: the item did not complete (timeout, stuck state ...)
        public int ForcedStops;
        public int ExpectedStopTeams;

        // minimap
        public bool MapAvailable;
        public int MapSamples, MapHits;
        public string MapNote = "";

        // screens
        public List<AtScreenScan> Scans = new List<AtScreenScan>();

        // fps
        public AtFps Fps = new AtFps();
        public AtSmooth Smooth = new AtSmooth();
        public AtWobble Wobble = new AtWobble();
        public AtWobble WobbleVisual = new AtWobble();   // same filter on the 3D car's visual yaw (diagnostic)
        public string VideoDir = "";      // run-relative folder of the clip frames (video/itemN), empty when none
        public int VideoFrames;
        public List<AtClipRequest> ClipRequests = new List<AtClipRequest>();   // from the request
        public List<AtClipOut> ClipResults = new List<AtClipOut>();            // what happened to them
        public bool ResRequested, ResRestored;                                  // resolution matrix: asked for on this item / the original resolution is back
        public string ResOriginal = "", ResNote = "";
        public List<AtResResult> ResResults = new List<AtResResult>();
        public int FbMark = -1, FbEnd = -1;                                     // RtFallbacks occurrence range of this item (-1 = not started / still open)
        public List<FallbackGroup> Fallbacks = new List<FallbackGroup>();       // RtFallbacks that fired during this item (uiFallbacks)
        public bool ArtTour, ArtTourRan;                                       // art tour requested / finished (AutoTestArtTour.cs)
        public List<string> ArtShots = new List<string>(), ArtSkipped = new List<string>();

        // markers
        public bool MarkerRequested, MarkerJson, MarkerPng;
        public string MarkerNote = "";

        public List<AtShot> Shots = new List<AtShot>();
        public int GateSamples;
    }

    // ------------------------------------------------------------------ summary.json model (the plan's schema)

    public class AtCheck
    {
        public string Name = "";
        public bool Pass;
        public string Detail = "";
        public double Value;
        /// <summary>Shown and recorded, but does not decide the item's pass/fail.</summary>
        public bool Informational;
        public AtCheck() { }
        public AtCheck(string name, bool pass, string detail, double value) { Name = name; Pass = pass; Detail = detail; Value = value; }
    }

    public class AtFpsOut { public int Frames; public double P5, P50, P95; }

    public class AtCarOut
    {
        public string Name = "", Team = "";
        public int Laps, PitIns, PitOuts, Stuck, Teleports;
        public bool Finished;
    }

    public class AtItemSummary
    {
        public int CircuitId;
        public string Location = "", Layout = "";
        public bool IsPack;
        public int LapsTarget;
        public string Result = "fail";
        public List<AtCheck> Checks = new List<AtCheck>();
        public AtFpsOut Fps = new AtFpsOut();
        public AtSmoothOut Smooth = new AtSmoothOut();
        public string VideoDir = "";
        public int VideoFrames;
        public List<AtClipOut> Clips = new List<AtClipOut>();   // only written when the item asked for clips
        public List<AtCarOut> Cars = new List<AtCarOut>();
        public int Crash, Runwide, Glitches;
        public List<int[]> PerCorner = new List<int[]>();
        public List<AtShot> Screenshots = new List<AtShot>();
    }

    public class AtRunSummary
    {
        public string RunId = "", ModVersion = "", StartedUtc = "", FinishedUtc = "";
        public bool QuitClean;
        public List<AtItemSummary> Items = new List<AtItemSummary>();

        public string ToJson()
        {
            JsonOut j = new JsonOut(true);
            j.BeginObject();
            j.Prop("runId", RunId);
            j.Prop("modVersion", ModVersion);
            j.Prop("startedUtc", StartedUtc);
            j.Prop("finishedUtc", FinishedUtc);
            j.Prop("quitClean", QuitClean);
            j.BeginArray("items");
            foreach (AtItemSummary it in Items)
            {
                j.BeginObject();
                j.Prop("circuitId", it.CircuitId);
                j.Prop("location", it.Location);
                j.Prop("layout", it.Layout);
                j.Prop("isPack", it.IsPack);
                j.Prop("lapsTarget", it.LapsTarget);
                j.Prop("result", it.Result);
                j.BeginObject("checks");
                foreach (AtCheck c in it.Checks)
                {
                    j.BeginObject(c.Name);
                    j.Prop("pass", c.Pass);
                    j.Prop("detail", c.Detail);
                    j.Prop("value", c.Value);
                    if (c.Informational) j.Prop("informational", true);
                    j.EndObject();
                }
                j.EndObject();
                j.BeginObject("fps");
                j.Prop("frames", it.Fps.Frames);
                j.Prop("p5", it.Fps.P5);
                j.Prop("p50", it.Fps.P50);
                j.Prop("p95", it.Fps.P95);
                j.EndObject();
                j.BeginObject("smoothness");
                j.Prop("frames", it.Smooth.Frames);
                j.Prop("seconds", it.Smooth.Seconds);
                j.Prop("meanFps", it.Smooth.MeanFps);
                j.Prop("p50Ms", it.Smooth.P50Ms);
                j.Prop("p95Ms", it.Smooth.P95Ms);
                j.Prop("p99Ms", it.Smooth.P99Ms);
                j.Prop("maxMs", it.Smooth.MaxMs);
                j.Prop("stutters", it.Smooth.Stutters);
                j.Prop("stuttersPerMin", it.Smooth.StuttersPerMin);
                j.Prop("hitches", it.Smooth.Hitches);
                j.Prop("hitchesPerMin", it.Smooth.HitchesPerMin);
                j.Prop("diag", it.Smooth.Diag);
                j.EndObject();
                j.BeginObject("video");
                j.Prop("dir", it.VideoDir);
                j.Prop("frames", it.VideoFrames);
                j.EndObject();
                if (it.Clips.Count > 0)
                {
                    j.BeginArray("clips");
                    foreach (AtClipOut c in it.Clips)
                    {
                        j.BeginObject();
                        j.Prop("turn", c.Turn);
                        j.Prop("dir", c.Dir);
                        j.Prop("frames", c.Frames);
                        j.Prop("skipped", c.Skipped);
                        j.Prop("reason", c.Reason);
                        j.Prop("car", c.Car);
                        j.Prop("lap", c.Lap);
                        j.EndObject();
                    }
                    j.EndArray();
                }
                j.BeginArray("cars");
                foreach (AtCarOut c in it.Cars)
                {
                    j.BeginObject();
                    j.Prop("name", c.Name);
                    j.Prop("team", c.Team);
                    j.Prop("laps", c.Laps);
                    j.Prop("pitIns", c.PitIns);
                    j.Prop("pitOuts", c.PitOuts);
                    j.Prop("stuck", c.Stuck);
                    j.Prop("teleports", c.Teleports);
                    j.Prop("finished", c.Finished);
                    j.EndObject();
                }
                j.EndArray();
                j.BeginObject("incidents");
                j.Prop("crash", it.Crash);
                j.Prop("runwide", it.Runwide);
                j.Prop("glitches", it.Glitches);
                j.EndObject();
                j.BeginArray("perCorner");
                foreach (int[] pc in it.PerCorner)
                {
                    j.BeginObject();
                    j.Prop("corner", pc[0]);
                    j.Prop("incidents", pc[1]);
                    j.EndObject();
                }
                j.EndArray();
                j.BeginArray("screenshots");
                foreach (AtShot s in it.Screenshots)
                {
                    j.BeginObject();
                    j.Prop("file", s.File);
                    j.Prop("what", s.What);
                    j.EndObject();
                }
                j.EndArray();
                j.EndObject();
            }
            j.EndArray();
            j.EndObject();
            return j.ToString() + "\n";
        }
    }

    // ------------------------------------------------------------------ frame statistics

    public class AtFps
    {
        private readonly List<double> frameSeconds = new List<double>();

        public int Frames { get { return frameSeconds.Count; } }

        public void Add(double seconds)
        {
            if (seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds)) frameSeconds.Add(seconds);
        }

        /// <summary>fps percentiles from unscaled frame times. p5 is the slow end (the 95th percentile frame time), p95 the fast end.</summary>
        public AtFpsOut Result()
        {
            AtFpsOut o = new AtFpsOut();
            o.Frames = frameSeconds.Count;
            if (o.Frames == 0) return o;
            List<double> s = new List<double>(frameSeconds);
            s.Sort();
            o.P5 = 1.0 / Percentile(s, 0.95);
            o.P50 = 1.0 / Percentile(s, 0.50);
            o.P95 = 1.0 / Percentile(s, 0.05);
            return o;
        }

        /// <summary>Nearest-rank percentile of a sorted list.</summary>
        public static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int idx = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (idx < 0) idx = 0;
            if (idx >= sorted.Count) idx = sorted.Count - 1;
            return sorted[idx];
        }
    }
}
