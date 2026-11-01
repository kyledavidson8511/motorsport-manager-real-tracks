using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RealTracks
{
    /// <summary>
    /// M1.1 A1: the Quick Race "Custom tracks" column. The stock screen lists its 16 venues in a 2-column grid (QuickRaceSetupScreen
    /// .CreateEntries, :100-125); pack venues used to be appended to that grid and ended up below the bottom bar. With
    /// UiCustomTrackColumn on, they are left out of the grid (Registration.RegisterInto) and listed in a panel of their own to the
    /// LEFT of the stock panel, built from the stock panel itself: same top and height, a header cloned from the stock one, and
    /// entries made from the stock list's own item prefab, in the stock ToggleGroup so the selection is exclusive across both lists.
    /// Selecting an entry runs UIQuickRaceCircuitEntry's own handler, i.e. QuickRaceSetupScreen.SetSelectedCircuit.
    /// </summary>
    public static class UiQuickRace
    {
        public const string PanelName = "RT_CustomTracks";
        private static readonly List<string> sEntryNames = new List<string>();
        private static GameObject sPanel;
        private static int sHandledScreen;          // instance id of the screen the column (or the grid fallback) was built for
        private static string sMode = "";           // "column" | "grid" | "" (nothing built)
        private static int sBuiltW, sBuiltH;        // Screen size the column was laid out for
        private static RectTransform sLca;          // the content block that was scaled to make room ("scaled" mode), and what it was before
        private static Vector3 sLcaScale0;
        private static Vector2 sLcaAnchored0;
        private static readonly List<UIQuickRaceCircuitEntry> sEntries = new List<UIQuickRaceCircuitEntry>();
        /// <summary>What the last build / rebuild produced (the auto-test's resolution matrix reads it).</summary>
        public static UiColumnInfo Last = new UiColumnInfo();

        public static bool UseColumn { get { return Main.Settings != null && Main.Settings.UiCustomTrackColumn; } }
        public static bool RebuildOnResize { get { return Main.Settings != null && Main.Settings.UiColumnRebuildOnResize; } }

        /// <summary>True if the venue is offered in the custom column (for the auto-test's "in Quick Race list").</summary>
        public static bool HasEntry(string location) { return sEntryNames.Contains(location); }

        // ------------------------------------------------------------------------------------------------ entry point

        public static void AfterCreateEntries(QuickRaceSetupScreen screen)
        {
            if (!UseColumn || screen == null || screen.circuitList == null || screen.circuitList.itemCount == 0) return;
            List<string> names = new List<string>();
            foreach (RegisteredCircuit rc in Registration.Registered) if (!names.Contains(rc.Location)) names.Add(rc.Location);
            if (names.Count == 0) { sEntryNames.Clear(); return; }                 // no pack circuits: no panel
            if (sHandledScreen == screen.GetInstanceID() && (sPanel != null || sEntryNames.Count > 0))   // re-entry: already there
            {
                // the layout was measured at one screen size; the screen objects are reused, so after a resolution change in the preferences the column would be stale
                if (sMode == "column" && RebuildOnResize && (Screen.width != sBuiltW || Screen.height != sBuiltH))
                {
                    Main.Log("RT UI: screen size changed from " + sBuiltW + "x" + sBuiltH + " to " + Screen.width + "x" + Screen.height + " since the column was laid out: rebuilding it");
                    Rebuild(screen, "re-entry at a new screen size");
                }
                return;
            }
            sEntryNames.Clear();
            sEntries.Clear();
            sHandledScreen = screen.GetInstanceID();
            Last = new UiColumnInfo();
            try
            {
                if (!BuildColumn(screen, names))
                {
                    if (sPanel != null) UnityEngine.Object.Destroy(sPanel);
                    sPanel = null;
                    AppendToGrid(screen, names, "no room or no usable stock panel");
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks: custom tracks column failed, listing the venues in the stock grid instead: " + e);
                RtFallbacks.Note("quick race column", "exception while building the column: " + e.GetType().Name + " " + e.Message);
                if (sPanel != null) UnityEngine.Object.Destroy(sPanel);
                sPanel = null;
                try { AppendToGrid(screen, names, "exception"); } catch (Exception e2) { Main.Logger.Error("Real Tracks: grid fallback failed: " + e2); }
            }
        }

        /// <summary>The old behaviour (used when the column is not possible): the stock loop's body for each pack venue.</summary>
        private static void AppendToGrid(QuickRaceSetupScreen screen, List<string> names, string why)
        {
            ToggleGroup group = screen.circuitList.GetComponent<ToggleGroup>();
            foreach (string n in names)
            {
                Circuit c = Game.instance.circuitManager.GetCircuitByLocationName(n);
                if (c == null) continue;
                UIQuickRaceCircuitEntry e = screen.circuitList.CreateListItem<UIQuickRaceCircuitEntry>();
                e.toggle.group = group;
                e.Setup(c);
                sEntryNames.Add(n);
                sEntries.Add(e);
            }
            Main.Log("RT UI: custom tracks column: " + sEntryNames.Count + " entries at <stock grid> (column not used: " + why + ")");
            sMode = "grid";
            Last.Mode = "grid"; Last.Entries = sEntryNames.Count; Last.Note = why; Last.ScreenW = Screen.width; Last.ScreenH = Screen.height;
            RtFallbacks.Note("quick race column", "the custom tracks column was not used, " + sEntryNames.Count + " venue(s) listed in the stock grid instead (" + why + ")");
        }

        // ------------------------------------------------------------------------------------------------ measuring (auto-test resolution matrix)

        /// <summary>
        /// Where the venue entries are right now: the column panel (or, in grid mode, the appended stock list items) against the root canvas rect.
        /// Mode / scale / fits are those of the last build; InsideScreen, canvas size and the x range are measured now, so a column that was not
        /// laid out again after a resolution change shows up as outside.
        /// </summary>
        public static UiColumnInfo Measure(QuickRaceSetupScreen screen)
        {
            UiColumnInfo i = new UiColumnInfo();
            i.Mode = sMode == "column" ? Last.Mode : sMode; i.Scale = Last.Scale; i.Fits = Last.Fits; i.Entries = sEntryNames.Count; i.Note = Last.Note;
            i.ScreenW = Screen.width; i.ScreenH = Screen.height;
            try
            {
                Canvas c = screen != null && screen.circuitList != null ? screen.circuitList.GetComponentInParent<Canvas>() : null;
                if (c == null) { i.Note = (i.Note + " no canvas to measure against").Trim(); return i; }
                RectTransform canvasRT = (RectTransform)c.rootCanvas.transform;
                i.CanvasW = canvasRT.rect.width; i.CanvasH = canvasRT.rect.height;
                double x0 = double.MaxValue, x1 = double.MinValue; bool inside = true; int n = 0;
                if (sMode == "column" && sPanel != null) { Rect r = RectIn(canvasRT, (RectTransform)sPanel.transform); x0 = r.xMin; x1 = r.xMax; n = 1; inside = r.xMin >= canvasRT.rect.xMin - 0.5f && r.xMax <= canvasRT.rect.xMax + 0.5f && r.yMin >= canvasRT.rect.yMin - 0.5f && r.yMax <= canvasRT.rect.yMax + 0.5f; }
                else if (sMode == "grid")
                    foreach (UIQuickRaceCircuitEntry e in sEntries)
                    {
                        if (e == null) continue;
                        Rect r = RectIn(canvasRT, (RectTransform)e.transform); n++;
                        x0 = Math.Min(x0, r.xMin); x1 = Math.Max(x1, r.xMax);
                        if (!(r.xMin >= canvasRT.rect.xMin - 0.5f && r.xMax <= canvasRT.rect.xMax + 0.5f && r.yMin >= canvasRT.rect.yMin - 0.5f && r.yMax <= canvasRT.rect.yMax + 0.5f)) inside = false;
                    }
                i.InsideScreen = n > 0 && inside; i.Left = n > 0 ? x0 : 0; i.Right = n > 0 ? x1 : 0;
            }
            catch (Exception e) { i.Note = (i.Note + " measure failed: " + e.Message).Trim(); }
            return i;
        }

        // ------------------------------------------------------------------------------------------------ rebuild after a screen size change

        /// <summary>
        /// Lays the column out again for the current screen size: removes the panel, puts the scaled content block back as it was before the column
        /// changed it, and builds the column from the stock panel's new geometry. The canvas rescales by itself when the resolution changes (the stock
        /// panels are anchored), but our panel and the content scale were fixed numbers measured at the old size. A grid fallback is left as it is (its
        /// entries are stock list items). Returns the new result (also in Last).
        /// </summary>
        public static UiColumnInfo Rebuild(QuickRaceSetupScreen screen, string why)
        {
            try
            {
                if (screen == null) { Last.Note = "no screen"; return Last; }
                if (sMode != "column")
                {
                    Main.Log("RT UI: column rebuild (" + why + "): nothing to rebuild, mode '" + sMode + "'" + (sMode == "grid" ? " (the entries are stock grid items)" : ""));
                    Last.ScreenW = Screen.width; Last.ScreenH = Screen.height;
                    if (sMode == "") Last.Note = "no column was built for this screen";
                    return Last;
                }
                string selected = null;
                foreach (UIQuickRaceCircuitEntry e in sEntries) if (e != null && e.toggle != null && e.toggle.isOn) selected = e.gameObject.name;
                if (sPanel != null) { sPanel.SetActive(false); UnityEngine.Object.Destroy(sPanel); }     // inactive first: its toggles leave the stock ToggleGroup at once
                sPanel = null;
                if (sLca != null) { sLca.localScale = sLcaScale0; sLca.anchoredPosition = sLcaAnchored0; sLca = null; }
                sEntryNames.Clear(); sEntries.Clear(); sMode = "";
                sHandledScreen = 0;
                Canvas.ForceUpdateCanvases();                       // stock panels take their new anchored rects before the column measures them
                Main.Log("RT UI: column rebuild (" + why + ") at screen " + Screen.width + "x" + Screen.height);
                AfterCreateEntries(screen);
                if (selected != null)
                    foreach (UIQuickRaceCircuitEntry e in sEntries)
                        if (e != null && e.gameObject.name == selected) { try { e.toggle.isOn = true; } catch (Exception) { } }
                if (sMode == "") { Last.Note = "rebuild produced nothing"; RtFallbacks.Note("quick race column", "rebuild at " + Screen.width + "x" + Screen.height + " produced no column"); }
                return Last;
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks: column rebuild failed: " + e);
                RtFallbacks.Note("quick race column", "rebuild at " + Screen.width + "x" + Screen.height + " failed: " + e.GetType().Name + " " + e.Message);
                Last.Note = "rebuild failed: " + e.Message;
                return Last;
            }
        }

        // ------------------------------------------------------------------------------------------------ column

        private static bool BuildColumn(QuickRaceSetupScreen screen, List<string> names)
        {
            UIGridList list = screen.circuitList;
            Transform grid = list.grid != null ? list.grid : list.transform;
            Canvas canvas = list.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            canvas = canvas.rootCanvas;
            RectTransform canvasRT = (RectTransform)canvas.transform;

            Transform panel = FindStockPanel(list.transform, grid);
            if (panel == null || !(panel is RectTransform) || panel.parent == null || !(panel.parent is RectTransform)) { Main.Log("RT UI: stock circuit panel not found above '" + list.name + "'"); return false; }
            RectTransform P = (RectTransform)panel;
            RectTransform parent = (RectTransform)panel.parent;
            GridLayoutGroup glg = grid.GetComponent<GridLayoutGroup>();
            Vector2 cell = glg != null ? glg.cellSize : Vector2.zero;
            if (cell.x < 10 || cell.y < 10)
            {
                RectTransform pr = list.itemPrefab != null ? list.itemPrefab.GetComponent<RectTransform>() : null;
                if (pr == null) return false;
                cell = pr.sizeDelta;
            }
            Vector2 spacing = glg != null ? glg.spacing : Vector2.zero;
            RectOffset pad = glg != null ? glg.padding : new RectOffset();

            // ---- stock geometry in canvas units (x right, y up)
            Rect stock = RectIn(canvasRT, P);
            Rect gridR = RectIn(canvasRT, (RectTransform)grid);
            Transform lca = CommonAncestor(P, screen.circuitImage != null ? screen.circuitImage.transform : null);
            double contentRight = ContentRight(lca, canvasRT, stock);
            double unit = parent.lossyScale.x / Math.Max(1e-6, canvasRT.lossyScale.x);   // canvas units per local unit of the panel's parent
            double marginX = (gridR.xMin - stock.xMin) + pad.left * unit;                  // stock grid inset inside the stock panel
            double colW = cell.x * unit + 2 * marginX;
            Rect info = screen.circuitImage != null ? RectIn(canvasRT, (RectTransform)screen.circuitImage.transform) : stock;
            double gap = info.xMin - stock.xMax;
            if (gap < 2 || gap > 60) gap = 16;

            // 0.2.3: the content block may be a full-width container (at 16:10 / 16:9 on 1920-wide screens its rect spans the canvas, which the 0.2.1 width
            // guard rejected, so the column fell back to the stock grid). Shrinking it is safe as long as it is not the canvas or the screen itself; the
            // real extent of what gets shrunk is ContentRight (its visible children), logged below with the container's children.
            bool lcaOk = lca != null && lca != canvas.transform && lca != screen.transform && lca is RectTransform;
            if (lca != null)
            {
                System.Text.StringBuilder kids = new System.Text.StringBuilder();
                for (int i = 0; i < lca.childCount && i < 12; i++) kids.Append(i == 0 ? "" : ", ").Append(lca.GetChild(i).name);
                Main.Log("RT UI: content block '" + lca.name + "' rect " + RectText(RectIn(canvasRT, (RectTransform)lca)) + ", children [" + kids + "]");
            }
            UiColumnPlan plan = UiColumnMath.Plan(canvasRT.rect.xMin, canvasRT.rect.xMax, stock.xMin, stock.yMax, stock.yMin, contentRight, colW, gap, lcaOk);
            Main.Log("RT UI: custom tracks plan: canvas " + canvasRT.rect.width.ToString("0") + "x" + canvasRT.rect.height.ToString("0") + " (screen " + Screen.width + "x" + Screen.height +
                     "), stock panel '" + P.name + "' x " + stock.xMin.ToString("0") + ".." + stock.xMax.ToString("0") + " y " + stock.yMin.ToString("0") + ".." + stock.yMax.ToString("0") +
                     ", grid cell " + cell.x.ToString("0") + "x" + cell.y.ToString("0") + " spacing " + spacing.x.ToString("0") + "," + spacing.y.ToString("0") + " pad " + pad.left + "/" + pad.top +
                     ", column w " + colW.ToString("0") + " gap " + gap.ToString("0") + ", content right " + contentRight.ToString("0") + ", scale-about '" + (lca != null ? lca.name : "none") +
                     "' ok=" + lcaOk + " -> " + plan.Mode + " (fits " + plan.Fits + ", scale " + plan.Scale.ToString("0.000") + ")");
            Last.Mode = plan.Mode; Last.Scale = plan.Scale; Last.Fits = plan.Fits; Last.ScreenW = Screen.width; Last.ScreenH = Screen.height; Last.CanvasW = canvasRT.rect.width; Last.CanvasH = canvasRT.rect.height;
            if (!plan.Fits) return false;

            // ---- the panel
            GameObject root = new GameObject(PanelName, typeof(RectTransform));
            root.layer = P.gameObject.layer;
            RectTransform R = (RectTransform)root.transform;
            R.SetParent(parent, false);
            R.SetSiblingIndex(P.GetSiblingIndex());          // right under the stock panel's position in the draw order
            R.anchorMin = R.anchorMax = new Vector2(0.5f, 0.5f);
            R.pivot = new Vector2(0, 1);
            R.localScale = Vector3.one;
            sPanel = root;
            root.SetActive(false);                           // build while inactive
            R.sizeDelta = new Vector2((float)(colW / unit), (float)(stock.height / unit));
            Vector3 topLeft = canvasRT.TransformPoint(new Vector3((float)plan.Left, (float)plan.Top, 0));
            topLeft.z = P.position.z;
            R.position = topLeft;

            CopyBackground(P, root);
            int cloned = 0;
            for (int i = 0; i < P.childCount; i++)
            {
                Transform c = P.GetChild(i);
                if (c == grid || grid.IsChildOf(c) || c.IsChildOf(grid)) continue;      // the list itself is rebuilt below
                if (c.name == PanelName) continue;
                GameObject cl = SafeClone(c.gameObject, R);
                if (cl != null) cloned++;
            }
            int labels = SetHeader(R, "CUSTOM TRACKS", "Real-world tracks from Real Tracks packs");
            Main.Log("RT UI: custom tracks panel built from '" + P.name + "': " + cloned + " child(ren) cloned, " + labels + " text(s) found; grid inset " + marginX.ToString("0") + ", grid top " + (stock.yMax - gridR.yMax).ToString("0") + " below the panel top");

            // ---- entries (the stock item prefab, same ToggleGroup)
            ToggleGroup group = list.GetComponent<ToggleGroup>();
            float topOffset = (float)((stock.yMax - gridR.yMax) / unit) + pad.top;
            float inset = (float)(marginX / unit);
            for (int i = 0; i < names.Count; i++)
            {
                Circuit c = Game.instance.circuitManager.GetCircuitByLocationName(names[i]);
                if (c == null || list.itemPrefab == null) continue;
                GameObject go = (GameObject)UnityEngine.Object.Instantiate(list.itemPrefab);
                go.name = "RT_CustomTrack_" + names[i];
                RectTransform er = (RectTransform)go.transform;
                er.SetParent(R, false);
                er.anchorMin = er.anchorMax = new Vector2(0, 1);
                er.pivot = new Vector2(0, 1);
                er.localScale = Vector3.one;
                er.sizeDelta = cell;
                er.anchoredPosition = new Vector2(inset, -(topOffset + i * (cell.y + spacing.y)));
                UIQuickRaceCircuitEntry e = go.GetComponent<UIQuickRaceCircuitEntry>();
                if (e == null) { UnityEngine.Object.Destroy(go); continue; }
                e.toggle.group = group;
                e.toggle.interactable = true;
                e.Setup(c);
                sEntryNames.Add(names[i]);
                sEntries.Add(e);
            }
            if (sEntryNames.Count == 0) return false;
            root.SetActive(true);

            // ---- make room at 16:9 if the plan says so: the whole content block shrinks about its right edge
            if (plan.Mode == "scaled" && lca != null)
            {
                sLca = lca as RectTransform; sLcaScale0 = lca.localScale; sLcaAnchored0 = sLca != null ? sLca.anchoredPosition : Vector2.zero;
                Vector3 pivot = canvasRT.TransformPoint(new Vector3((float)plan.PivotX, (float)stock.center.y, 0));
                pivot.z = lca.position.z;
                lca.localScale = lca.localScale * (float)plan.Scale;
                lca.position = pivot + (lca.position - pivot) * (float)plan.Scale;
            }
            Rect fin = RectIn(canvasRT, R);
            bool inside = fin.xMin >= canvasRT.rect.xMin - 0.5f && fin.xMax <= canvasRT.rect.xMax + 0.5f;
            sMode = "column"; sBuiltW = Screen.width; sBuiltH = Screen.height;
            Last.Mode = plan.Mode; Last.Scale = plan.Scale; Last.Fits = plan.Fits; Last.InsideScreen = inside; Last.Entries = sEntryNames.Count;
            Last.ScreenW = Screen.width; Last.ScreenH = Screen.height; Last.CanvasW = canvasRT.rect.width; Last.CanvasH = canvasRT.rect.height;
            Last.Left = fin.xMin; Last.Right = fin.xMax; Last.Note = "";
            Main.Log("RT UI: custom tracks column: " + sEntryNames.Count + " entries at " + RectText(fin) + " (canvas units, screen " + canvasRT.rect.width.ToString("0") + "x" + canvasRT.rect.height.ToString("0") +
                     ", mode " + plan.Mode + ", scale " + plan.Scale.ToString("0.000") + ", inside screen " + inside + ")");
            return true;
        }

        private static string RectText(Rect r)
        {
            return "x=" + r.xMin.ToString("0") + " y=" + r.yMin.ToString("0") + " w=" + r.width.ToString("0") + " h=" + r.height.ToString("0");
        }

        /// <summary>A rectangle's bounds in the canvas' own coordinates.</summary>
        private static Rect RectIn(RectTransform canvasRT, RectTransform rt)
        {
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 l = canvasRT.InverseTransformPoint(c[i]);
                x0 = Math.Min(x0, l.x); x1 = Math.Max(x1, l.x); y0 = Math.Min(y0, l.y); y1 = Math.Max(y1, l.y);
            }
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static double ContentRight(Transform lca, RectTransform canvasRT, Rect stock)
        {
            double best = stock.xMax;
            if (lca == null) return best;
            RectTransform[] all = lca.GetComponentsInChildren<RectTransform>(false);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == PanelName) continue;
                Rect r = RectIn(canvasRT, all[i]);
                if (r.width > 0.9f * canvasRT.rect.width || r.width < 1f) continue;
                if (r.xMax > canvasRT.rect.xMax + 1f) continue;       // parked off screen
                if (r.xMax > best) best = r.xMax;
            }
            return best;
        }

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            if (a == null || b == null) return null;
            HashSet<Transform> up = new HashSet<Transform>();
            for (Transform t = a; t != null; t = t.parent) up.Add(t);
            for (Transform t = b; t != null; t = t.parent) if (up.Contains(t)) return t;
            return null;
        }

        // ------------------------------------------------------------------------------------------------ cloning

        private static bool IsLabel(Component c) { return c is Graphic && c.GetType().Name.StartsWith("TextMeshPro", StringComparison.Ordinal); }

        /// <summary>The nearest ancestor of the list that holds at least two text labels outside the grid (the header and its subtitle).</summary>
        private static Transform FindStockPanel(Transform from, Transform grid)
        {
            for (Transform t = from; t != null && t.GetComponent<Canvas>() == null; t = t.parent)
            {
                int n = 0;
                Graphic[] gs = t.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < gs.Length; i++) if (IsLabel(gs[i]) && !gs[i].transform.IsChildOf(grid)) n++;
                if (n >= 2) return t;
            }
            return null;
        }

        /// <summary>Copies the panel's own Image (background) by value, onto the new root.</summary>
        private static void CopyBackground(RectTransform panel, GameObject root)
        {
            Image src = panel.GetComponent<Image>();
            if (src == null) return;
            Image dst = root.AddComponent<Image>();
            dst.sprite = src.sprite; dst.type = src.type; dst.color = src.color; dst.material = src.material; dst.raycastTarget = false;
            dst.fillCenter = src.fillCenter;
        }

        /// <summary>
        /// Clones a decorative child (header, backdrop) the safe way: the source is switched off while it is copied, so the copy is
        /// born inactive and nothing on it wakes up; every component that is not a plain graphic or layout piece (tooltips,
        /// animators, localisation, buttons, game scripts) is removed with DestroyImmediate; only then does the copy go live.
        /// </summary>
        private static GameObject SafeClone(GameObject src, Transform parent)
        {
            bool was = src.activeSelf;
            GameObject clone = null;
            try
            {
                src.SetActive(false);
                clone = (GameObject)UnityEngine.Object.Instantiate(src);
            }
            finally { src.SetActive(was); }
            clone.name = src.name;
            clone.transform.SetParent(parent, false);
            Strip(clone);
            clone.SetActive(was);
            return clone;
        }

        private static void Strip(GameObject go)
        {
            Component[] all = go.GetComponentsInChildren<Component>(true);
            // scripts and selectables first, then the rest (so nothing is removed from under a component that needs it)
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i];
                    if (c == null || c is Transform || c is CanvasRenderer || c is Graphic || c is LayoutGroup || c is LayoutElement ||
                        c is ContentSizeFitter || c is CanvasGroup || c is Mask || c is RectMask2D || c is Canvas) continue;
                    bool script = c is MonoBehaviour;
                    if ((pass == 0) != script) continue;
                    UnityEngine.Object.DestroyImmediate(c);
                }
            Graphic[] gs = go.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < gs.Length; i++) gs[i].raycastTarget = false;
        }

        /// <summary>Sets the title and subtitle on the cloned texts: the topmost is the title, the next one down the subtitle; others are blanked.</summary>
        private static int SetHeader(RectTransform root, string title, string subtitle)
        {
            List<Graphic> texts = new List<Graphic>();
            Graphic[] gs = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < gs.Length; i++) if (IsLabel(gs[i])) texts.Add(gs[i]);
            texts.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            for (int i = 0; i < texts.Count; i++) SetText(texts[i], i == 0 ? title : (i == 1 ? subtitle : ""));
            return texts.Count;
        }

        private static void SetText(Component label, string s)
        {
            PropertyInfo p = label.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(label, s, null);
        }
    }

    // CreateEntries (QuickRaceSetupScreen.cs:100) either builds the stock list (first visit of a screen instance) or returns at once
    // because the list already has items (re-entry); the postfix runs in both cases and builds the column once per screen instance.
    [HarmonyPatch(typeof(QuickRaceSetupScreen), "CreateEntries")]
    public static class Patch_QuickRaceEntries
    {
        public static void Postfix(QuickRaceSetupScreen __instance)
        {
            try { UiQuickRace.AfterCreateEntries(__instance); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: Quick Race custom tracks patch: " + e); RtFallbacks.Note("quick race column", "patch exception, stock screen left as it is: " + e.GetType().Name + " " + e.Message); }
        }
    }

    /// <summary>The UMM panel section for the UI work.</summary>
    public static class UiGui
    {
        public static void Draw(Settings s)
        {
            s.UiCustomTrackColumn = GUILayout.Toggle(s.UiCustomTrackColumn, " Quick Race: list pack venues in a 'Custom tracks' column left of the stock list (off = appended to the stock grid, which cuts them off; takes effect next time Quick Race opens)");
            s.UiCustomIntro = GUILayout.Toggle(s.UiCustomIntro, " Race intro: cover the donor's intro movie picture with a world map and the pack track's own outline (the movie's sound and timing stay)");
            s.UiColumnRebuildOnResize = GUILayout.Toggle(s.UiColumnRebuildOnResize, " Quick Race: lay the custom tracks column out again when the screen size changed since it was built (resolution changed in the preferences)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Intro map stage, seconds: " + s.UiIntroMapSeconds.ToString("0.0"), GUILayout.Width(220));
            s.UiIntroMapSeconds = GUILayout.HorizontalSlider(s.UiIntroMapSeconds, 1.5f, 8f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Intro outline draw time, seconds (0 = automatic): " + s.UiIntroRevealSeconds.ToString("0.0"), GUILayout.Width(330));
            s.UiIntroRevealSeconds = GUILayout.HorizontalSlider(s.UiIntroRevealSeconds, 0f, 10f, GUILayout.Width(220));
            GUILayout.EndHorizontal();
        }
    }
}
