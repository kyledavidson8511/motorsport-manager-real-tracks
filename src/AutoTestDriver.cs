using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Drives a request through the game as unattended Quick Races. One Tick per frame; every step is keyed on the game's own state
    /// (GameStateManager.currentState) and current screen, so it advances exactly as fast as the game allows. The route is documented
    /// with decompiled file:line in ROUTE.md.
    /// </summary>
    public static class AtDriver
    {
        // movie skipping (AtPatch_SkipMovie sets these)
        public static BaseMovieScreen PendingMovie;
        public static float PendingMovieSince;

        private static readonly MethodInfo movieContinue = typeof(BaseMovieScreen).GetMethod("Continue", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo selectedLayout = typeof(QuickRaceSetupScreen).GetField("mSelectedLayout", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo winnerDuration = typeof(SessionWinnerState).GetField("mDisplayDuration", BindingFlags.NonPublic | BindingFlags.Instance);

        private static float nextPickup, titleSince = -1f, itemT0, lastProgress, stateSince;
        private static GameState.Type lastState = GameState.Type.None;
        private static string lastScreen = "";
        private static readonly HashSet<string> once = new HashSet<string>();
        private static bool leftTitle;
        private static int setupFrames;
        private static string expectedName = "", donorName = "";
        private static bool crashed;

        private static bool applied;
        private static bool prevAi, prevBg;
        private static int prevVsync, prevTarget;

        // race
        private static bool raceSeen, mapTried;
        private static float raceStartReal, nextMapSample;
        private static double nextShotSim, lastSimT;
        private static int raceShots;

        private static bool Once(string key) { return once.Add(key); }

        // ------------------------------------------------------------------ main menu detection

        /// <summary>The title screen is up, nothing is changing, and no game (career or otherwise) exists.</summary>
        public static bool AtMainMenu()
        {
            App a = App.instance;
            if (a == null || a.gameStateManager == null || UIManager.instance == null) return false;
            GameStateManager g = a.gameStateManager;
            return g.currentState is TitleState && !g.isChangingState && UIManager.instance.currentScreen is TitleScreen
                && !UIManager.instance.isScreenFadeActive && Game.instance == null;
        }

        // ------------------------------------------------------------------ settings applied for the length of a run

        private static void Apply(AtRunCtx r, Settings s)
        {
            if (applied) return;
            applied = true;
            prevAi = ConsoleCommands.useAIForDrivers;
            if (s.TestAiDrivers) ConsoleCommands.useAIForDrivers = true;
            prevBg = Application.runInBackground; prevVsync = QualitySettings.vSyncCount; prevTarget = Application.targetFrameRate;
            if (s.TestUnattendedMode)
            {
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
        }

        /// <summary>Puts back everything Apply and the speed control changed.</summary>
        public static void Restore()
        {
            try
            {
                GameTimer.debugSimSpeed = 0f;
                if (applied)
                {
                    ConsoleCommands.useAIForDrivers = prevAi;
                    Application.runInBackground = prevBg;
                    QualitySettings.vSyncCount = prevVsync;
                    Application.targetFrameRate = prevTarget;
                    applied = false;
                }
                PendingMovie = null;
                AtMinimap.Reset();
                AtArtTour.Reset();
                AtResMatrix.EnsureRestored("auto-test restore");
            }
            catch { }
        }

        private static void ApplySpeed(AtRunCtx r, Settings s, float sinceRaceStart)
        {
            float ov = AtPhases.SpeedOverride();
            if (ov > 0f)
            {
                GameTimer.debugSimSpeed = ov;
                if (Game.instance != null) Game.instance.time.SetSpeedDontUnpause(GameTimer.Speed.Medium);
                return;
            }
            if (r.SimScale <= 0f) { GameTimer.debugSimSpeed = 0f; return; }
            float sc = r.SimScale;
            if (s.TestFpsWindowSeconds > 0 && sinceRaceStart < s.TestFpsWindowSeconds) sc = 1f;
            GameTimer.debugSimSpeed = sc;
            if (Game.instance != null) Game.instance.time.SetSpeedDontUnpause(GameTimer.Speed.Medium);
        }

        // ------------------------------------------------------------------ the per-frame driver

        public static void Tick(AtBehaviour host)
        {
            Settings s = Main.Settings;
            if (s == null || crashed) return;
            AtRunCtx r = AtRun.Current;
            float now = Time.realtimeSinceStartup;

            if (r == null)
            {
                if (!s.TestEnabled || !Main.Enabled) return;
                if (!AtMainMenu()) { titleSince = -1f; return; }
                if (titleSince < 0f) titleSince = now;
                if (now - titleSince < 2f || now < nextPickup) return;
                nextPickup = now + 2f;
                r = AtRun.TryPickUp();
                if (r == null) return;
                AtResMatrix.Reset();
                Apply(r, s);
                // run 3: the game paused when another window took focus; an unattended run must keep going in the background
                try { Application.runInBackground = true; } catch (Exception) { }
                StartItem(host, r, 0, now);
                return;
            }
            if (r.Finished) return;

            // GUARD 1: never drive anything but a single-event game
            if (Game.instance != null && Game.instance.gameType != Game.GameType.SingleEvent)
            {
                Abort(host, r, "a non-Quick-Race game is active (career guard)");
                return;
            }

            GameStateManager gsm = App.instance.gameStateManager;
            GameState.Type st = gsm.currentState != null ? gsm.currentState.type : GameState.Type.None;
            string scr = UIManager.instance.currentScreen != null ? UIManager.instance.currentScreen.GetType().Name : "";
            framesInState++;
            if (now >= nextFlush)
            {
                nextFlush = now + 1f;
                try { if (r.Events != null) r.Events.Flush(); if (r.Gates != null) r.Gates.Flush(); } catch { }
            }
            if (st != lastState || scr != lastScreen)
            {
                float spent = now - stateSince;
                if (spent > 0.5f) AtRun.Event("screenFps", "state", lastState.ToString(), "screen", lastScreen, "seconds", Math.Round(spent, 1), "fps", Math.Round(framesInState / spent, 1));
                framesInState = 0;
                AtRun.Event("state", "from", lastState.ToString(), "to", st.ToString(), "screen", scr);
                lastState = st; lastScreen = scr; stateSince = now; lastProgress = now;
                r.Phase = st + "/" + scr;
            }
            if (st != GameState.Type.TitleState) leftTitle = true;
            if (AtSim.SimT != lastSimT) { lastSimT = AtSim.SimT; lastProgress = now; }

            HandleMovie(host, r, now);
            CloseModManagerWindow();
            AnswerDilemmas();

            AtItemData d = r.Cur;
            if (d == null) return;
            if (now - itemT0 > s.TestTimeoutMinutes * 60f)
            {
                Abort(host, r, "timeout: " + s.TestTimeoutMinutes + " minutes passed (state " + st + ", screen " + scr + ")");
                return;
            }
            if (st != GameState.Type.Race && now - lastProgress > s.TestPhaseStuckSeconds)
            {
                Abort(host, r, "stuck for " + s.TestPhaseStuckSeconds + " s in state " + st + ", screen " + scr);
                return;
            }

            float dwell = now - stateSince;
            bool fade = UIManager.instance.isScreenFadeActive;
            switch (st)
            {
                case GameState.Type.TitleState:
                    if (leftTitle && AtMainMenu()) ItemDone(host, r, now);
                    break;
                case GameState.Type.QuickRaceSetup: DoSetup(host, r, d, gsm, fade, now); break;
                case GameState.Type.EventLoading: DoLoading(host, d, fade, dwell); break;
                case GameState.Type.RacePreSession: DoPreSession(fade, dwell); break;
                case GameState.Type.PreSessionHUB: DoHub(host, d, gsm, fade, dwell); break;
                case GameState.Type.RaceGrid: DoGrid(d, gsm, fade, dwell); break;
                case GameState.Type.Race: DoRace(host, r, d, s, now); break;
                case GameState.Type.SessionWinner: DoWinner(r, d, gsm, s); break;
                case GameState.Type.PostSessionDataCenter: DoPostData(gsm, fade, dwell); break;
                case GameState.Type.RacePostSession: DoResults(host, d, gsm, fade, dwell); break;
                case GameState.Type.TitleLoading: break;
                default:
                    Abort(host, r, "unexpected game state " + st + " (screen " + scr + ")");
                    break;
            }
        }

        private static void HandleMovie(AtBehaviour host, AtRunCtx r, float now)
        {
            BaseMovieScreen m = PendingMovie;
            if (m == null) return;
            UIScreen cur = UIManager.instance.currentScreen;
            // M1.1: at a pack venue with the intro overlay on, let the intro play long enough to photograph both stages
            // (map at 3 s, outline at map seconds + 3.5 s), then skip as before.
            float hold = 0.4f;
            AtItemData di = r.Cur;
            bool overlay = di != null && di.IsPack && m is MovieLocationScreen && Main.Settings.UiCustomIntro && Main.Settings.TestScreenshots && r.Req.Screenshots;
            if (overlay && cur == m && !UIManager.instance.isScreenFadeActive)
            {
                float since = now - PendingMovieSince;
                if (since > 3.0f && Once("shot-intro-map")) AtUi.Shot(host, "intro-map");
                if (since > Main.Settings.UiIntroMapSeconds + 3.5f && Once("shot-intro-outline")) AtUi.Shot(host, "intro-outline");
                hold = Main.Settings.UiIntroMapSeconds + 4.5f;
            }
            if (cur == m && !UIManager.instance.isScreenFadeActive && now - PendingMovieSince > hold)
            {
                PendingMovie = null;
                AtRun.Event("movieSkipped", "screen", m.GetType().Name);
                if (movieContinue != null) movieContinue.Invoke(m, null);
            }
            else if (cur != m && now - PendingMovieSince > 5f)
            {
                PendingMovie = null;
            }
        }

        // ------------------------------------------------------------------ items

        private static void StartItem(AtBehaviour host, AtRunCtx r, int index, float now)
        {
            once.Clear();
            leftTitle = false; setupFrames = 0; raceSeen = false; mapTried = false; raceShots = 0; lastSimT = -1;
            sCrashArmedSim = -1e9; sCrashesWhenArmed = -1; sNextCrashNudge = 0f;
            AtMinimap.Reset();
            AtArtTour.Reset();
            PendingMovie = null;
            r.ShotCount = 0;
            AtRun.BeginItem(r, index);
            itemT0 = now; lastProgress = now; stateSince = now; lastState = GameState.Type.None; lastScreen = "";
            GameTimer.debugSimSpeed = 0f;
            AtItemData d = r.Cur;
            Main.Log("Real Tracks auto-test: item " + (index + 1) + "/" + r.Items.Count + " " + d.Location + " " + d.Layout + ", " + d.LapsTarget + " laps");
            // the title screen's "Single race" button does exactly this (TitleScreen.cs:353)
            App.instance.gameStateManager.SetState(GameState.Type.QuickRaceSetup);
        }

        private static void ItemDone(AtBehaviour host, AtRunCtx r, float now)
        {
            AtItemData d = r.Cur;
            try
            {
                AtFpsOut f = d.Fps.Result();
                AtRun.Event("fps", "frames", f.Frames, "p5", Math.Round(f.P5, 1), "p50", Math.Round(f.P50, 1), "p95", Math.Round(f.P95, 1));
                AtRun.Event("minimap", "available", d.MapAvailable, "samples", d.MapSamples, "hits", d.MapHits, "note", d.MapNote);
            }
            catch { }
            GameTimer.debugSimSpeed = 0f;
            AtRun.EndItem(r);
            if (r.Index + 1 < r.Items.Count) StartItem(host, r, r.Index + 1, now);
            else FinishRun(r, "");
        }

        private static void FinishRun(AtRunCtx r, string reason)
        {
            Settings s = Main.Settings;
            bool quit = r.Req.Quit && s.TestQuitWhenDone;
            bool resBack = AtResMatrix.EnsureRestored("run finished");      // never leave the game at another resolution
            AtRun.Finish(r, reason);
            if (quit && resBack && AtBehaviour.Instance != null)
            {
                Main.Log("RT RES: waiting 2 s for the restored resolution to apply before quitting");
                AtBehaviour.Instance.StartCoroutine(AtBehaviour.QuitAfter(2f));
            }
            else if (quit)
            {
                Main.Log("Real Tracks auto-test: quitting the game (Application.Quit, nothing is saved)");
                Application.Quit();
            }
        }

        private static void Abort(AtBehaviour host, AtRunCtx r, string reason)
        {
            AtItemData d = r.Cur;
            if (AtResMatrix.EnsureRestored("abort") && d != null) d.ResRestored = true;
            if (d != null && r.ItemActive && d.Abort.Length == 0) d.Abort = reason;
            if (d != null) AtPhases.AbortClips(d, "item aborted: " + reason);
            Main.Logger.Error("Real Tracks auto-test: aborting: " + reason);
            AtRun.Event("abort", "reason", reason, "state", lastState.ToString(), "screen", lastScreen);
            try { if (r.ItemActive && Main.Settings.TestScreenshots && r.Req.Screenshots) AtUi.Shot(host, "abort"); } catch { }
            FinishRun(r, reason);
        }

        public static void Crash(Exception e)
        {
            Main.Logger.Error("Real Tracks auto-test: driver failed: " + e);
            AtRunCtx r = AtRun.Current;
            if (r == null || r.Finished) return;
            crashed = true;
            try { AtResMatrix.EnsureRestored("driver crash"); } catch { }
            try
            {
                AtItemData d = r.Cur;
                if (d != null && r.ItemActive && d.Abort.Length == 0) d.Abort = "driver exception: " + e.GetType().Name + " " + e.Message;
                AtRun.Event("abort", "reason", "driver exception", "message", e.ToString());
                FinishRun(r, "driver exception: " + e.Message);
            }
            catch { }
        }

        // ------------------------------------------------------------------ steps of the route

        private static void DoSetup(AtBehaviour host, AtRunCtx r, AtItemData d, GameStateManager gsm, bool fade, float now)
        {
            if (gsm.isChangingState || fade || Game.instance == null) return;
            QuickRaceSetupScreen scr = UIManager.instance.currentScreen as QuickRaceSetupScreen;
            if (scr == null || scr.circuitList.itemCount == 0) return;
            if (AtResMatrix.Active) { AtResMatrix.Step(host, r, d); lastProgress = now; return; }   // resolution matrix: setupFrames stands still so Launch still comes after it
            setupFrames++;
            if (setupFrames == 1)
            {
                CircuitManager cm = Game.instance.circuitManager;
                Circuit.TrackLayout want = CircuitManager.GetTrackLayoutFromString(d.Layout);
                Circuit c = cm.GetCircuitByLocationNameLayout(d.Location, want);
                if (c == null)
                {
                    Abort(host, r, "the game's circuit list has no '" + d.Location + "' layout " + d.Layout + " (is the track pack loaded and registered?)");
                    return;
                }
                d.CircuitId = c.circuitID;
                PackLayout pl = ActiveVenue.For(c);
                d.IsPack = pl != null;
                d.InQuickRaceList = Array.IndexOf(QuickRaceSetupScreen.circuitsArray, d.Location) >= 0 || UiQuickRace.HasEntry(d.Location);   // M1.1: pack venues sit in the custom column
                expectedName = Expected(c);
                donorName = pl != null && pl.Pack != null ? pl.Pack.DonorScene : "";
                // what the Quick Race screen does when somebody clicks the venue and the layout arrows
                scr.SetSelectedCircuit(d.Location);
                for (int i = 0; i < 6 && selectedLayout != null; i++)
                {
                    if ((Circuit.TrackLayout)selectedLayout.GetValue(scr) == want) break;
                    scr.SelectNextLayout();
                }
                scr.raceLengthToggle[0].isOn = true;
                scr.weatherToggle[1].isOn = true;
                scr.OnRaceOnlyButton();
                AtRun.Event("setup", "circuitId", c.circuitID, "isPack", d.IsPack, "inQuickRaceList", d.InQuickRaceList, "expected", expectedName, "donor", donorName);
            }
            else if (setupFrames == 6)
            {
                string shot = AtUi.Shot(host, "setup");
                AtScreenScan sc = AtUi.Scan("setup", expectedName, ""); // the setup list names every venue, so no donor-leak test here
                sc.Shot = shot;
                d.Scans.Add(sc);
                AtRun.Event("screenScan", "screen", "setup", "expected", expectedName, "found", sc.Found, "matches", sc.MatchCount, "texts", sc.TextCount, "sample", sc.Sample, "donorLeak", sc.DonorLeak);
            }
            else if (setupFrames == 8 && AtResMatrix.Wanted(r, d))
            {
                AtResMatrix.Begin(host, r, d);
            }
            else if (setupFrames == 10)
            {
                Launch(host, r, d, gsm);
            }
        }

        /// <summary>QuickRaceSetupScreen.OnContinueButton + ChooseTeamScreen.OnContinueButton for a race-only weekend, without the series/team screens and movies.</summary>
        private static void Launch(AtBehaviour host, AtRunCtx r, AtItemData d, GameStateManager gsm)
        {
            QuickRaceSetupState qs = (QuickRaceSetupState)gsm.GetState(GameState.Type.QuickRaceSetup);
            qs.SetCircuit(d.Location);
            qs.SetRaceLength(QuickRaceSetupState.RaceLength.Short);
            qs.SetTrackLayout(CircuitManager.GetTrackLayoutFromString(d.Layout));
            qs.SetWeatherSettings(SessionDetails.WeatherSettings.Dry);
            qs.SetRaceWeekend(QuickRaceSetupState.RaceWeekend.RaceOnly);
            qs.SetGridOptions(QuickRaceSetupState.GridOptions.Random);
            qs.SetDriver1GridPosition(1);
            qs.SetDriver2GridPosition(2);

            Championship champ = Game.instance.championshipManager.GetMainChampionship();
            Team best = null;
            int n = champ.standings.teamEntryCount;
            for (int i = 0; i < n; i++)
            {
                Team t = champ.standings.GetTeamEntry(i).GetEntity<Team>();
                if (best == null || t.GetStarsStat() > best.GetStarsStat()) best = t;
            }
            if (best == null) { Abort(host, r, "no team to manage in the main championship"); return; }
            Game.instance.time.Pause(GameTimer.PauseType.Game);
            Game.instance.ManageTeam(best);
            qs.PrepareCircuit();
            Game.instance.stateInfo.SetStateToGoToAfterPlayerConfirms(GameState.Type.RacePreSession);
            AtRun.Event("launch", "team", best.name, "championship", champ.series.ToString(), "laps", d.LapsTarget, "ai", ConsoleCommands.useAIForDrivers);
            gsm.SetState(GameState.Type.EventLoading);
        }

        private static void DoLoading(AtBehaviour host, AtItemData d, bool fade, float dwell)
        {
            if (UIManager.instance.currentScreen is RaceEventLoadingScreen && !fade && dwell > 1.0f && Once("scan-loading"))
            {
                string shot = AtUi.Shot(host, "loading");
                AtScreenScan sc = AtUi.Scan("loading", expectedName, d.IsPack ? donorName : "");
                sc.Shot = shot;
                d.Scans.Add(sc);
                AtRun.Event("screenScan", "screen", "loading", "expected", expectedName, "found", sc.Found, "matches", sc.MatchCount, "texts", sc.TextCount, "sample", sc.Sample, "donorLeak", sc.DonorLeak);
            }
        }

        // The day-transition screen moves on by itself after `duration` (DayTransitionScreen.cs:142-149). Shortening it made it call
        // GoToNextState during a screen fade, the ChangeScreen was refused (UIManager.cs:523-525) and the screen never retried
        // (run 1, orchestrator fix). Keep the game's own timer; if it's still here 15 s later with no fade, re-arm it once per 15 s.
        private static readonly FieldInfo sDtsExiting = typeof(DayTransitionScreen).GetField("mIsExiting", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo sDtsTimer = typeof(DayTransitionScreen).GetField("mTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static float sDtsRearmAt;

        // Unity Mod Manager opens its window at game start; during an auto-test it covers every screenshot (run 1-2).
        // Close it by reflection (the game runs UMM 0.33; we compile against 0.32): UnityModManager.UI.Instance.Opened / ToggleWindow(bool).
        private static float sUmmNextCheck;
        private static void CloseModManagerWindow()
        {
            if (Time.realtimeSinceStartup < sUmmNextCheck) return;
            sUmmNextCheck = Time.realtimeSinceStartup + 1f;
            try
            {
                Type ui = typeof(UnityModManagerNet.UnityModManager).GetNestedType("UI", BindingFlags.Public | BindingFlags.NonPublic);
                if (ui == null) return;
                PropertyInfo inst = ui.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object o = inst != null ? inst.GetValue(null, null) : null;
                if (o == null) return;
                PropertyInfo opened = ui.GetProperty("Opened", BindingFlags.Public | BindingFlags.Instance);
                if (opened == null || !(bool)opened.GetValue(o, null)) return;
                MethodInfo toggle = ui.GetMethod("ToggleWindow", new Type[] { typeof(bool) });
                if (toggle != null) { toggle.Invoke(o, new object[] { false }); AtRun.Event("modManagerWindowClosed"); }
            }
            catch (Exception) { }
        }

        // In-race dilemmas ("Shall we pit while the Virtual Safety Car is active?") pause the game until answered
        // (DilemmaWidget.cs:69-72, run 3). During an auto-test, press Ignore after a short look so the AI strategy stays in charge.
        private static float sDilemmaSeenAt = -1f;
        private static int framesInState;
        private static float nextFlush;
        // Forced crashes (request item "forceCrashes"): the game only allows a crash from lap 2 until 3 laps before the end, only while the
        // active race chunk has a crash budget (CrashDirector.cs:186-197) AND only for a car whose own crash points exceed the limit
        // (SessionEvents.IsReadyTo, SessionEvents.cs:168; the points build up slowly per gate). Run 18 (Monaco) added budget 8 times
        // (the old cap, spent within ~30 sim seconds because the 20 s guard ran on real time), got one crash 60 sim seconds later from a
        // car that happened to be ready, and then had no cap left for the second. Now: arm one car at a time (budget in the active chunk
        // when it has none + that car's crash points raised past the limit through the public SessionEvents.SetPoints), wait up to
        // ForceCrashWaitSimS sim seconds for the crash, then arm another; the cap counts arms (not budget), and the event says which
        // car was armed and the chunk budget.
        private static float sNextCrashNudge;
        private static double sCrashArmedSim = -1e9;
        private static int sCrashesWhenArmed = -1;
        public const double ForceCrashWaitSimS = 40.0;
        private static void ForceCrashes(AtItemData d, float now)
        {
            if (d.ForceCrashes <= 0 || now < sNextCrashNudge) return;
            sNextCrashNudge = now + 2f;
            try
            {
                SessionManager sm = Game.instance.sessionManager;
                int crashes = 0;
                foreach (AtIncident i in d.Incidents) if (i.Kind == "crash") crashes++;
                if (crashes >= d.ForceCrashes) return;
                if (sm.lap <= 1 || sm.lapCount - sm.lap < 3 || sm.flag != SessionManager.Flag.Green) return;
                if (!AtCrashPlan.ShouldArm(crashes, sCrashesWhenArmed, sCrashArmedSim, AtSim.SimT, ForceCrashWaitSimS, d.CrashesAdded, d.ForceCrashes)) return;
                CrashDirector cd = sm.raceDirector.crashDirector;
                int chunkBudget = -1;
                try
                {
                    FieldInfo af = typeof(CrashDirector).GetField("mActiveChunk", BindingFlags.NonPublic | BindingFlags.Instance);
                    object ac = af != null ? af.GetValue(cd) : null;
                    FieldInfo cf = ac != null ? ac.GetType().GetField("crashCount", BindingFlags.Public | BindingFlags.Instance) : null;
                    if (cf != null) chunkBudget = (int)cf.GetValue(ac);
                }
                catch (Exception) { }
                if (chunkBudget <= 0) { cd.AddCrash(); chunkBudget = chunkBudget < 0 ? -1 : 1; }
                // arm the running on-track car nearest to P8 (keeps the front of the order undisturbed)
                AtCarState pick = null; int bestGap = int.MaxValue;
                for (int i = 0; i < AtSim.Cars.Count; i++)
                {
                    AtCarState cs = AtSim.Cars[i];
                    try
                    {
                        RacingVehicle v = cs.V;
                        if (cs.Data.Retired || cs.Data.Finished || v.behaviourManager.isOutOfRace || v.timer.hasSeenChequeredFlag) continue;
                        if (v.pathState.pathStateGroup != PathStateManager.PathStateGroup.OnTrack || v.pathController.currentPathType != PathController.PathType.Track) continue;
                        if (v.behaviourManager.currentBehaviour.behaviourType != AIBehaviourStateManager.Behaviour.Racing) continue;
                        int gap = Mathf.Abs(v.standingsPosition - 8);
                        if (gap < bestGap) { bestGap = gap; pick = cs; }
                    }
                    catch (Exception) { }
                }
                string who = "none";
                if (pick != null) { pick.V.sessionEvents.SetPoints(SessionEvents.EventType.Crash, 1000000); who = pick.V.driver.lastName + " P" + pick.V.standingsPosition; }
                d.CrashesAdded++;
                sCrashArmedSim = AtSim.SimT; sCrashesWhenArmed = crashes;
                AtRun.Event("crashBudgetAdded", "lap", sm.lap, "crashesSoFar", crashes, "wanted", d.ForceCrashes, "armedCar", who, "chunkBudget", chunkBudget, "arm", d.CrashesAdded);
            }
            catch (Exception e) { AtRun.Event("crashBudgetFailed", "error", e.Message); sNextCrashNudge = now + 30f; }
        }

        private static float sDilemmaNextScan;
        private static void AnswerDilemmas()
        {
            // throttled: FindObjectsOfType every frame would itself cost frame time (smoothness window)
            if (Time.realtimeSinceStartup < sDilemmaNextScan) return;
            sDilemmaNextScan = Time.realtimeSinceStartup + 0.5f;
            DilemmaWidget open = null;
            try
            {
                foreach (DilemmaWidget w in UnityEngine.Object.FindObjectsOfType<DilemmaWidget>())
                    if (w != null && w.gameObject.activeInHierarchy && w.ignoreButton != null && w.ignoreButton.interactable) { open = w; break; }
            }
            catch (Exception) { return; }
            if (open == null) { sDilemmaSeenAt = -1f; return; }
            float t = Time.realtimeSinceStartup;
            if (sDilemmaSeenAt < 0f) { sDilemmaSeenAt = t; return; }
            if (t - sDilemmaSeenAt < 0.9f) return;
            sDilemmaSeenAt = -1f;
            string text = "";
            try { UnityEngine.UI.Text[] ts = open.GetComponentsInChildren<UnityEngine.UI.Text>(true); foreach (UnityEngine.UI.Text x in ts) text += x.text + " "; } catch (Exception) { }
            try { open.ignoreButton.onClick.Invoke(); AtRun.Event("dilemmaIgnored", "text", text); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: answering a dilemma failed: " + e.Message); }
        }

        private static AtScreenScan HudMinimapScan(string expectedName)
        {
            AtScreenScan sc = new AtScreenScan();
            sc.Screen = "hud";
            sc.Expected = expectedName;
            Circuit c = ActiveVenue.CurrentCircuit;
            string want = c == null ? "" : "Minimap_" + c.spriteName + c.GetTrackVariation();
            int widgets = 0, ok = 0;
            List<string> seen = new List<string>();
            foreach (UITrackLayout t in UnityEngine.Object.FindObjectsOfType<UITrackLayout>())
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                widgets++;
                string n = "(none)";
                try { Sprite sp = t.sprite; if (sp != null) n = sp.name; } catch (Exception) { }
                seen.Add(n);
                if (n == want) ok++;
            }
            sc.TextCount = widgets;
            sc.MatchCount = ok;
            sc.Found = widgets > 0 && ok == widgets;
            sc.Sample = "minimap widgets " + ok + "/" + widgets + " show " + want + " [" + string.Join(", ", seen.ToArray()) + "]";
            return sc;
        }

        private static void DoPreSession(bool fade, float dwell)
        {
            DayTransitionScreen dts = UIManager.instance.currentScreen as DayTransitionScreen;
            if (dts == null || fade) return;
            if (dwell < 15f) { sDtsRearmAt = 15f; return; }
            if (dwell >= sDtsRearmAt && sDtsExiting != null && sDtsTimer != null)
            {
                sDtsRearmAt = dwell + 15f;
                sDtsExiting.SetValue(dts, false);
                sDtsTimer.SetValue(dts, dts.duration + 1f);
                AtRun.Event("dayTransitionRearm", "dwell", dwell);
            }
        }

        private static void CheckLoaded(AtItemData d)
        {
            Game g = Game.instance;
            SessionManager sm = g.sessionManager;
            if (!sm.isCircuitLoaded || sm.eventDetails == null) return;
            Circuit c = sm.eventDetails.circuit;
            d.CircuitLoaded = true;
            d.CircuitId = c.circuitID;
            d.SceneName = c.scene;
            d.CircuitLocation = c.locationName;
            d.CircuitLayout = c.GetTrackVariation();
            PackLayout pl = ActiveVenue.For(c);
            d.IsPack = pl != null;
            d.VenueBuilt = pl != null && ActiveVenue.Built != null && ActiveVenue.Built.Layout == pl;
            string note = "";
            try { note = App.instance.preferencesManager.videoPreferences.isSimulationRunning2DMode ? "2D mode" : "3D mode"; } catch { }
            if (ActiveVenue.Built != null && pl != null)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in ActiveVenue.Built.Counts) parts.Add(kv.Key + "=" + kv.Value);
                if (parts.Count > 0) note += "; built: " + string.Join(", ", parts.ToArray());
                if (ActiveVenue.Built.Warnings.Count > 0) note += "; " + ActiveVenue.Built.Warnings.Count + " venue warning(s)";
            }
            d.LoadNote = note;
            AtRun.Event("circuitLoaded", "circuitId", d.CircuitId, "location", d.CircuitLocation, "layout", d.CircuitLayout, "scene", d.SceneName,
                "isPack", d.IsPack, "venueBuilt", d.VenueBuilt, "note", note);
        }

        private static void DoHub(AtBehaviour host, AtItemData d, GameStateManager gsm, bool fade, float dwell)
        {
            if (Once("loaded-check")) CheckLoaded(d);
            if (!(UIManager.instance.currentScreen is SessionHUBScreen) || fade || gsm.isChangingState || dwell < 1.5f) return;
            if (Once("scan-prerace"))
            {
                string shot = AtUi.Shot(host, "prerace");
                AtScreenScan sc = AtUi.Scan("preRace", expectedName, d.IsPack ? donorName : "");
                sc.Shot = shot;
                d.Scans.Add(sc);
                AtRun.Event("screenScan", "screen", "preRace", "expected", expectedName, "found", sc.Found, "matches", sc.MatchCount, "texts", sc.TextCount, "sample", sc.Sample, "donorLeak", sc.DonorLeak);
            }
            else if (AtArtTour.Wanted(d)) AtArtTour.Step(host, d);   // art tour (request flag artTour): close-up shots, then the run goes on as before
            else if (dwell > 3.0f && Once("take-session"))
            {
                // the "Go Race" answer of the hub's Start button (PreSessionHUBState.OnTakeSessionConfirmed)
                ((PreSessionHUBState)gsm.currentState).OnTakeSessionConfirmed();
            }
        }

        private static void DoGrid(AtItemData d, GameStateManager gsm, bool fade, float dwell)
        {
            if (Once("loaded-check")) CheckLoaded(d);
            GridScreen gs = UIManager.instance.currentScreen as GridScreen;
            if (gs != null && !fade && !gsm.isChangingState && dwell > 1.5f && Once("grid-continue"))
            {
                gs.OnContinueButton();   // GridScreen.cs:164: opens the SessionHUD, whose grid lights start the race
            }
        }

        private static void DoRace(AtBehaviour host, AtRunCtx r, AtItemData d, Settings s, float now)
        {
            Game g = Game.instance;
            SessionManager sm = g.sessionManager;
            if (!raceSeen)
            {
                raceSeen = true; raceStartReal = now; nextShotSim = 2.0; raceShots = 0; nextMapSample = now + 5f; mapTried = false;
                AtPhases.ResetForItem();
            }
            ApplySpeed(r, s, now - raceStartReal);
            bool paused = g.time.isPaused;

            // fps and smoothness come from the 1x window from lap 2 (AtPhases); then a video clip (Kyle's M1 feedback)
            if (s.TestDetFps) AtPhases.Tick(host, d, s, paused);

            if (s.TestDetMinimap && !paused && now >= nextMapSample)
            {
                nextMapSample = now + 1f;
                if (!mapTried) { mapTried = true; AtMinimap.Prepare(d); }
                AtMinimap.Sample(d);
            }

            ForceCrashes(d, now);

            // Race screenshots spread over the race (run-4 verifier: all three landed on lap 1): the start of lap 2, mid-race, the final lap.
            // Never inside the smoothness or video windows (run-7 verifier: a full-size screenshot landed in the 1x window).
            if (raceShots < s.TestRaceShots && !paused && AtPhases.Current != AtPhases.Phase.Smooth && AtPhases.Current != AtPhases.Phase.Video && AtPhases.Current != AtPhases.Phase.Clip)
            {
                int target = d.LapsTarget;
                int lapNow = AtSim.LeaderLap;   // 1-based current lap (run-5 events: lap 1 at the start)
                int[] want = { 2, Math.Max(2, (target + 1) / 2 + 1), Math.Max(2, target) };
                int k = raceShots < want.Length ? want[raceShots] : int.MaxValue;
                if (lapNow >= k && AtSim.SimT >= nextShotSim)
                {
                    raceShots++; nextShotSim = AtSim.SimT + 20.0;
                    AtUi.Shot(host, "race-lap" + lapNow);
                }
            }
            if (AtSim.SimT >= 8.0 && Once("scan-hud"))
            {
                // The race HUD shows no location text; its venue data is the minimap (UITrackLayout, UITrackLayout.cs:66-72).
                // Check every active minimap widget shows this circuit's sprite (orchestrator fix after run 2).
                AtScreenScan sc = HudMinimapScan(expectedName);
                d.Scans.Add(sc);
                AtRun.Event("screenScan", "screen", "hud", "expected", expectedName, "found", sc.Found, "matches", sc.MatchCount, "texts", sc.TextCount, "sample", sc.Sample, "donorLeak", sc.DonorLeak);
            }
            if (d.MarkerRequested && !paused && AtSim.SimT >= 15.0 && Once("marker-self"))
            {
                AtMarkers.SelfTest(host);
            }
            if (AtSim.SinceChequered > 400.0 && !sm.hasSessionEnded && Once("chequered-watchdog"))
            {
                d.Abort = "the cars did not all finish within 400 sim seconds of the chequered flag";
                AtRun.Event("abort", "reason", d.Abort);
                try { sm.EndSession(); } catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: EndSession failed: " + e.Message); }
            }
        }

        private static void DoWinner(AtRunCtx r, AtItemData d, GameStateManager gsm, Settings s)
        {
            AtPhases.AbortClips(d, "race ended");   // a turn clip must not slow the end of the race (no-op without clips)
            ApplySpeed(r, s, 9999f);
            if (!Once("winner")) return;
            try
            {
                if (winnerDuration != null) winnerDuration.SetValue(gsm.currentState, 0.5f);   // SessionWinnerState.cs:7 (default 10 s)
            }
            catch { }
            d.RaceFinished = Game.instance.sessionManager.hasSessionEnded;
            AtSim.FinishItem(d);
            AtRun.Event("raceEnd", "finished", d.RaceFinished, "simSeconds", Math.Round(d.RaceSimSeconds, 1), "cars", d.Cars.Count, "forcedStops", d.ForcedStops);
        }

        private static void DoPostData(GameStateManager gsm, bool fade, float dwell)
        {
            GameTimer.debugSimSpeed = 0f;
            if (UIManager.instance.currentScreen is BaseMovieScreen || fade || gsm.isChangingState || dwell < 1.5f) return;
            if (Once("postdata-continue")) gsm.currentState.OnContinueButton();   // PostSessionDataCenterState.cs:403
        }

        private static void DoResults(AtBehaviour host, AtItemData d, GameStateManager gsm, bool fade, float dwell)
        {
            if (fade || gsm.isChangingState) return;
            bool results = UIManager.instance.currentScreen is RaceResultsScreen;
            if ((results && dwell > 1.0f) || dwell > 25f)
            {
                if (Once("scan-results"))
                {
                    string shot = AtUi.Shot(host, "results");
                    AtScreenScan sc = AtUi.Scan("results", expectedName, d.IsPack ? donorName : "");
                    sc.Shot = shot;
                    d.Scans.Add(sc);
                    AtRun.Event("screenScan", "screen", "results", "expected", expectedName, "found", sc.Found, "matches", sc.MatchCount, "texts", sc.TextCount, "sample", sc.Sample, "donorLeak", sc.DonorLeak);
                }
                else if (dwell > 2.5f && Once("results-continue"))
                {
                    // RacePostSessionState.OnContinueButton: a Quick Race goes back to the title screen (RacePostSessionState.cs:93); the game is destroyed on exit (:24-31)
                    gsm.currentState.OnContinueButton();
                }
            }
        }

        private static string Expected(Circuit c)
        {
            string e = "";
            try { e = Localisation.LocaliseID(c.locationNameID); } catch { }
            if (string.IsNullOrEmpty(e) || e.StartsWith("PSG_") || e == c.locationNameID) e = c.locationName;
            return e;
        }
    }
}
