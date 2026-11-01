// Unity-free: the pack's optional harbour.json (SPEC-pack-format.md, "Harbour"): sea polygons at one water level and moored yachts.
// Compiled by the mod and by tools/autotest-unit-tests.
using System;
using System.Collections.Generic;
using System.IO;
using RealTracks.ArtGeo;

namespace RealTracks
{
    public class HarbourWater
    {
        public List<V2> Outer = new List<V2>();
        public List<IList<V2>> Holes = new List<IList<V2>>();
        public double AreaM2;
    }

    public class HarbourYacht
    {
        public float X, Z, YawDeg, Length;
        public float Beam;  // berth beam in metres (optional 6th field, added in M2 w8); 0 = not in the pack, see YachtPick.BeamOf
        public char Cls;    // 'b' big, 'm' medium, 's' small
    }

    public class PackHarbour
    {
        public float WaterLevel;
        public readonly List<HarbourWater> Water = new List<HarbourWater>();
        /// <summary>Pontoon slabs over the water (optional, M2 w8), flat at WaterLevel + PontoonTop.</summary>
        public readonly List<HarbourWater> Pontoons = new List<HarbourWater>();
        public float PontoonTop = 0.5f;
        /// <summary>How far below the water level the builder lowered the terrain inside the sea (optional "seabedDropM", default 8).</summary>
        public float SeabedDrop = 8f;
        public readonly List<HarbourYacht> Yachts = new List<HarbourYacht>();
        public readonly List<string> Problems = new List<string>();

        public static PackHarbour Load(string file) { return Parse(File.ReadAllText(file)); }

        public static PackHarbour Parse(string json)
        {
            Dictionary<string, object> m = MiniJson.ParseObject(json);
            PackHarbour h = new PackHarbour();
            if (MiniJson.Int(m, "format", 0) != 1) throw new Exception("harbour.json: unsupported format");
            if (!m.ContainsKey("waterLevel")) throw new Exception("harbour.json: no waterLevel");
            h.WaterLevel = (float)MiniJson.Num(m, "waterLevel", 0);
            Polys(MiniJson.Arr(m, "water"), "water", h.Water, h.Problems);
            Polys(MiniJson.Arr(m, "pontoons"), "pontoons", h.Pontoons, h.Problems);
            h.PontoonTop = (float)MiniJson.Num(m, "pontoonTopM", 0.5);
            h.SeabedDrop = (float)MiniJson.Num(m, "seabedDropM", 8.0);
            List<object> ys = MiniJson.Arr(m, "yachts");
            if (ys != null)
                for (int i = 0; i < ys.Count; i++)
                {
                    List<object> a = ys[i] as List<object>;
                    if (a == null || a.Count < 5 || !(a[0] is double) || !(a[1] is double) || !(a[2] is double) || !(a[3] is double) || !(a[4] is string) || ((string)a[4]).Length == 0)
                    { h.Problems.Add("yachts[" + i + "] is not [x, z, yawDeg, lengthM, class]"); continue; }
                    HarbourYacht y = new HarbourYacht();
                    y.X = (float)(double)a[0]; y.Z = (float)(double)a[1]; y.YawDeg = (float)(double)a[2]; y.Length = (float)(double)a[3]; y.Cls = ((string)a[4])[0];
                    if (a.Count >= 6 && a[5] is double) y.Beam = (float)(double)a[5];
                    if (y.Length <= 0 || y.Length > 200) { h.Problems.Add("yachts[" + i + "] length " + y.Length + " out of range"); continue; }
                    h.Yachts.Add(y);
                }
            return h;
        }

        private static void Polys(List<object> arr, string key, List<HarbourWater> into, List<string> problems)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> w = arr[i] as Dictionary<string, object>;
                if (w == null) { problems.Add(key + "[" + i + "] is not an object"); continue; }
                HarbourWater hw = new HarbourWater();
                hw.Outer = Ring(MiniJson.Arr(w, "outer"));
                if (hw.Outer.Count < 3) { problems.Add(key + "[" + i + "] has fewer than 3 points"); continue; }
                List<object> holes = MiniJson.Arr(w, "holes");
                if (holes != null) foreach (object o in holes) { List<V2> r = Ring(o as List<object>); if (r.Count >= 3) hw.Holes.Add(r); }
                hw.AreaM2 = MiniJson.Num(w, "areaM2", 0);
                into.Add(hw);
            }
        }

        private static List<V2> Ring(List<object> pts)
        {
            List<V2> r = new List<V2>();
            if (pts == null) return r;
            foreach (object o in pts)
            {
                List<object> p = o as List<object>;
                if (p != null && p.Count >= 2 && p[0] is double && p[1] is double) r.Add(new V2((float)(double)p[0], (float)(double)p[1]));
            }
            return r;
        }

        /// <summary>True when (x, z) lies on one of the pack's sea polygons (inside an outer ring, outside its holes).</summary>
        public bool IsSea(float x, float z)
        {
            V2 p = new V2(x, z);
            foreach (HarbourWater w in Water) if (Poly.Contains(w.Outer, w.Holes, p)) return true;
            return false;
        }

        /// <summary>Deterministic keep/drop of yacht i for the density slider (same yachts kept on every load).</summary>
        public static bool Keep(int i, float density)
        {
            if (density >= 0.999f) return true;
            if (density <= 0.001f) return false;
            uint h = (uint)i * 2654435761u;
            h ^= h >> 15;
            return (h % 1000u) / 1000f < density;
        }
    }

    /// <summary>
    /// Unity-free choice of the donor yacht model per berth (M2 w8). Run 18 moored the donor's sailboats in whole rows (small and half the
    /// medium berths); Port Hercule is white motor yachts. So: motor yachts on every berth; a sailboat only on a small berth whose hash falls
    /// under the "Sailboat share" setting; among the motor models, any whose scale to the berth stays in [MinScale, MaxScale], picked by a
    /// per-berth hash (variety); the scale fits both the berth length and beam (never wider than the berth), minus a per-berth jitter.
    /// </summary>
    public static class YachtPick
    {
        public const float SailHeightPerLength = 0.6f, SailHeightPerBeam = 2.4f;
        public const float MinScale = 0.25f, MaxScale = 1.8f;

        /// <summary>A sailboat is tall for its size (mast): height over 0.6 x length or over 2.4 x beam. Motor yachts: 0.3-0.4 x length.</summary>
        public static bool IsSail(float length, float beam, float height)
        {
            if (length <= 0f) return false;
            return height > SailHeightPerLength * length || (beam > 0f && height > SailHeightPerBeam * beam);
        }

        /// <summary>Deterministic 0..1 per berth index and salt (tools/trackbuild/harbour.py _h01 is the same hash).</summary>
        public static float Hash01(int i, uint salt)
        {
            uint h = unchecked((uint)i * 2654435761u + salt * 0x9E3779B9u);
            h ^= h >> 15;
            h = unchecked(h * 2246822519u);
            h ^= h >> 13;
            return (h % 10000u) / 10000f;
        }

        /// <summary>Berth beam: the pack's (6th yacht field) or the builder's rule max(3.5, 0.21 x length) for older packs.</summary>
        public static float BeamOf(float length, float beam) { return beam > 0f ? beam : Math.Max(3.5f, 0.21f * length); }

        /// <summary>True when berth i (class cls) gets a sailboat: small berths only, share sailShare, and only if the donor has one.</summary>
        public static bool WantsSail(char cls, int i, float sailShare, bool anySail, bool anyMotor)
        {
            if (!anySail) return false;
            if (!anyMotor) return true;
            return cls == 's' && Hash01(i, 1) < sailShare;
        }

        /// <summary>Index into the prototypes (lengths, sail flags) for berth i, or -1 when there are none.</summary>
        public static int Pick(char cls, int i, float berthLen, IList<float> lens, IList<bool> sail, float sailShare)
        {
            int n = lens.Count;
            if (n == 0) return -1;
            bool anySail = false, anyMotor = false;
            for (int k = 0; k < n; k++) { if (sail[k]) anySail = true; else anyMotor = true; }
            bool wantSail = WantsSail(cls, i, sailShare, anySail, anyMotor);
            List<int> c = new List<int>();
            int best = -1; double bestErr = double.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (sail[k] != wantSail || lens[k] <= 0f) continue;
                float s = berthLen / lens[k];
                if (s >= MinScale && s <= MaxScale) c.Add(k);
                double err = Math.Abs(Math.Log(s));
                if (err < bestErr) { bestErr = err; best = k; }
            }
            if (c.Count == 0) return best;
            return c[Math.Min(c.Count - 1, (int)(Hash01(i, 2) * c.Count))];
        }

        /// <summary>Scale of a prototype (length, beam) on a berth (length, beam): fits both, times 1 - jitter x hash (0..jitter smaller).</summary>
        public static float Scale(float berthLen, float berthBeam, float protoLen, float protoBeam, int i, float jitter)
        {
            if (protoLen <= 0f) return 1f;
            float s = berthLen / protoLen;
            if (berthBeam > 0f && protoBeam > 0f) s = Math.Min(s, berthBeam / protoBeam);
            float j = jitter < 0f ? 0f : (jitter > 0.5f ? 0.5f : jitter);
            s *= 1f - j * Hash01(i, 3);
            return s < 0.05f ? 0.05f : (s > 3f ? 3f : s);
        }
    }
}
