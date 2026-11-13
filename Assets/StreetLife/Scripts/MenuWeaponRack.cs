// A small weapons rack on the floor beside your fighter on the main menu's Character step, holding the loadout you've
// picked: primary on the top bar, secondary on the lower one, the lethal on the base. Random slots are left empty.
using System.Collections.Generic;
using UnityEngine;

namespace StreetLife
{
    public class MenuWeaponRack : MonoBehaviour
    {
        static MenuWeaponRack I;
        Transform root, guns; string shownKey; Material wood;

        /// show the rack next to the fighter, on the side away from the menu panel, facing the menu camera's spot
        public static void Show(Fighter f, string pri, string sec, string leth)
        {
            if (!f || !GunsDb.Loaded) { Hide(); return; }
            if (!I) I = new GameObject("Menu weapon rack").AddComponent<MenuWeaponRack>();
            I.Place(f, pri, sec, leth);
        }

        public static void Hide() { if (I && I.root) I.root.gameObject.SetActive(false); }

        void Place(Fighter f, string pri, string sec, string leth)
        {
            if (!root) BuildFrame();
            root.gameObject.SetActive(true);
            // where MainMenu's character-step camera sits (see its LateUpdate), so the rack stays put while the camera eases in
            var fwd = new Vector3(Mathf.Sin(f.yaw), 0, Mathf.Cos(f.yaw));
            var perp = new Vector3(fwd.z, 0, -fwd.x);
            var toCam = (fwd * 4.2f - perp * 1.3f); toCam.y = 0; toCam.Normalize();
            var right = Vector3.Cross(Vector3.up, -toCam).normalized;   // screen right as the camera sees it
            root.position = f.pos + right * 1.25f - toCam * .1f;   // a clear gap between the fighter and the rack
            root.rotation = Quaternion.LookRotation(toCam);
            var k = pri + "|" + sec + "|" + leth;
            if (k == shownKey) return;
            shownKey = k;
            if (guns) Destroy(guns.gameObject);
            guns = new GameObject("weapons").transform; guns.SetParent(root, false);
            Hang(pri, .82f, .95f);
            Hang(sec, .48f, .75f);
            Hang(leth, .06f, .16f);
        }

        void BuildFrame()
        {
            root = new GameObject("rack").transform; root.SetParent(transform, false);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            wood = new Material(sh) { name = "rack" }; wood.SetColor("_BaseColor", new Color(.07f, .07f, .075f)); if (wood.HasProperty("_Smoothness")) wood.SetFloat("_Smoothness", .35f);
            Block(new Vector3(0, .03f, 0), new Vector3(1f, .06f, .34f));          // base
            Block(new Vector3(-.46f, .5f, -.04f), new Vector3(.05f, 1f, .05f));   // uprights
            Block(new Vector3(.46f, .5f, -.04f), new Vector3(.05f, 1f, .05f));
            Block(new Vector3(0, .8f, -.04f), new Vector3(.97f, .035f, .035f));   // bars
            Block(new Vector3(0, .46f, -.04f), new Vector3(.97f, .035f, .035f));
        }

        void Block(Vector3 at, Vector3 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); var col = g.GetComponent<Collider>(); if (col) Destroy(col);
            g.transform.SetParent(root, false); g.transform.localPosition = at; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = wood;
        }

        /// build the weapon side-on, sized to `length` along the rack, resting at height y just in front of the bars
        void Hang(string id, float y, float length)
        {
            if (string.IsNullOrEmpty(id) || id == "random") return;
            WNode[] nodes = null;
            if (id == "sword") nodes = GunsDb.Data.sword != null ? GunsDb.Data.sword.nodes : null;
            else if (GunsDb.Held(id) != null) nodes = GunsDb.Held(id).nodes;
            else if (GunsDb.Proj(id) != null) nodes = GunsDb.Proj(id).nodes;
            if (nodes == null) return;
            var slot = new GameObject(id).transform; slot.SetParent(guns, false);
            var holder = new GameObject("model").transform; holder.SetParent(slot, false);
            var rends = new List<Renderer>();
            WeaponModels.Build(nodes, holder, id, -1, true, rends);
            var fl = WeaponModels.Find(holder, "flame"); if (fl) fl.gameObject.SetActive(false);
            // measure in the slot's own space
            slot.position = Vector3.zero; slot.rotation = Quaternion.identity;
            var bb = Measure(rends); if (bb.size == Vector3.zero) { Destroy(slot.gameObject); return; }
            if (bb.size.z > bb.size.x) { holder.localRotation = Quaternion.Euler(0, -90, 0); bb = Measure(rends); }
            float s = length / Mathf.Max(.01f, Mathf.Max(bb.size.x, bb.size.y));
            holder.localScale *= s; bb = Measure(rends);
            holder.localPosition -= new Vector3(bb.center.x, bb.min.y, bb.center.z);
            slot.SetParent(guns, false); slot.localPosition = new Vector3(0, y, .07f); slot.localRotation = Quaternion.identity;
        }

        static Bounds Measure(List<Renderer> rends)
        {
            var bb = new Bounds(); bool any = false;
            foreach (var r in rends) if (r && r.gameObject.activeInHierarchy) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            return any ? bb : new Bounds();
        }

        void OnDestroy() { if (wood) Destroy(wood); if (I == this) I = null; }
    }
}
