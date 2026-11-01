using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RealTracks.ArtGeo;
using UnityEngine;

namespace RealTracks
{
    // =====================================================================================================
    // Track pack model and loader (pack format v1, see SPEC-pack-format.md). Owned by the orchestrator:
    // packages read from it; they do not change it.
    // =====================================================================================================

    public static class PackRegistry
    {
        public static readonly List<TrackPack> Packs = new List<TrackPack>();
        public static readonly List<string> Errors = new List<string>();

        public static void LoadAll(string dir)
        {
            Packs.Clear();
            Errors.Clear();
            if (!Directory.Exists(dir))
            {
                Main.Log("Real Tracks: no packs folder at " + dir);
                return;
            }
            string[] subs = Directory.GetDirectories(dir);
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);     // a fixed load order: when two packs clash, the same one is always the one rejected
            foreach (string sub in subs)
            {
                string manifest = Path.Combine(sub, "manifest.json");
                if (!File.Exists(manifest)) continue;
                try
                {
                    TrackPack p = TrackPack.Load(sub);
                    TrackPack clash = Packs.Find(q => q.PackIndex == p.PackIndex);
                    if (clash != null)
                        throw new Exception("packIndex " + p.PackIndex + " is already used by the pack '" + clash.Id + "' (" + Path.GetFileName(clash.Dir) + "); every pack needs its own packIndex 0..99, because circuit ids are 9000 + packIndex*10 + layout number");
                    clash = Packs.Find(q => string.Equals(q.Id, p.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(q.Display, p.Display, StringComparison.OrdinalIgnoreCase));
                    if (clash != null)
                        throw new Exception("the id '" + p.Id + "' / display name '" + p.Display + "' is already used by the pack '" + clash.Id + "' (" + clash.Display + "); venue names must be distinct because the game finds circuits by location name");
                    Packs.Add(p);
                    Main.Log("Real Tracks: pack '" + p.Id + "' (" + p.Display + "): " + p.BuiltLayoutSummary() + "; art-tour views: " + (p.Views == null ? "none in the manifest (generic set)" : p.Views.Count + " from the manifest") +
                             (p.ViewErrors.Count > 0 ? ", " + p.ViewErrors.Count + " invalid entr" + (p.ViewErrors.Count == 1 ? "y" : "ies") + " dropped (" + string.Join("; ", p.ViewErrors.ToArray()) + ")" : ""));
                }
                catch (Exception e)
                {
                    string msg = "Pack in " + Path.GetFileName(sub) + " failed to load: " + e.Message;
                    Errors.Add(msg);
                    if (Main.Logger != null) Main.Logger.Error(msg + "\n" + e);
                }
            }
        }

        public static PackLayout FindLayoutByCircuitId(int circuitId)
        {
            foreach (TrackPack p in Packs)
                foreach (PackLayout l in p.Layouts)
                    if (l.CircuitId == circuitId) return l;
            return null;
        }

        public static TrackPack FindPack(string idOrDisplay)
        {
            foreach (TrackPack p in Packs)
                if (string.Equals(p.Id, idOrDisplay, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Display, idOrDisplay, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }
    }

    public class TrackPack
    {
        public string Dir;
        public int Format;
        public string Id, Display, CountryKey, CountryNameID, Hemisphere;
        public int PackIndex;
        public string DonorScene, ClimateFrom, AmbienceFrom, DriverStatsFrom;
        public bool GaragesOnLeft = true;
        public string Crs;
        public double OriginE, OriginN, YBase;
        public string TerrainFile, TerrainHeader, OrthoFile, FeaturesFile, LoadingFile, CreditsFile;
        /// <summary>Venue WGS84 position (manifest "location", optional; M1.1): used by the race-intro overlay.</summary>
        public bool HasLocation;
        public double Lat, Lon;
        /// <summary>Optional art-tour views (manifest "views", ArtViews.cs): null when the manifest has none (the generic default set is used); only valid entries are kept.</summary>
        public List<ViewSpec> Views;
        /// <summary>Optional harbour.json (manifest "harbour"): sea polygons and moored yachts; null when the pack has none.</summary>
        public string HarbourFile;
        public readonly List<string> ViewErrors = new List<string>();
        public Rect OrthoBounds; // xMin = x0, yMin = z0, width/height in metres (pack-local x/z)
        public readonly List<PackLayout> Layouts = new List<PackLayout>();
        public readonly List<Dictionary<string, object>> Sources = new List<Dictionary<string, object>>();

        private PackTerrain mTerrain;
        private List<Feature> mFeatures;

        public string PathOf(string rel) { return string.IsNullOrEmpty(rel) ? null : Path.Combine(Dir, rel); }

        public string BuiltLayoutSummary()
        {
            StringBuilder sb = new StringBuilder();
            foreach (PackLayout l in Layouts)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(l.Key).Append(l.Built ? " (id " + l.CircuitId + ")" : " (not built)");
            }
            return sb.ToString();
        }

        /// <summary>Terrain heightfield, loaded on first use (a few MB).</summary>
        public PackTerrain Terrain
        {
            get
            {
                if (mTerrain == null && TerrainFile != null) mTerrain = PackTerrain.Load(PathOf(TerrainHeader), PathOf(TerrainFile));
                return mTerrain;
            }
        }

        public List<Feature> Features
        {
            get
            {
                if (mFeatures == null) mFeatures = Feature.LoadAll(PathOf(FeaturesFile));
                return mFeatures;
            }
        }

        public static TrackPack Load(string dir)
        {
            Dictionary<string, object> m = MiniJson.ParseObject(File.ReadAllText(Path.Combine(dir, "manifest.json")));
            TrackPack p = new TrackPack();
            p.Dir = dir;
            p.Format = MiniJson.Int(m, "format", 0);
            if (p.Format != 1) throw new Exception("unsupported pack format " + p.Format);
            p.Id = MiniJson.Str(m, "id", null);
            if (string.IsNullOrEmpty(p.Id)) throw new Exception("manifest has no id");
            p.PackIndex = MiniJson.Int(m, "packIndex", -1);
            if (p.PackIndex < 0 || p.PackIndex > 99) throw new Exception("packIndex must be 0..99");
            p.Display = MiniJson.Str(m, "display", p.Id);
            p.CountryKey = MiniJson.Str(m, "countryKey", "");
            p.CountryNameID = MiniJson.Str(m, "countryNameID", "");
            p.Hemisphere = MiniJson.Str(m, "hemisphere", "North");
            p.DonorScene = MiniJson.Str(m, "donorScene", "Phoenix");
            p.ClimateFrom = MiniJson.Str(m, "climateFrom", p.DonorScene);
            p.AmbienceFrom = MiniJson.Str(m, "ambienceFrom", p.DonorScene);
            p.DriverStatsFrom = MiniJson.Str(m, "driverStatsFrom", p.DonorScene);
            p.GaragesOnLeft = MiniJson.Str(m, "pitGarageSide", "left") != "right";

            Dictionary<string, object> frame = MiniJson.Obj(m, "frame");
            if (frame != null)
            {
                p.Crs = MiniJson.Str(frame, "crs", "");
                p.OriginE = MiniJson.Num(frame, "originE", 0);
                p.OriginN = MiniJson.Num(frame, "originN", 0);
                p.YBase = MiniJson.Num(frame, "yBase", 0);
            }
            Dictionary<string, object> terrain = MiniJson.Obj(m, "terrain");
            if (terrain != null)
            {
                p.TerrainFile = MiniJson.Str(terrain, "file", null);
                p.TerrainHeader = MiniJson.Str(terrain, "header", null);
            }
            Dictionary<string, object> ortho = MiniJson.Obj(m, "ortho");
            if (ortho != null)
            {
                p.OrthoFile = MiniJson.Str(ortho, "file", null);
                p.OrthoBounds = MiniJson.Bounds(ortho, "bounds");
            }
            Dictionary<string, object> loc = MiniJson.Obj(m, "location");
            if (loc != null && loc.ContainsKey("lat") && loc.ContainsKey("lon"))
            {
                p.Lat = MiniJson.Num(loc, "lat", 0);
                p.Lon = MiniJson.Num(loc, "lon", 0);
                p.HasLocation = p.Lat >= -90 && p.Lat <= 90 && p.Lon >= -180 && p.Lon <= 180;
            }
            p.FeaturesFile = MiniJson.Str(m, "features", null);
            p.LoadingFile = MiniJson.Str(m, "loading", null);
            p.CreditsFile = MiniJson.Str(m, "credits", null);
            p.HarbourFile = MiniJson.Str(m, "harbour", null);

            object rawViews;
            m.TryGetValue("views", out rawViews);
            p.Views = ViewSpec.ParseList(rawViews, p.ViewErrors);

            List<object> sources = MiniJson.Arr(m, "sources");
            if (sources != null)
                foreach (object o in sources)
                    if (o is Dictionary<string, object>) p.Sources.Add((Dictionary<string, object>)o);

            List<object> layouts = MiniJson.Arr(m, "layouts");
            if (layouts == null || layouts.Count == 0) throw new Exception("manifest has no layouts");
            if (layouts.Count > 6) throw new Exception("manifest has " + layouts.Count + " layouts; at most 6 (letters A-F, circuit ids 9000 + packIndex*10 + 0..5)");
            for (int i = 0; i < layouts.Count; i++)
            {
                Dictionary<string, object> lo = layouts[i] as Dictionary<string, object>;
                if (lo == null) throw new Exception("layout " + i + " is not an object");
                p.Layouts.Add(PackLayout.FromJson(p, i, lo));
            }
            return p;
        }
    }

    public class PackLayout
    {
        public TrackPack Pack;
        public int Index;
        public int CircuitId;
        public string Key, Letter, Name;
        public bool Built;
        public float LengthM;
        public string PathsFile, WidthFile, MinimapFile;
        /// <summary>Optional per-turn real corner speeds (corners_KEY.csv, SPEC-pack-format.md); null when the pack has none.</summary>
        public string CornersFile;
        public Rect MinimapBounds; // pack-local x/z
        /// <summary>Tunnels on this layout (layout key "tunnels": name, s0, s1 along [track]; optional, ArtTunnel.cs). Empty when the pack has none.</summary>
        public List<RealTracks.ArtGeo.TunnelSpan> Tunnels = new List<RealTracks.ArtGeo.TunnelSpan>();
        public readonly List<string> TunnelErrors = new List<string>();
        /// <summary>Locations-column name → value (string, double or bool), with the reason alongside.</summary>
        public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public readonly Dictionary<string, string> DataReasons = new Dictionary<string, string>();

        private LayoutPaths mPaths;

        public Circuit.TrackLayout TrackLayout
        {
            get
            {
                switch (Letter)
                {
                    case "B": return Circuit.TrackLayout.TrackB;
                    case "C": return Circuit.TrackLayout.TrackC;
                    case "D": return Circuit.TrackLayout.TrackD;
                    case "E": return Circuit.TrackLayout.TrackE;
                    case "F": return Circuit.TrackLayout.TrackF;
                    default: return Circuit.TrackLayout.TrackA;
                }
            }
        }

        /// <summary>Paths, loaded on first use. Throws if the file is bad: call it before touching the scene.</summary>
        public LayoutPaths Paths
        {
            get
            {
                if (mPaths == null) mPaths = LayoutPaths.Load(Pack.PathOf(PathsFile), Pack.PathOf(WidthFile));
                return mPaths;
            }
        }

        public static PackLayout FromJson(TrackPack p, int index, Dictionary<string, object> o)
        {
            PackLayout l = new PackLayout();
            l.Pack = p;
            l.Index = index;
            l.CircuitId = 9000 + p.PackIndex * 10 + index;
            l.Key = MiniJson.Str(o, "key", "L" + index);
            l.Letter = MiniJson.Str(o, "letter", ((char)('A' + index)).ToString());
            l.Name = MiniJson.Str(o, "name", l.Key);
            l.Built = MiniJson.Bool(o, "built", false);
            l.LengthM = (float)MiniJson.Num(o, "lengthM", 0);
            l.PathsFile = MiniJson.Str(o, "paths", null);
            l.WidthFile = MiniJson.Str(o, "width", null);
            l.MinimapFile = MiniJson.Str(o, "minimap", null);
            l.CornersFile = MiniJson.Str(o, "corners", null);
            l.MinimapBounds = MiniJson.Bounds(o, "minimapBounds");
            object rawTunnels;
            if (o.TryGetValue("tunnels", out rawTunnels)) l.Tunnels = RealTracks.ArtGeo.TunnelGeo.ParseList(rawTunnels, l.TunnelErrors);
            Dictionary<string, object> data = MiniJson.Obj(o, "data");
            if (data != null)
            {
                foreach (KeyValuePair<string, object> kv in data)
                {
                    Dictionary<string, object> cell = kv.Value as Dictionary<string, object>;
                    if (cell != null)
                    {
                        object v;
                        if (cell.TryGetValue("value", out v)) l.Data[kv.Key] = v;
                        l.DataReasons[kv.Key] = MiniJson.Str(cell, "reason", "");
                    }
                    else
                    {
                        l.Data[kv.Key] = kv.Value;
                    }
                }
            }
            if (l.Built && (l.PathsFile == null || !File.Exists(p.PathOf(l.PathsFile))))
            {
                throw new Exception("layout " + l.Key + " is marked built but its paths file is missing");
            }
            return l;
        }

        public bool TryGetString(string column, out string value)
        {
            object v;
            value = null;
            if (!Data.TryGetValue(column, out v) || v == null) return false;
            value = v is double ? ((double)v).ToString(CultureInfo.InvariantCulture) : v.ToString();
            return true;
        }

        public bool TryGetFloat(string column, out float value)
        {
            object v;
            value = 0f;
            if (!Data.TryGetValue(column, out v) || v == null) return false;
            if (v is double) { value = (float)(double)v; return true; }
            double d;
            if (double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { value = (float)d; return true; }
            return false;
        }
    }

    public class GarageSlot
    {
        public int Number;              // 1..12 in pit-lane order
        public Vector3 Position;        // TeamPitGarage transform target
        public float YawDeg;            // Unity yaw: 0 = +z, 90 = +x
        public List<Vector3> PitboxEntry, PitboxExit;
        public List<Vector3>[] GarageEntry = new List<Vector3>[2];
        public List<Vector3>[] GarageExit = new List<Vector3>[2];
    }

    public class LayoutPaths
    {
        public List<Vector3> Track, RacingLine, PitEntry, PitLane, PitExit, Sectors;
        public readonly List<List<Vector3>> Crash = new List<List<Vector3>>();
        public readonly List<List<Vector3>> RunWide = new List<List<Vector3>>();
        public readonly List<List<Vector3>> Cut = new List<List<Vector3>>();
        public readonly SortedDictionary<int, GarageSlot> Garages = new SortedDictionary<int, GarageSlot>();
        /// <summary>Per [track] point: distance along the centreline, left and right half-widths (m).</summary>
        public float[] S, WidthLeft, WidthRight;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private GarageSlot Slot(int n)
        {
            GarageSlot g;
            if (!Garages.TryGetValue(n, out g)) { g = new GarageSlot(); g.Number = n; Garages[n] = g; }
            return g;
        }

        public static LayoutPaths Load(string pathsFile, string widthFile)
        {
            LayoutPaths f = new LayoutPaths();
            List<Vector3> cur = null;
            GarageSlot curGarage = null;
            int lineNo = 0;
            foreach (string raw in File.ReadAllLines(pathsFile))
            {
                lineNo++;
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                if (l[0] == '[')
                {
                    if (!l.EndsWith("]")) throw new Exception(Path.GetFileName(pathsFile) + ":" + lineNo + ": bad section " + l);
                    string sec = l.Substring(1, l.Length - 2).Trim();
                    string[] parts = sec.Split(' ');
                    string name = parts[0];
                    string arg = parts.Length > 1 ? parts[1] : null;
                    cur = new List<Vector3>();
                    curGarage = null;
                    switch (name)
                    {
                        case "track": f.Track = cur; break;
                        case "racingline": f.RacingLine = cur; break;
                        case "pitentry": f.PitEntry = cur; break;
                        case "pitlane": f.PitLane = cur; break;
                        case "pitexit": f.PitExit = cur; break;
                        case "sectors": f.Sectors = cur; break;
                        case "crash": f.Crash.Add(cur); break;
                        case "runwide": f.RunWide.Add(cur); break;
                        case "cut": f.Cut.Add(cur); break;
                        case "garage": curGarage = f.Slot(int.Parse(arg, Inv)); cur = null; break;
                        case "pitboxentry": f.Slot(int.Parse(arg, Inv)).PitboxEntry = cur; break;
                        case "pitboxexit": f.Slot(int.Parse(arg, Inv)).PitboxExit = cur; break;
                        case "garageentry":
                        case "garageexit":
                        {
                            string[] nc = arg.Split('.');
                            GarageSlot g = f.Slot(int.Parse(nc[0], Inv));
                            int car = int.Parse(nc[1], Inv) - 1;
                            if (car < 0 || car > 1) throw new Exception("garage car must be 1 or 2: " + l);
                            if (name == "garageentry") g.GarageEntry[car] = cur; else g.GarageExit[car] = cur;
                            break;
                        }
                        default: cur = null; break; // unknown sections are skipped (forward compatible)
                    }
                    continue;
                }
                string[] p = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (curGarage != null)
                {
                    curGarage.Position = new Vector3(F(p[0]), F(p[1]), F(p[2]));
                    curGarage.YawDeg = p.Length > 3 ? F(p[3]) : 0f;
                    continue;
                }
                if (cur == null) continue;
                cur.Add(new Vector3(F(p[0]), F(p[1]), F(p[2])));
            }

            string file = Path.GetFileName(pathsFile);
            if (f.Track == null || f.Track.Count < 10) throw new Exception(file + ": [track] missing or too short");
            if (f.RacingLine == null || f.RacingLine.Count != f.Track.Count) throw new Exception(file + ": [racingline] missing or not the same length as [track]");
            if (f.PitEntry == null || f.PitLane == null || f.PitExit == null) throw new Exception(file + ": a pit section is missing");
            if (f.Garages.Count == 0) throw new Exception(file + ": no [garage N] sections");
            foreach (GarageSlot g in f.Garages.Values)
            {
                if (g.PitboxEntry == null || g.PitboxExit == null || g.GarageEntry[0] == null || g.GarageExit[0] == null ||
                    g.GarageEntry[1] == null || g.GarageExit[1] == null)
                {
                    throw new Exception(file + ": garage " + g.Number + " is missing a pitbox or garage path");
                }
            }

            int n = f.Track.Count;
            f.S = new float[n];
            f.WidthLeft = new float[n];
            f.WidthRight = new float[n];
            for (int i = 1; i < n; i++) f.S[i] = f.S[i - 1] + Vector3.Distance(f.Track[i - 1], f.Track[i]);
            for (int i = 0; i < n; i++) { f.WidthLeft[i] = 6f; f.WidthRight[i] = 6f; }
            if (widthFile != null && File.Exists(widthFile))
            {
                string[] lines = File.ReadAllLines(widthFile);
                for (int k = 1; k < lines.Length; k++)
                {
                    string[] c = lines[k].Split(',');
                    if (c.Length < 4) continue;
                    int i = int.Parse(c[0].Trim(), Inv);
                    if (i < 0 || i >= n) continue;
                    f.WidthLeft[i] = F(c[2]);
                    f.WidthRight[i] = F(c[3]);
                }
            }
            return f;
        }

        private static float F(string s) { return float.Parse(s.Trim(), NumberStyles.Float, Inv); }
    }

    public class PackTerrain
    {
        public int Nx, Nz;
        public float X0, Z0, Dx, Dz;
        public float[] H;

        public float XMax { get { return X0 + (Nx - 1) * Dx; } }
        public float ZMax { get { return Z0 + (Nz - 1) * Dz; } }

        public static PackTerrain Load(string header, string bin)
        {
            Dictionary<string, object> h = MiniJson.ParseObject(File.ReadAllText(header));
            PackTerrain t = new PackTerrain();
            t.Nx = MiniJson.Int(h, "nx", 0);
            t.Nz = MiniJson.Int(h, "nz", 0);
            t.X0 = (float)MiniJson.Num(h, "x0", 0);
            t.Z0 = (float)MiniJson.Num(h, "z0", 0);
            t.Dx = (float)MiniJson.Num(h, "dx", 1);
            t.Dz = (float)MiniJson.Num(h, "dz", 1);
            byte[] b = File.ReadAllBytes(bin);
            if (t.Nx < 2 || t.Nz < 2 || b.Length != 4 * t.Nx * t.Nz)
            {
                throw new Exception("terrain size mismatch: header " + t.Nx + "x" + t.Nz + ", file " + b.Length + " bytes");
            }
            t.H = new float[t.Nx * t.Nz];
            bool swap = !BitConverter.IsLittleEndian;
            for (int i = 0; i < t.H.Length; i++)
            {
                if (swap) Array.Reverse(b, i * 4, 4);
                t.H[i] = BitConverter.ToSingle(b, i * 4);
            }
            return t;
        }

        public float At(int ix, int iz)
        {
            ix = Mathf.Clamp(ix, 0, Nx - 1);
            iz = Mathf.Clamp(iz, 0, Nz - 1);
            return H[iz * Nx + ix];
        }

        /// <summary>Bilinear height at pack-local x/z (clamped to the grid).</summary>
        public float Sample(float x, float z)
        {
            float fx = Mathf.Clamp((x - X0) / Dx, 0f, Nx - 1.001f);
            float fz = Mathf.Clamp((z - Z0) / Dz, 0f, Nz - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = At(ix, iz), b = At(ix + 1, iz), c = At(ix, iz + 1), d = At(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }
    }

    public class Feature
    {
        public string Kind, Geom, Name, Osm, Colour, Surface;
        public List<Vector2> Coords = new List<Vector2>(); // pack-local x, z
        public float Height = -1f;  // -1 = unknown
        public int Levels = -1;
        // M1.1 optional keys (SPEC-pack-format.md): stand facing decided by the builder, tower without windows
        public string Role;                 // grandstand: "seating" | "block" | "skip"; null = decide at run time
        public bool HasFront;
        public Vector2 Front;               // unit vector from the stand / pit building towards the track / pit lane
        public bool Windows = true;         // tower: false = plain shaft
        public string LaneSide;             // pit building: side of the pit lane the garages are on

        public static List<Feature> LoadAll(string file)
        {
            List<Feature> list = new List<Feature>();
            if (file == null || !File.Exists(file)) return list;
            Dictionary<string, object> root = MiniJson.ParseObject(File.ReadAllText(file));
            List<object> arr = MiniJson.Arr(root, "features");
            if (arr == null) return list;
            foreach (object o in arr)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                Feature f = new Feature();
                f.Kind = MiniJson.Str(d, "kind", "");
                f.Geom = MiniJson.Str(d, "geom", "");
                f.Name = MiniJson.Str(d, "name", null);
                f.Osm = MiniJson.Str(d, "osm", null);
                f.Colour = MiniJson.Str(d, "colour", null);
                f.Surface = MiniJson.Str(d, "surface", null);
                f.Height = (float)MiniJson.Num(d, "height", -1);
                f.Levels = MiniJson.Int(d, "levels", -1);
                f.Role = MiniJson.Str(d, "role", null);
                f.LaneSide = MiniJson.Str(d, "laneSide", null);
                f.Windows = MiniJson.Bool(d, "windows", true);
                List<object> fr = MiniJson.Arr(d, "front");
                if (fr != null && fr.Count >= 2) { f.Front = new Vector2((float)MiniJson.ToNum(fr[0]), (float)MiniJson.ToNum(fr[1])); f.HasFront = f.Front.sqrMagnitude > 1e-6f; }
                List<object> coords = MiniJson.Arr(d, "coords");
                if (coords != null)
                {
                    foreach (object c in coords)
                    {
                        List<object> xy = c as List<object>;
                        if (xy != null && xy.Count >= 2) f.Coords.Add(new Vector2((float)MiniJson.ToNum(xy[0]), (float)MiniJson.ToNum(xy[1])));
                    }
                }
                list.Add(f);
            }
            return list;
        }
    }

    /// <summary>Small JSON reader: objects → Dictionary&lt;string, object&gt;, arrays → List&lt;object&gt;, numbers → double.</summary>
    public static class MiniJson
    {
        public static Dictionary<string, object> ParseObject(string json)
        {
            int i = 0;
            object o = Value(json, ref i);
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) throw new Exception("JSON root is not an object");
            return d;
        }

        public static object Parse(string json)
        {
            int i = 0;
            return Value(json, ref i);
        }

        private static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new Exception("JSON ended early");
            char c = s[i];
            if (c == '{')
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new Exception("JSON: ':' expected at " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new Exception("JSON: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                List<object> a = new List<object>();
                i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new Exception("JSON: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new Exception("JSON: unexpected '" + c + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new Exception("JSON: string expected at " + i);
            i++;
            StringBuilder sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        public static double ToNum(object o)
        {
            if (o is double) return (double)o;
            if (o is bool) return (bool)o ? 1 : 0;
            double d;
            return o != null && double.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return v is double ? ((double)v).ToString(CultureInfo.InvariantCulture) : v.ToString();
        }

        public static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return ToNum(v);
        }

        public static int Int(Dictionary<string, object> d, string k, int def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return (int)Math.Round(ToNum(v));
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return v is bool ? (bool)v : ToNum(v) != 0;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v as List<object> : null;
        }

        /// <summary>[x0, z0, x1, z1] → Rect(x0, z0, x1-x0, z1-z0).</summary>
        public static Rect Bounds(Dictionary<string, object> d, string k)
        {
            List<object> a = Arr(d, k);
            if (a == null || a.Count < 4) return new Rect();
            float x0 = (float)ToNum(a[0]), z0 = (float)ToNum(a[1]), x1 = (float)ToNum(a[2]), z1 = (float)ToNum(a[3]);
            return new Rect(x0, z0, x1 - x0, z1 - z0);
        }

        /// <summary>Writes a JSON string literal (for log writers).</summary>
        public static string Quote(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
