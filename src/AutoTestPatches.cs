using System;
using System.Reflection;
using HarmonyLib;

namespace RealTracks
{
    // Every body is wrapped so a failure here can never break the game; each target was checked against the decompile
    // (see ROUTE.md for the file:line of every one).

    /// <summary>Called after every simulation step of a SessionState (SessionState.SimulationUpdate, SessionState.cs:64). RaceSessionState and RaceGridState do not override it.</summary>
    [HarmonyPatch(typeof(SessionState), "SimulationUpdate")]
    public static class AtPatch_SessionStep
    {
        public static void Postfix()
        {
            try { RoadEdgeMonitor.OnStep(); AtSim.OnStep(); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test (step): " + e.Message); }
        }
    }

    /// <summary>The cars are on the grid when the race state is entered (RaceSessionState.OnEnter, RaceSessionState.cs:7). Also resets the sim clock.</summary>
    [HarmonyPatch(typeof(RaceSessionState), "OnEnter")]
    public static class AtPatch_RaceEnter
    {
        public static void Postfix()
        {
            try { RoadEdgeMonitor.OnRaceEnter(); AtSim.OnRaceEnter(); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test (race start): " + e); }
        }
    }

    /// <summary>Gate speeds (RacingVehicle.OnEnterGate, RacingVehicle.cs:533).</summary>
    [HarmonyPatch(typeof(RacingVehicle), "OnEnterGate")]
    public static class AtPatch_Gate
    {
        public static void Postfix(RacingVehicle __instance, int inGateID)
        {
            try { if (AtRun.Current != null) AtSim.OnGate(__instance, inGateID); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test (gate): " + e.Message); }
        }
    }

    /// <summary>
    /// The requested lap count (DesignDataManager.CalculateRaceLapCount, DesignDataManager.cs:101). Every caller (SessionManager.cs:459,
    /// Fuel.cs:129, PitStopManager.cs:153, SessionStrategy.cs:1244, SimulationUtility.cs:771) then sees the same number.
    /// </summary>
    [HarmonyPatch(typeof(DesignDataManager), "CalculateRaceLapCount")]
    public static class AtPatch_Laps
    {
        public static void Postfix(ref int __result)
        {
            try
            {
                AtRunCtx r = AtRun.Current;
                if (r == null || !r.ItemActive || r.Cur == null) return;
                if (Game.instance == null || Game.instance.gameType != Game.GameType.SingleEvent) return;
                __result = r.Cur.LapsTarget;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test (laps): " + e.Message); }
        }
    }

    /// <summary>
    /// GUARD 2: no save is ever written while an auto-test runs (SaveSystem.SaveImplementation, SaveSystem.cs:455). The game itself only
    /// saves in a career (SaveSystem.cs:461) and the auto-test refuses to run in one (AtDriver).
    /// </summary>
    [HarmonyPatch(typeof(SaveSystem), "SaveImplementation")]
    public static class AtPatch_NoSave
    {
        public static bool Prefix()
        {
            try { return !AutoTest.Running; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Movies are skipped during an auto-test (BaseMovieScreen.PlayNextMovieInQueue, BaseMovieScreen.cs:137). The patch only stops the Bink
    /// playback; AtDriver then calls the screen's own Continue (BaseMovieScreen.cs:171) once the screen has settled, so the screen change
    /// is not started from inside OnEnter.
    /// </summary>
    [HarmonyPatch(typeof(BaseMovieScreen), "PlayNextMovieInQueue")]
    public static class AtPatch_SkipMovie
    {
        private static readonly FieldInfo queue = AccessTools.Field(typeof(BaseMovieScreen), "mMovieQueue");

        public static bool Prefix(BaseMovieScreen __instance)
        {
            try
            {
                if (AtRun.Current == null || !Main.Settings.TestSkipMovies) return true;
                if (queue == null || queue.GetValue(__instance) == null) return true;
                AtDriver.PendingMovie = __instance;
                AtDriver.PendingMovieSince = UnityEngine.Time.realtimeSinceStartup;
                return false;
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test (movie): " + e.Message);
                return true;
            }
        }
    }

    /// <summary>The note box uses Esc itself; keep the game's pause menu out of it (GameState.CheckForEscapeButton, GameState.cs:149).</summary>
    [HarmonyPatch(typeof(GameState), "CheckForEscapeButton")]
    public static class AtPatch_Escape
    {
        public static bool Prefix()
        {
            try { return !AtMarkers.NoteOpen; }
            catch { return true; }
        }
    }
}

namespace RealTracks
{
    /// <summary>
    /// Stock-game bug exposed by short auto-test races (run 3, stock Phoenix B): after the leader takes the flag,
    /// PathController.GetRaceDistanceUsingGates01 (PathController.cs:1062-1074) can exceed 1 by up to 1/lapCount, and
    /// DriverForm.GetAverageFormForSpan / GetCurrentForm (DriverForm.cs:137-165, 246-254) then index past their form table
    /// (IndexOutOfRangeException at the chequered flag). Real races are long enough that the overshoot stays inside the table.
    /// While an auto-test runs (and only then, toggle TestClampSessionTime), keep the normalised session time inside [0, 0.999].
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(SessionManager), "GetNormalizedSessionTime")]
    public static class AtPatch_ClampSessionTime
    {
        public static void Postfix(ref float __result)
        {
            try
            {
                if (!AutoTest.Running || !Main.Settings.TestClampSessionTime) return;
                if (__result > 0.999f) __result = 0.999f;
                else if (__result < 0f) __result = 0f;
            }
            catch (System.Exception) { }
        }
    }
}
