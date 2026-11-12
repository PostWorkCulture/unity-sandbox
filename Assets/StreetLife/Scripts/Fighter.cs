// A fighter's body: movement, jumping, double jump, ladders, crouch, stamina and health regen.
// A line-by-line port of the web game's Duels physics (physics / physics1 / moveAxis / depen / ladderStep / tickStam),
// run at the same fixed 60 Hz step against the same collision boxes, so it moves exactly like the browser version.
using UnityEngine;

namespace StreetLife
{
    public partial class Fighter : MonoBehaviour
    {
        // ---- constants straight from the web game ----
        public const float R = .45f, HH = 2.25f, GRAV = 36f, JUMP = 12f, STEP = .55f, AIR_STEP = .3f, SC = .8f;
        public const float SIM = 1f / 60f;
        public const float DJ_COST = 15f, STAM_MAX = 100f, STAM_DRAIN = 20f, STAM_REGEN = 40f, STAM_DELAY = .5f, STAM_MIN = 25f;
        public const float MOVE_MUL = 1.125f, REGEN_DELAY = 3f, REGEN_RATE = 10f, LADDER_SPEED = 4.6f;

        [Header("Character")]
        public string characterName = "Wednesday";
        public float charSpeed = 1.1f;   // Wednesday: fastest runner
        public float charJump = 1f;
        public float hpMax = 100f;

        [Header("State (read only)")]
        public Vector3 pos, vel;
        public float yaw, pitch;          // yaw: 0 faces +z, increases turning right; pitch: up is positive
        public bool grounded, ads, crouchOn, crouching;
        public float cv;                  // crouch amount 0..1
        public float stam = STAM_MAX, hp = 100f, armor;
        public bool stamLock;
        public Ladder onLadder;
        public float lastHurt = -99f, simTime;

        // input for the next sim steps
        [System.NonSerialized] public float inFwd, inSide;
        [System.NonSerialized] public bool inJump, inJumpPress;

        int airJ; bool jumpLock; float stamRest;
        float acc;
        public Vector3 prevPos { get; private set; }
        public float Alpha => acc / SIM;                  // interpolation factor for visuals
        public Vector3 VisualPos => Vector3.Lerp(prevPos, pos, Alpha);
        public float HorizontalSpeed => new Vector2(vel.x, vel.z).magnitude;
        public float Hh => HH * (1 - .3f * cv);
        public System.Action<float> onLand;              // fall speed when landing hard
        public System.Action onJump, onDoubleJump;

        public void Place(Vector3 p, float yawRad)
        {
            pos = prevPos = p; vel = Vector3.zero; yaw = yawRad; pitch = 0; grounded = true; onLadder = null;
            stam = STAM_MAX; stamLock = false; hp = hpMax; cv = 0; crouchOn = false; acc = 0;
        }

        void Update()
        {
            if (!BoxWorld.Loaded || !alive) return;
            acc = Mathf.Min(acc + Time.deltaTime, SIM * 4);
            while (acc >= SIM - 1e-6f)
            {
                acc -= SIM; prevPos = pos;
                Step(SIM);
                inJumpPress = false;
            }
        }

        void Step(float dt)
        {
            simTime += dt;
            float fi = Frozen ? 0 : inFwd, si = Frozen ? 0 : inSide;
            if (Frozen) { inJump = false; inJumpPress = false; ads = false; }
            float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            float wx = s * fi + c * si, wz = c * fi - s * si;   // forward (sin, cos), right (cos, -sin)
            float wl = Mathf.Sqrt(wx * wx + wz * wz); if (wl > 1) { wx /= wl; wz /= wl; }
            if (onLadder != null) ads = false;
            UpdCrouch(crouchOn, dt);
            float spd = (ads ? 5.6f : 11.25f) * MOVE_MUL;
            Physics(dt, wx, wz, inJump, spd, inJumpPress);
            TickStam(dt);
            TickCombat(dt);
        }

        // ---------------------------------------------------------------- physics
        void Physics(float dt, float wx, float wz, bool jump, float speed, bool jumpPress)
        {
            if (stamLock) speed *= .75f;
            float v = Mathf.Max(HorizontalSpeed, speed * 1.25f * charSpeed) + Mathf.Abs(vel.y) * .5f;
            int n = Mathf.Min(5, Mathf.Max(1, Mathf.Max(Mathf.CeilToInt(dt * 60 - .05f), Mathf.CeilToInt(v * dt / .45f))));
            if (n == 1) Physics1(dt, wx, wz, jump, speed, jumpPress);
            else for (int i = 0; i < n; i++) Physics1(dt / n, wx, wz, jump, speed, i == 0 && jumpPress);
            var bd = BoxWorld.Bounds;
            pos.x = Mathf.Clamp(pos.x, bd.x0 + R, bd.x1 - R);
            pos.z = Mathf.Clamp(pos.z, bd.z0 + R, bd.z1 - R);
        }

        void Physics1(float dt, float wx, float wz, bool jump, float speed, bool jumpPress)
        {
            if (BoxWorld.Ladders.Count > 0 && LadderStep(dt, wx, wz, jump)) return;
            Depen();
            speed *= charSpeed * 1.2f;
            if (crouching) { speed *= .5f; jump = false; }
            float a = grounded ? 70f : 28f;
            float tx = wx * speed, tz = wz * speed, dvx = tx - vel.x, dvz = tz - vel.z, dl = Mathf.Sqrt(dvx * dvx + dvz * dvz), st = a * dt;
            if (dl <= st) { vel.x = tx; vel.z = tz; } else { vel.x += dvx / dl * st; vel.z += dvz / dl * st; }
            float jv = JUMP * charJump;
            if (grounded) airJ = 1;
            if ((jumpPress || jump) && crouchOn) crouchOn = false;
            if (jump && grounded && !jumpLock) { vel.y = jv; grounded = false; airJ = 1; jumpLock = true; onJump?.Invoke(); }
            else if (jumpPress && !grounded && airJ > 0 && stam >= DJ_COST) { vel.y = Mathf.Max(vel.y, jv * 1.05f); airJ--; stam -= DJ_COST; stamRest = 0; onDoubleJump?.Invoke(); }
            if (!jump) jumpLock = false;

            float vy0 = vel.y;
            if (!grounded) vel.y -= GRAV * dt * (vel.y < 0 ? 1.2f : 1f);
            if (!MoveAxis(vel.x * dt, 0)) vel.x = 0;
            if (!MoveAxis(0, vel.z * dt)) vel.z = 0;
            // average of start and end velocity: exact for constant gravity, so jump height is the same at any frame rate
            float ny = pos.y + (grounded ? vel.y : (vy0 + vel.y) / 2) * dt, sup = 0;
            var B = BoxWorld.Boxes; float r8 = R * .8f;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (b.x0 < pos.x + r8 && b.x1 > pos.x - r8 && b.z0 < pos.z + r8 && b.z1 > pos.z - r8 && b.y1 <= pos.y + (grounded ? STEP : .05f) + .001f && b.y1 > sup) sup = b.y1;
            }
            float H = Hh;
            if (vel.y > 0)
                for (int i = 0; i < B.Count; i++)
                {
                    var b = B[i];
                    if (b.x0 < pos.x + r8 && b.x1 > pos.x - r8 && b.z0 < pos.z + r8 && b.z1 > pos.z - r8 && b.y0 >= pos.y + H - .05f && b.y0 < ny + H) { ny = b.y0 - H; vel.y = 0; }
                }
            if (ny <= sup)
            {
                if (!grounded && vel.y < -7) onLand?.Invoke(-vel.y);
                ny = sup; vel.y = 0; grounded = true;
            }
            else if (grounded && ny > sup && pos.y - sup < .6f && vel.y <= 0) { ny = sup; vel.y = 0; }
            else grounded = false;
            pos.y = ny;
        }

        static bool Overlaps(in Box b, float x, float z, float y, float h)
            => b.x0 < x + R && b.x1 > x - R && b.z0 < z + R && b.z1 > z - R && b.y1 > y + .02f && b.y0 < y + h;

        bool MoveAxis(float dx, float dz)
        {
            float nx = pos.x + dx, nz = pos.z + dz, lift = 0, H = Hh;
            float allow = grounded ? STEP : vel.y < 6 ? AIR_STEP : 0;
            var B = BoxWorld.Boxes;
            for (int i = 0; i < B.Count; i++)
            {
                var b = B[i];
                if (!Overlaps(b, nx, nz, pos.y, H)) continue;
                float need = b.y1 - pos.y;
                if (need <= allow)
                {
                    bool blocked = false;
                    for (int j = 0; j < B.Count && !blocked; j++)
                    {
                        if (j == i) continue; var o = B[j];
                        if (Overlaps(o, nx, nz, pos.y + need, H) && o.y1 > pos.y + need + .02f && o.y0 >= pos.y + need + .02f && o.y0 < pos.y + need + H) blocked = true;
                    }
                    if (!blocked) { lift = Mathf.Max(lift, need); continue; }
                }
                if (Overlaps(b, pos.x, pos.z, pos.y, H) && b.y0 < pos.y + H * .5f) continue;
                return false;
            }
            var bd = BoxWorld.Bounds;
            pos.x = Mathf.Clamp(nx, bd.x0 + R, bd.x1 - R);
            pos.z = Mathf.Clamp(nz, bd.z0 + R, bd.z1 - R);
            pos.y += lift;
            return true;
        }

        void Depen()
        {
            float H = Hh; var B = BoxWorld.Boxes; var bd = BoxWorld.Bounds;
            var c = new Vector2[4];
            for (int it = 0; it < 4; it++)
            {
                bool moved = false;
                for (int i = 0; i < B.Count; i++)
                {
                    var b = B[i];
                    if (!(b.x0 < pos.x + R && b.x1 > pos.x - R && b.z0 < pos.z + R && b.z1 > pos.z - R && b.y1 > pos.y + .02f && b.y0 < pos.y + H)) continue;
                    if (b.y1 - pos.y <= STEP) continue;
                    if (b.y0 > pos.y + H * .6f) continue;
                    c[0] = new Vector2(b.x1 + R + .01f - pos.x, 0); c[1] = new Vector2(b.x0 - R - .01f - pos.x, 0);
                    c[2] = new Vector2(0, b.z1 + R + .01f - pos.z); c[3] = new Vector2(0, b.z0 - R - .01f - pos.z);
                    System.Array.Sort(c, (p, q) => Mathf.Abs(p.x + p.y).CompareTo(Mathf.Abs(q.x + q.y)));
                    foreach (var d in c)
                    {
                        float nx = pos.x + d.x, nz = pos.z + d.y;
                        if (nx < bd.x0 + R || nx > bd.x1 - R || nz < bd.z0 + R || nz > bd.z1 - R) continue;
                        pos.x = nx; pos.z = nz; if (d.x != 0) vel.x = 0; else vel.z = 0; moved = true; break;
                    }
                }
                if (!moved) break;
            }
        }

        bool LadderStep(float dt, float wx, float wz, bool jump)
        {
            foreach (var L in BoxWorld.Ladders)
            {
                if (pos.x < L.x0 - R || pos.x > L.x1 + R || pos.z < L.z0 - R || pos.z > L.z1 + R || pos.y < L.y0 - .15f || pos.y > L.y1 + .05f) continue;
                float into = wx * L.nx + wz * L.nz;
                if (onLadder != L)
                {
                    bool atTop = pos.y > L.y1 - .25f && into < -.2f;
                    if (!(into > .3f || (jump && into > -.2f) || atTop)) continue;
                    onLadder = L;
                }
                if (Mathf.Abs(wx * L.nz - wz * L.nx) > .75f && Mathf.Abs(into) < .3f) { onLadder = null; return false; }
                float vy = (into > .3f || jump) ? LADDER_SPEED : into < -.3f ? -LADDER_SPEED : 0;
                vel = Vector3.zero;
                // hug the ladder: ease in to stand just off the rungs, centred across them
                float cx = (L.x0 + L.x1) / 2, cz = (L.z0 + L.z1) / 2; bool ax = L.nx != 0;
                float tx = ax ? L.rung - L.nx * .42f : cx, tz = ax ? cz : L.rung - L.nz * .42f, k = Mathf.Min(1, dt * 10);
                pos.x += (tx - pos.x) * k; pos.z += (tz - pos.z) * k;
                grounded = false; pos.y += vy * dt;
                if (pos.y <= L.y0) { pos.y = L.y0; onLadder = null; grounded = true; }
                else if (pos.y >= L.y1) { pos.y = L.y1 + .02f; pos.x += L.nx; pos.z += L.nz; onLadder = null; grounded = true; }
                return true;
            }
            onLadder = null; return false;
        }

        bool CanStand()
        {
            foreach (var b in BoxWorld.Boxes)
                if (b.x0 < pos.x + R && b.x1 > pos.x - R && b.z0 < pos.z + R && b.z1 > pos.z - R && b.y0 >= pos.y + Hh - .05f && b.y0 < pos.y + HH) return false;
            return true;
        }

        void UpdCrouch(bool want, float dt)
        {
            if (!want && cv > 0 && !CanStand()) want = true;
            crouching = want; cv = Mathf.Clamp01(cv + (want ? 1 : -1) * dt * 7);
        }

        // ---------------------------------------------------------------- stamina and regen
        void TickStam(float dt)
        {
            if (Frozen) return;
            bool walking = HorizontalSpeed > 1.5f;
            if (walking || ads)
            {
                stam = Mathf.Max(0, stam - STAM_DRAIN * (ads ? .6f : .075f) * dt); stamRest = 0;
                if (stam <= 0) stamLock = true;
            }
            else
            {
                stamRest += dt;
                if (stamRest > STAM_DELAY) stam = Mathf.Min(STAM_MAX, stam + STAM_REGEN * dt);
                if (stam >= STAM_MIN) stamLock = false;
            }
            if (hp < hpMax && simTime - lastHurt > REGEN_DELAY) hp = Mathf.Min(hpMax, hp + REGEN_RATE * dt);
        }

        public Vector3 Head => new Vector3(pos.x, pos.y + 2.25f * SC * (1 - .27f * cv), pos.z);
        public Vector3 VisualHead { get { var p = VisualPos; return new Vector3(p.x, p.y + 2.25f * SC * (1 - .27f * cv), p.z); } }
        public Vector3 Forward => new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        public Vector3 Right => new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        public Vector3 Aim { get { float cp = Mathf.Cos(pitch); return new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(pitch), Mathf.Cos(yaw) * cp); } }
    }
}
