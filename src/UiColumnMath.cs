using System;

namespace RealTracks
{
    /// <summary>
    /// Unity-free placement math of the Quick Race "Custom tracks" column (M1.1 A1). All numbers are canvas units, x to the right,
    /// y up, measured at runtime from the stock "SELECT CIRCUIT" panel; compiled into the net8 test as well.
    /// </summary>
    public struct UiColumnPlan
    {
        public bool Fits;            // the column lies inside the screen after the plan is applied
        public double Left, Right, Top, Bottom;   // the column's rectangle (before the optional content scale is applied)
        public double Scale;         // 1 = stock content untouched; < 1 = the whole content block is scaled about its right edge
        public double PivotX;        // x the scale is applied about
        public double ScaledLeft;    // left edge of the column after the scale
        public string Mode;          // "left" (room beside the stock panel), "scaled" (content block shrunk to make room), "none" (no room)
    }

    /// <summary>What the Quick Race column looks like after its last build (read by the auto-test's resolution matrix).</summary>
    public class UiColumnInfo
    {
        public string Mode = "";             // "left" | "scaled" | "none" (no room) | "grid" (fell back to the stock grid) | "" (not built)
        public double Scale = 1;
        public bool Fits, InsideScreen;
        public int Entries, ScreenW, ScreenH;
        public double CanvasW, CanvasH, Left, Right;
        public string Note = "";
    }

    public static class UiColumnMath
    {
        public const double Margin = 8.0, MinScale = 0.72;

        /// <summary>
        /// screenLeft/screenRight: the canvas edges. stockLeft/Right/Top/Bottom: the stock panel. contentRight: right edge of the whole
        /// content block (the stock panel plus the chosen-circuit area). colW: width of one stock entry column plus padding.
        /// The column gets the stock panel's top and bottom and sits `gap` to its left.
        /// </summary>
        public static UiColumnPlan Plan(double screenLeft, double screenRight, double stockLeft, double stockTop, double stockBottom,
                                        double contentRight, double colW, double gap, bool allowScale)
        {
            UiColumnPlan p = new UiColumnPlan();
            p.Top = stockTop; p.Bottom = stockBottom;
            p.Right = stockLeft - gap;
            p.Left = p.Right - colW;
            p.Scale = 1; p.PivotX = contentRight; p.ScaledLeft = p.Left; p.Mode = "left";
            if (p.Left >= screenLeft + Margin) { p.Fits = true; return p; }
            if (allowScale)
            {
                double s = (contentRight - (screenLeft + Margin)) / (contentRight - p.Left);
                if (s >= MinScale)
                {
                    p.Scale = s; p.Mode = "scaled";
                    p.ScaledLeft = contentRight + s * (p.Left - contentRight);
                    p.Fits = p.ScaledLeft >= screenLeft + Margin - 0.5 && contentRight <= screenRight;
                    return p;
                }
            }
            p.Fits = false; p.Mode = "none";
            return p;
        }
    }
}
