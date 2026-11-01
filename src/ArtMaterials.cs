using System;
using System.Collections.Generic;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    /// <summary>
    /// Materials and textures for the generated venue art. Everything here is created at runtime (no asset files): Standard
    /// shader with generated textures, so there is never a pink "missing shader" material. Donor materials are only used when
    /// their shader is supported; otherwise the generated fallback is used.
    /// </summary>
    public static class ArtMaterials
    {
        private static readonly Dictionary<string, Material> sMats = new Dictionary<string, Material>();
        private static readonly List<UnityEngine.Object> sCreated = new List<UnityEngine.Object>();
        private static Shader sShader;
        private static Material sShaderDonor;
        public static string ShaderName = "none";

        /// <summary>Colour of the ground outside the orthophoto and of the horizon skirt (set from the photo's edge colour).</summary>
        public static Color GroundColour = new Color(0.40f, 0.38f, 0.24f);

        /// <summary>Colour of the orthophoto edge per skirt row (SkirtGeo.Rows entries), filled while the orthophoto is decoded; null when unavailable.</summary>
        public static Color[] SkirtEdge;
        /// <summary>Raw / smoothed per-row edge colours (rows*3) and the mean (3) of the last skirt material, for logging.</summary>
        public static float[] LastRaw, LastSmooth, LastMean;

        /// <summary>
        /// The M1.1 skirt material: a (rings x rows) gradient texture. Row r column k = lerp(edge colour of row r, `far`, smoothstep(ring distance / fadeM)).
        /// Bilinear, no mip maps and clamped, so a vertex whose uv is a texel centre gets exactly that texel.
        /// </summary>
        public static Material MakeSkirtFade(float[] rings, RealTracks.ArtGeo.SkirtGeo.ColourSpec spec, float xSize, float zSize)
        {
            return MakeSkirtFade(rings, spec, xSize, zSize, null);
        }

        /// <summary>As above; rows flagged in seaRows (M2 w11, SeaGeo.SeaRows) keep the photo's sea-bed colour out to the rim (SkirtGeo.ColourSea).</summary>
        public static Material MakeSkirtFade(float[] rings, RealTracks.ArtGeo.SkirtGeo.ColourSpec spec, float xSize, float zSize, bool[] seaRows)
        {
            int K = rings.Length, rows = SkirtGeo.Rows;
            Texture2D t = new Texture2D(K, rows, TextureFormat.RGBA32, false);
            Color[] px = new Color[K * rows];
            float[] raw = new float[rows * 3];
            for (int r = 0; r < rows; r++)
            {
                Color e = (SkirtEdge != null && r < SkirtEdge.Length) ? SkirtEdge[r] : GroundColour;
                raw[r * 3] = e.r; raw[r * 3 + 1] = e.g; raw[r * 3 + 2] = e.b;
            }
            float[] smooth = SkirtGeo.SmoothEdges(raw, xSize, zSize, spec.SmoothM), mean = SkirtGeo.MeanEdge(raw, xSize, zSize), c3 = new float[3];
            LastRaw = raw; LastSmooth = smooth; LastMean = mean;
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < K; k++)
                {
                    if (seaRows != null && r < seaRows.Length && seaRows[r]) SkirtGeo.ColourSea(raw, r, rings[k], spec, c3);
                    else SkirtGeo.Colour(raw, smooth, mean, r, rings[k], spec, c3);
                    px[r * K + k] = new Color(c3[0], c3[1], c3[2], 1f);
                }
            t.SetPixels(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false);
            Track(t);
            Material m = Make("Skirt fade", t, Color.white, 0f);
            Set("skirt_fade", m);
            return m;
        }

        /// <summary>Forget every material and destroy what we created (called when a new venue is built).</summary>
        public static void Reset()
        {
            foreach (UnityEngine.Object o in sCreated) if (o != null) UnityEngine.Object.Destroy(o);
            sCreated.Clear();
            sMats.Clear();
            sShader = null;
            GroundColour = new Color(0.40f, 0.38f, 0.24f);
            SkirtEdge = null;
        }

        public static void Track(UnityEngine.Object o) { if (o != null) sCreated.Add(o); }

        /// <summary>A donor material whose shader may be borrowed when no built-in shader can be found.</summary>
        public static void SetShaderDonor(Material m) { sShaderDonor = m; }

        public static bool IsUsable(Material m)
        {
            if (m == null || m.shader == null) return false;
            string n = m.shader.name ?? "";
            if (n.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return m.shader.isSupported;
        }

        private static Shader PickShader()
        {
            if (sShader != null) return sShader;
            string[] names = { "Standard", "Legacy Shaders/Diffuse", "Diffuse", "Mobile/Diffuse" };
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null && s.isSupported) { sShader = s; ShaderName = n; return s; }
            }
            if (sShaderDonor != null && IsUsable(sShaderDonor)) { sShader = sShaderDonor.shader; ShaderName = "donor:" + sShader.name; }
            return sShader;
        }

        public static Texture2D Tex(int w, int h, Func<int, int, Rng, Color> f, bool mips, TextureWrapMode wrap)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            Rng rnd = new Rng(1234 + w * 7 + h);
            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y, rnd);
            t.SetPixels(px);
            t.wrapMode = wrap;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.Apply(mips);
            Track(t);
            return t;
        }

        private static float Noise(Rng r, float amp) { return (r.Next() - 0.5f) * 2f * amp; }

        private static Color Shade(Color c, float d) { return new Color(Mathf.Clamp01(c.r + d), Mathf.Clamp01(c.g + d), Mathf.Clamp01(c.b + d), c.a); }

        public static Material Make(string name, Texture tex, Color colour, float gloss)
        {
            Shader sh = PickShader();
            if (sh == null) throw new Exception("no usable shader found for generated art");
            Material m = new Material(sh);
            m.name = "RT " + name;
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_Color")) m.color = colour;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            Track(m);
            return m;
        }

        /// <summary>Standard shader "cutout" rendering mode (alpha test).</summary>
        private static void MakeCutout(Material m, float cutoff)
        {
            if (!m.HasProperty("_Mode")) return;
            m.SetFloat("_Mode", 1f);
            m.SetInt("_SrcBlend", 1); m.SetInt("_DstBlend", 0); m.SetInt("_ZWrite", 1);
            m.EnableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 2450;
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", cutoff);
        }

        /// <summary>Standard shader "fade" rendering mode (alpha blended).</summary>
        private static void MakeFade(Material m)
        {
            if (!m.HasProperty("_Mode")) return;
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", 5); m.SetInt("_DstBlend", 10); m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }

        public static bool Has(string key) { return sMats.ContainsKey(key); }
        public static void Set(string key, Material m) { sMats[key] = m; }

        /// <summary>OSM colour value → paint colour (named COTA paints, or #RRGGBB). Unknown → light tarmac.</summary>
        public static Color RunoffColour(string v)
        {
            switch ((v ?? "").Trim().ToLowerInvariant())
            {
                case "red": return new Color(0.72f, 0.07f, 0.09f);
                case "white": return new Color(0.90f, 0.90f, 0.88f);
                case "blue": return new Color(0.05f, 0.18f, 0.62f);
                case "gravel": return new Color(0.62f, 0.56f, 0.46f);
            }
            string h = v.Trim().TrimStart('#');
            if (h.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(h.Substring(0, 2), 16), g = Convert.ToInt32(h.Substring(2, 2), 16), b = Convert.ToInt32(h.Substring(4, 2), 16);
                    return new Color(r / 255f, g / 255f, b / 255f);
                }
                catch (Exception) { }
            }
            return new Color(0.42f, 0.42f, 0.43f);
        }

        /// <summary>Material by key; generated on first use. Keys: see the switch.</summary>
        public static Material Get(string key)
        {
            Material m;
            if (sMats.TryGetValue(key, out m) && m != null) return m;
            m = Create(key);
            sMats[key] = m;
            return m;
        }

        private static Material Create(string key)
        {
            switch (key)
            {
                case "ground": // ground outside the photo, tiled in the ortho UV domain by the caller (mainTextureScale)
                {
                    Color g = GroundColour;
                    Texture2D t = Tex(128, 128, (x, y, r) => Shade(g, Noise(r, 0.05f)), true, TextureWrapMode.Repeat);
                    return Make("Ground", t, Color.white, 0f);
                }
                case "skirt":
                {
                    Color g = GroundColour;
                    Texture2D t = Tex(128, 128, (x, y, r) => Shade(g, Noise(r, 0.04f)), true, TextureWrapMode.Repeat);
                    return Make("Skirt", t, Color.white, 0f);
                }
                case "kerb":
                {
                    // one tile = 2 m along the road: 1 m red, 1 m white (u across, v along)
                    Texture2D t = Tex(32, 64, (x, y, r) =>
                    {
                        Color c = y < 32 ? new Color(0.78f, 0.07f, 0.06f) : new Color(0.93f, 0.93f, 0.92f);
                        return Shade(c, Noise(r, 0.02f));
                    }, true, TextureWrapMode.Repeat);
                    return Make("Kerb", t, Color.white, 0.25f);
                }
                case "paint": // red / white / blue diagonal stripes, tile = 6 m x 6 m
                {
                    Color[] pal = { new Color(0.72f, 0.07f, 0.09f), new Color(0.90f, 0.90f, 0.88f), new Color(0.05f, 0.18f, 0.62f) };
                    Texture2D t = Tex(128, 128, (x, y, r) =>
                    {
                        int k = Stripes.Index((x + 0.5f) / 128f, (y + 0.5f) / 128f);
                        return Shade(pal[k], Noise(r, 0.025f));
                    }, true, TextureWrapMode.Repeat);
                    return Make("Painted run-off", t, Color.white, 0.2f);
                }
                case "monaco_runoff": // Monaco uses compact dark tarmac/concrete aprons, bounded by the normal red-white kerb rather than COTA-style tricolour paint.
                {
                    Texture2D t = Tex(128, 128, (x, y, r) =>
                    {
                        float seam = ((x + y / 3) % 40 < 2) ? -0.045f : 0f;
                        float g = 0.34f + seam + Noise(r, 0.022f);
                        // sparse pale aggregate gives the high-camera apron a believable aged street-surface variation.
                        if ((x * 17 + y * 11) % 97 < 3) g += 0.05f;
                        return new Color(g * 0.98f, g, g * 1.02f);
                    }, true, TextureWrapMode.Repeat);
                    return Make("Monaco tarmac apron", t, Color.white, 0.10f);
                }
                case "runoff": // lighter painted tarmac
                {
                    Texture2D t = Tex(128, 128, (x, y, r) => { float g = 0.40f + Noise(r, 0.025f); return new Color(g, g, g * 1.02f); }, true, TextureWrapMode.Repeat);
                    return Make("Run-off tarmac", t, Color.white, 0.12f);
                }
                case "parking":
                {
                    Texture2D t = Tex(128, 128, (x, y, r) => { float g = 0.27f + Noise(r, 0.03f); return new Color(g, g, g * 1.03f); }, true, TextureWrapMode.Repeat);
                    return Make("Car park", t, Color.white, 0.05f);
                }
                case "bld_wall":
                case "pit_wall": // tile 12 m x 7 m: two floors, window bands
                case "bld_wall_render":
                case "bld_wall_stone":
                case "bld_wall_glass":
                case "bld_monaco_cream":
                case "bld_monaco_ochre":
                case "bld_monaco_pale":
                {
                    bool pit = key == "pit_wall";
                    bool stone = key == "bld_wall_stone", render = key == "bld_wall_render", curtain = key == "bld_wall_glass";
                    bool monacoCream = key == "bld_monaco_cream", monacoOchre = key == "bld_monaco_ochre", monacoPale = key == "bld_monaco_pale";
                    bool monaco = monacoCream || monacoOchre || monacoPale;
                    Color wall = pit ? new Color(0.78f, 0.78f, 0.80f) : (monacoCream ? new Color(0.78f, 0.70f, 0.58f) : (monacoOchre ? new Color(0.66f, 0.48f, 0.35f) : (monacoPale ? new Color(0.69f, 0.68f, 0.61f) : (stone ? new Color(0.61f, 0.56f, 0.49f) : (render ? new Color(0.78f, 0.72f, 0.66f) : new Color(0.80f, 0.77f, 0.70f))))));
                    Color glass = pit ? new Color(0.14f, 0.20f, 0.28f) : (curtain ? new Color(0.12f, 0.24f, 0.32f) : new Color(0.22f, 0.27f, 0.33f));
                    Texture2D t = Tex(128, 64, (x, y, r) =>
                    {
                        int fy = y % 32;
                        bool band = fy >= 9 && fy <= 22 && (x % 16) >= 3 && (x % 16) <= 13;
                        if (pit) band = fy >= 8 && fy <= 24;
                        if (curtain) band = (x % 20) >= 2 && (x % 20) <= 17 && (y % 32) >= 3 && (y % 32) <= 28;
                        // Horizontal balcony/slab shadows give the compact residential blocks a Monaco-like rhythm from overhead.
                        if (monaco && !band && (fy == 6 || fy == 7 || fy == 25)) return Shade(new Color(wall.r * 0.70f, wall.g * 0.70f, wall.b * 0.70f), Noise(r, 0.012f));
                        if (stone && !band && ((x / 16 + y / 9) % 2 == 0)) return Shade(new Color(wall.r * 0.88f, wall.g * 0.88f, wall.b * 0.88f), Noise(r, 0.018f));
                        return band ? Shade(glass, Noise(r, 0.015f)) : Shade(wall, Noise(r, 0.02f));
                    }, true, TextureWrapMode.Repeat);
                    return Make(pit ? "Pit building" : (curtain ? "Building curtain wall" : (monacoCream ? "Monaco cream facade" : (monacoOchre ? "Monaco muted ochre facade" : (monacoPale ? "Monaco pale stone facade" : (stone ? "Building stone" : (render ? "Building render" : "Building")))))), t, Color.white, curtain ? 0.28f : 0.1f);
                }
                case "bld_roof":
                case "bld_roof_terracotta":
                case "bld_roof_green":
                case "bld_roof_units":
                case "bld_roof_monaco_tile":
                case "bld_roof_monaco_pale":
                {
                    bool terra = key == "bld_roof_terracotta", green = key == "bld_roof_green", units = key == "bld_roof_units";
                    bool monacoTile = key == "bld_roof_monaco_tile", monacoPale = key == "bld_roof_monaco_pale";
                    Texture2D t = Tex(32, 32, (x, y, r) =>
                    {
                        // Flat extrusions read as enormous roofs from the hub camera.  Use subtle mineral finishes;
                        // saturated orange tiles and flat lawn-green were visually implausible at this scale.
                        if (terra) { float n = Noise(r, 0.028f); bool seam = (x % 8) < 1 || (y % 6) < 1; return Shade(seam ? new Color(0.34f, 0.31f, 0.28f) : new Color(0.54f, 0.49f, 0.42f), n); }
                        if (green) { float n = Noise(r, 0.035f); return Shade(new Color(0.46f, 0.48f, 0.43f), n); }
                        if (monacoTile) { float n = Noise(r, 0.026f); bool seam = (x % 8) < 1 || (y % 6) < 1; return Shade(seam ? new Color(0.33f, 0.20f, 0.15f) : new Color(0.54f, 0.32f, 0.23f), n); }
                        if (monacoPale) { float n = Noise(r, 0.028f); bool seam = (x % 12) < 1 || (y % 12) < 1; return Shade(seam ? new Color(0.43f, 0.40f, 0.34f) : new Color(0.62f, 0.58f, 0.49f), n); }
                        if (units) { float g = 0.34f + Noise(r, 0.025f); bool unit = (x > 6 && x < 18 && y > 8 && y < 20); return unit ? new Color(0.17f, 0.20f, 0.22f) : new Color(g, g, g * 1.04f); }
                        float flat = 0.36f + Noise(r, 0.03f); return new Color(flat, flat, flat * 1.04f);
                    }, true, TextureWrapMode.Repeat);
                    return Make(monacoTile ? "Monaco muted tile roof" : (monacoPale ? "Monaco pale terrace roof" : (terra ? "Warm limestone roof" : (green ? "Pale gravel roof" : (units ? "Service roof" : "Slate roof")))), t, Color.white, 0.05f);
                }
                case "stand_seat":
                {
                    Texture2D t = Tex(32, 32, (x, y, r) => Shade(new Color(0.20f, 0.24f, 0.33f), Noise(r, 0.02f)), true, TextureWrapMode.Repeat);
                    return Make("Stand seats", t, Color.white, 0.05f);
                }
                case "stand_crowd": // one tile = 2.4 m along the stand, one row of spectators
                {
                    Color[] shirts = {
                        new Color(0.80f, 0.12f, 0.10f), new Color(0.15f, 0.30f, 0.70f), new Color(0.92f, 0.92f, 0.90f), new Color(0.95f, 0.78f, 0.15f),
                        new Color(0.12f, 0.12f, 0.14f), new Color(0.20f, 0.55f, 0.30f), new Color(0.55f, 0.20f, 0.55f), new Color(0.90f, 0.45f, 0.10f) };
                    Color[] skin = { new Color(0.87f, 0.68f, 0.55f), new Color(0.70f, 0.50f, 0.36f), new Color(0.45f, 0.30f, 0.22f), new Color(0.93f, 0.78f, 0.66f) };
                    int[] shirtOf = new int[8], skinOf = new int[8];
                    Rng pick = new Rng(77);
                    for (int i = 0; i < 8; i++) { shirtOf[i] = pick.Int(shirts.Length); skinOf[i] = pick.Int(skin.Length); }
                    Texture2D t = Tex(128, 32, (x, y, r) =>
                    {
                        int person = (x / 16) % 8;
                        float lx = (x % 16) - 7.5f;
                        float v = y / 31f;
                        if (v > 0.62f)
                        {
                            float dy = (v - 0.80f) * 32f, dxp = lx;
                            if (dxp * dxp + dy * dy < 30f) return Shade(skin[skinOf[person]], Noise(r, 0.03f));
                            return new Color(0.16f, 0.17f, 0.2f);
                        }
                        if (Math.Abs(lx) < 7f) return Shade(shirts[shirtOf[person]], Noise(r, 0.04f));
                        return new Color(0.16f, 0.17f, 0.2f);
                    }, true, TextureWrapMode.Repeat);
                    return Make("Stand crowd", t, Color.white, 0f);
                }
                case "concrete":
                {
                    Texture2D t = Tex(64, 64, (x, y, r) => { float g = 0.62f + Noise(r, 0.04f); return new Color(g, g, g * 0.98f); }, true, TextureWrapMode.Repeat);
                    return Make("Concrete", t, Color.white, 0.05f);
                }
                case "stand_roof":
                {
                    Texture2D t = Tex(32, 32, (x, y, r) => { float g = 0.78f + Noise(r, 0.03f); return new Color(g, g, g); }, true, TextureWrapMode.Repeat);
                    return Make("Stand roof", t, Color.white, 0.2f);
                }
                case "tunnel": // poured-concrete lining, one tile = 4 m x 4 m, a darker joint on two edges of every tile
                {
                    Texture2D t = Tex(64, 64, (x, y, r) => { float g = 0.50f + Noise(r, 0.03f); if (x % 64 < 2 || y % 64 < 2) g -= 0.09f; return new Color(g, g, g * 0.97f); }, true, TextureWrapMode.Repeat);
                    return Make("Tunnel concrete", t, Color.white, 0.08f);
                }
                case "tunnel_light": // emissive strips (Standard shader emission; a flat bright colour where the donor shader has none)
                {
                    Material lm = Make("Tunnel lamp", null, new Color(1f, 0.93f, 0.78f), 0f);
                    if (lm.HasProperty("_EmissionColor"))
                    {
                        lm.EnableKeyword("_EMISSION");
                        lm.SetColor("_EmissionColor", new Color(1.6f, 1.45f, 1.1f));
                    }
                    return lm;
                }
                case "barrier": return Make("Barrier", Get("concrete").mainTexture, new Color(0.92f, 0.92f, 0.92f), 0.05f);
                case "tower_concrete": // plain pale concrete, no window bands (M1.1: Kyle asked for a plain column)
                {
                    Texture2D t = Tex(32, 32, (x, y, r) => { float g = 0.86f + Noise(r, 0.015f); return new Color(g, g, g * 0.98f); }, true, TextureWrapMode.Repeat);
                    return Make("Tower", t, Color.white, 0.1f);
                }
                case "pit_facade": // one tile = 12 m wide and the whole building height: garage level, then two glass levels with white slabs
                {
                    Color white = new Color(0.88f, 0.88f, 0.87f), door = new Color(0.17f, 0.18f, 0.20f), glass1 = new Color(0.12f, 0.18f, 0.25f), glass2 = new Color(0.20f, 0.28f, 0.36f);
                    Texture2D t = Tex(128, 128, (x, y, r) =>
                    {
                        float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 128f;       // v = 0 at the ground
                        if (v < 0.30f)                                              // garage level: two 6 m bays with dark doors
                        {
                            float bu = (u * 2f) % 1f;
                            if (v > 0.03f && bu > 0.07f && bu < 0.93f) return Shade(door, Noise(r, 0.015f) + (v > 0.27f ? 0.07f : 0f));
                            return Shade(white, Noise(r, 0.02f));
                        }
                        if (v < 0.37f) return Shade(white, Noise(r, 0.02f));       // first-floor slab
                        if (v < 0.66f) return (u * 6f) % 1f < 0.05f ? Shade(white, -0.1f) : Shade(glass1, Noise(r, 0.015f));
                        if (v < 0.71f) return Shade(white, Noise(r, 0.02f));
                        if (v < 0.94f) return (u * 6f) % 1f < 0.05f ? Shade(white, -0.1f) : Shade(glass2, Noise(r, 0.015f));
                        return Shade(white, Noise(r, 0.02f));
                    }, true, TextureWrapMode.Repeat);
                    return Make("Pit facade", t, Color.white, 0.25f);
                }
                case "pit_side": // plain light-grey panels, tile 12 m x 7 m
                {
                    Texture2D t = Tex(64, 64, (x, y, r) => { float g = 0.74f + Noise(r, 0.02f); if (y % 16 == 0 || x % 32 == 0) g -= 0.07f; return new Color(g, g, g * 1.01f); }, true, TextureWrapMode.Repeat);
                    return Make("Pit side walls", t, Color.white, 0.1f);
                }
                case "pit_monaco_facade": // low cream quay garages; one tile = 12 m wide over the compact Monaco building height
                {
                    Color cream = new Color(0.82f, 0.73f, 0.60f), shade = new Color(0.62f, 0.53f, 0.43f);
                    Color door = new Color(0.19f, 0.25f, 0.28f), shutter = new Color(0.35f, 0.48f, 0.52f);
                    Texture2D t = Tex(128, 128, (x, y, r) =>
                    {
                        float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 128f;
                        float bay = (u * 2f) % 1f;
                        if (v < 0.52f)
                        {
                            if (v > 0.05f && bay > 0.07f && bay < 0.93f) return Shade(door, Noise(r, 0.014f) + (v > 0.46f ? 0.05f : 0f));
                            return Shade(cream, Noise(r, 0.018f));
                        }
                        if (v < 0.60f) return Shade(shade, Noise(r, 0.012f)); // shallow continuous balcony/slab
                        if (v < 0.91f)
                        {
                            float window = (u * 6f) % 1f;
                            return window < 0.10f ? Shade(cream, -0.12f) : Shade(shutter, Noise(r, 0.012f));
                        }
                        return Shade(cream, Noise(r, 0.018f));
                    }, true, TextureWrapMode.Repeat);
                    return Make("Monaco low pit facade", t, Color.white, 0.16f);
                }
                case "pit_monaco_side":
                {
                    Texture2D t = Tex(64, 64, (x, y, r) =>
                    {
                        Color c = new Color(0.75f, 0.68f, 0.57f);
                        if (y % 18 < 2 || x % 30 < 2) c = new Color(0.58f, 0.52f, 0.44f);
                        return Shade(c, Noise(r, 0.018f));
                    }, true, TextureWrapMode.Repeat);
                    return Make("Monaco pit side walls", t, Color.white, 0.08f);
                }
                case "pit_monaco_slab":
                {
                    Texture2D t = Tex(32, 32, (x, y, r) => Shade(new Color(0.83f, 0.80f, 0.72f), Noise(r, 0.018f)), true, TextureWrapMode.Repeat);
                    return Make("Monaco pit balconies", t, Color.white, 0.12f);
                }
                case "tower_glass": return Make("Tower glass", null, new Color(0.18f, 0.26f, 0.34f), 0.7f);
                case "fence": // chain-link cut-out: grid lines plus a post at the left edge of each 3 m panel
                {
                    Texture2D t = Tex(64, 64, (x, y, r) =>
                    {
                        bool post = x < 3;
                        bool line = (x % 8) == 0 || (y % 8) == 0;
                        bool top = y > 60;
                        return (post || line || top) ? new Color(0.45f, 0.47f, 0.50f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0f);
                    }, true, TextureWrapMode.Repeat);
                    Material m = Make("Fence", t, Color.white, 0.2f);
                    MakeCutout(m, 0.35f);
                    return m;
                }
                case "tree_canopy":
                {
                    Texture2D t = Tex(64, 64, (x, y, r) =>
                    {
                        float n = r.Next();
                        return Color.Lerp(new Color(0.16f, 0.27f, 0.12f), new Color(0.30f, 0.40f, 0.17f), n);
                    }, true, TextureWrapMode.Repeat);
                    return Make("Tree canopy", t, Color.white, 0f);
                }
                case "tree_trunk": return Make("Tree trunk", null, new Color(0.28f, 0.20f, 0.13f), 0f);
                case "water":
                {
                    Texture2D t = Tex(64, 64, (x, y, r) => Shade(new Color(0.16f, 0.30f, 0.34f, 0.85f), Noise(r, 0.02f)), true, TextureWrapMode.Repeat);
                    Material m = Make("Water", t, Color.white, 0.85f);
                    MakeFade(m);
                    return m;
                }
            }
            if (key.StartsWith("runoff:"))
            {
                // OSM-coloured run-off paint (orchestrator, M1 integration): "runoff:red", "runoff:#B7B4AD", "runoff:gravel"
                Color c = RunoffColour(key.Substring(7));
                float noise = key == "runoff:gravel" ? 0.06f : 0.02f;
                Texture2D t = Tex(64, 64, (x, y, r) => Shade(c, Noise(r, noise)), true, TextureWrapMode.Repeat);
                return Make("Run-off " + key.Substring(7), t, Color.white, key == "runoff:gravel" ? 0f : 0.15f);
            }
            throw new Exception("unknown art material '" + key + "'");
        }
    }
}
