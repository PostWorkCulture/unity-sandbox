// Gives every character the web game's rim light (Shaders/CharacterRim.shader) as an extra additive material on each of
// its renderers. Rigs rebuild when someone picks a character, so it re-checks every second (cheap: a renderer count).
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class CharacterLook : MonoBehaviour
    {
        static Material rim;
        float t; int lastCount = -1;

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

        void LateUpdate()
        {
            if ((t -= Time.unscaledDeltaTime) > 0) return; t = 1f;
            if (!rim) rim = Resources.Load<Material>("StreetLife/CharacterRim");
            if (!rim) return;
            GetComponentsInChildren(true, rs);
            if (rs.Count == lastCount) return;
            lastCount = rs.Count;
            foreach (var r in rs)
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                r.GetSharedMaterials(ms);
                if (ms.Count == 0 || ms.Contains(rim)) continue;
                ms.Add(rim); r.SetSharedMaterials(ms);
            }
        }
    }
}
