// Gives every character the web game's rim light (Shaders/CharacterRim.shader) as an extra additive material on each of
// its renderers. Rigs rebuild when someone picks a character, so it re-checks every second (cheap: a renderer count).
// Colour review (maps chat, Pete 14 Nov: "difficult to see the enemy over all the other bright items"): anyone on another
// team than you gets a warm red-orange rim instead of the cool one, a little wider, so enemies stand out from the scenery on
// every map; you and your teammates keep the cool rim. Rechecked each second, so mode and team changes follow.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class CharacterLook : MonoBehaviour
    {
        static Material rim, enemyRim;
        float t; int lastCount = -1; bool lastEnemy;
        CharacterRig rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { new GameObject("CharacterLook (finds rigs)").AddComponent<Finder>(); }

        class Finder : MonoBehaviour
        {
            float t;
            void Update()
            {
                if ((t -= Time.unscaledDeltaTime) > 0) return; t = 1f;
                foreach (var r in FindObjectsByType<CharacterRig>()) if (!r.GetComponent<CharacterLook>()) r.gameObject.AddComponent<CharacterLook>();
            }
        }

        static readonly List<Renderer> rs = new List<Renderer>();
        static readonly List<Material> ms = new List<Material>();

        bool IsEnemy()
        {
            if (!rig) rig = GetComponent<CharacterRig>();
            var f = rig ? rig.fighter : null; var rm = RoundManager.Instance; var me = rm ? rm.player : null;
            return f && me && f != me && f.team != me.team && !MainMenu.Open;
        }

        void LateUpdate()
        {
            if ((t -= Time.unscaledDeltaTime) > 0) return; t = 1f;
            if (!rim) rim = Resources.Load<Material>("StreetLife/CharacterRim");
            if (!rim) return;
            if (!enemyRim) { enemyRim = new Material(rim) { name = "CharacterRim enemy" }; enemyRim.SetColor("_RimColor", new Color(.85f, .26f, .12f, 1)); enemyRim.SetFloat("_Power", 3.2f); }
            bool enemy = IsEnemy();
            GetComponentsInChildren(true, rs);
            if (rs.Count == lastCount && enemy == lastEnemy) return;
            lastCount = rs.Count; lastEnemy = enemy;
            var want = enemy ? enemyRim : rim; var other = enemy ? rim : enemyRim;
            foreach (var r in rs)
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                r.GetSharedMaterials(ms);
                if (ms.Count == 0) continue;
                int i = ms.IndexOf(other);
                if (i >= 0) { ms[i] = want; r.SetSharedMaterials(ms); continue; }
                if (ms.Contains(want)) continue;
                ms.Add(want); r.SetSharedMaterials(ms);
            }
        }
    }
}
