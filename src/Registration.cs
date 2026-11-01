using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>WP2 settings (prefix Reg). Every behaviour of the registration and art serving has a toggle.</summary>
    public partial class Settings
    {
        /// <summary>Register built pack layouts as Quick Race circuits (read when a Quick Race game starts).</summary>
        public bool RegEnabled = true;
        /// <summary>Serve the pack's minimap and loading art. Off = every art slot shows the donor venue's art.</summary>
        public bool RegServeArt = true;
        /// <summary>In 2D mode pack venues run on the donor's 2D track; this hides them from Quick Race instead.</summary>
        public bool RegHideIn2D = false;
        /// <summary>Do not store saved car setups for pack venues (the career setup list cannot resolve their circuit ids).</summary>
        public bool RegBlockSetupSave = true;
        /// <summary>Do not record pack venues in the cross-game "tracks raced on" achievements file.</summary>
        public bool RegSkipAchievementStore = true;
    }

    /// <summary>One circuit that Registration added to a Quick Race game (for the panel and the log).</summary>
    public class RegisteredCircuit
    {
        public int CircuitId;
        public string Location, Key, Letter, SpriteName, LocationNameId, DonorLocation, DonorSprite, Scene, Country;
        public RowResult Row;
    }

    /// <summary>
    /// WP2: registers built pack layouts as Circuits in the NON-CAREER (Quick Race) circuit manager only, lists the pack
    /// venue in Quick Race, localises its names and serves its art. The Harmony patches are in RegistrationPatches.cs.
    /// Stock circuits are never modified: everything here only adds, or only acts on circuits whose id is 9000+.
    /// </summary>
    public static class Registration
    {
        /// <summary>The circuits added to the most recent Quick Race circuit manager.</summary>
        public static readonly List<RegisteredCircuit> Registered = new List<RegisteredCircuit>();
        public static readonly List<string> Notes = new List<string>();

        // localisation id -> display name; id -> the donor location's id (for the gender table)
        private static readonly Dictionary<string, string> sIdToName = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> sIdToDonorId = new Dictionary<string, string>();
        // location display -> pack (ambience, achievements)
        private static readonly Dictionary<string, TrackPack> sNameToPack = new Dictionary<string, TrackPack>();
        // atlas sprite names -> what they stand for
        private static readonly Dictionary<string, PackLayout> sMinimapNames = new Dictionary<string, PackLayout>();
        private static readonly Dictionary<string, TrackPack> sImageNames = new Dictionary<string, TrackPack>();
        // pack id -> the donor circuit's spriteName
        private static readonly Dictionary<string, string> sDonorSprite = new Dictionary<string, string>();

        private static string[] sStockQuickRace;

        /// <summary>Puts the Quick Race list back to the stock 16 names (used if registration fails part-way).</summary>
        public static void RestoreStockList()
        {
            if (sStockQuickRace != null) QuickRaceSetupScreen.circuitsArray = (string[])sStockQuickRace.Clone();
        }

        public static void Init()
        {
            // Id tables for every loaded pack: harmless until a pack circuit exists, and Localisation needs them early.
            foreach (TrackPack p in PackRegistry.Packs)
            {
                sIdToName[RegPlan.LocationNameId(p.PackIndex)] = p.Display;
            }
            Main.Log("Real Tracks: registration ready for " + PackRegistry.Packs.Count + " pack(s); Quick Race registration is " + (Main.Settings.RegEnabled ? "ON" : "OFF"));
        }

        // ------------------------------------------------------------------------------------------ queries

        /// <summary>True for a circuit that this mod registered (id 9000+ and a known pack layout). Stock circuits: false.</summary>
        public static bool IsPackCircuit(Circuit c) { return ActiveVenue.For(c) != null; }

        public static TrackPack PackOf(Circuit c)
        {
            PackLayout l = ActiveVenue.For(c);
            return l == null ? null : l.Pack;
        }

        public static bool IsPackLocation(string locationName)
        {
            return locationName != null && sNameToPack.ContainsKey(locationName);
        }

        /// <summary>The name of the stock location whose ambience a pack location uses, or null if it is not a pack location.</summary>
        public static string AmbienceFor(string locationName)
        {
            TrackPack p;
            return locationName != null && sNameToPack.TryGetValue(locationName, out p) ? p.AmbienceFrom : null;
        }

        public static string DonorSprite(TrackPack p)
        {
            string s;
            return p != null && sDonorSprite.TryGetValue(p.Id, out s) ? s : null;
        }

        public static string DonorSprite(Circuit c) { return DonorSprite(PackOf(c)); }

        public static bool TryGetName(string localisationId, out string name) { return sIdToName.TryGetValue(localisationId, out name); }

        public static string DonorLocationId(string localisationId)
        {
            string s;
            return sIdToDonorId.TryGetValue(localisationId, out s) ? s : null;
        }

        public static bool TryGetMinimapLayout(string atlasName, out PackLayout l) { return sMinimapNames.TryGetValue(atlasName, out l); }
        public static bool TryGetImagePack(string atlasName, out TrackPack p) { return sImageNames.TryGetValue(atlasName, out p); }

        /// <summary>
        /// True while the game runs in 2D mode (Video_3D off). Pack venues then run on the donor's 2D scene and art, so
        /// VenueBuilder must not rebuild a 2D scene (EventLoadingState.cs:125-131 loads circuit.scene + "2D").
        /// </summary>
        public static bool Running2D
        {
            get
            {
                try { return App.instance != null && !App.instance.preferencesManager.GetSettingBool(Preference.pName.Video_3D, true); }
                catch (Exception) { return false; }
            }
        }

        // ------------------------------------------------------------------------------------------ plan

        public class PackPlan
        {
            public TrackPack Pack;
            public List<PackLayout> Layouts = new List<PackLayout>();
        }

        /// <summary>Which packs and layouts will be registered now (settings, 2D mode, name clashes, layout A present).</summary>
        public static List<PackPlan> Plan(List<string> notes)
        {
            List<PackPlan> plan = new List<PackPlan>();
            if (!Main.Settings.RegEnabled) return plan;
            if (Main.Settings.RegHideIn2D && Running2D)
            {
                notes.Add("2D mode is on and 'hide pack venues in 2D' is set: no pack venues offered");
                return plan;
            }
            HashSet<string> stockNames = new HashSet<string>();
            HashSet<string> stockScenes = new HashSet<string>();
            List<DatabaseEntry> rows = App.instance.database.locationData;
            for (int i = 0; i < rows.Count; i++)
            {
                stockNames.Add(rows[i].GetStringValue("Location"));
                stockScenes.Add(rows[i].GetStringValue("Scene"));
            }
            foreach (TrackPack p in PackRegistry.Packs)
            {
                if (stockNames.Contains(p.Display))
                {
                    notes.Add(p.Id + ": display name '" + p.Display + "' is the name of a stock venue, not registered");
                    continue;
                }
                if (!stockScenes.Contains(p.DonorScene))
                {
                    notes.Add(p.Id + ": donor scene '" + p.DonorScene + "' is not a stock scene, not registered");
                    continue;
                }
                List<string> built = new List<string>();
                foreach (PackLayout l in p.Layouts) if (l.Built) built.Add(l.Letter);
                List<string> letters = RegPlan.ContiguousLetters(built);
                if (letters.Count == 0)
                {
                    notes.Add(p.Id + ": layout A is not built, not offered in Quick Race");
                    continue;
                }
                PackPlan pp = new PackPlan();
                pp.Pack = p;
                foreach (string letter in letters)
                {
                    foreach (PackLayout l in p.Layouts)
                        if (l.Built && l.Letter == letter) { pp.Layouts.Add(l); break; }
                }
                if (built.Count > letters.Count)
                    notes.Add(p.Id + ": built layouts after a gap in the letters are not offered (Quick Race steps through A, B, C in order)");
                plan.Add(pp);
            }
            return plan;
        }

        // ------------------------------------------------------------------------------------------ registration

        /// <summary>
        /// Called from the postfix on CircuitManager.LoadCircuitsFromDatabase, only for the non-career game's own manager.
        /// Adds the planned circuits (atomically per pack) and rebuilds the Quick Race location list.
        /// </summary>
        public static void RegisterInto(CircuitManager mgr)
        {
            Registered.Clear();
            Notes.Clear();
            if (sStockQuickRace == null) sStockQuickRace = (string[])QuickRaceSetupScreen.circuitsArray.Clone();

            List<string> offered = new List<string>(sStockQuickRace);
            List<PackPlan> plan = Plan(Notes);
            foreach (PackPlan pp in plan)
            {
                try
                {
                    List<Circuit> made = new List<Circuit>();
                    List<RegisteredCircuit> info = new List<RegisteredCircuit>();
                    BuildPack(mgr, pp, made, info);
                    // every layout built without an exception: now add
                    foreach (Circuit c in made) mgr.circuits.Add(c);
                    Registered.AddRange(info);
                    sNameToPack[pp.Pack.Display] = pp.Pack;
                    // with the custom column the venue is listed there (UiQuickRace), not in the stock grid
                    if (!Main.Settings.UiCustomTrackColumn) offered.Add(pp.Pack.Display);
                    Main.Log("Real Tracks: Quick Race venue '" + pp.Pack.Display + "' registered with " + made.Count + " layout(s).");
                }
                catch (Exception e)
                {
                    Notes.Add(pp.Pack.Id + ": registration failed: " + e.Message);
                    Main.Logger.Error("Real Tracks: registering pack '" + pp.Pack.Id + "' failed, it is not offered: " + e);
                }
            }
            // Quick Race builds its list from this array right after LoadCircuitsFromDatabase returns (QuickRaceSetupScreen.cs:107-112)
            // and looks every name up in the manager, so only names that registered successfully go in.
            QuickRaceSetupScreen.circuitsArray = offered.ToArray();
            foreach (string n in Notes) Main.Log("Real Tracks: " + n);
        }

        private static RowValues DonorValues(Circuit d)
        {
            RowValues v = new RowValues();
            v.TrackLengthMiles = d.trackLengthMiles;
            v.SafetyCar = d.safetyCarFlagProbability;
            v.VirtualSafetyCar = d.virtualSafetyCarProbability;
            v.RubberModifier = d.trackRubberModifier;
            v.TravelCostMillions = (float)d.travelCost / (float)GameStatsConstants.millionScalar;
            v.MinimapRotation = d.minimapRotation;
            v.MinimapAxisX = d.minimapAxisX;
            v.TS = d.trackStatsCharacteristics.GetStat(CarStats.StatType.TopSpeed);
            v.ACC = d.trackStatsCharacteristics.GetStat(CarStats.StatType.Acceleration);
            v.DEC = d.trackStatsCharacteristics.GetStat(CarStats.StatType.Braking);
            v.HSC = d.trackStatsCharacteristics.GetStat(CarStats.StatType.HighSpeedCorners);
            v.MSC = d.trackStatsCharacteristics.GetStat(CarStats.StatType.MediumSpeedCorners);
            v.LSC = d.trackStatsCharacteristics.GetStat(CarStats.StatType.LowSpeedCorners);
            v.Tyre1 = d.firstTyreOption.ToString();
            v.Tyre2 = d.secondTyreOption.ToString();
            v.Tyre3 = d.thirdTyreOption.ToString();
            v.TyreWear = d.tyreWearRate.ToString();
            v.FuelBurn = d.fuelBurnRate.ToString();
            return v;
        }

        private static Circuit FindStock(CircuitManager mgr, string scene)
        {
            List<Circuit> all = mgr.circuits;
            for (int i = 0; i < all.Count; i++)
                if (all[i].circuitID < 9000 && all[i].scene == scene) return all[i];
            return null;
        }

        private static void BuildPack(CircuitManager mgr, PackPlan pp, List<Circuit> made, List<RegisteredCircuit> info)
        {
            TrackPack pack = pp.Pack;
            Circuit donor = FindStock(mgr, pack.DonorScene);
            if (donor == null) throw new Exception("no stock circuit uses donor scene '" + pack.DonorScene + "'");
            Circuit climateDonor = FindStock(mgr, pack.ClimateFrom) ?? donor;

            string[] compounds = Enum.GetNames(typeof(TyreSet.Compound));
            string locId = RegPlan.LocationNameId(pack.PackIndex);
            string sprite = RegPlan.SpriteName(pack.Id);

            // Country: the manifest's key and id when the game knows them, else the donor's (noted).
            string natKey = pack.CountryKey;
            if (string.IsNullOrEmpty(natKey) || !App.instance.nationalityManager.nationalitiesDict.ContainsKey(natKey))
            {
                Notes.Add(pack.Id + ": country key '" + natKey + "' is not a game nationality, using the donor's '" + donor.nationalityKey + "'");
                natKey = donor.nationalityKey;
            }
            string countryId = pack.CountryNameID;
            if (string.IsNullOrEmpty(countryId) || countryId.Length != "PSG_10000001".Length || !Localisation.HasID(countryId))
            {
                if (!string.IsNullOrEmpty(countryId)) Notes.Add(pack.Id + ": countryNameID '" + countryId + "' is not a game text id, using the donor's '" + donor.countryNameID + "'");
                countryId = donor.countryNameID;
            }

            DriverStatsProgression stats = Game.instance.driverStatsProgressionManager.GetDriverStatsProgression(pack.DriverStatsFrom);
            if (stats == null)
            {
                Notes.Add(pack.Id + ": no driver stats progression named '" + pack.DriverStatsFrom + "', using the donor circuit's");
                stats = donor.driverStats;
            }

            RowValues donorRow = DonorValues(donor);
            foreach (PackLayout l in pp.Layouts)
            {
                RowResult rr = RowMapper.Map(l.Data, l.LengthM, donorRow, compounds);
                RowValues v = rr.Values;

                Circuit c = new Circuit();
                c.circuitID = l.CircuitId;
                c.locationName = pack.Display;
                c.locationNameID = locId;
                c.nationalityKey = natKey;
                c.countryNameID = countryId;
                c.spriteName = sprite;
                c.scene = pack.DonorScene;
                c.trackLayout = l.TrackLayout;
                c.isInNorthHemisphere = pack.Hemisphere == "North";
                c.travelCost = (long)(v.TravelCostMillions * (float)GameStatsConstants.millionScalar);
                c.trackRubberModifier = v.RubberModifier;
                c.safetyCarFlagProbability = v.SafetyCar;
                c.virtualSafetyCarProbability = v.VirtualSafetyCar;
                c.minimapRotation = v.MinimapRotation;
                c.minimapAxisX = v.MinimapAxisX;
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.TopSpeed, v.TS);
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.Acceleration, v.ACC);
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.Braking, v.DEC);
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.HighSpeedCorners, v.HSC);
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.MediumSpeedCorners, v.MSC);
                c.trackStatsCharacteristics.SetStat(CarStats.StatType.LowSpeedCorners, v.LSC);
                c.trackLengthMiles = v.TrackLengthMiles;
                c.firstTyreOption = (TyreSet.Compound)(int)Enum.Parse(typeof(TyreSet.Compound), v.Tyre1);
                c.secondTyreOption = (TyreSet.Compound)(int)Enum.Parse(typeof(TyreSet.Compound), v.Tyre2);
                c.thirdTyreOption = (TyreSet.Compound)(int)Enum.Parse(typeof(TyreSet.Compound), v.Tyre3);
                c.tyreWearRate = (Circuit.Rate)(int)Enum.Parse(typeof(Circuit.Rate), v.TyreWear);
                c.fuelBurnRate = (Circuit.Rate)(int)Enum.Parse(typeof(Circuit.Rate), v.FuelBurn);
                // Best and worst lap time exactly as CircuitManager.cs:51-56 (120 and 90 mph; miles -> minutes -> seconds).
                float inMinutes = c.trackLengthMiles / 120f * 60f;
                float inMinutes2 = c.trackLengthMiles / 90f * 60f;
                c.bestPossibleLapTime = GameUtility.MinutesToSeconds(inMinutes);
                c.worstPossibleLapTime = GameUtility.MinutesToSeconds(inMinutes2);
                ClimateManager cm = Game.instance.climateManager;
                c.climate = v.ClimateIndex >= 0 ? cm.GetClimate(Mathf.Clamp(v.ClimateIndex, 0, cm.climateCount - 1)) : climateDonor.climate;
                c.driverStats = stats;
                c.globeLocationID = donor.globeLocationID;
                made.Add(c);

                RegisteredCircuit rc = new RegisteredCircuit();
                rc.CircuitId = l.CircuitId;
                rc.Location = pack.Display;
                rc.Key = l.Key;
                rc.Letter = l.Letter;
                rc.SpriteName = sprite;
                rc.LocationNameId = locId;
                rc.DonorLocation = donor.locationName;
                rc.DonorSprite = donor.spriteName;
                rc.Scene = pack.DonorScene;
                rc.Country = natKey;
                rc.Row = rr;
                info.Add(rc);

                sMinimapNames["Minimap_" + sprite + c.GetTrackVariation()] = l;
                foreach (string fb in rr.Fallbacks) RtFallbacks.Note("locations row " + pack.Display + " " + l.Key, fb);
                Main.Log("Real Tracks: circuit " + l.CircuitId + " " + pack.Display + " " + l.Letter + " (" + l.Key + "): " + v.TrackLengthMiles.ToString("0.000") + " mi, " +
                         rr.FromPack.Count + " columns from the pack, " + rr.Fallbacks.Count + " fallbacks" +
                         (rr.Fallbacks.Count > 0 ? ": " + string.Join("; ", rr.Fallbacks.ToArray()) : "") +
                         (rr.Warnings.Count > 0 ? " | WARNINGS: " + string.Join("; ", rr.Warnings.ToArray()) : ""));
            }
            sImageNames["TrackImages-" + sprite] = pack;
            sDonorSprite[pack.Id] = donor.spriteName;
            sIdToName[locId] = pack.Display;
            sIdToDonorId[locId] = donor.locationNameID;
        }

        // ------------------------------------------------------------------------------------------ panel

        public static void DrawGui(Settings s)
        {
            GUILayout.Label("Real Tracks adds each built pack layout to Quick Race as its own venue (city name, own minimap). Career games are never touched.");
            s.RegEnabled = GUILayout.Toggle(s.RegEnabled, " Register pack venues in Quick Race (read each time a Quick Race game starts; takes effect next game start)");
            s.RegServeArt = GUILayout.Toggle(s.RegServeArt, " Serve the pack's own minimap and loading picture (off = the donor venue's art in every slot)");
            s.RegHideIn2D = GUILayout.Toggle(s.RegHideIn2D, " In 2D mode, hide pack venues from Quick Race (otherwise they run on the donor's 2D track, art and all)");
            s.RegBlockSetupSave = GUILayout.Toggle(s.RegBlockSetupSave, " Do not store saved car setups made at pack venues (a career could not list them)");
            s.RegSkipAchievementStore = GUILayout.Toggle(s.RegSkipAchievementStore, " Do not record pack venues in the cross-game 'tracks raced on' file");

            GUILayout.Space(4);
            if (Registered.Count > 0)
            {
                GUILayout.Label("<b>Registered in the current/last Quick Race game</b> (read-only):");
                foreach (RegisteredCircuit rc in Registered) DrawRow(rc);
            }
            else
            {
                GUILayout.Label("<b>Pack layouts that will register</b> (manifest values; nothing registered yet in this session):");
                foreach (TrackPack p in PackRegistry.Packs)
                {
                    foreach (PackLayout l in p.Layouts)
                    {
                        if (!l.Built) { GUILayout.Label("  " + p.Display + " " + l.Letter + " (" + l.Key + "): not built"); continue; }
                        StringBuilder sb = new StringBuilder();
                        foreach (KeyValuePair<string, object> kv in l.Data) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("  ");
                        GUILayout.Label("  id " + l.CircuitId + "  " + p.Display + " " + l.Letter + " (" + l.Key + "), donor " + p.DonorScene + ": " + sb);
                    }
                }
            }
            foreach (string n in Notes) GUILayout.Label("  <color=#ffcc66>" + n + "</color>");
        }

        private static void DrawRow(RegisteredCircuit rc)
        {
            RowResult rr = rc.Row;
            GUILayout.Label("  id " + rc.CircuitId + "  <b>" + rc.Location + " " + rc.Letter + " (" + rc.Key + ")</b>  donor " + rc.DonorLocation + " (art '" + rc.DonorSprite + "'), sprite '" + rc.SpriteName + "', text id " + rc.LocationNameId + ", country " + rc.Country);
            RowValues v = rr.Values;
            GUILayout.Label("      length " + v.TrackLengthMiles.ToString("0.000") + " mi | SC " + v.SafetyCar.ToString("0.##") + " VSC " + v.VirtualSafetyCar.ToString("0.##") +
                            " rubber " + v.RubberModifier.ToString("0.##") + " | travel " + v.TravelCostMillions.ToString("0.###") + "m | minimap rot " + v.MinimapRotation + " axis " + (v.MinimapAxisX ? "x" : "z") +
                            " | TS " + v.TS + " ACC " + v.ACC + " DEC " + v.DEC + " HSC " + v.HSC + " MSC " + v.MSC + " LSC " + v.LSC +
                            " | tyres " + v.Tyre1 + "/" + v.Tyre2 + "/" + v.Tyre3 + " wear " + v.TyreWear + " fuel " + v.FuelBurn);
            if (rr.Fallbacks.Count > 0) GUILayout.Label("      <color=#ffcc66>fell back: " + string.Join("; ", rr.Fallbacks.ToArray()) + "</color>");
            if (rr.Warnings.Count > 0) GUILayout.Label("      <color=#ff8080>warnings: " + string.Join("; ", rr.Warnings.ToArray()) + "</color>");
        }
    }
}
