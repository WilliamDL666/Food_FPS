using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // Unity FPSSample assets (Unity Companion License): Terraformer soldier, first-person arms, rifle/sniper
    // and their animations. Materials were rebuilt for the Built-in pipeline by Editor/FPSImport.cs.
    public static class FPSAssets
    {
        static bool? available;
        public static bool Available { get { if (available == null) available = Resources.Load<GameObject>("FPS/Char/Terraformer_A") != null; return available.Value; } }

        // FBX material slot → rebuilt material in Resources/FPS/Mats ("-" = hidden helper layer)
        static readonly Dictionary<string, string> Slots = new Dictionary<string, string> {
            { "Female_Terraformer_A_Backpack_Glass", "Terraformer_A_Backpack_Glass" }, { "Female_Terraformer_A_Backpack", "Terraformer_A_Backpack" },
            { "Female_Terraformer_A_Suit", "Terraformer_A_Suit" }, { "Female_Terraformer_A_Eye", "Terraformer_A_Eyes" },
            { "Female_Terraformer_A_Head", "Terraformer_A_Skin" }, { "Female_Terraformer_A_Teeth", "Terraformer_A_Teeth" },
            { "Female_Terraformer_A_Helmet", "Terraformer_A_Helmet" }, { "Female_Terraformer_A_Helmet_Glass", "Terraformer_A_Helmet_Glass" },
            { "Female_Terraformer_A_Hair", "Terraformer_A_Hair" }, { "FirstPerson_Female_Terraformer_A_Suit", "Terraformer_A_1P" },
            { "Terraformer_Weapon_A_BaseColor_A", "Terraformer_Weapon_A_FirstPerson" }, { "Terraformer_Weapon_A_Glass_A_BaseColor_A", "Terraformer_Weapon_A_Glass_1P" },
            { "Terraformer_Weapon_A_Liquids_A_BaseColor_A", "Terraformer_Weapon_A_Liquids_1P" }, { "Terraformer_Weapon_A_Container_A_BaseColor_A", "Terraformer_Weapon_A_Container_1P" },
            { "Terraformer_Weapon_A_Material", "Terraformer_Weapon_A" }, { "Terraformer_Weapon_Glass_Material", "Terraformer_Weapon_A_Glass" },
            { "Terraformer_Weapon_Liquid_Material", "Terraformer_Weapon_A_Liquids" }, { "Terraformer_Weapon_Container_Material", "Terraformer_Weapon_A_Container" },
            { "clash_Terraformer_Weapon_Glass_Material", "Terraformer_Weapon_A_Glass" }, { "clash_Terraformer_Weapon_Liquid_Material", "Terraformer_Weapon_A_Liquids" },
            { "Terraformer_Sniper_Weapon_Container_Material", "Terraformer_Sniper_A_Container" }, { "Terraformer_Sniper_Weapon_A_Material", "Terraformer_Weapon_A" },
            { "Terraformer_Weapon_A1", "Terraformer_Weapon_A" }, { "Terraformer_Sniper_Weapon_ScopeBarrel_A_Material", "Terraformer_Sniper_A_BarrelScope" },
        };
        static Material hidden;
        static Material Hidden
        {
            get
            {
                if (hidden == null) { hidden = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")); hidden.color = new Color(0, 0, 0, 0); }
                return hidden;
            }
        }

        // Instantiate a model and swap every material for its Built-in rebuild.
        // suit: multiplies the armour colour; glow: emissive tint (team colour); visor: helmet glass colour (hero colour).
        public static GameObject Spawn(string path, Color? suit = null, Color? glow = null, Color? visor = null)
        {
            var prefab = Resources.Load<GameObject>("FPS/" + path);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string slot = mats[i].name.Replace(" (Instance)", "");
                    if (slot.ToLower().Contains("beeswax")) { mats[i] = Hidden; continue; }
                    if (!Slots.TryGetValue(slot, out var name)) continue;
                    var m = Resources.Load<Material>("FPS/Mats/" + name);
                    if (m == null) continue;
                    if (suit != null || glow != null || visor != null)
                    {
                        m = new Material(m);
                        if (suit != null && (name.Contains("Suit") || name.Contains("Helmet") && !name.Contains("Glass") || name.Contains("Backpack") && !name.Contains("Glass") || name.EndsWith("_1P")))
                            m.color = m.color * suit.Value;
                        if (glow != null && m.IsKeywordEnabled("_EMISSION")) m.SetColor("_EmissionColor", glow.Value);
                        if (visor != null && name.Contains("Helmet_Glass")) { var v = visor.Value; v.a = .55f; m.color = v; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", visor.Value * .35f); }
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            return go;
        }

        public static string WeaponModel(WeaponSpec w, bool firstPerson)
        {
            bool sniper = w.sight == "scope" && w.adsZoom >= 3f;
            return "Weapon/Terraformer_Weapon_A_" + (firstPerson ? "1P" : "3P") + (sniper ? "--Sniper" : "");
        }

        // Clips from specific .anim.fbx files (Resources path = file name without ".fbx").
        public static AnimationClip[] Clips(string folder, params string[] files)
        {
            var list = new List<AnimationClip>();
            foreach (var f in files)
                foreach (var c in Resources.LoadAll<AnimationClip>("FPS/" + folder + "/" + f + ".anim"))
                    if (!c.name.StartsWith("__")) list.Add(c);
            return list.ToArray();
        }

        public static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // Seat the weapon in the right hand: barrel pointing from the right hand toward the left hand
        // (sampled in the idle pose), pistol grip in the palm. The offset is then kept fixed so the gun
        // follows the right hand through reloads, melee and sprint animations.
        public static void Grip(Transform gun, Transform rightHand, Transform leftHand, Transform rigRoot, bool alongView = false)
        {
            // first person: barrel parallel to the view (classic lower-right hold); third person: toward the left hand
            Vector3 dir = alongView ? rigRoot.forward : (leftHand.position - rightHand.position).normalized;
            Quaternion rot = Quaternion.LookRotation(dir, rigRoot.up);
            Vector3 palm = rightHand.position - rightHand.up * .07f;     // fingers run along the hand's -Y
            Vector3 grip = new Vector3(0, -.045f, .03f);                 // pistol grip, just under the trigger
            gun.SetParent(rightHand, true);
            gun.rotation = rot;
            gun.position = palm - rot * Vector3.Scale(grip, gun.lossyScale);
        }
    }

    // First-person arms + weapon driven by the FPSSample first-person clips.
    public class FPSViewModel : MonoBehaviour
    {
        public Fighter owner;
        public Transform gun, muzzle, slide, magazine, rightHand, leftHand;
        int seatFrames;
        ClipPlayer arms, weapon;
        float lastShot, reloadStart = -10, landUntil, hitUntil; bool wasGrounded = true, wasReloading;
        static readonly Dictionary<string, string> WeaponName = new Dictionary<string, string> {
            { "Idle", "Idle" }, { "Run", "Run" }, { "Sprint", "Sprint_Weapon" }, { "ScopeAim", "ScopeAim_Weapon" }, { "Reload", "Reload" },
            { "Melee", "Melee" }, { "Grenade", "Grenade" }, { "InAir", "InAir" }, { "LandToStand", "LandToStand" }, { "HitReaction", "HitReaction_Weapon" }, { "Shoot_Instant", "Shoot" } };

        public static FPSViewModel Create(Fighter f, Transform parent)
        {
            var rig = FPSAssets.Spawn("Char/Terraformer_A--FirstPerson", f.team == 0 ? new Color(.45f, .58f, 1f) : new Color(1f, .42f, .36f), Config.TeamColor(f.team) * 1.4f);
            var g = FPSAssets.Spawn(FPSAssets.WeaponModel(f.W, true));
            if (rig == null || g == null) { if (rig) Destroy(rig); if (g) Destroy(g); return null; }
            rig.name = "第一人称手臂（FPSSample）";
            rig.transform.SetParent(parent, false); rig.transform.localPosition = Vector3.zero; rig.transform.localRotation = Quaternion.identity;
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            var vm = rig.AddComponent<FPSViewModel>();
            vm.owner = f; vm.gun = g.transform;
            var animA = rig.GetComponent<Animator>(); var animW = g.GetComponent<Animator>();
            animA.applyRootMotion = false; animA.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animW.applyRootMotion = false; animW.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            vm.arms = new ClipPlayer(animA, FPSAssets.Clips("Anim1P", "Firstperson--Idle", "Firstperson--Run", "Firstperson--Sprint", "Firstperson--ScopeAim",
                "Firstperson--Reload", "Firstperson--Melee", "Firstperson--Grenade", "Firstperson--InAir", "Firstperson--LandToStand", "Firstperson--HitReaction", "Firstperson--Shoot"),
                "Reload", "Melee", "Grenade", "LandToStand", "HitReaction", "Shoot_Instant");
            vm.weapon = new ClipPlayer(animW, FPSAssets.Clips("Anim1P", "Firstperson--Idle_Weapon", "Firstperson--Run_Weapon", "Firstperson--Sprint_Weapon", "Firstperson--ScopeAim_Weapon",
                "Firstperson--Reload_Weapon", "Firstperson--Melee_Weapon", "Firstperson--Grenade_Weapon", "Firstperson--InAir_Weapon", "Firstperson--LandToStand_Weapon", "Firstperson--HitReaction_Weapon", "Firstperson--Shoot_Weapon"),
                "Reload", "Melee", "Grenade", "LandToStand", "HitReaction_Weapon", "Shoot");
            // the gun is seated after the first animated frame (bones are still in bind pose here)
            g.transform.SetParent(rig.transform, false);
            vm.rightHand = FPSAssets.Find(rig.transform, "Right_Hand"); vm.leftHand = FPSAssets.Find(rig.transform, "Left_Hand");
            vm.muzzle = FPSAssets.Find(g.transform, "Bullit_Spawn");
            vm.slide = FPSAssets.Find(g.transform, "Slide");
            vm.magazine = FPSAssets.Find(g.transform, "Magazine");
            vm.arms.Play("Idle", .01f); vm.weapon.Play("Idle", .01f);
            return vm;
        }

        void Play(string arm, float fade = .15f, float speed = 1f, bool restart = false)
        {
            arms.Play(arm, fade, speed, restart);
            if (WeaponName.TryGetValue(arm, out var w)) weapon.Play(w, fade, speed, restart);
        }

        public void OnShot() { lastShot = Time.time; if (!owner.W.automatic && !owner.aiming) Play("Shoot_Instant", .03f, 1.8f, true); }
        public void OnHit() { hitUntil = Time.time + .4f; }

        void Update()
        {
            if (owner == null) return;
            arms.Tick(Time.deltaTime); weapon.Tick(Time.deltaTime);
            var f = owner;
            float speed = new Vector2(f.velocity.x, f.velocity.z).magnitude;
            if (!wasGrounded && f.grounded) landUntil = Time.time + .45f;
            wasGrounded = f.grounded;
            if (f.IsReloading && !wasReloading) { reloadStart = Time.time; Play("Reload", .1f, 2f / Mathf.Max(.4f, f.W.reload), true); }
            wasReloading = f.IsReloading;
            if (f.IsReloading) return;                                                   // reload clip runs to the end
            if (Time.time < f.meleeUntil) { Play("Melee", .05f, 3.2f); return; }
            if (Time.time - lastShot < .25f && !f.W.automatic && !f.aiming && !arms.Finished("Shoot_Instant")) return;
            if (f.aiming) Play("ScopeAim", .12f);
            else if (!f.grounded && !f.Mantling) Play("InAir", .2f);
            else if (Time.time < landUntil) Play("LandToStand", .08f, 1.4f);
            else if (Time.time < hitUntil && speed < .5f) Play("HitReaction", .05f, 1.3f);
            else if (f.sprinting || f.Sliding || f.SprintRaiseK < 1f - .15f / Mathf.Max(.15f, f.W.sprintToFire)) Play("Sprint", .15f, Mathf.Clamp(speed / 6.8f, .6f, 1.3f));   // V22：冲刺转开火期间保持冲刺姿势，最后 0.15 s 交叉淡回持枪（总时长 = sprintToFire）
            else if (speed > .6f) Play("Run", .15f, Mathf.Clamp(speed / 4.2f, .5f, 1.4f));
            else Play("Idle", .2f);
        }

        void LateUpdate()
        {
            // wait two animated frames in the idle pose, then lock the gun to the right hand
            if (seatFrames < 0 || rightHand == null || leftHand == null) return;
            if (++seatFrames < 3) return;
            FPSAssets.Grip(gun, rightHand, leftHand, transform, true);
            seatFrames = -1;
        }

        void OnDestroy() { if (arms != null) arms.Destroy(); if (weapon != null) weapon.Destroy(); if (gun != null) Destroy(gun.gameObject); }
    }
}
