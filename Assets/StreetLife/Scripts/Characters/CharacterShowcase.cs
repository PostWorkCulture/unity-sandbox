// The main menu's Character and Loadout steps: show the character properly.
//   Character step: the gun / sword are hidden and the arms hang relaxed, so nothing covers the face.
//   Loadout step: the gun stays (it's what you're picking) and the character turns three-quarters, so it points past the camera.
//   Both: drag (mouse or finger) on the open part of the screen to turn the character round; it keeps a little spin when let go.
// Looks only: the fighter's own yaw, the camera and the weapons are untouched. Added by CharacterSkin at every scene load.
// Reads MainMenu's step and CharacterRig's rest pose by reflection (both private); if either is renamed it quietly does less.
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace StreetLife
{
    [DefaultExecutionOrder(200)]   // after CharacterRig (0) and WeaponPose (100), so the last word on the pose is ours
    public class CharacterShowcase : MonoBehaviour
    {
        /// how far the character turns on the Loadout step (degrees), so the gun points past the camera rather than into it
        public static float LoadoutTurn = -38f;
        /// degrees of turn per pixel dragged (at 1080 p)
        public static float DragSpeed = .45f;

        static readonly FieldInfo stepF = typeof(MainMenu).GetField("step", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo setStepM = typeof(MainMenu).GetMethod("SetStep", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo restF = typeof(CharacterRig).GetField("rest", BindingFlags.Instance | BindingFlags.NonPublic);
        static CharacterShowcase I;

        MainMenu menu; UIDocument doc;
        float spin, vel, offset; bool dragging; Vector2 last;
        CharacterRig rig; Quaternion lastBase, lastOut; bool applied;
        readonly List<Renderer> hidden = new List<Renderer>();
        HashSet<Material> charMats;

        void Awake() { I = this; }

        int Step()
        {
            if (!MainMenu.Open || stepF == null) return -1;
            if (!menu) { menu = FindAnyObjectByType<MainMenu>(); doc = menu ? menu.GetComponent<UIDocument>() : null; }
            return menu ? (int)stepF.GetValue(menu) : -1;
        }

        static CharacterRig PlayerRig() { foreach (var p in FindObjectsByType<PlayerController>()) return p.GetComponent<CharacterRig>(); return null; }

        void LateUpdate()
        {
            int step = Step();
            var r = PlayerRig();
            if (r != rig) { ShowWeapons(); rig = r; applied = false; }
            if (step < 1 || !rig || !rig.Built) { ShowWeapons(); spin = vel = offset = 0; dragging = false; applied = false; return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            Drag(dt);
            offset = Mathf.Lerp(offset, step == 2 ? LoadoutTurn : 0, 1 - Mathf.Exp(-dt * 6));

            // the rig sets its own rotation from the fighter's yaw every frame; ours goes on top
            var t = rig.transform;
            if (applied && t.rotation == lastOut) t.rotation = lastBase;
            lastBase = t.rotation;
            t.rotation = Quaternion.Euler(0, spin + offset, 0) * lastBase;
            lastOut = t.rotation; applied = true;

            if (step == 1) { HideWeapons(); RelaxArms(); } else ShowWeapons();
        }

        // ---- drag to turn
        void Drag(float dt)
        {
            var p = UnityEngine.InputSystem.Pointer.current;
            if (p == null) return;
            var pos = p.position.ReadValue();
            if (p.press.wasPressedThisFrame && Free(pos)) { dragging = true; last = pos; vel = 0; }
            if (dragging && p.press.isPressed)
            {
                float d = -(pos.x - last.x) * DragSpeed * 1080f / Mathf.Max(1, Screen.height);
                spin += d; vel = Mathf.Lerp(vel, d / Mathf.Max(dt, 1e-3f), .5f); last = pos;
            }
            else
            {
                if (dragging) dragging = false;
                spin += vel * dt; vel *= Mathf.Exp(-dt * 4); if (Mathf.Abs(vel) < 2) vel = 0;
            }
            spin = Mathf.Repeat(spin + 180, 360) - 180;
        }

        // is the pointer over open screen (the character), not a button, list or panel?
        bool Free(Vector2 screen)
        {
            var panel = doc && doc.rootVisualElement != null ? doc.rootVisualElement.panel : null;
            if (panel == null) return true;
            var e = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y)));
            for (; e != null; e = e.parent)
            {
                if (e is Button || e is ScrollView || e is Scroller || e is TextField || e is Slider) return false;
                if (e.resolvedStyle.backgroundColor.a > .3f && e.worldBound.width < panel.visualTree.worldBound.width * .9f) return false;   // a card
            }
            return true;
        }

        // ---- the Character step: no gun, arms down
        void HideWeapons()
        {
            if (charMats == null) { var set = CharacterSet.Load(); charMats = new HashSet<Material>(set && set.materials != null ? set.materials : new Material[0]); }
            foreach (var rd in rig.GetComponentsInChildren<Renderer>(false))
            {
                if (!rd.enabled || rd.sharedMaterial == null || charMats.Contains(rd.sharedMaterial)) continue;
                rd.enabled = false; hidden.Add(rd);
            }
        }

        void ShowWeapons()
        {
            bool vis = !rig || rig.Visible;
            foreach (var rd in hidden) if (rd) rd.enabled = vis;
            hidden.Clear();
        }

        void RelaxArms()
        {
            var rest = restF != null ? restF.GetValue(rig) as Dictionary<Transform, Quaternion> : null;
            if (rest == null) return;
            float b = Mathf.Sin(Time.unscaledTime * 1.6f) * 1.2f;   // a little breathing
            foreach (var kv in rest)
            {
                var n = kv.Key ? kv.Key.name : null;
                if (n == "aL.p") kv.Key.localRotation = kv.Value * Quaternion.Euler(b, 0, -6);
                else if (n == "aR.p") kv.Key.localRotation = kv.Value * Quaternion.Euler(-b, 0, 6);
                else if (n == "aL.e" || n == "aR.e") kv.Key.localRotation = kv.Value * Quaternion.Euler(10, 0, 0);
            }
        }

        readonly System.Text.StringBuilder watchLog = new System.Text.StringBuilder();
        System.Collections.IEnumerator Watch()
        {
            var tally = new Dictionary<string, int[]>();   // name -> [samples, held, alive-and-not-holding (unexplained)]
            var why = new Dictionary<string, string>();
            for (float t = 0; t < 20; t += .25f)
            {
                foreach (var f in FindObjectsByType<Fighter>())
                {
                    if (!f.isBot) continue;
                    var wp = f.GetComponent<WeaponPose>(); var w = f.GetComponent<Weapons>(); var rg = f.GetComponent<CharacterRig>();
                    string k = (CharacterSkin.Of(rg) ?? f.displayName);
                    if (!tally.TryGetValue(k, out var c)) tally[k] = c = new int[3];
                    c[0]++; bool held = wp && wp.HeldVisible;
                    if (held) c[1]++;
                    else if (f.alive && !f.downed && !f.InFinisher && f.onLadder == null && w && w.Gun != "sword") { c[2]++; why[k] = $"gun {w.Gun} pose {(wp ? wp.enabled.ToString() : "none")} rig {(rg && rg.Built)} vis {(rg && rg.Visible)}"; }
                }
                yield return new WaitForSeconds(.25f);
            }
            foreach (var kv in tally) watchLog.Append($"[{kv.Key}: {kv.Value[0]} samples, holding {kv.Value[1]}, unexplained empty {kv.Value[2]}{(why.TryGetValue(kv.Key, out var y) ? " last: " + y : "")}] ");
        }

        /// remote test hook (Logs/streetlife-cmd.txt: "call CharacterShowcase.Test step 1" / "char lia" / "spin 90")
        public static string Test(string arg)
        {
            var a = (arg ?? "").Trim().Split(' ');
            var m = FindAnyObjectByType<MainMenu>();
            if (a.Length < 2) return "usage: step <n> | char <id> | spin <deg>";
            switch (a[0])
            {
                case "step": if (m && setStepM != null) { setStepM.Invoke(m, new object[] { int.Parse(a[1]) }); return "step " + a[1]; } return "no menu";
                case "char": { var rg = PlayerRig(); CharacterSkin.PlayerChoice = a[1]; if (rg) CharacterSkin.Apply(rg, a[1]); return "char " + a[1]; }
                case "bots":
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var f in FindObjectsByType<Fighter>())
                        {
                            var w = f.GetComponent<Weapons>(); var wp = f.GetComponent<WeaponPose>(); var rg = f.GetComponent<CharacterRig>();
                            sb.Append($"[{f.displayName} bot:{f.isBot} W:{(w ? w.Gun : "none")} pose:{(wp ? (wp.enabled ? "on" : "off") : "none")} held:{(wp && wp.HeldVisible)} rigVis:{(rg && rg.Visible)} skin:{CharacterSkin.Of(rg)}] ");
                        }
                        return sb.ToString();
                    }
                case "botcam":
                    {   // a picture of every fighter from 3 m in front, three-quarters on, to Logs/botcam_<name>.png
                        var go = new GameObject("botcam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 40; cam.nearClipPlane = .05f;
                        var rt = new RenderTexture(640, 640, 24); cam.targetTexture = rt; var sb = new System.Text.StringBuilder();
                        float side = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 35f;
                        foreach (var f in FindObjectsByType<Fighter>())
                        {
                            var fwd = Quaternion.Euler(0, f.yaw * Mathf.Rad2Deg + side, 0) * Vector3.forward;
                            var c = f.pos + Vector3.up * 1.15f; go.transform.position = c + fwd * 3.2f + Vector3.up * .3f; go.transform.LookAt(c);
                            cam.Render(); RenderTexture.active = rt; var tex = new Texture2D(640, 640, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); tex.Apply(); RenderTexture.active = null;
                            var path = System.IO.Path.Combine(Application.dataPath, "../Logs/botcam_" + (f.isBot ? (CharacterSkin.Of(f.GetComponent<CharacterRig>()) ?? f.displayName) : "you") + ".png");
                            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex); sb.Append(path + " ");
                        }
                        cam.targetTexture = null; Destroy(rt); Destroy(go); return sb.ToString();
                    }
                case "watch":   // sample every fighter 4x a second for 20 s; "watch report" prints the tally
                    if (a[1] == "report") return I ? I.watchLog.ToString() : "no showcase";
                    if (I) { I.watchLog.Clear(); I.StartCoroutine(I.Watch()); return "watching 20 s"; }
                    return "no showcase";
                case "spin": if (I) { I.spin = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture); I.vel = 0; return "spin " + a[1]; } return "no showcase";
            }
            return "unknown " + a[0];
        }
    }
}
