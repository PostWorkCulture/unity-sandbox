// Practice dummies to try the weapons on until the bot arrives (phase 6): a few of the web game's characters stand
// at spawn points holding a random gun, turn to face you, take damage with the web game's hitboxes, regenerate,
// and come back somewhere else 3 s after going down. T makes them strafe side to side.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class PracticeRange : MonoBehaviour
    {
        public Armory armory;
        public MaterialLibrary library;
        public Fighter player;
        [Range(0, 8)] public int count = 3;
        public bool strafe;

        class Dummy { public Fighter f; public CharacterRig rig; public Arsenal arsenal; public float downT; public float strafePhase; }
        readonly List<Dummy> dummies = new List<Dummy>();
        public int Count => dummies.Count;

        void Start() { Shuffle(); }

        /// New characters and guns for every dummy, each at a fresh spawn point.
        public void Shuffle()
        {
            foreach (var d in dummies) if (d.f) Destroy(d.f.gameObject);
            dummies.Clear();
            if (!armory || armory.Catalog.characters == null) return;
            var ids = new List<string>(); foreach (var c in armory.Catalog.characters) if (player == null || c.id != player.characterId) ids.Add(c.id);
            var guns = armory.Slot("primary"); guns.AddRange(armory.Slot("secondary"));
            for (int i = 0; i < count && ids.Count > 0; i++)
            {
                int k = Random.Range(0, ids.Count); string id = ids[k]; ids.RemoveAt(k);
                var go = new GameObject("Dummy " + id);
                var f = go.AddComponent<Fighter>(); f.team = 1;
                var a = go.AddComponent<Arsenal>(); a.armory = armory; a.primary = guns.Count > 0 ? guns[Random.Range(0, guns.Count)] : "gar"; a.secondary = "smg"; a.lethal = "";
                var rig = go.AddComponent<CharacterRig>(); rig.fighter = f; rig.arsenal = a; rig.armory = armory; rig.library = library; rig.characterId = id; a.rig = rig;
                var d = new Dummy { f = f, rig = rig, arsenal = a, strafePhase = Random.value * 6 };
                f.onDown = _ => { d.downT = 3; d.rig.SetVisible(false); };
                dummies.Add(d);
                Place(d);
            }
        }

        void Place(Dummy d)
        {
            var sp = BoxWorld.Spawns;
            Vector3 p = Vector3.zero; float yaw = 0;
            if (sp.Count > 0)
            {
                // the spawn furthest from the player and the other dummies, with a little randomness
                float best = -1;
                for (int tries = 0; tries < 12; tries++)
                {
                    var s = sp[Random.Range(0, sp.Count)]; var q = new Vector3(s.x, s.y, s.z);
                    float score = player ? Mathf.Min(Vector3.Distance(q, player.pos), 40) : 0;
                    foreach (var o in dummies) if (o != d && o.f && o.f.alive) score = Mathf.Min(score, Vector3.Distance(q, o.f.pos) * 1.5f);
                    score += Random.value * 6;
                    if (score > best) { best = score; p = q; yaw = s.yaw; }
                }
            }
            d.f.Place(p, yaw);
            d.rig.SetVisible(true);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame && !Hud.PickerOpen) strafe = !strafe;
            foreach (var d in dummies)
            {
                if (!d.f) continue;
                if (!d.f.alive) { d.downT -= Time.deltaTime; if (d.downT <= 0) Place(d); continue; }
                // turn to face the player
                if (player)
                {
                    var to = player.pos - d.f.pos; to.y = 0;
                    if (to.sqrMagnitude > .01f) d.f.yaw = Mathf.LerpAngle(d.f.yaw * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 1 - Mathf.Exp(-3 * Time.deltaTime)) * Mathf.Deg2Rad;
                }
                d.f.inFwd = 0;
                d.f.inSide = strafe ? Mathf.Sign(Mathf.Sin(Time.time * 1.3f + d.strafePhase)) : 0;
            }
        }
    }
}
