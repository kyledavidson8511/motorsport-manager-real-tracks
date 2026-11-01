using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace RealTracks
{
    /// <summary>
    /// Settings are split across files with `partial`: each package adds its own fields in its own file.
    /// Every behaviour gets a toggle or slider (shown in the UMM panel, Ctrl+F10).
    /// </summary>
    public partial class Settings : UnityModManager.ModSettings
    {
        public bool LogEvents = true;

        /// <summary>Folder holding the track packs. Empty = the mod's own "tracks" folder.</summary>
        public string PacksFolder = "";

        /// <summary>Folder for auto-test and marker output. Empty = the mod's own "logs" folder.</summary>
        public string LogFolder = "";

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }

    public static class Main
    {
        public const string Version = "1.0.0";
        public static bool Enabled;
        public static Settings Settings;
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static string ModPath;

        public static string PacksDir
        {
            get { return string.IsNullOrEmpty(Settings.PacksFolder) ? Path.Combine(ModPath, "tracks") : Settings.PacksFolder; }
        }

        public static string LogDir
        {
            get { return string.IsNullOrEmpty(Settings.LogFolder) ? Path.Combine(ModPath, "logs") : Settings.LogFolder; }
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnToggle = (e, v) => { Enabled = v; return true; };
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = e => Settings.Save(e);

            try
            {
                PackRegistry.LoadAll(PacksDir);
            }
            catch (Exception e)
            {
                Logger.Error("Real Tracks: loading track packs failed: " + e);
            }

            new Harmony(modEntry.Info.Id).PatchAll(Assembly.GetExecutingAssembly());
            Enabled = true;

            SafeInit("registration", Registration.Init);
            SafeInit("venue builder", VenueBuilder.Init);
            SafeInit("auto-test", AutoTest.Init);

            Logger.Log("Real Tracks " + Version + " loaded: " + PackRegistry.Packs.Count + " track pack(s) from " + PacksDir);
            return true;
        }

        private static void SafeInit(string what, Action init)
        {
            try { init(); }
            catch (Exception e) { Logger.Error("Real Tracks: " + what + " init failed: " + e); }
        }

        public static void Log(string m)
        {
            if (Settings != null && Settings.LogEvents && Logger != null) Logger.Log(m);
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings s = Settings;
            GUILayout.Label("<b>Real Tracks " + Version + "</b> — " + PackRegistry.Packs.Count + " track pack(s) loaded.");
            foreach (TrackPack p in PackRegistry.Packs)
            {
                GUILayout.Label("  • " + p.Display + " (" + p.Id + "): " + p.BuiltLayoutSummary());
            }
            foreach (string err in PackRegistry.Errors)
            {
                GUILayout.Label("  <color=#ff8080>" + err + "</color>");
            }
            s.LogEvents = GUILayout.Toggle(s.LogEvents, " Log Real Tracks events to the UMM log");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Track packs folder (empty = Mods\\RealTracks\\tracks):", GUILayout.ExpandWidth(false));
            s.PacksFolder = GUILayout.TextField(s.PacksFolder ?? "", GUILayout.Width(500));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Test and marker logs folder (empty = Mods\\RealTracks\\logs):", GUILayout.ExpandWidth(false));
            s.LogFolder = GUILayout.TextField(s.LogFolder ?? "", GUILayout.Width(500));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            Section("Venues (Quick Race)", Registration.DrawGui);
            Section("Venue builder (3D)", VenueBuilder.DrawGui);
            Section("Venue art (terrain, kerbs, props)", ArtGenerator.DrawGui);
            Section("Quick Race list and race intro", UiGui.Draw);
            Section("Racing line and corner speeds", LineCorners.DrawGui);
            Section("Steering on hills", SteeringPitch.DrawGui);
            Section("Auto-test and feedback markers", AutoTest.DrawGui);
        }

        private static void Section(string title, Action<Settings> draw)
        {
            GUILayout.Label("<b>" + title + "</b>");
            try { draw(Settings); }
            catch (Exception e) { GUILayout.Label("(panel error: " + e.Message + ")"); }
            GUILayout.Space(6);
        }
    }
}
