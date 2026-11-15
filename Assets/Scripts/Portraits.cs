using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonSiege
{
    // Renders hero portraits and weapon icons into textures once at start-up (a hidden photo studio
    // below the map), so the UI can show real 3D art instead of text-only cards.
    public static class Portraits
    {
        public static readonly Dictionary<Role, Texture2D> hero = new Dictionary<Role, Texture2D>();
        public static readonly Dictionary<Role, Texture2D> gun = new Dictionary<Role, Texture2D>();
        public static readonly Dictionary<string, Texture2D> gunById = new Dictionary<string, Texture2D>();   // V22：按枪 id（击杀播报 / 死亡回顾画实际用的枪，含免费手枪）
        public static bool Ready;

        public static System.Collections.IEnumerator Build()
        {
            var studio = new GameObject("Portrait Studio");
            studio.transform.position = new Vector3(0, -300, 0);
            var camGo = new GameObject("Portrait Camera"); camGo.transform.SetParent(studio.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); cam.fieldOfView = 26; cam.nearClipPlane = .05f; cam.farClipPlane = 20; cam.allowHDR = false;
            cam.renderingPath = RenderingPath.Forward;
            var key = new GameObject("key").AddComponent<Light>(); key.transform.SetParent(studio.transform, false);
            key.type = LightType.Directional; key.intensity = 1.2f; key.transform.rotation = Quaternion.Euler(20, 150, 0); key.color = new Color(1f, .9f, .85f);
            var rim = new GameObject("rim").AddComponent<Light>(); rim.transform.SetParent(studio.transform, false);
            rim.type = LightType.Directional; rim.intensity = 1.6f; rim.transform.rotation = Quaternion.Euler(10, -30, 0);
            var ambient = RenderSettings.ambientLight;
            foreach (var h in Config.Heroes)
            {
                rim.color = h.tint;
                bool toon = ToonAssets.Available;
                bool fps = FPSAssets.Available && !toon;
                bool isG = !fps && !string.IsNullOrEmpty(h.gltf) && Resources.Load<GameObject>("Models/Chars2/" + h.gltf) != null;
                var m = toon ? Arena.Model("Toon/Chars/" + ToonAssets.CharFor(h.role)) : fps ? FPSAssets.Spawn("Char/Terraformer_A", new Color(.85f, .88f, .95f), h.tint * 1.4f, h.tint) : isG ? Arena.Model("Chars2/" + h.gltf) : Arena.Model("Characters/" + h.model);
                if (m == null) continue;
                Arena.Fit(m, new Vector3(0, toon && ToonAssets.BeanAvailable ? 1.5f : 1.78f, 0));   // 豆子角色戴帽子，留出帽子的高度
                m.transform.SetParent(studio.transform, false);
                m.transform.localRotation = Quaternion.Euler(0, 160, 0);
                var anim = m.GetComponentInChildren<Animator>();
                ClipPlayer cp = null;
                if (toon && m != null)
                {
                    foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                        if (ToonAssets.IsGunNode(r.name))
                        {
                            r.enabled = false;
                            if (r.name != ToonAssets.GunFor(h.weapon)) continue;
                            var dg = DessertGuns.Create(h.weapon, new GunParts(), false); dg.SetParent(r.transform, false); dg.localRotation = Quaternion.Euler(0, 90, 0);
                            Vector3 ls = r.transform.lossyScale; dg.localScale = new Vector3(1.6f / ls.x, 1.6f / ls.y, 1.6f / ls.z);
                        }
                    ToonAssets.Recolor(m, Color.Lerp(h.tint, Color.white, .25f), h.tint);
                    if (ToonAssets.BeanAvailable) ToonAssets.AddHat(m, h.role);
                }
                if (toon && anim != null) { cp = new ClipPlayer(anim, ClipPlayer.Load("Toon/Chars/" + ToonAssets.CharFor(h.role))); cp.Pose("Idle", .4f); }
                else if (fps && anim != null) { cp = new ClipPlayer(anim, FPSAssets.Clips("Anim3P", "Stand--Idle")); cp.Pose("Idle", .5f); }
                else if (isG && anim != null) { cp = new ClipPlayer(anim, ClipPlayer.Load("Models/Chars2/" + h.gltf)); cp.Pose("Idle_Gun_Pointing", .3f); }
                else if (anim != null) { anim.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim/Fighter"); anim.Update(.3f); }
                if (isG) foreach (var r in m.GetComponentsInChildren<Renderer>()) foreach (var mat in r.materials)
                        if (mat.HasProperty("baseColorFactor")) { Color bc = mat.GetColor("baseColorFactor"); Color.RGBToHSV(bc, out float hh, out float ss, out float vv); if (ss < .35f && vv > .25f) mat.SetColor("baseColorFactor", Color.Lerp(bc, h.tint, .45f)); }
                camGo.transform.localPosition = new Vector3(0, 1.45f, 2.6f); camGo.transform.LookAt(m.transform.position + Vector3.up * 1.35f);
                yield return null;
                hero[h.role] = Snap(cam, 256, 320);
                if (cp != null) cp.Destroy();
                Object.Destroy(m);

                Transform g;
                if (toon) g = GunModel.Create(h.weapon, out _, h.tint, false);
                else if (fps) { var go = FPSAssets.Spawn(FPSAssets.WeaponModel(h.weapon, false)); g = go.transform; var wa = go.GetComponent<Animator>(); if (wa) wa.enabled = false; }
                else g = GunModel.Create(h.weapon, out _, h.tint, false);
                g.SetParent(studio.transform, false); g.localPosition = new Vector3(0, 1, 0); g.localRotation = Quaternion.Euler(0, 90, 0);
                rim.color = Color.white;
                camGo.transform.localPosition = new Vector3(0, 1.02f, -2.6f); camGo.transform.LookAt(g.position + Vector3.forward * .25f);
                cam.fieldOfView = 20;
                yield return null;
                gun[h.role] = Snap(cam, 320, 120); gunById[h.weapon.id] = gun[h.role];
                Object.Destroy(g.gameObject);
                cam.fieldOfView = 26;
            }
            if (!gunById.ContainsKey(Config.Pistol.id))
            {
                bool fpsP = FPSAssets.Available && !ToonAssets.Available;
                Transform pg = null;
                if (fpsP) { var go = FPSAssets.Spawn(FPSAssets.WeaponModel(Config.Pistol, false)); if (go != null) { pg = go.transform; var wa = go.GetComponent<Animator>(); if (wa) wa.enabled = false; } }
                else pg = GunModel.Create(Config.Pistol, out _, Color.white, false);
                if (pg != null)
                {
                    pg.SetParent(studio.transform, false); pg.localPosition = new Vector3(0, 1, 0); pg.localRotation = Quaternion.Euler(0, 90, 0);
                    rim.color = Color.white; cam.fieldOfView = 16;
                    camGo.transform.localPosition = new Vector3(0, 1.02f, -2.6f); camGo.transform.LookAt(pg.position + Vector3.forward * .15f);
                    yield return null;
                    gunById[Config.Pistol.id] = Snap(cam, 320, 120);
                    Object.Destroy(pg.gameObject);
                }
            }
            Object.Destroy(studio);
            Ready = true;
        }

        static Texture2D Snap(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }

    // X-ray silhouettes: revealed enemies (red) and teammates (faint team colour) drawn through walls.
    [RequireComponent(typeof(Camera))]
    public class XRayOverlay : MonoBehaviour
    {
        CommandBuffer cb; Material enemy, ally, rim; Camera cam;
        void OnEnable()
        {
            cam = GetComponent<Camera>();
            cb = new CommandBuffer { name = "XRay" };
            cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb);
            var baseMat = Resources.Load<Material>("Materials/XRay");
            if (baseMat == null) return;
            enemy = new Material(baseMat); enemy.SetColor("_Color", new Color(1f, .15f, .2f) * 1.5f);
            ally = new Material(baseMat); ally.SetColor("_Color", Config.Blue * .45f);
            var rimBase = Resources.Load<Material>("Materials/Rim");
            if (rimBase != null) { rim = new Material(rimBase); rim.SetColor("_Color", new Color(1f, .18f, .2f) * 1.4f); }
        }
        void OnDisable() { if (cam != null && cb != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cb); }
        void LateUpdate()
        {
            if (cb == null || enemy == null || Match.I == null) return;
            cb.Clear();
            var p = Match.I.player; if (p == null) return;
            bool jammed = p.alive && Time.time < p.jammedUntil;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || f == p || f.avatar == null) continue;
                bool visible = Skills.LineClear(transform.position, f.Chest, f) || Skills.LineClear(transform.position, f.Head, f);
                Material m;
                if (visible)
                {
                    // V4：视线内的敌人加红色描边高亮（试玩反馈：敌人看不清）
                    if (f.team == p.team || rim == null || Vector3.Distance(transform.position, f.Chest) > 160f) continue;
                    rim.SetColor("_Color", new Color(1f, .16f, .18f) * (f.Revealed ? 2.2f : 1.5f) * GameSettings.EnemyOutline);
                    m = rim;
                }
                else m = f.team == p.team ? ally : (!jammed && f.Revealed ? enemy : null);
                if (m == null) continue;
                if (f.team != p.team && m == enemy) m.SetColor("_Color", new Color(1f, .12f, .16f) * (1.6f + .6f * Mathf.Sin(Time.time * 8)));
                foreach (var r in f.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                    for (int i = 0; i < r.sharedMesh.subMeshCount; i++) cb.DrawRenderer(r, m, i);
            }
        }
    }
}
