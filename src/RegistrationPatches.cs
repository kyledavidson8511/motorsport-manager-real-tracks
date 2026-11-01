using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RealTracks
{
    // =====================================================================================================
    // WP2 Harmony patches. Rules for every patch here:
    //   * the whole body is in try/catch (log via Main.Logger.Error, leave the game's behaviour intact);
    //   * a guard decides "is this a pack circuit / pack id?" before anything is touched, so stock circuits,
    //     stock ids and stock names pass through unchanged;
    //   * nothing here reads or writes a career: the only place that adds circuits is Patch_RegisterCircuits, which
    //     requires Game.instance.gameType == SingleEvent.
    // =====================================================================================================

    /// <summary>Temporarily shows a pack circuit under its donor's spriteName while a stock method reads it.</summary>
    public class SwapState
    {
        public Circuit Circuit;
        public string Saved;
    }

    public static class DonorSwap
    {
        public static SwapState Begin(Circuit c)
        {
            if (c == null || !Registration.IsPackCircuit(c)) return null;
            string donor = Registration.DonorSprite(c);
            if (string.IsNullOrEmpty(donor)) return null;
            SwapState s = new SwapState();
            s.Circuit = c;
            s.Saved = c.spriteName;
            c.spriteName = donor;
            return s;
        }

        public static void End(SwapState s)
        {
            if (s != null && s.Circuit != null) s.Circuit.spriteName = s.Saved;
        }
    }

    /// <summary>Cached private-field access (no static initialisers that could throw inside a patch).</summary>
    public static class Priv
    {
        private static readonly Dictionary<string, FieldInfo> sCache = new Dictionary<string, FieldInfo>();

        public static T Get<T>(object o, string field) where T : class
        {
            if (o == null) return null;
            string key = o.GetType().FullName + "." + field;
            FieldInfo f;
            if (!sCache.TryGetValue(key, out f))
            {
                f = AccessTools.Field(o.GetType(), field);
                sCache[key] = f;
            }
            return f == null ? null : f.GetValue(o) as T;
        }

        public static void Set(object o, string field, object value)
        {
            if (o == null) return;
            string key = o.GetType().FullName + "." + field;
            FieldInfo f;
            if (!sCache.TryGetValue(key, out f))
            {
                f = AccessTools.Field(o.GetType(), field);
                sCache[key] = f;
            }
            if (f != null) f.SetValue(o, value);
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 1. Registration: the only patch that adds circuits, and only to a non-career game's own manager.
    //    Target CircuitManager.LoadCircuitsFromDatabase(Database, ClimateManager) (CircuitManager.cs:17), called by
    //    Game.StartNewGame (Game.cs:261) and again by QuickRaceSetupScreen.CreateEntries (QuickRaceSetupScreen.cs:107).
    //    Career games reach it only through StartNewGame with gameType == Career, which the guard rejects.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(CircuitManager), "LoadCircuitsFromDatabase")]
    public static class Patch_RegisterCircuits
    {
        public static void Postfix(CircuitManager __instance)
        {
            try
            {
                Game g = Game.instance;
                if (g == null || g.isCareer || g.gameType != Game.GameType.SingleEvent) return;   // career guard
                if (!ReferenceEquals(g.circuitManager, __instance)) return;                        // only the game's own manager
                Registration.RegisterInto(__instance);
            }
            catch (Exception e)
            {
                Registration.RestoreStockList();
                Main.Logger.Error("Real Tracks: circuit registration failed, Quick Race is left stock: " + e);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 2. Names: ids "PSG_9000nnnn" are not in the localisation sheets. Localisation.LocaliseID(string, string, GameObject, string)
    //    (Localisation.cs:119) is where every UI text lookup ends (the 1-argument overload at :107 calls it).
    //    Only our own ids are answered; everything else runs the original.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(Localisation), "LocaliseID", new Type[] { typeof(string), typeof(string), typeof(GameObject), typeof(string) })]
    public static class Patch_LocaliseId
    {
        public static bool Prefix(string inID, ref string __result)
        {
            try
            {
                // our ids are "PSG_9000nnnn"; stock ids are "PSG_1nnnnnnn": a cheap test keeps the hot path hot
                if (inID == null || inID.Length != 12 || inID[4] != '9') return true;
                string name;
                if (Registration.TryGetName(inID, out name)) { __result = name; return false; }
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: LocaliseID patch: " + e); }
            return true;
        }
    }

    /// <summary>
    /// Word-gender lookups by location id (StringVariableParser.cs:2025, 2180) log an error for unknown keys and return "Error".
    /// Pack ids borrow the donor location's gender entry (SpecialStringsGenderTable.cs:35).
    /// </summary>
    [HarmonyPatch(typeof(SpecialStringsGenderTable), "GetGenderForLanguage")]
    public static class Patch_GenderKey
    {
        public static void Prefix(ref string inKey)
        {
            try
            {
                string donor = Registration.DonorLocationId(inKey);
                if (donor != null) inKey = donor;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: gender key patch: " + e); }
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 3. Atlas art. AtlasManager.GetSprite(Atlas, string) (AtlasManager.cs:97) is the single lookup behind
    //    UITrackLayout.UpdateIcon (UITrackLayout.cs:70-71: Minimap_<sprite><layout> in TrackOutlines1) and
    //    UICircuitImage.UpdateIcon (UICircuitImage.cs:36: TrackImages-<sprite> in TrackImages). It would log a warning
    //    and return null for our names. Only names registered by Registration are answered.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(AtlasManager), "GetSprite")]
    public static class Patch_AtlasSprite
    {
        private static readonly HashSet<string> sLogged = new HashSet<string>();

        public static bool Prefix(AtlasManager.Atlas inAtlas, ref string inName, ref Sprite __result)
        {
            try
            {
                if (inName == null || (inAtlas != AtlasManager.Atlas.TrackOutlines1 && inAtlas != AtlasManager.Atlas.TrackImages)) return true;
                if (inName.Length < 12 || inName.IndexOf("RT_", StringComparison.Ordinal) < 0) return true;   // stock names never contain RT_

                PackLayout layout = null;
                TrackPack pack = null;
                bool mini = inAtlas == AtlasManager.Atlas.TrackOutlines1 && Registration.TryGetMinimapLayout(inName, out layout);
                bool image = !mini && inAtlas == AtlasManager.Atlas.TrackImages && Registration.TryGetImagePack(inName, out pack);
                if (!mini && !image) return true;

                TrackPack p = mini ? layout.Pack : pack;
                bool serve = Main.Settings.RegServeArt && !Registration.Running2D;   // 2D mode: donor art, it matches the donor's 2D track
                if (serve)
                {
                    Sprite s = mini ? PackArt.Minimap(layout) : PackArt.CircuitImage(pack);
                    if (s != null) { __result = s; return false; }
                    if (sLogged.Add(inName)) Main.Log("Real Tracks: no pack art for " + inName + ", using the donor's");
                }
                string donor = Registration.DonorSprite(p);
                if (donor == null) return true;   // unknown donor: the original warns and returns null, as for any missing sprite
                inName = mini ? "Minimap_" + donor + layout.Letter : "TrackImages-" + donor;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: atlas sprite patch: " + e); }
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 4. Loading pictures, 3D flow: RaceEventLoadingScreen.SetBackgroundImage (RaceEventLoadingScreen.cs:55) loads
    //    "UI/LoadingScreenImages/" + spriteName into the private RawImage `image`. The prefix shows the donor's name so
    //    the stock load always succeeds; the postfix then puts the pack picture on the RawImage; the finalizer restores
    //    the circuit's name even if the original throws.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(RaceEventLoadingScreen), "SetBackgroundImage")]
    public static class Patch_LoadingScreen
    {
        public static void Prefix(RaceEventLoadingScreen __instance, out SwapState __state)
        {
            __state = null;
            try { __state = DonorSwap.Begin(Priv.Get<Circuit>(__instance, "mCircuit")); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: loading screen patch (prefix): " + e); }
        }

        public static void Postfix(RaceEventLoadingScreen __instance)
        {
            try
            {
                Circuit c = Priv.Get<Circuit>(__instance, "mCircuit");
                if (c == null || !Main.Settings.RegServeArt) return;
                TrackPack p = Registration.PackOf(c);
                if (p == null) return;
                Texture2D t = PackArt.Loading(p);
                RawImage img = Priv.Get<RawImage>(__instance, "image");
                if (t != null && img != null) img.texture = t;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: loading screen patch (postfix): " + e); }
        }

        public static Exception Finalizer(Exception __exception, SwapState __state)
        {
            DonorSwap.End(__state);
            return __exception;
        }
    }

    // 5. Loading picture behind the 2D HUD: UIBackground.Activate2DBackground (UIBackground.cs:107-118) loads the same folder
    //    for the event's circuit and caches by spriteName in mLastBackgroundImageSpriteName. Same swap; the postfix also
    //    resets that cache key to our own name so a later stock circuit with the donor's name reloads its own picture.
    [HarmonyPatch(typeof(UIBackground), "Activate2DBackground")]
    public static class Patch_Background2D
    {
        private static Circuit EventCircuit()
        {
            Game g = Game.instance;
            if (g == null || g.sessionManager == null || g.sessionManager.eventDetails == null) return null;
            return g.sessionManager.eventDetails.circuit;
        }

        public static void Prefix(out SwapState __state)
        {
            __state = null;
            try { __state = DonorSwap.Begin(EventCircuit()); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: 2D background patch (prefix): " + e); }
        }

        public static void Postfix(UIBackground __instance)
        {
            try
            {
                Circuit c = EventCircuit();
                TrackPack p = Registration.PackOf(c);
                if (p == null || !Main.Settings.RegServeArt) return;
                Texture2D t = PackArt.Loading(p);
                if (t == null) return;
                __instance.backgroundImage.texture = t;
                Priv.Set(__instance, "mLastBackgroundImageSpriteName", RegPlan.SpriteName(p.Id));
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: 2D background patch (postfix): " + e); }
        }

        public static Exception Finalizer(Exception __exception, SwapState __state)
        {
            DonorSwap.End(__state);
            return __exception;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 6. Track intro movie: EventLoadingState.GetFirstScreenForState (EventLoadingState.cs:79-88) queues
    //    "TrackMovies/" + spriteName. A pack circuit has no movie, so it plays the donor's (the movie system is not
    //    asked for a missing file, which could stall the load).
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(EventLoadingState), "GetFirstScreenForState")]
    public static class Patch_EventMovie
    {
        public static void Prefix(out SwapState __state)
        {
            __state = null;
            try
            {
                Game g = Game.instance;
                if (g == null || g.player == null || g.player.team == null) return;
                RaceEventDetails ev = g.player.team.championship.GetCurrentEventDetails();
                __state = DonorSwap.Begin(ev != null ? ev.circuit : null);
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: track movie patch: " + e); }
        }

        public static Exception Finalizer(Exception __exception, SwapState __state)
        {
            DonorSwap.End(__state);
            return __exception;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 7. 2D mode (Simulation2D). EventLoadingState.DoCircuitLoad loads circuit.scene + "2D" (EventLoadingState.cs:127-131),
    //    i.e. the donor's 2D scene, so a pack circuit plays on the donor's 2D track. These two methods read spriteName for
    //    the 2D track image (Simulation2D.cs:53) and a per-venue minimap placement switch (:62-130): both get the donor's name.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(Simulation2D), "SetCircuit")]
    public static class Patch_Sim2DCircuit
    {
        public static void Prefix(Circuit inCircuit, out SwapState __state)
        {
            __state = null;
            try { __state = DonorSwap.Begin(inCircuit); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: 2D circuit patch: " + e); }
        }

        public static Exception Finalizer(Exception __exception, SwapState __state)
        {
            DonorSwap.End(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Simulation2D), "SetupMinimap")]
    public static class Patch_Sim2DMinimap
    {
        public static void Prefix(Simulation2D __instance, out SwapState __state)
        {
            __state = null;
            try { __state = DonorSwap.Begin(Priv.Get<Circuit>(__instance, "mCurrentCircuit")); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: 2D minimap patch: " + e); }
        }

        public static Exception Finalizer(Exception __exception, SwapState __state)
        {
            DonorSwap.End(__state);
            return __exception;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 8. Ambience: scSessionAmbience.Start(string locationName, ...) (scSessionAmbience.cs:40) switches on the location
    //    name; an unknown name plays plain wind. Pack locations use the manifest's ambienceFrom (the donor's name).
    //    Callers: SessionHUBScreen.cs:77 and AudioCircuitCarsTrackWidget.cs:192.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(scSessionAmbience), "Start")]
    public static class Patch_Ambience
    {
        public static void Prefix(ref string locationName)
        {
            try
            {
                string a = Registration.AmbienceFor(locationName);
                if (a != null) locationName = a;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: ambience patch: " + e); }
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // 9. Files outside the career save that a Quick Race at a pack venue could otherwise write:
    //    a) the cross-game "tracks raced on" file (Cloud/Achievements.ach), written by AddTrackVariationRacedOn
    //       (MM2/CrossGameAchievementDataStorage.cs:42, called from RaceEventResults.cs:1002 after a race);
    //    b) saved car setups (Cloud/RaceSetups/*.sav), keyed by circuit id and listed in careers too, where the id would not
    //       resolve (UISavedSetupEntry.cs:102-107 dereferences GetCircuitByID(...) without a null check).
    //    Each has its own toggle.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(MM2.CrossGameAchievementDataStorage), "AddTrackVariationRacedOn")]
    public static class Patch_NoAchievementStore
    {
        public static bool Prefix(string location)
        {
            try
            {
                if (Main.Settings.RegSkipAchievementStore && Registration.IsPackLocation(location)) return false;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: achievements patch: " + e); }
            return true;
        }
    }

    [HarmonyPatch(typeof(SessionSetupManager), "SaveSetup", new Type[] { typeof(SessionSetupSaveFile), typeof(RacingVehicle) })]
    public static class Patch_NoSetupSave
    {
        public static bool Prefix(SessionSetupSaveFile inSaveFile)
        {
            try
            {
                if (Main.Settings.RegBlockSetupSave && inSaveFile != null && PackRegistry.FindLayoutByCircuitId(inSaveFile.circuitID) != null)
                {
                    Main.Log("Real Tracks: saved setups are not stored for pack venues (circuit " + inSaveFile.circuitID + "); the setup stays in this race only.");
                    return false;
                }
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: setup save patch: " + e); }
            return true;
        }
    }
}
