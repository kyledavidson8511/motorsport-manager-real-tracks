using System;
using HarmonyLib;

namespace RealTracks
{
    // Targets checked against the decompiled source:
    //   CircuitScene.SetTrackLayout(Circuit.TrackLayout inLayout)  public   CircuitScene.cs:396
    //   CircuitScene.Start()                                       private  CircuitScene.cs:156
    // Every body is wrapped in try/catch and leaves the game's behaviour intact; every body starts with the
    // "is this a pack circuit?" test (ActiveVenue.CurrentLayout) inside VenueBuilder, so stock circuits are untouched.

    [HarmonyPatch(typeof(CircuitScene), "SetTrackLayout")]
    public static class Patch_Venue_SetTrackLayout
    {
        public static void Prefix(CircuitScene __instance, ref Circuit.TrackLayout inLayout)
        {
            try { VenueBuilder.BeforeSetTrackLayout(__instance, ref inLayout); }
            catch (Exception e) { Main.Logger.Error("Real Tracks venue (SetTrackLayout prefix) failed, game continues: " + e); }
        }

        public static void Postfix(CircuitScene __instance)
        {
            try { VenueBuilder.AfterSetTrackLayout(__instance); }
            catch (Exception e) { Main.Logger.Error("Real Tracks venue (SetTrackLayout postfix) failed, game continues: " + e); }
        }
    }

    [HarmonyPatch(typeof(CircuitScene), "Start")]
    public static class Patch_Venue_Start
    {
        public static void Postfix(CircuitScene __instance)
        {
            try { VenueBuilder.AfterStart(__instance); }
            catch (Exception e) { Main.Logger.Error("Real Tracks venue (Start postfix) failed, game continues: " + e); }
        }
    }
}
