using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity-free (see AutoTestJson.cs). The trackers here are fed by the game side once per simulation step; the
// check functions turn an AtItemData into the summary.json "checks" entries, so the net8 test exercises the same code.
namespace RealTracks
{
    /// <summary>No car moved more than StuckMeters over StuckSeconds of sim time while it was eligible (racing, not in a box/garage/grid/queue).</summary>
    public sealed class AtStuckTracker
    {
        private float ax, az;
        private double aT;
        private bool armed, flagged;

        /// <summary>True once when the car has just been stuck for the window.</summary>
        public bool Update(double simT, float x, float z, bool eligible, float meters, float seconds)
        {
            if (!eligible) { armed = false; flagged = false; return false; }
            if (!armed) { ax = x; az = z; aT = simT; armed = true; flagged = false; return false; }
            float dx = x - ax, dz = z - az;
            if (dx * dx + dz * dz >= meters * meters) { ax = x; az = z; aT = simT; flagged = false; return false; }
            if (!flagged && simT - aT >= seconds) { flagged = true; return true; }
            return false;
        }

        public void Reset() { armed = false; flagged = false; }
    }

    /// <summary>One car's pit round trip: pit-lane entry, then the box, then back on track.</summary>
    public sealed class AtPitTrack
    {
        public int Ins, Outs, Boxes, Trips;
        public bool Open;
        public double InT, BoxT, OutT;
        private bool boxSeen;

        /// <summary>Returns the event that just happened (pitIn, pitBox, pitOut, pitOutNoStop, pitGarage) or null.</summary>
        public string Observe(double simT, bool inLane, bool inBox, bool onTrack, bool inGarage)
        {
            if (!Open)
            {
                if (inLane || inBox)
                {
                    Open = true; boxSeen = inBox; Ins++; InT = simT;
                    if (inBox) { Boxes++; BoxT = simT; }
                    return "pitIn";
                }
                return null;
            }
            if (inBox && !boxSeen) { boxSeen = true; Boxes++; BoxT = simT; return "pitBox"; }
            if (onTrack)
            {
                bool stopped = boxSeen;
                Outs++;
                if (stopped) Trips++;
                Open = false; boxSeen = false; OutT = simT;
                return stopped ? "pitOut" : "pitOutNoStop";
            }
            if (inGarage) { Open = false; boxSeen = false; return "pitGarage"; }
            return null;
        }
    }

    /// <summary>Rules shared by the game side and the test.</summary>
    public static class AtRules
    {
        // Mirrors PathStateManager.StateType (decompiled PathStateManager.cs:8-19); the mod checks the numbering at startup.
        public const int StGarage = 0, StGarageEntry = 1, StGarageExit = 2, StGrid = 3, StPitbox = 4, StPitboxEntry = 5, StPitboxExit = 6, StPitlane = 7, StRacing = 8;

        /// <summary>
        /// States in which the game itself places a car (Vehicle.MovePosition): GaragePathState:43,45; GridPathState:79,102;
        /// PitboxEntryPathState:49; PitboxPathState:50. A big jump into or out of these is by design, not a glitch.
        /// </summary>
        public static bool IsByDesignJumpState(int pathState)
        {
            return pathState == StGarage || pathState == StGarageEntry || pathState == StGarageExit || pathState == StGrid
                || pathState == StPitbox || pathState == StPitboxEntry || pathState == StPitboxExit;
        }

        public static bool IsTeleport(float dx, float dz, float meters, bool previousStateByDesign, bool currentStateByDesign)
        {
            if (previousStateByDesign || currentStateByDesign) return false;
            return dx * dx + dz * dz > meters * meters;
        }

        /// <summary>Index of the nearest point (2D), or -1.</summary>
        public static int NearestIndex(float[] xs, float[] zs, float x, float z)
        {
            int best = -1; float bd = float.MaxValue;
            if (xs == null || zs == null) return -1;
            for (int i = 0; i < xs.Length && i < zs.Length; i++)
            {
                float dx = xs[i] - x, dz = zs[i] - z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>Signature of a log message for "same error" comparisons: first line, digits folded, 140 chars.</summary>
        public static string ErrSig(string message)
        {
            if (message == null) return "";
            int nl = message.IndexOf('\n');
            string s = nl >= 0 ? message.Substring(0, nl) : message;
            StringBuilder sb = new StringBuilder(s.Length);
            bool lastDigit = false;
            for (int i = 0; i < s.Length && sb.Length < 140; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { if (!lastDigit) sb.Append('#'); lastDigit = true; }
                else { sb.Append(c); lastDigit = false; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Strips &lt;rich text&gt; tags and collapses white space.</summary>
        public static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>' && inTag) { inTag = false; continue; }
                if (inTag) continue;
                if (c == '\n' || c == '\r' || c == '\t') c = ' ';
                if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] == ' ') continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static bool ContainsWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word)) return false;
            int from = 0;
            while (true)
            {
                int i = text.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                bool leftOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int e = i + word.Length;
                bool rightOk = e >= text.Length || !char.IsLetterOrDigit(text[e]);
                if (leftOk && rightOk) return true;
                from = i + 1;
            }
        }

        /// <summary>Scans UI texts for a name. Returns the number of texts that contain it; sample = the first match (cut to 80 chars).</summary>
        public static int ScanTexts(IList<string> texts, string name, out string sample)
        {
            sample = "";
            int n = 0;
            if (texts == null) return 0;
            for (int i = 0; i < texts.Count; i++)
            {
                string t = StripTags(texts[i]);
                if (ContainsWord(t, name))
                {
                    n++;
                    if (sample.Length == 0) sample = t.Length > 80 ? t.Substring(0, 80) : t;
                }
            }
            return n;
        }
    }

    /// <summary>A copy of a minimap sprite's alpha channel, for "is this world position on the drawn outline".</summary>
    public sealed class AtAlphaGrid
    {
        public readonly int W, H;
        private readonly byte[] a;

        public AtAlphaGrid(int w, int h, byte[] alpha) { W = w; H = h; a = alpha; }

        /// <summary>u, v in 0..1 (v = 0 at the bottom row, like Unity textures). True if any pixel within radiusPx has alpha at or above threshold.</summary>
        public bool HitNear(float u, float v, int radiusPx, byte threshold)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            int px = (int)Math.Min(W - 1, Math.Floor(u * W));
            int py = (int)Math.Min(H - 1, Math.Floor(v * H));
            for (int dy = -radiusPx; dy <= radiusPx; dy++)
            {
                int y = py + dy;
                if (y < 0 || y >= H) continue;
                for (int dx = -radiusPx; dx <= radiusPx; dx++)
                {
                    int x = px + dx;
                    if (x < 0 || x >= W) continue;
                    if (dx * dx + dy * dy > radiusPx * radiusPx) continue;
                    if (a[y * W + x] >= threshold) return true;
                }
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ the checks

    public static class AtChecks
    {
        public static readonly string[] CheckOrder =
        {
            "loaded", "exceptions", "gridFormed", "allCarsLapped", "noStuck", "noTeleports", "pitRoundTrips",
            "incidentsClean", "minimapOnOutline", "screensShowData", "fps", "smoothness", "steeringSmoothness", "markerSelfTest"
        };

        public static AtRunSummary BuildSummary(string runId, string modVersion, string startedUtc, string finishedUtc, bool quitClean, List<AtItemData> items, AtCfg cfg)
        {
            AtRunSummary s = new AtRunSummary();
            s.RunId = runId; s.ModVersion = modVersion; s.StartedUtc = startedUtc; s.FinishedUtc = finishedUtc; s.QuitClean = quitClean;
            for (int i = 0; i < items.Count; i++)
            {
                AtItemData d = items[i];
                AtItemData stock = null;
                if (d.IsPack)
                {
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (!items[k].IsPack && items[k].CircuitLoaded && items[k].Fps.Frames > 0) { stock = items[k]; break; }
                    }
                }
                s.Items.Add(Summarise(d, cfg, stock));
            }
            return s;
        }

        public static AtItemSummary Summarise(AtItemData d, AtCfg cfg, AtItemData stock)
        {
            AtItemSummary o = new AtItemSummary();
            o.CircuitId = d.CircuitId;
            o.Location = d.Location;
            o.Layout = d.Layout;
            o.IsPack = d.IsPack;
            o.LapsTarget = d.LapsTarget;

            if (cfg.Loaded) o.Checks.Add(Loaded(d));
            if (cfg.Exceptions) o.Checks.Add(Exceptions(d, stock));
            if (cfg.Grid) o.Checks.Add(GridFormed(d));
            if (cfg.Lapped) o.Checks.Add(AllCarsLapped(d));
            if (cfg.Stuck) o.Checks.Add(NoStuck(d, cfg));
            if (cfg.Teleport) o.Checks.Add(NoTeleports(d, cfg));
            if (cfg.Pit) o.Checks.Add(PitRoundTrips(d));
            if (cfg.Incidents) o.Checks.Add(IncidentsClean(d, cfg));
            if (cfg.Minimap) o.Checks.Add(MinimapOnOutline(d, cfg));
            if (cfg.Screens) o.Checks.Add(ScreensShowData(d, stock));
            if (cfg.Fps) o.Checks.Add(Fps(d, stock, cfg));
            if (cfg.Fps) o.Checks.Add(AtSmoothCheck.Evaluate(d, stock));
            o.Checks.Add(AtWobbleCheck.Evaluate(d, stock));
            if (d.ArtTour) o.Checks.Add(ArtTourCheck(d));
            if (d.ResRequested) o.Checks.Add(AtResChecks.Resolutions(d));
            if (cfg.UiFallbacks) o.Checks.Add(AtResChecks.UiFallbacks(d));
            if (cfg.MarkerSelf && d.MarkerRequested) o.Checks.Add(MarkerSelfTest(d));

            o.Fps = d.Fps.Result();
            o.Smooth = d.Smooth.Result();
            o.VideoDir = d.VideoDir;
            o.VideoFrames = d.VideoFrames;
            o.Clips.AddRange(d.ClipResults);
            for (int i = 0; i < d.Cars.Count; i++)
            {
                AtCarData c = d.Cars[i];
                AtCarOut co = new AtCarOut();
                co.Name = c.Name; co.Team = c.Team; co.Laps = c.Laps; co.PitIns = c.PitIns; co.PitOuts = c.PitOuts;
                co.Stuck = c.Stuck; co.Teleports = c.Teleports; co.Finished = c.Finished;
                o.Cars.Add(co);
            }
            foreach (AtIncident inc in d.Incidents)
            {
                if (inc.Kind == "crash") o.Crash++;
                else if (inc.Kind == "runwide") o.Runwide++;
            }
            o.Glitches = d.Glitches.Count;
            int corners = d.CornerCount;
            foreach (int k in d.PerCorner.Keys) if (k > corners) corners = k;
            for (int c = 1; c <= corners; c++)
            {
                int n;
                d.PerCorner.TryGetValue(c, out n);
                o.PerCorner.Add(new[] { c, n });
            }
            o.Screenshots.AddRange(d.Shots);

            bool allPass = d.Abort.Length == 0;
            foreach (AtCheck c in o.Checks) if (!c.Pass && !c.Informational) allPass = false;
            o.Result = allPass ? "pass" : "fail";
            return o;
        }

        /// <summary>Informational (never fails an item): the art tour's screenshots, and what it skipped and why.</summary>
        public static AtCheck ArtTourCheck(AtItemData d)
        {
            AtCheck c = new AtCheck("artTour", true, "", d.ArtShots.Count);
            c.Informational = true;
            if (!d.ArtTourRan) { c.Detail = "requested but not run (the item ended before the pre-race hub)"; return c; }
            c.Detail = d.ArtShots.Count + " shots: " + string.Join(", ", d.ArtShots.ToArray()) + (d.ArtSkipped.Count > 0 ? "; skipped: " + string.Join("; ", d.ArtSkipped.ToArray()) : "");
            return c;
        }

        private static string Ordinal(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        public static AtCheck Loaded(AtItemData d)
        {
            List<string> bad = new List<string>();
            if (d.Abort.Length > 0 && !d.CircuitLoaded) bad.Add("run aborted before the circuit loaded: " + d.Abort);
            if (!d.CircuitLoaded && d.Abort.Length == 0) bad.Add("circuit scene never loaded");
            if (d.CircuitLoaded)
            {
                if (!string.Equals(d.CircuitLocation, d.Location, StringComparison.OrdinalIgnoreCase)) bad.Add("loaded location '" + d.CircuitLocation + "' is not the requested '" + d.Location + "'");
                if (!string.Equals(d.CircuitLayout, d.Layout, StringComparison.OrdinalIgnoreCase)) bad.Add("loaded layout " + d.CircuitLayout + " is not the requested " + d.Layout);
                if (d.IsPack && !d.VenueBuilt) bad.Add("pack circuit but no venue was built (ActiveVenue.Built is null)");
                if (d.LapsActual >= 0 && d.LapsActual != d.LapsTarget) bad.Add("race is " + d.LapsActual + " laps, not the requested " + d.LapsTarget);
            }
            string detail = d.CircuitLoaded
                ? "circuit " + d.CircuitId + " " + d.CircuitLocation + " layout " + d.CircuitLayout + ", scene '" + d.SceneName + "', " + (d.IsPack ? "pack venue" + (d.VenueBuilt ? " built" : " NOT built") : "stock venue")
                    + ", in Quick Race list: " + (d.InQuickRaceList ? "yes" : "no") + (d.LoadNote.Length > 0 ? "; " + d.LoadNote : "")
                : "not loaded";
            if (bad.Count > 0) detail += " | " + string.Join("; ", bad.ToArray());
            return new AtCheck("loaded", bad.Count == 0, detail, d.CircuitId);
        }

        public static AtCheck Exceptions(AtItemData d, AtItemData stock)
        {
            List<string> news = new List<string>();
            int newErr = 0;
            foreach (KeyValuePair<string, int> kv in d.ErrorSigs)
            {
                bool inStock = stock != null && stock.ErrorSigs.ContainsKey(kv.Key);
                if (!inStock)
                {
                    newErr += kv.Value;
                    if (news.Count < 3) news.Add(kv.Key);
                }
            }
            // stock item: only exceptions fail (its errors are the baseline).
            // pack item: no errors at all, or (with a stock item in the run) every error signature must also occur in the stock item.
            bool pass;
            if (!d.IsPack) pass = d.ExceptionCount == 0;
            else if (stock == null) pass = d.ErrorCount == 0;
            else pass = newErr == 0;
            string detail = d.ErrorCount + " error/exception log line(s) (" + d.ExceptionCount + " exceptions)";
            if (d.IsPack && stock != null) detail += "; " + newErr + " not seen in the stock item";
            if (d.Errors.Count > 0) detail += "; first: " + Trunc(d.Errors[0].Message, 120);
            if (news.Count > 0 && d.IsPack) detail += "; new: " + string.Join(" | ", news.ToArray());
            return new AtCheck("exceptions", pass, detail, d.ErrorCount);
        }

        public static AtCheck GridFormed(AtItemData d)
        {
            if (!d.GridChecked) return new AtCheck("gridFormed", false, "the race never reached the grid check (" + (d.Abort.Length > 0 ? d.Abort : "no race start seen") + ")", 0);
            bool pass = d.GridCars > 0 && d.GridOk == d.GridCars;
            return new AtCheck("gridFormed", pass, d.GridOk + " of " + d.GridCars + " cars on their grid boxes; " + d.GridDetail, d.GridOk);
        }

        public static AtCheck AllCarsLapped(AtItemData d)
        {
            if (d.Cars.Count == 0) return new AtCheck("allCarsLapped", false, "no cars were tracked (" + (d.Abort.Length > 0 ? d.Abort : "race did not start") + ")", 0);
            List<string> short_ = new List<string>();
            int retired = 0, ok = 0;
            foreach (AtCarData c in d.Cars)
            {
                if (c.Retired) retired++;
                if (c.Laps >= d.LapsTarget || c.Finished) ok++;
                else if (!c.Retired) short_.Add(c.Name + " " + c.Laps + "/" + d.LapsTarget);
            }
            bool pass = short_.Count == 0 && d.Cars.Count > 0 && (d.RaceFinished || retired == d.Cars.Count);
            string detail = ok + " of " + d.Cars.Count + " cars completed " + d.LapsTarget + " lap(s) or finished; " + retired + " retired (allowed, logged)";
            if (!d.RaceFinished) detail += "; race did not finish" + (d.Abort.Length > 0 ? " (" + d.Abort + ")" : "");
            if (short_.Count > 0) detail += "; short: " + string.Join(", ", short_.GetRange(0, Math.Min(5, short_.Count)).ToArray());
            return new AtCheck("allCarsLapped", pass, detail, ok);
        }

        public static AtCheck NoStuck(AtItemData d, AtCfg cfg)
        {
            List<string> l = new List<string>();
            int n = 0;
            foreach (AtGlitch g in d.Glitches)
            {
                if (g.Kind != "stuck") continue;
                n++;
                if (l.Count < 4) l.Add(g.Car + " lap " + g.Lap + " t=" + AtJson.Num(Math.Round(g.SimT, 0)) + "s");
            }
            string detail = n == 0 ? "no car held under " + AtJson.Num(cfg.StuckMeters) + " m for " + AtJson.Num(cfg.StuckSeconds) + " s of sim time" : n + " stuck event(s): " + string.Join("; ", l.ToArray());
            return new AtCheck("noStuck", n == 0, detail, n);
        }

        public static AtCheck NoTeleports(AtItemData d, AtCfg cfg)
        {
            List<string> l = new List<string>();
            int n = 0;
            foreach (AtGlitch g in d.Glitches)
            {
                if (g.Kind != "teleport") continue;
                n++;
                if (l.Count < 4) l.Add(g.Car + " lap " + g.Lap + " " + AtJson.Num(Math.Round(g.Meters, 0)) + " m at t=" + AtJson.Num(Math.Round(g.SimT, 0)) + "s");
            }
            string detail = n == 0 ? "no jump over " + AtJson.Num(cfg.TeleportMeters) + " m in one sim step (garage, pit box and grid placement excluded)" : n + " teleport(s): " + string.Join("; ", l.ToArray());
            return new AtCheck("noTeleports", n == 0, detail, n);
        }

        public static AtCheck PitRoundTrips(AtItemData d)
        {
            if (d.Cars.Count == 0) return new AtCheck("pitRoundTrips", false, "no cars were tracked", 0);
            Dictionary<string, int> trips = new Dictionary<string, int>();
            List<string> teams = new List<string>();
            List<string> open = new List<string>();
            int total = 0;
            foreach (AtCarData c in d.Cars)
            {
                if (!trips.ContainsKey(c.Team)) { trips[c.Team] = 0; teams.Add(c.Team); }
                trips[c.Team] += c.PitTrips;
                total += c.PitTrips;
                if (c.PitOpen && !c.Retired) open.Add(c.Name);
            }
            List<string> missing = new List<string>();
            List<string> exempt = new List<string>();
            foreach (string t in teams)
            {
                if (trips[t] >= 1) continue;
                bool allOut = true;
                foreach (AtCarData c in d.Cars) if (c.Team == t && !c.Retired) allOut = false;
                if (allOut) exempt.Add(t); else missing.Add(t);   // both cars retired (crash/failure) before a stop: nothing to test
            }
            bool pass = missing.Count == 0 && open.Count == 0;
            string detail = (teams.Count - missing.Count - exempt.Count) + " of " + (teams.Count - exempt.Count) + " teams made a pit round trip (entry, box, back on track); " + total + " trips in all, " + d.ForcedStops + " forced";
            if (missing.Count > 0) detail += "; no stop: " + string.Join(", ", missing.GetRange(0, Math.Min(6, missing.Count)).ToArray()) + (d.LapsTarget < 3 ? " (race too short to force a stop)" : "");
            if (open.Count > 0) detail += "; never came out: " + string.Join(", ", open.ToArray());
            if (exempt.Count > 0) detail += "; exempt (both cars retired before stopping): " + string.Join(", ", exempt.ToArray());
            return new AtCheck("pitRoundTrips", pass, detail, total);
        }

        public static AtCheck IncidentsClean(AtItemData d, AtCfg cfg)
        {
            int dirty = 0, unknownCorner = 0, crash = 0, runwide = 0, cut = 0;
            List<string> l = new List<string>();
            foreach (AtIncident i in d.Incidents)
            {
                if (i.Kind == "crash") crash++; else if (i.Kind == "runwide") runwide++; else cut++;
                if (i.Corner <= 0) unknownCorner++;
                if (i.Glitch) { dirty++; if (l.Count < 3) l.Add(i.Kind + " by " + i.Car + " at corner " + i.Corner); }
            }
            string detail = d.Incidents.Count + " incident(s): " + crash + " crash, " + runwide + " run-wide, " + cut + " cut-corner";
            if (d.Incidents.Count == 0) detail += " (none happened this race, so path usage after incidents was not exercised)";
            if (unknownCorner > 0) detail += "; " + unknownCorner + " without a corner";
            if (dirty > 0) detail += "; stuck or teleport within " + AtJson.Num(cfg.IncidentGlitchWindowSeconds) + " s after: " + string.Join("; ", l.ToArray());
            bool forcedOk = true;
            if (d.ForceCrashes > 0)
            {
                forcedOk = crash >= d.ForceCrashes;
                detail += "; forced crashes requested " + d.ForceCrashes + ", happened " + crash + (forcedOk ? "" : " (NOT ENOUGH: the crash path was not exercised as asked)");
            }
            return new AtCheck("incidentsClean", dirty == 0 && forcedOk, detail, d.Incidents.Count);
        }

        public static AtCheck MinimapOnOutline(AtItemData d, AtCfg cfg)
        {
            if (!d.MapAvailable) return new AtCheck("minimapOnOutline", false, "minimap not available: " + (d.MapNote.Length > 0 ? d.MapNote : "no HUD minimap sprite was readable"), 0);
            double ratio = d.MapSamples == 0 ? 0 : (double)d.MapHits / d.MapSamples;
            bool pass = d.MapSamples >= cfg.MinimapMinSamples && ratio >= cfg.MinimapRequiredRatio;
            string detail = d.MapHits + " of " + d.MapSamples + " car samples on or near the outline (" + AtJson.Num(Math.Round(ratio * 100, 1)) + " %, need " + AtJson.Num(Math.Round(cfg.MinimapRequiredRatio * 100, 0)) + " %)";
            if (d.MapSamples < cfg.MinimapMinSamples) detail += "; too few samples (" + cfg.MinimapMinSamples + " needed)";
            if (d.MapNote.Length > 0) detail += "; " + d.MapNote;
            return new AtCheck("minimapOnOutline", pass, detail, Math.Round(ratio * 100, 1));
        }

        private static readonly string[] DefaultRequiredScreens = { "setup", "loading", "hud", "results" }; // preRace (the hub) shows no location name, stock or pack

        public static AtCheck ScreensShowData(AtItemData d, AtItemData stock)
        {
            List<string> required = new List<string>(DefaultRequiredScreens);
            if (d.IsPack && stock != null)
            {
                foreach (AtScreenScan s in stock.Scans) if (s.Found && !required.Contains(s.Screen)) required.Add(s.Screen);
            }
            List<string> parts = new List<string>();
            List<string> failed = new List<string>();
            int found = 0;
            string expected = "";
            Dictionary<string, AtScreenScan> by = new Dictionary<string, AtScreenScan>();
            foreach (AtScreenScan s in d.Scans) { by[s.Screen] = s; if (expected.Length == 0) expected = s.Expected; if (s.Found) found++; }
            foreach (AtScreenScan s in d.Scans)
            {
                parts.Add(s.Screen + ":" + (s.Found ? "found" + (s.MatchCount > 1 ? " x" + s.MatchCount : "") : "absent") + "(" + s.TextCount + " texts)");
                if (s.DonorLeak.Length > 0) failed.Add(s.Screen + " shows the donor name '" + s.DonorLeak + "'");
            }
            foreach (string r in required)
            {
                AtScreenScan s;
                if (!by.TryGetValue(r, out s)) failed.Add(r + " screen was never scanned");
                else if (!s.Found) failed.Add("'" + s.Expected + "' not on the " + r + " screen");
            }
            string detail = "looking for '" + expected + "': " + string.Join(", ", parts.ToArray());
            if (failed.Count > 0) detail += " | " + string.Join("; ", failed.ToArray());
            return new AtCheck("screensShowData", failed.Count == 0, detail, found);
        }

        public static AtCheck Fps(AtItemData d, AtItemData stock, AtCfg cfg)
        {
            AtFpsOut f = d.Fps.Result();
            if (f.Frames == 0) return new AtCheck("fps", false, "no race frames were sampled", 0);
            string detail = f.Frames + " frames at 1x; fps p5 " + AtJson.Num(Math.Round(f.P5, 1)) + ", p50 " + AtJson.Num(Math.Round(f.P50, 1)) + ", p95 " + AtJson.Num(Math.Round(f.P95, 1));
            if (!d.IsPack || stock == null)
            {
                detail += d.IsPack ? "; no stock item in this run to compare against" : "; stock item (the baseline)";
                return new AtCheck("fps", true, detail, Math.Round(f.P50, 1));
            }
            AtFpsOut b = stock.Fps.Result();
            double ratio = b.P50 > 0 ? f.P50 / b.P50 : 1;
            bool pass = ratio >= 1.0 - cfg.FpsMaxLossRatio;
            detail += "; stock " + stock.Location + " " + stock.Layout + " p50 " + AtJson.Num(Math.Round(b.P50, 1)) + " (pack is " + AtJson.Num(Math.Round(ratio * 100, 0)) + " % of it, must be at least " + AtJson.Num(Math.Round((1.0 - cfg.FpsMaxLossRatio) * 100, 0)) + " %)";
            return new AtCheck("fps", pass, detail, Math.Round(f.P50, 1));
        }

        public static AtCheck MarkerSelfTest(AtItemData d)
        {
            bool pass = d.MarkerJson && d.MarkerPng;
            string detail = "marker json " + (d.MarkerJson ? "written" : "MISSING") + ", screenshot " + (d.MarkerPng ? "written" : "MISSING") + (d.MarkerNote.Length > 0 ? "; " + d.MarkerNote : "");
            return new AtCheck("markerSelfTest", pass, detail, (d.MarkerJson ? 1 : 0) + (d.MarkerPng ? 1 : 0));
        }

        private static string Trunc(string s, int n)
        {
            if (s == null) return "";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > n ? s.Substring(0, n) + "..." : s;
        }
    }

    /// <summary>Marker file contents (the game side fills it, this writes it).</summary>
    public class AtMarkerCar
    {
        public string Name = "", Team = "", State = "";
        public int Position, Lap;
        public float X, Y, Z, SpeedKph;
        public bool OnTrack;
    }

    public class AtMarker
    {
        public int Number;
        public string UtcTime = "", Location = "", Layout = "", Session = "", Flag = "", Note = "", Screenshot = "", Source = "";
        public double RealSeconds, SimSeconds;
        public int Lap, LapCount;
        public float[] CamPos = new float[3], CamRot = new float[3];
        public float CamFov;
        public int NearestCorner;
        public float NearestCornerDistanceM, LapDistanceM, TrackLengthM;
        public List<AtMarkerCar> Cars = new List<AtMarkerCar>();

        public string ToJson()
        {
            JsonOut j = new JsonOut(true);
            j.BeginObject();
            j.Prop("marker", Number);
            j.Prop("utc", UtcTime);
            j.Prop("source", Source);
            j.Prop("realSeconds", Math.Round(RealSeconds, 2));
            j.Prop("simSeconds", Math.Round(SimSeconds, 2));
            j.Prop("location", Location);
            j.Prop("layout", Layout);
            j.Prop("session", Session);
            j.Prop("flag", Flag);
            j.Prop("lap", Lap);
            j.Prop("lapCount", LapCount);
            j.Prop("note", Note);
            j.Prop("screenshot", Screenshot);
            j.BeginObject("camera");
            j.BeginArray("position"); foreach (float f in CamPos) j.Item(Math.Round(f, 2)); j.EndArray();
            j.BeginArray("rotation"); foreach (float f in CamRot) j.Item(Math.Round(f, 2)); j.EndArray();
            j.Prop("fov", Math.Round(CamFov, 1));
            j.EndObject();
            j.BeginObject("nearestCorner");
            j.Prop("corner", NearestCorner);
            j.Prop("distanceM", Math.Round(NearestCornerDistanceM, 1));
            j.Prop("lapDistanceM", Math.Round(LapDistanceM, 1));
            j.Prop("trackLengthM", Math.Round(TrackLengthM, 1));
            j.EndObject();
            j.BeginArray("cars");
            foreach (AtMarkerCar c in Cars)
            {
                j.BeginObject();
                j.Prop("name", c.Name);
                j.Prop("team", c.Team);
                j.Prop("position", c.Position);
                j.Prop("lap", c.Lap);
                j.BeginArray("pos"); j.Item(Math.Round(c.X, 2)); j.Item(Math.Round(c.Y, 2)); j.Item(Math.Round(c.Z, 2)); j.EndArray();
                j.Prop("speedKph", Math.Round(c.SpeedKph, 1));
                j.Prop("onTrack", c.OnTrack);
                j.Prop("state", c.State);
                j.EndObject();
            }
            j.EndArray();
            j.EndObject();
            return j.ToString() + "\n";
        }
    }
}
