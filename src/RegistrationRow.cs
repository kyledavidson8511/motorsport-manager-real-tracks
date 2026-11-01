using System;
using System.Collections.Generic;
using System.Globalization;

namespace RealTracks
{
    // =====================================================================================================
    // Pure helpers for WP2 (venue registration): no Unity or game types, so the dry-run test project
    // (wp2/test) compiles this file directly under net8.
    //   - RowMapper:   a pack layout's "data" cells -> the values of one Locations row, with fallbacks
    //   - RegPlan:     which layouts of a pack can be offered in Quick Race, ids for localisation, names
    // =====================================================================================================

    /// <summary>The Locations-row values a Circuit is built from (plain numbers and names).</summary>
    public class RowValues
    {
        public float TrackLengthMiles, SafetyCar, VirtualSafetyCar, RubberModifier, TravelCostMillions;
        public int MinimapRotation;
        public bool MinimapAxisX;
        public float TS, ACC, DEC, HSC, MSC, LSC;
        public string Tyre1, Tyre2, Tyre3, TyreWear, FuelBurn;
        /// <summary>0..4 as CircuitManager.GetClimateFromString maps it, or -1 = use the donor's climate object.</summary>
        public int ClimateIndex = -1;

        public RowValues Copy() { return (RowValues)MemberwiseClone(); }
    }

    public class RowResult
    {
        public RowValues Values;
        /// <summary>Columns taken from the pack manifest, as "column = value".</summary>
        public readonly List<string> FromPack = new List<string>();
        /// <summary>Columns that fell back (to the donor row, or to a derived value), as "column: why".</summary>
        public readonly List<string> Fallbacks = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    public static class RowMapper
    {
        public const float MetresPerMile = 1609.344f;
        public static readonly string[] RateNames = { "VeryLow", "Low", "Medium", "High", "VeryHigh" };

        public static int ClimateIndex(string name)
        {
            // Mirrors CircuitManager.GetClimateFromString (CircuitManager.cs:107-128): anything else is climate 0.
            switch (name)
            {
                case "Moderate": return 1;
                case "Warm": return 2;
                case "Tropical": return 3;
                case "Dry": return 4;
                default: return 0;
            }
        }

        private static bool TryFloat(IDictionary<string, object> data, string col, out float v)
        {
            v = 0f;
            object o;
            if (data == null || !data.TryGetValue(col, out o) || o == null) return false;
            double d;
            if (o is double) d = (double)o;
            else if (o is bool) d = (bool)o ? 1 : 0;
            else if (!double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            v = (float)d;
            return true;
        }

        private static bool TryString(IDictionary<string, object> data, string col, out string s)
        {
            s = null;
            object o;
            if (data == null || !data.TryGetValue(col, out o) || o == null) return false;
            s = o is double ? ((double)o).ToString(CultureInfo.InvariantCulture) : o.ToString();
            return s.Length > 0;
        }

        private static string Canonical(string value, string[] names)
        {
            foreach (string n in names)
                if (string.Equals(n, value, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        private static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>
        /// Maps a layout's data cells onto a row. Missing or unusable cells fall back to <paramref name="donor"/>
        /// (the donor scene's first stock circuit), except the track length, which falls back to lengthM first.
        /// <paramref name="compoundNames"/> are the names of TyreSet.Compound.
        /// </summary>
        public static RowResult Map(IDictionary<string, object> data, float lengthM, RowValues donor, string[] compoundNames)
        {
            RowResult r = new RowResult();
            RowValues v = donor.Copy();
            r.Values = v;

            // ---- track length (miles)
            float len;
            bool haveLen = TryFloat(data, "Track Length", out len);
            if (haveLen && (len < 0.1f || len > 20f))
            {
                r.Warnings.Add("Track Length " + F(len) + " mi is outside 0.1..20, ignored");
                haveLen = false;
            }
            if (haveLen)
            {
                v.TrackLengthMiles = len;
                r.FromPack.Add("Track Length = " + F(len) + " mi");
                if (lengthM > 0f)
                {
                    float fromM = lengthM / MetresPerMile;
                    if (Math.Abs(fromM - len) / fromM > 0.02f)
                        r.Warnings.Add("Track Length " + F(len) + " mi differs from lengthM " + F(lengthM) + " m (" + F(fromM) + " mi) by more than 2%");
                }
            }
            else if (lengthM > 0f)
            {
                v.TrackLengthMiles = lengthM / MetresPerMile;
                r.Fallbacks.Add("Track Length: missing or unusable, derived from lengthM " + F(lengthM) + " m = " + F(v.TrackLengthMiles) + " mi");
            }
            else
            {
                r.Fallbacks.Add("Track Length: missing, no lengthM either, donor value " + F(donor.TrackLengthMiles) + " mi");
            }

            // ---- floats with ranges
            v.SafetyCar = Float(data, "Safety Car Flag Chance", donor.SafetyCar, 0f, 1f, r);
            v.VirtualSafetyCar = Float(data, "Virtual Safety Car Chance", donor.VirtualSafetyCar, 0f, 1f, r);
            v.RubberModifier = Float(data, "Track Rubber Modifier", donor.RubberModifier, 0.01f, 5f, r);
            v.TravelCostMillions = Float(data, "Travel Cost", donor.TravelCostMillions, 0f, 1000f, r);
            v.TS = Float(data, "TS", donor.TS, 0f, 10f, r);
            v.ACC = Float(data, "ACC", donor.ACC, 0f, 10f, r);
            v.DEC = Float(data, "DEC", donor.DEC, 0f, 10f, r);
            v.HSC = Float(data, "HSC", donor.HSC, 0f, 10f, r);
            v.MSC = Float(data, "MSC", donor.MSC, 0f, 10f, r);
            v.LSC = Float(data, "LSC", donor.LSC, 0f, 10f, r);

            // ---- minimap
            float rot;
            if (TryFloat(data, "Minimap Rotation", out rot))
            {
                v.MinimapRotation = (int)Math.Round(rot);
                r.FromPack.Add("Minimap Rotation = " + v.MinimapRotation);
            }
            else r.Fallbacks.Add("Minimap Rotation: missing, donor value " + donor.MinimapRotation);

            string axis;
            if (TryString(data, "Minimap Axis", out axis) && (axis == "x" || axis == "z"))
            {
                v.MinimapAxisX = axis == "x";
                r.FromPack.Add("Minimap Axis = " + axis);
            }
            else r.Fallbacks.Add("Minimap Axis: " + (axis == null ? "missing" : "'" + axis + "' is not x or z") + ", donor value " + (donor.MinimapAxisX ? "x" : "z"));

            // ---- tyres and rates
            v.Tyre1 = Name(data, "First Tyre Option", donor.Tyre1, compoundNames, r);
            v.Tyre2 = Name(data, "Second Tyre Option", donor.Tyre2, compoundNames, r);
            v.Tyre3 = Name(data, "Third Tyre Option", donor.Tyre3, compoundNames, r);
            v.TyreWear = Name(data, "Tyre Wear Rate", donor.TyreWear, RateNames, r);
            v.FuelBurn = Name(data, "Fuel Burn Rate", donor.FuelBurn, RateNames, r);

            // ---- climate (optional column; otherwise the donor's climate object)
            string climate;
            if (TryString(data, "Climate", out climate))
            {
                v.ClimateIndex = ClimateIndex(climate);
                r.FromPack.Add("Climate = " + climate + " (index " + v.ClimateIndex + ")");
            }
            return r;
        }

        private static float Float(IDictionary<string, object> data, string col, float donor, float min, float max, RowResult r)
        {
            float x;
            if (!TryFloat(data, col, out x))
            {
                r.Fallbacks.Add(col + ": missing or not a number, donor value " + F(donor));
                return donor;
            }
            if (x < min || x > max)
            {
                r.Fallbacks.Add(col + ": " + F(x) + " is outside " + F(min) + ".." + F(max) + ", donor value " + F(donor));
                return donor;
            }
            r.FromPack.Add(col + " = " + F(x));
            return x;
        }

        private static string Name(IDictionary<string, object> data, string col, string donor, string[] allowed, RowResult r)
        {
            string s;
            if (!TryString(data, col, out s))
            {
                r.Fallbacks.Add(col + ": missing, donor value " + donor);
                return donor;
            }
            string canon = Canonical(s, allowed);
            if (canon == null)
            {
                r.Fallbacks.Add(col + ": '" + s + "' is not one of " + string.Join("|", allowed) + ", donor value " + donor);
                return donor;
            }
            r.FromPack.Add(col + " = " + canon);
            return canon;
        }
    }

    public static class RegPlan
    {
        /// <summary>
        /// Quick Race selects layouts by index (QuickRaceSetupScreen.SetSelectedCircuit starts at TrackA, Next/Previous step
        /// 0..layoutCount-1: QuickRaceSetupScreen.cs:164-196), so only the built layouts that run unbroken from "A" can be offered.
        /// </summary>
        public static List<string> ContiguousLetters(ICollection<string> builtLetters)
        {
            List<string> ok = new List<string>();
            for (char c = 'A'; c <= 'F'; c++)
            {
                string l = c.ToString();
                bool has = false;
                foreach (string b in builtLetters) if (string.Equals(b, l, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                if (!has) break;
                ok.Add(l);
            }
            return ok;
        }

        /// <summary>
        /// Unique localisation id for a pack's location name: "PSG_" + 8 digits = 12 characters, as
        /// Localisation.LocaliseID(string) requires (Localisation.cs:113). The stock ids are PSG_1xxxxxxx; 9000xxxx never collides.
        /// </summary>
        public static string LocationNameId(int packIndex)
        {
            return "PSG_9000" + packIndex.ToString("D4", CultureInfo.InvariantCulture);
        }

        /// <summary>Sprite name used as the key for every spriteName consumer (e.g. "RT_austin").</summary>
        public static string SpriteName(string packId)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("RT_");
            foreach (char c in packId) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
