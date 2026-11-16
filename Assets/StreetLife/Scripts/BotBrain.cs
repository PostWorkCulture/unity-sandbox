// The bot: sees, remembers, picks where to go by its play style, walks the nav grid (stairs, jumps, double jumps,
// drops and ladders) through the same movement code as the player, and aims and pulls the trigger like the web game's
// bots (reaction time, aim error that settles, turn speed, bursts and pauses, deliberate misses).
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StreetLife
{
    [RequireComponent(typeof(Fighter)), DefaultExecutionOrder(-10)]
    public class BotBrain : MonoBehaviour
    {
        public string styleId = "duelist";
        public string difficulty = "normal";
        public Fighter target;

        /// Distance the bot's gun likes to fight at (the weapons chat can set RangeForWeapon per gun).
        public static Func<string, float> RangeForWeapon = id => id == "sword" ? 2.5f : id == "shotgun" || id == "autoshot" ? 7f : 20f;
        public float IdealRange => RangeForWeapon(f.primary) * Style.range;

        public BotStyle Style => BotStyles.All.TryGetValue(styleId, out var s) ? s : BotStyles.All["duelist"];
        public BotDifficulty Dif => BotStyles.Levels.TryGetValue(difficulty, out var d) ? d : BotStyles.Levels["normal"];
        public string Mode { get; private set; } = "roam";
        public float roundTimeLeft = 90;   // set by the round manager (late-round pushes)

        Fighter f;
        // perception
        bool sees; float seeT, lostT = 99; Vector3? lastSeen; float senseT;
        // navigation
        List<int> path; int pathIx; Vector3 goal; float repathT, thinkT, stuckT, jumpHoldT; bool djPending; Vector3 lastPos;
        int goalNode = -1;
        // style memory
        Vector3? spot; float spotT; int shotsAtSpot, lastShots; float strafeDir = 1, strafeT, jukeT, peekT; bool springing;
        float flankSide = 1; int ladderDir = 1; bool wasOnLadder; float sideStepT, sideDir = 1;
        // aim
        float aimYaw, aimPitch, errScale = 1, errYaw, errPitch, errT, burstShots, pauseT, hesitate, triggerT; bool aimHead;

        void Awake() { f = GetComponent<Fighter>(); if (!GetComponent<BotExtras>()) gameObject.AddComponent<BotExtras>(); }

        // read by BotExtras (abilities, lethals)
        public bool Sees => sees;
        public float SeeT => seeT;
        /// set by BotExtras for a trip to the shop kiosk: walk there, whatever the style says
        public Vector3? ForceGoal;
        public float LostT => lostT;
        public Vector3? LastSeen => lastSeen;
        public float StuckT => stuckT;

        public string DebugInfo => $"{styleId} {Mode} sees:{sees} path:{(path == null ? -1 : path.Count)} ix:{pathIx} stuck:{stuckT:F1} ladder:{(f.onLadder != null)} wp:{(path != null && pathIx < path.Count ? NavGrid.Nodes[path[pathIx]].p.ToString() : "-")} {(path != null && pathIx > 0 && pathIx < path.Count ? NavGrid.EdgeBetween(path[pathIx - 1], path[pathIx], out _).ToString() : "")}";

        public void ResetForRound(string style)
        {
            styleId = style; Mode = "roam";
            sees = false; seeT = 0; lostT = 99; lastSeen = null; path = null; goalNode = -1; spot = null; shotsAtSpot = 0;
            springing = false; flankSide = Random.value < .5f ? -1 : 1; strafeDir = flankSide; pauseT = 0; burstShots = 0;
            aimYaw = f.yaw; aimPitch = 0; lastShots = f.shots; stuckT = 0; lastPos = f.pos;
            f.inFwd = f.inSide = 0; f.inJump = f.inFire = false;
        }

        void Update()
        {
            if (!f.alive || !NavGrid.Ready || target == null) { if (f) { f.inFwd = f.inSide = 0; f.inFire = false; f.inJump = false; } return; }
            float dt = Time.deltaTime;
            Perceive(dt);
            thinkT -= dt;
            if (thinkT <= 0) { thinkT = Random.Range(.12f, .22f); Think(); }
            Steer(dt);
            Aim(dt);
        }

        // ------------------------------------------------------------------ perception
        Vector3 Eye => f.Head;
        Vector3 Body(Fighter t) => new Vector3(t.pos.x, t.pos.y + 1.25f * Fighter.SC, t.pos.z);

        void Perceive(float dt)
        {
            bool was = sees;
            sees = target.alive && !(f.blindT > 0) && (NavGrid.Los(Eye, Body(target)) || NavGrid.Los(Eye, target.Head));
            if (sees) { seeT += dt; lostT = 0; lastSeen = target.pos; if (!was) OnFirstSight(); }
            else { seeT = 0; lostT += dt; }
            // hearing: shots give away where you are; being hit tells it roughly where from
            if (target.alive && Time.time - target.lastShotTime < .3f && Vector3.Distance(target.lastShotPos, f.pos) < 45) { lastSeen = target.lastShotPos; lostT = Mathf.Min(lostT, .5f); }
            if (f.lastBy == target && Time.time - f.lastHitTime < .2f) { lastSeen = target.pos; lostT = Mathf.Min(lostT, .3f); }
            // without contact for a while, every style gets a rough idea where you are (sooner for the aggressive ones),
            // so a round never turns into two players hiding from each other
            if (!sees && target.alive && lostT > (Style.aggr >= .6f ? 6f : 13f)) { lastSeen = target.pos + new Vector3(Random.Range(-7f, 7f), 0, Random.Range(-7f, 7f)); lostT = 1; }
            // HeartBeat running, or you've been tagged: it knows exactly where you are
            if (target.alive && (f.senseT > 0 || target.Tagged)) { lastSeen = target.pos; lostT = Mathf.Min(lostT, .5f); }
            // hunters always have a rough fix
            if (styleId == "hunter") { senseT -= dt; if (senseT <= 0 && target.alive) { senseT = Random.Range(2.5f, 4f); lastSeen = target.pos + new Vector3(Random.Range(-4f, 4f), 0, Random.Range(-4f, 4f)); lostT = Mathf.Min(lostT, 1); } }
        }

        void OnFirstSight()
        {
            var D = Dif;
            errScale = 1; hesitate = Random.value < D.hes ? Random.Range(.3f, .7f) : 0;
            NewError();
        }

        // ------------------------------------------------------------------ decisions
        void Think()
        {
            var P = Style;
            float dist = Flat(target.pos - f.pos).magnitude;
            bool hurt = f.hp < f.hpMax * P.retreat;
            bool push = target.alive && target.hp < 35 || (roundTimeLeft < 25 && f.hp + f.armor < target.hp + target.armor);
            float ideal = IdealRange;

            if (!target.alive) { Mode = "roam"; Roam(); return; }
            if (ForceGoal.HasValue) { Mode = "shop"; GoTo(ForceGoal.Value, .6f); return; }
            if (hurt && !push && styleId != "rusher") { Mode = "retreat"; GoHide(Mathf.Max(ideal, 14), true); return; }
            if (push && (sees || lastSeen.HasValue)) { Mode = "push"; GoTo(target.pos, .8f); return; }

            switch (styleId)
            {
                case "rusher":
                    Mode = sees ? "rush" : lastSeen.HasValue ? "chase" : "roam";
                    if (sees || lastSeen.HasValue) GoTo(sees ? target.pos : lastSeen.Value, .8f); else Roam();
                    break;

                case "sniper":
                    if (!spot.HasValue || shotsAtSpot >= Mathf.Max(1, P.shots) || Time.time - spotT > 25)
                    { spot = FindSpot(high: true, minDist: 22, maxDist: 60, needView: true); spotT = Time.time; shotsAtSpot = 0; lastShots = f.shots; }
                    shotsAtSpot = f.shots - lastShots;
                    if (dist < 7 && sees) { Mode = "backoff"; GoAway(10); }
                    else { Mode = "snipe"; GoTo(spot ?? f.pos, .6f); }
                    break;

                case "lurker":
                    {
                        float facing = Vector3.Dot(target.Forward, Flat(f.pos - target.pos).normalized);   // < 0: it's behind you
                        if (sees && (dist < P.spring || facing < -.3f)) springing = true;
                        if (springing) { Mode = "ambush"; Engage(dist, ideal); }
                        else { Mode = "lurk"; if (!spot.HasValue || Time.time - spotT > 12 || NavGrid.Los(target.Head, (spot ?? f.pos) + Vector3.up * 1.6f)) { spot = FindHideNear(target.pos, 8, 16); spotT = Time.time; } GoTo(spot ?? f.pos, .6f); }
                    }
                    break;

                case "flanker":
                    if (sees && dist > 12 && Time.time - spotT > 4)
                    {
                        Mode = "flank";
                        var side = target.Right * flankSide;
                        spot = Nearest(target.pos + side * 14f); spotT = Time.time;
                    }
                    if (Mode == "flank" && spot.HasValue && Flat(spot.Value - f.pos).magnitude > 2 && Time.time - spotT < 6) GoTo(spot.Value, .8f);
                    else if (sees) { Mode = "fight"; Engage(dist, ideal); }
                    else Chase();
                    break;

                case "turtle":
                    if (sees) { Mode = "peek"; Peek(); }
                    else if (lastSeen.HasValue && lostT < 6) { Mode = "hold"; Stop(); }
                    else Chase();
                    break;

                case "camper":
                    if (!spot.HasValue) { spot = FindSpot(high: false, minDist: 0, maxDist: 60, needView: true); spotT = Time.time; }
                    Mode = "camp";
                    if (sees && dist < 6) Engage(dist, ideal); else GoTo(spot.Value, .5f);
                    break;

                case "breacher":
                    if (sees && dist > 9)
                    {
                        Mode = "hop";
                        if (!spot.HasValue || Flat(spot.Value - f.pos).magnitude < 1.5f || NavGrid.Los(target.Head, spot.Value + Vector3.up * 1.6f))
                        { spot = FindHideNear(target.pos, Mathf.Max(4, dist - 9), Mathf.Max(6, dist - 3)); spotT = Time.time; }
                        GoTo(spot ?? target.pos, .6f);
                    }
                    else if (sees) { Mode = "breach"; GoTo(target.pos, .8f); }
                    else Chase();
                    break;

                case "kiter":
                    if (sees) { Mode = "kite"; Engage(dist, ideal, band: .15f); } else Chase();
                    break;

                case "hunter":
                    if (sees) { Mode = "fight"; Engage(dist, ideal); } else Chase();
                    break;

                default: // duelist
                    if (sees) { Mode = "duel"; Engage(dist, ideal); } else Chase();
                    break;
            }
        }

        void Chase()
        {
            if (lastSeen.HasValue && lostT < 10) { Mode = "search"; GoTo(lastSeen.Value, 1.2f); }
            else { Mode = "roam"; Roam(); }
        }

        /// Hold a distance band around the ideal range, strafing while in it.
        void Engage(float dist, float ideal, float band = .25f)
        {
            if (dist > ideal * (1 + band)) GoTo(target.pos, ideal * .9f);
            else if (dist < ideal * (1 - band)) GoAway(ideal);
            else Stop();
        }

        void Peek()
        {
            peekT -= .17f;
            if (peekT <= 0) { peekT = Style.hold * Random.Range(.7f, 1.3f); strafeDir = -strafeDir; }
            Stop();
        }

        void Roam()
        {
            if (path == null || pathIx >= path.Count || Time.time - spotT > 9)
            {
                var n = NavGrid.RandomNode(); spotT = Time.time;
                if (n != null) SetGoal(n.id);
            }
        }

        void Stop() { path = null; goalNode = -1; }

        void GoTo(Vector3 p, float arrive)
        {
            if (Flat(p - f.pos).magnitude < arrive && Mathf.Abs(p.y - f.pos.y) < 1.2f) { Stop(); return; }
            int n = NavGrid.Nearest(p, 4f, 2.5f);
            if (n < 0) return;
            if (n != goalNode || path == null || Time.time > repathT || PathBlocked()) SetGoal(n);
        }

        // a car pulled in, or something got shoved, across the next few steps
        bool PathBlocked()
        {
            if (path == null) return false;
            for (int i = pathIx; i < path.Count && i < pathIx + 4; i++) if (NavGrid.Blocked(path[i])) return true;
            return false;
        }

        void GoAway(float minDist)
        {
            var away = Flat(f.pos - target.pos).normalized;
            var cand = f.pos + away * 6f;
            int n = NavGrid.Nearest(cand, 4f, 1.5f);
            if (n >= 0 && Flat(NavGrid.Nodes[n].p - target.pos).magnitude > Flat(f.pos - target.pos).magnitude) SetGoal(n);
            else GoHide(minDist, false);
        }

        void GoHide(float minDist, bool far)
        {
            if (!spot.HasValue || Time.time - spotT > 5 || NavGrid.Los(target.Head, spot.Value + Vector3.up * 1.6f))
            { spot = FindHideNear(f.pos, 4, 18, awayFrom: target.pos, minFromTarget: minDist); spotT = Time.time; }
            GoTo(spot ?? f.pos, .6f);
        }

        void SetGoal(int node)
        {
            int from = NavGrid.Nearest(f.pos, 2f, .8f, false);
            if (from < 0) from = NavGrid.Nearest(f.pos, 4f, 3f, false);
            goalNode = node; repathT = Time.time + 1.5f;
            path = NavGrid.FindPath(from, node, 25000);
            pathIx = path != null && path.Count > 1 ? 1 : 0;
        }

        // ------------------------------------------------------------------ spots
        Vector3? Nearest(Vector3 p) { int n = NavGrid.Nearest(p, 6f, 20f); return n >= 0 ? NavGrid.Nodes[n].p : (Vector3?)null; }

        Vector3? FindHideNear(Vector3 around, float rMin, float rMax, Vector3? awayFrom = null, float minFromTarget = 0)
        {
            Vector3? best = null; float bv = float.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                float a = Random.Range(0, Mathf.PI * 2), r = Random.Range(rMin, rMax);
                int n = NavGrid.Nearest(around + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r), 2f, 8f);
                if (n < 0) continue;
                var p = NavGrid.Nodes[n].p;
                if (NavGrid.Los(target.Head, p + Vector3.up * 1.6f)) continue;     // must be out of sight
                float dt = Flat(p - target.pos).magnitude;
                if (dt < minFromTarget) continue;
                float v = Flat(p - f.pos).magnitude + (awayFrom.HasValue ? -dt * .3f : 0);
                if (v < bv) { bv = v; best = p; }
            }
            return best;
        }

        Vector3? FindSpot(bool high, float minDist, float maxDist, bool needView)
        {
            Vector3? best = null; float bv = float.MinValue;
            for (int i = 0; i < 60; i++)
            {
                var nd = NavGrid.RandomNode(); if (nd == null) break;
                var p = nd.p; float dt = Flat(p - target.pos).magnitude;
                if (dt < minDist || dt > maxDist) continue;
                float v = (high ? p.y * 3 : p.y) - Flat(p - f.pos).magnitude * .05f;
                if (needView)
                {
                    int view = 0;
                    for (int k = 0; k < 6; k++) { var o = NavGrid.RandomNode(); if (o != null && NavGrid.Los(p + Vector3.up * 1.8f, o.p + Vector3.up * 1.2f)) view++; }
                    v += view * 2;
                }
                if (v > bv) { bv = v; best = p; }
            }
            return best;
        }

        // ------------------------------------------------------------------ steering (path following, jumps, ladders)
        void Steer(float dt)
        {
            Vector3 move = Vector3.zero; bool jump = false;
            if (path != null && pathIx < path.Count)
            {
                // skip ahead along straight, flat stretches
                for (int k = Mathf.Min(path.Count - 1, pathIx + 6); k > pathIx; k--)
                {
                    bool flat = true;
                    for (int q = Mathf.Max(1, pathIx); q <= k && flat; q++) if (NavGrid.EdgeBetween(path[q - 1], path[q], out _) != EdgeKind.Walk) flat = false;
                    var pk = NavGrid.Nodes[path[k]].p;
                    if (flat && Mathf.Abs(pk.y - f.pos.y) < .6f && NavGrid.Los(f.pos + Vector3.up * .6f, pk + Vector3.up * .6f) && NavGrid.Los(f.pos + Vector3.up * 1.6f, pk + Vector3.up * 1.6f)) { pathIx = k; break; }
                }
                var wp = NavGrid.Nodes[path[pathIx]].p;
                int prev = pathIx > 0 ? path[pathIx - 1] : -1;
                int ladder = -1; var kind = EdgeKind.Walk;
                if (prev >= 0) kind = NavGrid.EdgeBetween(prev, path[pathIx], out ladder);
                var to = Flat(wp - f.pos);
                if (kind == EdgeKind.Ladder && ladder >= 0)
                {
                    var L = BoxWorld.Ladders[ladder];
                    var n = new Vector3(L.nx, 0, L.nz);
                    bool up = wp.y > f.pos.y;
                    ladderDir = up ? 1 : -1;
                    if (f.onLadder != null) move = up ? n : -n;                       // keep climbing (or sliding down)
                    else
                    {
                        // walk to the foot (or onto the top edge), then into the ladder
                        bool ax = L.nx != 0; float cx = (L.x0 + L.x1) / 2, cz = (L.z0 + L.z1) / 2;
                        var foot = up ? new Vector3(ax ? L.rung - L.nx * .6f : cx, f.pos.y, ax ? cz : L.rung - L.nz * .6f)
                                      : new Vector3(ax ? L.rung + L.nx * .2f : cx, f.pos.y, ax ? cz : L.rung + L.nz * .2f);
                        var tf = Flat(foot - f.pos);
                        move = tf.magnitude > .35f ? tf.normalized : (up ? n : -n);
                        if (up && tf.magnitude < .7f) move = (move + n).normalized;
                    }
                    if (to.magnitude < .6f && Mathf.Abs(wp.y - f.pos.y) < .5f && f.onLadder == null) pathIx++;
                }
                else
                {
                    float dy = wp.y - f.pos.y;
                    var edgeDir = prev >= 0 ? Flat(wp - NavGrid.Nodes[prev].p) : to;
                    edgeDir = edgeDir.sqrMagnitude > 1e-4f ? edgeDir.normalized : Vector3.zero;
                    // dropping down: keep walking off the edge until you actually fall (the cell centre can still be over the ledge)
                    if (kind == EdgeKind.Drop && dy < -.6f && to.magnitude < .35f) move = edgeDir;
                    else move = to.magnitude > .01f ? to.normalized : edgeDir;
                    if (to.magnitude < .45f && Mathf.Abs(dy) < 1.0f) pathIx++;
                }
            }
            // once on a ladder, always finish the climb (whatever else is going on)
            if (f.onLadder != null && !wasOnLadder)
            {
                float goalY = path != null && path.Count > 0 ? NavGrid.Nodes[path[path.Count - 1]].p.y : f.pos.y;
                if (!(path != null && pathIx < path.Count && pathIx > 0 && NavGrid.EdgeBetween(path[pathIx - 1], path[pathIx], out _) == EdgeKind.Ladder))
                    ladderDir = goalY > f.pos.y + .5f ? 1 : -1;
            }
            wasOnLadder = f.onLadder != null;
            if (f.onLadder != null)
            {
                var L = f.onLadder;
                move = new Vector3(L.nx, 0, L.nz) * ladderDir;
            }
            // strafing / juking in a fight
            if (sees && Mode != "flank" && Mode != "hop" && Mode != "retreat" && f.onLadder == null)
            {
                var P = Style; var D = Dif;
                strafeT -= dt;
                if (strafeT <= 0) { strafeT = Random.Range(.35f, .9f) / Mathf.Max(.3f, P.strafe); strafeDir = Random.value < .7f ? -strafeDir : strafeDir; }
                var side = new Vector3(f.Aim.z, 0, -f.Aim.x).normalized * strafeDir;
                float s = Mathf.Clamp01(P.strafe * D.strafe);
                move = (move + side * s).normalized * Mathf.Max(move.magnitude, s);
                jukeT -= dt;
                if (jukeT <= 0) { jukeT = 1f; if (f.grounded && Random.value < P.jump * .6f) { jump = true; jumpHoldT = .2f; } }
            }
            // unstick: no progress while trying to move -> hop, then pick a new path
            if (move.sqrMagnitude > .01f && f.onLadder == null && f.HorizontalSpeed < .6f) stuckT += dt; else stuckT = Mathf.Max(0, stuckT - dt * 2);
            if (stuckT > .7f && f.grounded)
            {
                if (BoxWorld.Ray(f.Head, Vector3.up, 1.2f) < 0) { jump = true; jumpHoldT = .25f; }   // hop over it (unless there's a ceiling)
                else if (sideStepT <= 0) { sideStepT = .5f; sideDir = Random.value < .5f ? -1 : 1; }
            }
            if (sideStepT > 0) { sideStepT -= dt; move = (move + new Vector3(move.z, 0, -move.x) * sideDir * 1.5f).normalized; }
            if (stuckT > 2.2f) { stuckT = 0; path = null; goalNode = -1; }

            // jump input: hold a moment; press again near the top of the arc for a double jump
            if (jump) { f.inJumpPress = true; }
            jumpHoldT -= dt; f.inJump = jumpHoldT > 0;
            if (djPending && !f.grounded && f.vel.y < 2.5f) { f.inJumpPress = true; djPending = false; }
            if (f.grounded && jumpHoldT <= 0) djPending = false;

            // inputs are relative to where the bot faces
            var fw = f.Forward; var rt = f.Right;
            f.inFwd = Mathf.Clamp(Vector3.Dot(move, fw), -1, 1);
            f.inSide = Mathf.Clamp(Vector3.Dot(move, rt), -1, 1);
            f.ads = false;
            lastPos = f.pos;
        }

        // ------------------------------------------------------------------ aim and trigger
        void NewError()
        {
            var D = Dif; var P = Style;
            float e = D.err * P.err * Mathf.Deg2Rad;
            errYaw = Random.Range(-1f, 1f) * e; errPitch = Random.Range(-.6f, .6f) * e;
            if (Random.value < D.miss * .25f) errYaw += Mathf.Sign(errYaw == 0 ? 1 : errYaw) * Random.Range(2f, 4f) * Mathf.Deg2Rad;
            aimHead = Random.value < D.head;
            errT = Random.Range(.25f, .6f);
        }

        void Aim(float dt)
        {
            var D = Dif; var P = Style;
            float wantYaw, wantPitch;
            if (sees)
            {
                var tp = aimHead ? target.Head : Body(target);
                tp += target.vel * (D.lead * .12f);                                  // a little lead on a moving target
                var d = tp - Eye;
                wantYaw = Mathf.Atan2(d.x, d.z); wantPitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude);
                errScale = Mathf.Max(D.sway, errScale - dt * D.settle * .5f);       // first-sight error settles, sway never does
                errT -= dt; if (errT <= 0) NewError();
                wantYaw += errYaw * errScale; wantPitch += errPitch * errScale;
            }
            else
            {
                var mv = new Vector3(f.vel.x, 0, f.vel.z);
                if (lastSeen.HasValue && lostT < 3) { var d = lastSeen.Value + Vector3.up * 1.4f - Eye; wantYaw = Mathf.Atan2(d.x, d.z); wantPitch = 0; }
                else if (mv.magnitude > 1.5f) { wantYaw = Mathf.Atan2(mv.x, mv.z); wantPitch = 0; }
                else { wantYaw = aimYaw; wantPitch = 0; }
            }
            float turn = D.turn * dt;
            aimYaw = aimYaw + Mathf.Clamp(Mathf.DeltaAngle(aimYaw * Mathf.Rad2Deg, wantYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad, -turn, turn);
            aimPitch = Mathf.MoveTowards(aimPitch, Mathf.Clamp(wantPitch, -1.3f, 1.3f), turn);
            f.yaw = aimYaw; f.pitch = aimPitch;

            // trigger: after the reaction time, when lined up, in bursts with pauses
            bool fire = false;
            if (sees && !f.Frozen)
            {
                if (hesitate > 0) hesitate -= dt;
                float off = Mathf.Abs(Mathf.DeltaAngle(aimYaw * Mathf.Rad2Deg, wantYaw * Mathf.Rad2Deg) * Mathf.Deg2Rad);
                bool ready = seeT > D.react * P.react && hesitate <= 0 && off < D.tol + Mathf.Abs(errYaw * errScale);
                if (pauseT > 0) pauseT -= dt;
                else if (ready)
                {
                    triggerT += dt;
                    fire = (triggerT % (.2f * D.rof)) < .2f;                             // slower bots let go now and then
                    if (f.shots - (int)burstShots >= D.burst) { burstShots = f.shots; pauseT = Random.Range(D.pauseMin, D.pauseMax); }
                }
            }
            else burstShots = f.shots;
            float dist = Flat(target.pos - f.pos).magnitude;
            if (RumbleLadder.Swung(f.primary) && dist > 3f) fire = false;   // sword, and Rumble's knife and bat
            f.inFire = fire;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    }
}
