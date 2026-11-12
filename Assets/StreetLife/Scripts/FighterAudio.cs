// Footsteps, landings, the double-jump whoosh, ladder rungs and the low-health heartbeat, with the web game's sounds.
using UnityEngine;

namespace StreetLife
{
    /// Footsteps, landings, double-jump whoosh, ladder rungs and the low-health heartbeat for a fighter.
    [RequireComponent(typeof(Fighter))]
    public class FighterAudio : MonoBehaviour
    {
        Fighter f; float stepDist, ladderDist, heartT; Vector3 last; int lastStep = -1;
        void Awake()
        {
            f = GetComponent<Fighter>(); last = f.pos;
            f.onLand += v => Sfx.Play("land", f.pos, Mathf.Clamp((v - 7) / 14 + .35f, .35f, 1f), 8);
            f.onDoubleJump += () => { if (!f.isBot) Sfx.Play2D("swing", .5f); else Sfx.Play("swing", f.pos, .5f, 10); };
        }

        void Update()
        {
            var d = f.pos - last; last = f.pos;
            if (!f.alive) return;
            if (f.onLadder != null)
            {
                ladderDist += Mathf.Abs(d.y);
                if (ladderDist > .7f) { ladderDist = 0; Sfx.Play("step3", f.pos, .35f, 8, 1.3f); }
            }
            else if (f.grounded)
            {
                float h = new Vector2(d.x, d.z).magnitude;
                if (h < 1f) stepDist += h;
                float spd = f.HorizontalSpeed;
                if (stepDist > 2.4f && spd > 2f)
                {
                    stepDist = 0;
                    int k; do k = Random.Range(1, 6); while (k == lastStep);
                    lastStep = k;
                    float vol = Mathf.Clamp(spd / 14f, .25f, .7f) * (f.crouching ? .4f : 1f);
                    if (f.isBot) Sfx.Play("step" + k, f.pos, vol, 10, Random.Range(.94f, 1.06f));
                    else Sfx.Play2D("step" + k, vol * .55f, Random.Range(.94f, 1.06f));
                }
            }
            // heartbeat when you're low
            if (!f.isBot && f.hp < f.hpMax * .3f)
            {
                heartT -= Time.deltaTime;
                if (heartT <= 0) { heartT = f.hp < f.hpMax * .15f ? .75f : 1.05f; Sfx.Play2D("heart", .7f); }
            }
            else heartT = 0;
        }
    }
}
