namespace RealTracks
{
    /// <summary>Unity-free decision for the auto-test's forced crashes (unit-tested in tools/autotest-unit-tests).</summary>
    public static class AtCrashPlan
    {
        /// <summary>True when another car should be armed: crashes are still missing, the previous arm has either produced a crash or waited
        /// waitSimS sim seconds, and the number of arms stays under 6 per wanted crash.</summary>
        public static bool ShouldArm(int crashes, int crashesWhenArmed, double armedSim, double nowSim, double waitSimS, int arms, int wanted)
        {
            if (wanted <= 0 || crashes >= wanted) return false;
            if (arms >= wanted * 6) return false;
            if (crashesWhenArmed < 0) return true;                 // never armed
            if (crashes > crashesWhenArmed) return true;           // the last arm produced a crash: arm the next
            return nowSim - armedSim >= waitSimS;                  // otherwise give it waitSimS sim seconds
        }
    }
}
