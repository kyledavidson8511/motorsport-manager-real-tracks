using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealTracks
{
    /// <summary>Per-car bookkeeping for one race.</summary>
    public sealed class AtCarState
    {
        public RacingVehicle V;
        public AtCarData Data;
        public bool HasPos;
        public float X, Z;
        public int LastState = -1;
        public readonly AtStuckTracker Stuck = new AtStuckTracker();
        public readonly AtPitTrack Pit = new AtPitTrack();
        public PathController.PathType LastPath = PathController.PathType.Track;
        public int LastCorner;
        public bool RetireLogged, FinishLogged;
        public int LastLap;
        public readonly AtWobbleTracker Wobble = new AtWobbleTracker();
        public readonly AtWobbleTracker VisualWobble = new AtWobbleTracker();
    }

    /// <summary>
    /// Everything that happens once per simulation step during the race (hooked from SessionState.SimulationUpdate, see AtPatches).
    /// Detectors here: gridFormed, allCarsLapped, noStuck, noTeleports, pitRoundTrips, incidentsClean, gate speeds.
    /// </summary>
    public static class AtSim
    {
        public static double SimT;
        public static int LeaderLap;
        public static readonly List<AtCarState> Cars = new List<AtCarState>();
        private static readonly Dictionary<int, AtCarState> byId = new Dictionary<int, AtCarState>();
        private static double chequeredAt = -1;
        private static int stepErrors;
        private static PathData trackData;
        private static readonly Dictionary<string, bool> teamForced = new Dictionary<string, bool>();

        /// <summary>Settings.TestForceStops pit-forcing fires at this lap (1 = the first lap is done).</summary>
        private static int ForceLap(AtItemData d) { return Math.Max(1, d.LapsTarget / 3); }

        /// <summary>The detectors mirror PathStateManager.StateType by number (see AtRules); complain loudly if the game ever changes it.</summary>
        public static void CheckEnumNumbering()
        {
            bool ok = (int)PathStateManager.StateType.Garage == AtRules.StGarage
                && (int)PathStateManager.StateType.GarageEntry == AtRules.StGarageEntry
                && (int)PathStateManager.StateType.GarageExit == AtRules.StGarageExit
                && (int)PathStateManager.StateType.Grid == AtRules.StGrid
                && (int)PathStateManager.StateType.Pitbox == AtRules.StPitbox
                && (int)PathStateManager.StateType.PitboxEntry == AtRules.StPitboxEntry
                && (int)PathStateManager.StateType.PitboxExit == AtRules.StPitboxExit
                && (int)PathStateManager.StateType.Pitlane == AtRules.StPitlane
                && (int)PathStateManager.StateType.Racing == AtRules.StRacing;
            if (!ok) Main.Logger.Error("Real Tracks auto-test: PathStateManager.StateType numbering changed; teleport detector is unreliable");
        }

        public static void ResetItem()
        {
            SimT = 0; LeaderLap = 0; chequeredAt = -1; trackData = null;
            Cars.Clear(); byId.Clear(); teamForced.Clear();
        }

        public static void EndRun()
        {
            ResetItem();
            AtDriver.Restore();
        }

        // ------------------------------------------------------------------ race start (RaceSessionState.OnEnter postfix)

        public static void OnRaceEnter()
        {
            SimT = 0; LeaderLap = 0; chequeredAt = -1;
            AtRunCtx r = AtRun.Current;
            if (r == null || !r.ItemActive) return;
            AtItemData d = r.Cur;
            Game g = Game.instance;
            SessionManager sm = g.sessionManager;
            VehicleManager vm = g.vehicleManager;
            d.RaceStarted = true;
            d.LapsActual = sm.lapCount;
            Cars.Clear(); byId.Clear(); d.Cars.Clear(); teamForced.Clear();
            try { trackData = sm.circuit.GetTrackPath().data; d.CornerCount = trackData.corners.Count; } catch { trackData = null; }

            int n = vm.vehicleCount;
            Dictionary<string, bool> teams = new Dictionary<string, bool>();
            for (int i = 0; i < n; i++)
            {
                RacingVehicle v = vm.GetVehicle(i);
                AtCarState cs = new AtCarState();
                cs.V = v;
                AtCarData c = new AtCarData();
                c.CarId = v.id;
                c.Name = CarName(v);
                c.Team = TeamName(v);
                c.TeamIndex = teams.Count;
                if (!teams.ContainsKey(c.Team)) teams[c.Team] = true;
                cs.Data = c;
                d.Cars.Add(c);
                Cars.Add(cs);
                byId[v.id] = cs;
            }
            d.ExpectedStopTeams = teams.Count;
            GridCheck(d, sm, vm);
            AtRun.Event("raceStart", "laps", d.LapsActual, "target", d.LapsTarget, "cars", n, "teams", teams.Count, "flag", sm.flag.ToString(),
                "raceStart", sm.championship.rules.raceStart.ToString());
        }

        private static void GridCheck(AtItemData d, SessionManager sm, VehicleManager vm)
        {
            d.GridChecked = true;
            d.GridCars = vm.vehicleCount;
            d.GridOk = 0;
            List<string> bad = new List<string>();
            List<Vector3> pos = new List<Vector3>();
            float worst = 0f;
            bool rolling = sm.championship.rules.raceStart == ChampionshipRules.RaceStart.RollingStart;
            for (int i = 0; i < vm.vehicleCount; i++)
            {
                RacingVehicle v = vm.GetVehicle(i);
                try
                {
                    Vector3 p = v.transform.position;
                    pos.Add(p);
                    bool stateOk = v.pathState.stateType == PathStateManager.StateType.Grid || v.pathState.stateType == PathStateManager.StateType.Racing;
                    if (rolling)
                    {
                        if (stateOk) d.GridOk++; else bad.Add(CarName(v) + " state " + v.pathState.stateType);
                        continue;
                    }
                    GridBox gb = sm.circuit.GetGridBox(v.standingsPosition);
                    Vector3 b = gb.position;
                    float dist = Mathf.Sqrt((p.x - b.x) * (p.x - b.x) + (p.z - b.z) * (p.z - b.z));
                    if (dist > worst) worst = dist;
                    if (dist <= 1.0f && stateOk) d.GridOk++;
                    else bad.Add(CarName(v) + " P" + v.standingsPosition + " " + dist.ToString("0.0") + " m off box, state " + v.pathState.stateType);
                }
                catch (Exception e)
                {
                    pos.Add(Vector3.zero);
                    bad.Add(CarName(v) + " P" + v.standingsPosition + ": " + e.GetType().Name);
                }
            }
            // two cars on one spot is not a formed grid either
            float minGap = float.MaxValue;
            for (int i = 0; i < pos.Count; i++)
                for (int j = i + 1; j < pos.Count; j++)
                {
                    float dx = pos[i].x - pos[j].x, dz = pos[i].z - pos[j].z;
                    float dd = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dd < minGap) minGap = dd;
                }
            if (pos.Count > 1 && minGap < 3f)
            {
                d.GridOk = Math.Max(0, d.GridOk - 1);
                bad.Add("two cars " + minGap.ToString("0.0") + " m apart");
            }
            d.GridDetail = (rolling ? "rolling start (boxes not used)" : "worst " + worst.ToString("0.00") + " m from its box, closest cars " + (minGap == float.MaxValue ? 0f : minGap).ToString("0.0") + " m apart");
            if (bad.Count > 0) d.GridDetail += "; " + string.Join("; ", bad.GetRange(0, Math.Min(4, bad.Count)).ToArray());
            AtRun.Event("gridCheck", "cars", d.GridCars, "ok", d.GridOk, "worstM", Math.Round(worst, 2), "detail", d.GridDetail);
        }

        // ------------------------------------------------------------------ per simulation step

        public static void OnStep()
        {
            GameStateManager gsm = App.instance != null ? App.instance.gameStateManager : null;
            if (gsm == null || !(gsm.currentState is RaceSessionState)) return;
            if (Game.instance == null || Game.instance.sessionManager == null) return;
            float dt = GameTimer.simulationDeltaTime;
            if (dt <= 0f) return;
            SimT += dt;
            AtRunCtx r = AtRun.Current;
            SessionManager sm = Game.instance.sessionManager;
            LeaderLap = sm.lap;
            if (r == null || !r.ItemActive || Cars.Count == 0) return;
            AtItemData d = r.Cur;
            AtCfg cfg = AutoTest.Cfg();
            bool started = sm.hasSessionStarted && !sm.isRollingOut;
            bool flagOk = sm.flag == SessionManager.Flag.Green || sm.flag == SessionManager.Flag.Yellow || sm.flag == SessionManager.Flag.SafetyCar || sm.flag == SessionManager.Flag.VirtualSafetyCar;
            d.RaceSimSeconds = SimT;

            if (sm.flag == SessionManager.Flag.Chequered && chequeredAt < 0)
            {
                chequeredAt = SimT;
                AtRun.Event("chequered", "leaderLap", sm.lap, "laps", sm.lapCount);
            }

            for (int i = 0; i < Cars.Count; i++)
            {
                AtCarState cs = Cars[i];
                RacingVehicle v = cs.V;
                try { StepCar(r, d, cfg, sm, cs, v, started && flagOk); }
                catch (Exception e)
                {
                    if (stepErrors++ < 5) Main.Logger.Error("Real Tracks auto-test: step failed for car " + cs.Data.Name + ": " + e);
                }
            }
        }

        private static void StepCar(AtRunCtx r, AtItemData d, AtCfg cfg, SessionManager sm, AtCarState cs, RacingVehicle v, bool running)
        {
            AtCarData c = cs.Data;
            Vector3 p = v.transform.position;
            int stateNum = (int)v.pathState.stateType;
            PathStateManager.PathStateGroup grp = v.pathState.pathStateGroup;
            PathController.PathType pt = v.pathController.currentPathType;
            bool out_ = v.behaviourManager.isOutOfRace;

            c.Laps = Math.Max(c.Laps, v.timer.lap);
            if (c.Laps != cs.LastLap)
            {
                cs.LastLap = c.Laps;
                AtRun.Event("lap", "car", c.Name, "laps", c.Laps, "pos", v.standingsPosition);
            }
            if (v.timer.hasSeenChequeredFlag && !c.Finished)
            {
                c.Finished = true;
                if (!cs.FinishLogged) { cs.FinishLogged = true; AtRun.Event("carFinished", "car", c.Name, "laps", c.Laps, "pos", v.standingsPosition); }
            }
            if (out_ && !c.Retired)
            {
                c.Retired = true;
                c.RetireReason = v.behaviourManager.currentBehaviour != null ? v.behaviourManager.currentBehaviour.behaviourType.ToString() : "";
                if (!cs.RetireLogged) { cs.RetireLogged = true; AtRun.Event("retire", "car", c.Name, "reason", c.RetireReason, "lap", c.Laps); }
            }

            // corner for incident attribution: remembered while the car is on the main track
            if (pt == PathController.PathType.Track) { int k = CornerIndex(v); if (k > 0) cs.LastCorner = k; }

            // incidents: the car enters a crash / run-wide / cut-corner lane
            if (pt != cs.LastPath)
            {
                string kind = pt == PathController.PathType.CrashLane ? "crash" : pt == PathController.PathType.RunWideLane ? "runwide" : pt == PathController.PathType.CutCornerLane ? "cutcorner" : null;
                if (kind != null && cfg.Incidents)
                {
                    AtIncident inc = new AtIncident();
                    inc.SimT = SimT; inc.Kind = kind; inc.Car = c.Name; inc.Corner = cs.LastCorner; inc.Lap = c.Laps + 1;
                    d.Incidents.Add(inc);
                    if (inc.Corner > 0) { int n; d.PerCorner.TryGetValue(inc.Corner, out n); d.PerCorner[inc.Corner] = n + 1; }
                    AtRun.Event("incident", "kind", kind, "car", c.Name, "corner", inc.Corner, "lapOfCar", inc.Lap);
                }
            }
            cs.LastPath = pt;

            // teleports (per step, not per frame, so the sim scale does not matter)
            if (cs.HasPos && cfg.Teleport)
            {
                float dx = p.x - cs.X, dz = p.z - cs.Z;
                if (AtRules.IsTeleport(dx, dz, cfg.TeleportMeters, AtRules.IsByDesignJumpState(cs.LastState), AtRules.IsByDesignJumpState(stateNum)))
                {
                    double m = Math.Sqrt(dx * dx + dz * dz);
                    c.Teleports++;
                    AddGlitch(d, "teleport", c.Name, c.Laps + 1, m);
                    AtRun.Event("teleport", "car", c.Name, "meters", Math.Round(m, 1), "state", ((PathStateManager.StateType)stateNum).ToString(), "path", pt.ToString(),
                        "x", Math.Round(p.x, 1), "z", Math.Round(p.z, 1));
                }
            }
            // steering smoothness: heading reversals per km on the main track (AutoTestWobble.cs)
            if (running && grp == PathStateManager.PathStateGroup.OnTrack && pt == PathController.PathType.Track && !out_ && cs.HasPos && v.speed > 10f)
            {
                try
                {
                    Vector3 fw = v.transform.forward;
                    double hdg = Math.Atan2(fw.x, fw.z) * 180.0 / Math.PI;
                    double step = Math.Sqrt((p.x - cs.X) * (p.x - cs.X) + (p.z - cs.Z) * (p.z - cs.Z));
                    double swing;
                    d.Wobble.AddStep(step, c.Laps + 1);
                    if (cs.Wobble.Step(hdg, step, out swing))
                    {
                        int gate = -1;
                        try { gate = v.pathController.GetCurrentPath().previousGateIdAccessor; } catch { }
                        bool edge = false;
                        try { edge = Math.Abs(v.pathController.GetCurrentPath().pathSpace - v.steeringManager.pathCenterPathSpace) > 0.6f; } catch { }
                        d.Wobble.Add(gate, swing, step, c.Laps + 1, edge);
                    }
                    // the same filter on the 3D car's visual yaw (UnityVehicle transform), to tell sim steering wobble from visual wobble
                    try
                    {
                        UnityVehicle uv = v.unityVehicle;
                        if (uv != null)
                        {
                            Vector3 uf = uv.transform.forward;
                            double vh = Math.Atan2(uf.x, uf.z) * 180.0 / Math.PI;
                            double vs;
                            d.WobbleVisual.AddStep(step, c.Laps + 1);
                            if (cs.VisualWobble.Step(vh, step, out vs))
                            {
                                int vg = -1;
                                try { vg = v.pathController.GetCurrentPath().previousGateIdAccessor; } catch { }
                                d.WobbleVisual.Add(vg, vs, step, c.Laps + 1, false);
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            }
            else { cs.Wobble.Reset(); cs.VisualWobble.Reset(); }
            cs.X = p.x; cs.Z = p.z; cs.HasPos = true; cs.LastState = stateNum;

            // stuck
            bool eligible = running && cfg.Stuck && grp == PathStateManager.PathStateGroup.OnTrack && pt == PathController.PathType.Track
                && !out_ && !v.timer.hasSeenChequeredFlag && v.movementEnabled;
            if (cs.Stuck.Update(SimT, p.x, p.z, eligible, cfg.StuckMeters, cfg.StuckSeconds))
            {
                c.Stuck++;
                AddGlitch(d, "stuck", c.Name, c.Laps + 1, 0);
                AtRun.Event("stuck", "car", c.Name, "seconds", cfg.StuckSeconds, "state", ((PathStateManager.StateType)stateNum).ToString(), "path", pt.ToString(),
                    "x", Math.Round(p.x, 1), "z", Math.Round(p.z, 1), "speedKph", Math.Round(v.speed * 3.6f, 1));
            }

            // pit round trips
            if (cfg.Pit)
            {
                string pe = cs.Pit.Observe(SimT, grp == PathStateManager.PathStateGroup.InPitlane, grp == PathStateManager.PathStateGroup.InPitbox,
                    grp == PathStateManager.PathStateGroup.OnTrack, grp == PathStateManager.PathStateGroup.InGarage);
                if (pe != null) AtRun.Event(pe, "car", c.Name, "team", c.Team, "lap", c.Laps + 1, "forced", c.ForcedStop);
                c.PitIns = cs.Pit.Ins; c.PitOuts = cs.Pit.Outs; c.PitBoxes = cs.Pit.Boxes; c.PitTrips = cs.Pit.Trips; c.PitOpen = cs.Pit.Open;
            }

            // make sure every team stops at least once
            if (Main.Settings.TestForceStops && running && !out_) MaybeForceStop(d, sm, cs, v, grp, pt);
        }

        private static void MaybeForceStop(AtItemData d, SessionManager sm, AtCarState cs, RacingVehicle v, PathStateManager.PathStateGroup grp, PathController.PathType pt)
        {
            AtCarData c = cs.Data;
            if (c.ForcedStop || grp != PathStateManager.PathStateGroup.OnTrack || pt != PathController.PathType.Track) return;
            if (v.strategy.status != SessionStrategy.Status.NoActionRequired || v.strategy.IsGoingToPit() || v.strategy.HasQueuedOrderToPit()) return;
            if (d.LapsTarget < 3 || v.timer.lap < ForceLap(d)) return;
            if (sm.GetLapsRemaining() < 2 || sm.flag != SessionManager.Flag.Green) return;
            // one forced stop per team, and only for a team that has not stopped (or been sent) yet
            bool teamHasStop = false;
            bool forcedBefore;
            if (teamForced.TryGetValue(c.Team, out forcedBefore))
            {
                // keep the team marked unless the car we forced retired without coming back out
                bool forcedCarGone = false;
                for (int i = 0; i < Cars.Count; i++)
                    if (Cars[i].Data.Team == c.Team && Cars[i].Data.ForcedStop && Cars[i].Data.Retired && Cars[i].Data.PitOuts == 0) forcedCarGone = true;
                teamHasStop = !forcedCarGone;
            }
            if (!teamHasStop)
            {
                for (int i = 0; i < Cars.Count; i++)
                {
                    AtCarData o = Cars[i].Data;
                    if (o.Team != c.Team) continue;
                    // run 8: a car that pitted in and then retired there doesn't count; its team-mate still needs a round trip
                    bool roundTrip = o.PitOuts > 0;
                    bool stillGoing = !o.Retired && (o.PitIns > o.PitOuts || o.ForcedStop);
                    if (roundTrip || stillGoing) { teamHasStop = true; break; }
                }
            }
            if (teamHasStop) return;
            teamForced[c.Team] = true;
            c.ForcedStop = true;
            d.ForcedStops++;
            try
            {
                v.strategy.SetAIPitOrder(SessionStrategy.PitOrder.Tyres);
                if (v.championship.rules.isRefuelingOn) v.strategy.SetAIPitOrder(SessionStrategy.PitOrder.Refuel);
                v.strategy.Pit();
                AtRun.Event("forcedStop", "car", c.Name, "team", c.Team, "lap", v.timer.lap + 1);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test: forcing a pit stop failed: " + e.Message);
            }
        }

        private static void AddGlitch(AtItemData d, string kind, string car, int lap, double meters)
        {
            AtGlitch g = new AtGlitch();
            g.SimT = SimT; g.Kind = kind; g.Car = car; g.Lap = lap; g.Meters = meters;
            d.Glitches.Add(g);
            AtCfg cfg = AutoTest.Cfg();
            foreach (AtIncident inc in d.Incidents)
                if (inc.Car == car && SimT - inc.SimT <= cfg.IncidentGlitchWindowSeconds && SimT >= inc.SimT) inc.Glitch = true;
        }

        private static int CornerIndex(RacingVehicle v)
        {
            try
            {
                if (trackData == null) return 0;
                PathData.Corner c = null;
                PathController pc = v.pathController;
                if (pc.IsInCorner()) c = pc.GetCurrentCorner();
                else
                {
                    PathData.Straight st = pc.GetCurrentStraight();
                    if (st != null) c = st.nextCorner;
                }
                if (c == null) return 0;
                int i = trackData.corners.IndexOf(c);
                return i >= 0 ? i + 1 : 0;
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------------ gate speeds (RacingVehicle.OnEnterGate postfix)

        public static void OnGate(RacingVehicle v, int gateId)
        {
            AtRunCtx r = AtRun.Current;
            if (r == null || !r.ItemActive || r.Gates == null) return;
            if (!(App.instance.gameStateManager.currentState is RaceSessionState)) return;
            AtCfg cfg = AutoTest.Cfg();
            if (!cfg.GateSpeeds || gateId % cfg.GateEvery != 0) return;
            if (v.pathController.currentPathType != PathController.PathType.Track) return;
            AtCarState cs;
            if (!byId.TryGetValue(v.id, out cs)) return;
            double sM = 0;
            try
            {
                List<PathData.Gate> gates = v.pathController.GetCurrentPath().data.gates;
                if (gateId >= 0 && gateId < gates.Count) sM = gates[gateId].startDistance;
            }
            catch { }
            AtRun.GateRow(AtLines.GateRow(r.Cur.Location + " " + r.Cur.Layout, cs.Data.Name, cs.Data.Laps + 1, gateId, sM, v.speed * 3.6f));
            r.Cur.GateSamples++;
        }

        // ------------------------------------------------------------------ end of an item

        public static void FinishItem(AtItemData d)
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                AtCarState cs = Cars[i];
                try
                {
                    cs.Data.PitOpen = cs.Pit.Open;
                    cs.Data.Laps = Math.Max(cs.Data.Laps, cs.V.timer.lap);
                    if (cs.V.timer.hasSeenChequeredFlag) cs.Data.Finished = true;
                    // run-7 verifier: the last car can be classified before the tracker sees its final crossing; log it here
                    if (cs.Data.Finished && !cs.Data.Retired && !cs.FinishLogged)
                    {
                        cs.FinishLogged = true;
                        AtRun.Event("carFinished", "car", cs.Data.Name, "laps", cs.Data.Laps, "pos", cs.V.standingsPosition, "late", true);
                    }
                }
                catch { }
            }
            d.RaceSimSeconds = SimT;
            Cars.Clear(); byId.Clear();
        }

        /// <summary>Seconds of sim time since the chequered flag came out (-1 = not yet).</summary>
        public static double SinceChequered { get { return chequeredAt < 0 ? -1 : SimT - chequeredAt; } }

        // ------------------------------------------------------------------ names

        public static string CarName(RacingVehicle v)
        {
            try
            {
                string n = v.GetName();
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            try { return v.driver.name; } catch { return "car " + v.id; }
        }

        public static string TeamName(RacingVehicle v)
        {
            try { return v.driver.contract.GetTeam().name; } catch { return "team ?"; }
        }
    }
}
