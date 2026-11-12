using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // Third-person body: Quaternius humanoid model + Kenney idle/run/jump clips (retargeted),
    // with IK for the rifle hold, aim look-at and a foot-IK crouch.
    public class FighterAvatar : MonoBehaviour
    {
        public Fighter owner;
        Animator anim;
        Transform gun, body, chest;
        public Transform Body { get { return body; } }
        GameObject teamRing, plate; bool vanished; readonly List<Renderer> hiddenOnDeath = new List<Renderer>();
        Vector3 muzzleLocal, foreLocal;
        GunParts parts; Renderer[] bodyRenderers; MaterialPropertyBlock mpb; float flashUntil2; Color teamEmission;
        GameObject bombPack;
        public Transform LaserOrigin { get { return parts != null ? parts.laser : null; } }
        Light flashLight;
        float flashUntil, flinch, deathT = -1, crouchBlend;
        Vector3 deathAxis;
        Quaternion deathStart;
        Vector3 lastPos;
        float speedSmoothed, dirSmoothed;
        public Vector3 MuzzlePosition { get { return muzzleT != null ? muzzleT.position : gun != null ? gun.TransformPoint(muzzleLocal) : owner.Eye; } }
        // V5 glTF hero path: generic rig + its own gun animations, played through ClipPlayer.
        bool gltf; ClipPlayer clipPlayer; Transform wristR, head; float hitUntil, rollUntil;
        bool fpsRig; Transform muzzleT, handR, handL; int seatFrames;   // V12: FPSSample Terraformer
        bool toon; Renderer toonGun; Vector3 toonMuzzleLocal; Transform torso;   // V14: Quaternius toon soldier
        static readonly System.Collections.Generic.Dictionary<string, string> BoneNames = new System.Collections.Generic.Dictionary<string, string> {
            { "Head", "Head" }, { "UpperChest", "Chest" }, { "Chest", "Chest" }, { "LeftShoulder", "Shoulder.L" }, { "RightShoulder", "Shoulder.R" },
            { "LeftUpperArm", "UpperArm.L" }, { "RightUpperArm", "UpperArm.R" }, { "LeftLowerArm", "LowerArm.L" }, { "RightLowerArm", "LowerArm.R" } };
        Transform Bone(HumanBodyBones b)
        {
            if (!gltf || fpsRig) return anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;
            if (!BoneNames.TryGetValue(b.ToString(), out var n)) return null;
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == n) return t;
            return null;
        }
        void OnDestroy() { if (clipPlayer != null) clipPlayer.Destroy(); }

        public static FighterAvatar Create(Fighter f, bool visible)
        {
            if (ToonAssets.Available) { var ta = CreateToon(f, visible); if (ta != null) return ta; }
            if (FPSAssets.Available) { var fa = CreateFps(f, visible); if (fa != null) return fa; }
            var hero = f.Hero;
            bool useGltf = !string.IsNullOrEmpty(hero.gltf) && Resources.Load<GameObject>("Models/Chars2/" + hero.gltf) != null;
            var model = useGltf ? Arena.Model("Chars2/" + hero.gltf) : Arena.Model("Characters/" + hero.model);
            if (model == null)
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(model.GetComponent<Collider>());
                model.transform.localScale = new Vector3(.7f, .9f, .7f);
            }
            model.name = "角色模型 " + hero.name;
            Arena.Fit(model, SizeFor(model));
            var root = new GameObject("Avatar");
            root.transform.SetParent(f.transform, false);
            model.transform.SetParent(root.transform, false);
            var av = model.AddComponent<FighterAvatar>();
            av.owner = f; av.body = root.transform;
            av.gltf = useGltf;
            av.anim = model.GetComponentInChildren<Animator>();
            if (useGltf && av.anim != null)
            {
                av.anim.applyRootMotion = false; av.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                av.clipPlayer = new ClipPlayer(av.anim, ClipPlayer.Load("Models/Chars2/" + hero.gltf), "Death", "HitRecieve", "Roll", "Gun_Shoot");
                av.clipPlayer.Play("Idle_Gun", .01f);
                av.chest = av.Bone(HumanBodyBones.UpperChest); av.head = av.Bone(HumanBodyBones.Head);
                foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Wrist.R") av.wristR = t;
            }
            else if (av.anim != null)
            {
                av.anim.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim/Fighter");
                av.anim.applyRootMotion = false;
                av.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                av.chest = av.anim.isHuman ? (av.anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? av.anim.GetBoneTransform(HumanBodyBones.Chest)) : null;
            }
            Color team = Config.TeamColor(f.team);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (useGltf && m.HasProperty("baseColorFactor"))
                    {
                        // Hero colour identity: push the neutral armour panels toward the hero tint.
                        Color bc = m.GetColor("baseColorFactor");
                        Color.RGBToHSV(bc, out float hh, out float ss, out float vv);
                        // V10：军用配色 —— 蓝队灰蓝作战服，红队狼棕作战服，英雄色只留一点点
                        Color uniform = f.team == 0 ? new Color(.24f, .28f, .31f) : new Color(.37f, .31f, .23f);
                        if (ss < .35f && vv > .25f) bc = Color.Lerp(Color.Lerp(bc, uniform, .75f), hero.tint, .1f);
                        else bc = Color.Lerp(bc, new Color(bc.grayscale, bc.grayscale, bc.grayscale), .55f) * .85f;   // desaturate the cartoon accents
                        bc.a = 1;
                        m.SetColor("baseColorFactor", bc);
                        m.SetColor("emissiveFactor", team * (f.team == 0 ? .04f : .08f));
                        m.EnableKeyword("_EMISSION");
                        continue;
                    }
                    m.SetFloat("_Glossiness", .3f);
                    // Lift Quaternius' very dark albedo a little so silhouettes read at night; enemies get a faint red rim.
                    m.color = Color.Lerp(m.color, Color.white, .2f);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", team * (f.team == 0 ? .12f : .22f));
                }
                r.materials = mats;
            }
            if (av.chest != null)
            {
                // Team light strips on chest and back: 第一眼分清敌我（GDD 2.1）。
                for (int i = -1; i <= 1; i += 2)
                {
                    var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(strip.GetComponent<Collider>());
                    strip.transform.SetParent(av.chest, false);
                    strip.transform.position = av.chest.position + root.transform.forward * .15f * i + Vector3.up * .05f;
                    strip.transform.rotation = root.transform.rotation;
                    strip.transform.localScale = Vector3.one; strip.transform.localScale = new Vector3(.4f / av.chest.lossyScale.x, .055f / av.chest.lossyScale.y, .03f / av.chest.lossyScale.z);
                    strip.GetComponent<Renderer>().sharedMaterial = Arena.Glow(team, 3f);
                    strip.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            if (av.anim != null && (av.anim.isHuman || av.gltf)) av.Gear(f, team);
            var bodyLight = new GameObject("身体光").AddComponent<Light>();
            bodyLight.transform.SetParent(root.transform, false); bodyLight.transform.localPosition = new Vector3(0, 1.5f, -.45f);
            bodyLight.type = LightType.Point; bodyLight.color = Color.Lerp(team, Color.white, .25f); bodyLight.range = 2.6f; bodyLight.intensity = .7f; bodyLight.shadows = LightShadows.None;
            av.gun = GunModel.Create(f.W, out av.parts, hero.tint, false);
            av.gun.localScale = Vector3.one * 1.2f;
            av.muzzleLocal = av.parts.muzzle.localPosition; av.foreLocal = av.parts.foregrip;
            av.gun.SetParent(root.transform, false);
            av.bodyRenderers = model.GetComponentsInChildren<Renderer>();
            av.mpb = new MaterialPropertyBlock(); av.teamEmission = team * (f.team == 0 ? .04f : .08f);
            if (av.chest != null)
            {
                av.bombPack = new GameObject("棱能炸弹背包");
                av.bombPack.transform.SetParent(av.chest, false);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(av.bombPack.transform, false);
                box.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(glow.GetComponent<Collider>());
                glow.transform.SetParent(av.bombPack.transform, false);
                glow.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .25f, .1f), 4f);
                av.bombPack.transform.position = av.chest.position - root.transform.forward * .2f;
                av.bombPack.transform.rotation = root.transform.rotation;
                Vector3 inv = new Vector3(1f / av.chest.lossyScale.x, 1f / av.chest.lossyScale.y, 1f / av.chest.lossyScale.z);
                box.transform.localScale = Vector3.Scale(new Vector3(.32f, .4f, .16f), inv);
                glow.transform.localScale = Vector3.Scale(new Vector3(.2f, .06f, .17f), inv);
                av.bombPack.SetActive(false);
            }
            av.flashLight = new GameObject("muzzle light").AddComponent<Light>();
            av.flashLight.transform.SetParent(av.gun, false); av.flashLight.transform.localPosition = av.muzzleLocal;
            av.flashLight.type = LightType.Point; av.flashLight.range = 5; av.flashLight.intensity = 0; av.flashLight.color = Color.Lerp(hero.tint, Color.white, .4f);
            av.lastPos = f.transform.position;
            if (!visible)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            return av;
        }

        // Terraformer soldier: team-coloured armour lamps, hero-coloured visor, rifle seated in the right hand.
        static FighterAvatar CreateFps(Fighter f, bool visible)
        {
            var hero = f.Hero; Color team = Config.TeamColor(f.team);
            // V13：队伍色拉开 —— 蓝队钴蓝装甲，红队猩红装甲（之前橙白色和黄昏场景融在一起）
            Color suit = f.team == 0 ? new Color(.34f, .5f, 1f) : new Color(1f, .3f, .26f);
            var model = FPSAssets.Spawn("Char/Terraformer_A", suit, team * 1.6f, hero.tint);
            if (model == null) return null;
            model.name = "角色模型 " + hero.name + "（Terraformer）";
            var root = new GameObject("Avatar"); root.transform.SetParent(f.transform, false);
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * (1.92f / 1.903f);
            var av = model.AddComponent<FighterAvatar>();
            av.owner = f; av.body = root.transform; av.gltf = true; av.fpsRig = true;
            av.anim = model.GetComponent<Animator>();
            av.anim.applyRootMotion = false; av.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            av.clipPlayer = new ClipPlayer(av.anim, FPSAssets.Clips("Anim3P", "Stand--Idle", "Stand--Crouch_Idle", "Stand--Shoot", "Stand--Reload", "Stand--Melee", "Stand--Grenade",
                "Stand--Death_N", "Stand--Death_S", "Stand--Death_E", "Stand--Death_W", "Stand--Reaction_N", "Stand--Reaction_S", "Stand--Reaction_E", "Stand--Reaction_W",
                "Locomotion--Run_N", "Locomotion--Run_S", "Locomotion--Run_E", "Locomotion--Run_W", "Locomotion--Run_NE", "Locomotion--Run_NW", "Locomotion--Run_SE", "Locomotion--Run_SW",
                "Locomotion--Sprint_N", "Jump--InAir", "Emotes--Victory", "Emotes--Defeat"),
                "Death_N", "Death_S", "Death_E", "Death_W", "Reaction_N", "Reaction_S", "Reaction_E", "Reaction_W", "Shoot", "Melee", "Grenade", "Defeat");
            av.chest = av.anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? av.anim.GetBoneTransform(HumanBodyBones.Chest);
            av.head = av.anim.GetBoneTransform(HumanBodyBones.Head);
            var rh = av.anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = av.anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var g = FPSAssets.Spawn(FPSAssets.WeaponModel(f.W, false));
            if (g != null)
            {
                g.transform.SetParent(rh != null ? rh : root.transform, false);   // seated properly after the first animated frames
                av.gun = g.transform; av.muzzleT = FPSAssets.Find(g.transform, "Bullit_Spawn");
                av.handR = rh; av.handL = lh;
                var wa = g.GetComponent<Animator>(); if (wa != null) wa.enabled = false;
            }
            av.parts = new GunParts { muzzle = av.muzzleT, laser = av.muzzleT };
            av.clipPlayer.Play("Idle", .01f);
            av.bodyRenderers = model.GetComponentsInChildren<Renderer>();
            av.mpb = new MaterialPropertyBlock(); av.teamEmission = team * 1.6f;
            var bodyLight = new GameObject("身体光").AddComponent<Light>();
            bodyLight.transform.SetParent(root.transform, false); bodyLight.transform.localPosition = new Vector3(0, 1.5f, -.45f);
            bodyLight.type = LightType.Point; bodyLight.color = Color.Lerp(team, Color.white, .3f); bodyLight.range = 2.4f; bodyLight.intensity = .55f; bodyLight.shadows = LightShadows.None;
            if (av.chest != null)
            {
                av.bombPack = new GameObject("棱能炸弹背包");
                av.bombPack.transform.SetParent(av.chest, false);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(av.bombPack.transform, false);
                box.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
                var glow = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(glow.GetComponent<Collider>());
                glow.transform.SetParent(av.bombPack.transform, false);
                glow.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .25f, .1f), 4f);
                av.bombPack.transform.position = av.chest.position - root.transform.forward * .3f;
                av.bombPack.transform.rotation = root.transform.rotation;
                Vector3 inv = new Vector3(1f / av.chest.lossyScale.x, 1f / av.chest.lossyScale.y, 1f / av.chest.lossyScale.z);
                box.transform.localScale = Vector3.Scale(new Vector3(.3f, .36f, .14f), inv);
                glow.transform.localScale = Vector3.Scale(new Vector3(.2f, .05f, .15f), inv);
                av.bombPack.SetActive(false);
            }
            if (av.muzzleT != null)
            {
                av.flashLight = new GameObject("muzzle light").AddComponent<Light>();
                av.flashLight.transform.SetParent(av.muzzleT, false);
                av.flashLight.type = LightType.Point; av.flashLight.range = 5; av.flashLight.intensity = 0; av.flashLight.color = new Color(1f, .8f, .5f);
            }
            av.lastPos = f.transform.position;
            // V18：脚下一圈队伍颜色的光环，敌我一眼分清
            av.teamRing = GameObject.CreatePrimitive(PrimitiveType.Quad); av.teamRing.name = "队伍光环"; Destroy(av.teamRing.GetComponent<Collider>());
            av.teamRing.transform.SetParent(root.transform, false); av.teamRing.transform.localPosition = Vector3.up * .04f; av.teamRing.transform.localRotation = Quaternion.Euler(90, 0, 0); av.teamRing.transform.localScale = Vector3.one * 1.5f;
            var rr = av.teamRing.GetComponent<Renderer>(); rr.sharedMaterial = ToonAssets.RingMaterial(Config.TeamColor(f.team)); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
            if (!visible) foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            if (!visible) av.teamRing.GetComponent<Renderer>().enabled = false;
            return av;
        }

        static FighterAvatar CreateToon(Fighter f, bool visible)
        {
            var hero = f.Hero; Color team = Config.TeamColor(f.team);
            string ch = ToonAssets.CharFor(hero.role);
            var model = Arena.Model("Toon/Chars/" + ch);
            if (model == null) return null;
            model.name = "角色模型 " + hero.name + "（卡通）";
            var root = new GameObject("Avatar"); root.transform.SetParent(f.transform, false);
            model.transform.SetParent(root.transform, false);
            // keep only the gun this fighter carries
            // hide the kit's guns; the dessert gun is mounted on the hand pose of the matching kit gun
            Renderer gunR = null; string want = ToonAssets.GunFor(f.W);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (ToonAssets.IsGunNode(r.name)) { if (r.name == want) gunR = r; r.enabled = false; }
            // size from body + head only (the gun rack would skew it)
            Bounds b = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) if (r.enabled && !ToonAssets.IsGunNode(r.name)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            float k = 1.85f / Mathf.Max(.1f, b.size.y);
            model.transform.localScale = Vector3.one * k;
            model.transform.localPosition = new Vector3(0, -b.min.y * k, 0);
            ToonAssets.Recolor(model, Color.Lerp(team, Color.white, .1f), hero.tint);
            if (ch == "Character_Bean") ToonAssets.AddHat(model, hero.role, f.human ? Collection.EquippedHat : null);   // V17：豆子角色戴美食帽子
            var av = model.AddComponent<FighterAvatar>();
            av.owner = f; av.body = root.transform; av.gltf = true; av.toon = true;
            av.anim = model.GetComponentInChildren<Animator>();
            av.anim.applyRootMotion = false; av.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            av.clipPlayer = new ClipPlayer(av.anim, ClipPlayer.Load("Toon/Chars/" + ch), "Death", "HitReact", "Jump_Land", "Punch", "Wave");
            av.clipPlayer.Play("Idle", .01f);
            foreach (var t in model.GetComponentsInChildren<Transform>()) { if (t.name == "Torso") av.torso = t; if (t.name == "Head" && t.childCount > 0) av.head = t; }
            av.chest = av.torso;
            av.toonGun = gunR;
            av.parts = new GunParts();
            if (gunR != null)
            {
                var dg = DessertGuns.Create(f.W, av.parts, false);
                var dgAnim = dg.GetComponent<FoodGunAnim>(); if (dgAnim != null) dgAnim.owner = f;
                dg.SetParent(gunR.transform, false);
                dg.localRotation = Quaternion.Euler(0, 90, 0);   // kit guns point along their local +X
                Vector3 ls = gunR.transform.lossyScale; dg.localScale = new Vector3(1.6f / ls.x, 1.6f / ls.y, 1.6f / ls.z);
                av.gun = dg; av.muzzleT = av.parts.muzzle;
            }
            av.bodyRenderers = model.GetComponentsInChildren<Renderer>();
            av.mpb = new MaterialPropertyBlock(); av.teamEmission = Color.black;
            if (av.chest != null)
            {
                av.bombPack = new GameObject("棱能炸弹背包");
                av.bombPack.transform.SetParent(av.chest, false);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(av.bombPack.transform, false);
                box.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .3f, .1f), 2.5f);
                av.bombPack.transform.position = av.chest.position - root.transform.forward * .32f;
                av.bombPack.transform.rotation = root.transform.rotation;
                Vector3 inv = new Vector3(1f / av.chest.lossyScale.x, 1f / av.chest.lossyScale.y, 1f / av.chest.lossyScale.z);
                box.transform.localScale = Vector3.Scale(new Vector3(.3f, .32f, .16f), inv);
                av.bombPack.SetActive(false);
            }
            if (av.muzzleT != null)
            {
                av.flashLight = new GameObject("muzzle light").AddComponent<Light>();
                av.flashLight.transform.SetParent(av.muzzleT, false);
                av.flashLight.type = LightType.Point; av.flashLight.range = 5; av.flashLight.intensity = 0; av.flashLight.color = new Color(1f, .8f, .5f);
            }
            av.lastPos = f.transform.position;
            if (!visible) foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            return av;
        }

        void UpdateToon()
        {
            clipPlayer.Tick(Time.deltaTime);
            if (deathT >= 0)
            {
                deathT += Time.deltaTime;
                if (deathT > .7f && !vanished) Vanish();   // V18：阵亡后"噗"地消失，留下一个盘子
                return;
            }
            var ph = Match.I != null ? Match.I.phase : Phase.Live;
            bool won = ph == Phase.RoundEnd && owner.team == Match.I.lastRoundWinner || ph == Phase.MatchEnd && (Match.FFA ? Match.I.FFALeader() == owner : owner.team == Match.I.lastRoundWinner);
            string em = owner.Food != null ? owner.Food.EmoteClip : null;
            if (won) { clipPlayer.Play(Mathf.Repeat(Time.time + owner.slot, 4f) < 2f ? "Yes" : "Wave", .2f); return; }   // 胜利庆祝
            if (em != null) { clipPlayer.Play(em, .15f); return; }
            Vector3 v = (owner.transform.position - lastPos) / Mathf.Max(Time.deltaTime, .0001f);
            lastPos = owner.transform.position; v.y = 0;
            if (v.magnitude > 20) v = Vector3.zero;
            float speed = v.magnitude, fwd = Vector3.Dot(v, owner.transform.forward);
            bool firing = owner.lastShotAt > Time.time - .25f;
            if (owner.bombBusy || owner.usingInteract != null) clipPlayer.Play("Duck", .2f);
            else if (Time.time < owner.meleeUntil) clipPlayer.Play("Punch", .05f, 1.6f, true);
            else if (!owner.grounded && !owner.Mantling) clipPlayer.Play("Jump_Idle", .15f);
            else if (Time.time < hitUntil && speed < .5f) clipPlayer.Play("HitReact", .05f, 1.2f);
            else if (speed > .5f)
            {
                float rate = Mathf.Clamp(speed / (owner.sprinting ? 6.5f : 4.8f), .55f, 1.5f) * (fwd < -.3f ? -1f : 1f);
                if (speed < 2.5f) clipPlayer.Play(firing ? "Walk_Shoot" : "Walk", .15f, Mathf.Abs(rate) * 1.3f);
                else clipPlayer.Play(firing ? "Run_Shoot" : "Run_Gun", .15f, Mathf.Abs(rate));
            }
            else if (owner.crouching || owner.Prone) clipPlayer.Play("Duck", .2f);
            else clipPlayer.Play("Idle_Shoot", .15f, firing ? 1f : .0001f);   // combat-ready: rifle up, frozen on the aim pose unless firing
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(owner.Prone ? 72f : -flinch * 8f, 0, flinch * 4f), Time.deltaTime * 10f);
            body.localPosition = Vector3.Lerp(body.localPosition, owner.Prone ? new Vector3(0, .25f, -.55f) : Vector3.zero, Time.deltaTime * 10f);   // V20：趴下姿态
            flinch = Mathf.MoveTowards(flinch, 0, Time.deltaTime * 5f);
        }

        void UpdateFps()
        {
            clipPlayer.Tick(Time.deltaTime);
            if (deathT >= 0) { deathT += Time.deltaTime; return; }
            Vector3 v = (owner.transform.position - lastPos) / Mathf.Max(Time.deltaTime, .0001f);
            lastPos = owner.transform.position; v.y = 0;
            if (v.magnitude > 20) v = Vector3.zero;
            float speed = v.magnitude, fwd = Vector3.Dot(v, owner.transform.forward), side = Vector3.Dot(v, owner.transform.right);
            bool firing = owner.lastShotAt > Time.time - .2f;
            if (owner.bombBusy || owner.usingInteract != null) clipPlayer.Play("Crouch_Idle", .2f);
            else if (owner.IsReloading) clipPlayer.Play("Reload", .15f, 1.47f / Mathf.Max(.4f, owner.W.reload));
            else if (Time.time < owner.meleeUntil) clipPlayer.Play("Melee", .05f, 2.4f);
            else if (!owner.grounded && !owner.Mantling) clipPlayer.Play("InAir", .2f);
            else if (speed > .5f)
            {
                float rate = Mathf.Clamp(speed / 4.6f, .55f, 1.5f);
                if (owner.sprinting) clipPlayer.Play("Sprint_N", .15f, Mathf.Clamp(speed / 6.8f, .7f, 1.3f));
                else
                {
                    float ang = Mathf.Atan2(side, fwd) * Mathf.Rad2Deg;   // 0 = forward, 90 = right
                    string[] dirs = { "Run_N", "Run_NE", "Run_E", "Run_SE", "Run_S", "Run_SW", "Run_W", "Run_NW" };
                    int idx = Mathf.RoundToInt(Mathf.Repeat(ang, 360f) / 45f) % 8;
                    clipPlayer.Play(dirs[idx], .18f, rate);
                }
            }
            else if (Time.time < hitUntil) { }
            else if (owner.crouching) clipPlayer.Play("Crouch_Idle", .2f);
            else clipPlayer.Play(firing ? "Shoot" : "Idle", firing ? .05f : .2f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, owner.crouching && speed > .5f ? 1 : 0, Time.deltaTime * 6f);
            body.localPosition = Vector3.up * (-.3f * crouchBlend);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(-flinch * 6f, 0, flinch * 3f), Time.deltaTime * 14f);
            flinch = Mathf.MoveTowards(flinch, 0, Time.deltaTime * 5f);
        }

        static Vector3 SizeFor(GameObject model)
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return Vector3.one;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = 1.92f / Mathf.Max(.01f, b.size.y);   // V4：角色放大，远处更容易辨认
            return b.size * k;
        }

        // Readability lights (helmet band, shoulder LEDs) + hero-specific gear silhouettes.
        GameObject Bolt(HumanBodyBones bone, PrimitiveType t, Vector3 offset, Vector3 size, Material m, Vector3 euler = default(Vector3))
        {
            var b = Bone(bone);
            if (b == null) return null;
            var o = GameObject.CreatePrimitive(t); Destroy(o.GetComponent<Collider>());
            o.transform.SetParent(b, false);
            o.transform.position = b.position + body.rotation * offset;
            o.transform.rotation = body.rotation * Quaternion.Euler(euler);
            Vector3 ls = b.lossyScale;
            o.transform.localScale = new Vector3(size.x / ls.x, size.y / ls.y, size.z / ls.z);
            o.GetComponent<Renderer>().sharedMaterial = m;
            o.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return o;
        }

        void Gear(Fighter f, Color team)
        {
            var glow = Arena.Glow(team, 4.5f); var tint = Arena.Glow(f.Hero.tint, 3.5f);
            var metal = Resources.Load<Material>("Materials/MetalPlate");
            Bolt(HumanBodyBones.Head, PrimitiveType.Cube, new Vector3(0, .1f, .02f), new Vector3(.27f, .035f, .27f), glow);
            Bolt(HumanBodyBones.LeftShoulder, PrimitiveType.Cube, new Vector3(-.06f, .06f, 0), new Vector3(.12f, .035f, .14f), glow);
            Bolt(HumanBodyBones.RightShoulder, PrimitiveType.Cube, new Vector3(.06f, .06f, 0), new Vector3(.12f, .035f, .14f), glow);
            switch (f.role)
            {
                case Role.Falcon:   // 喷射背包
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(i * .1f, -.05f, -.22f), new Vector3(.09f, .16f, .09f), metal);
                        Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(i * .1f, -.22f, -.22f), new Vector3(.07f, .02f, .07f), tint);
                    }
                    break;
                case Role.Rivet:    // 工具背包 + 天线
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cube, new Vector3(0, -.02f, -.2f), new Vector3(.3f, .34f, .14f), metal);
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cube, new Vector3(0, .05f, -.28f), new Vector3(.31f, .03f, .02f), tint);
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(.12f, .3f, -.2f), new Vector3(.012f, .2f, .012f), metal);
                    break;
                case Role.Ironwall: // 肩甲 + 臂载护盾发射器
                    Bolt(HumanBodyBones.LeftUpperArm, PrimitiveType.Cube, new Vector3(-.03f, .08f, 0), new Vector3(.17f, .05f, .2f), metal);
                    Bolt(HumanBodyBones.RightUpperArm, PrimitiveType.Cube, new Vector3(.03f, .08f, 0), new Vector3(.17f, .05f, .2f), metal);
                    Bolt(HumanBodyBones.LeftLowerArm, PrimitiveType.Cylinder, new Vector3(0, 0, 0), new Vector3(.16f, .015f, .16f), tint, new Vector3(0, 0, 90));
                    break;
                case Role.Raven:    // 侦查天线 + 目镜
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(-.1f, .25f, -.16f), new Vector3(.015f, .28f, .015f), metal);
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Sphere, new Vector3(-.1f, .54f, -.16f), new Vector3(.04f, .04f, .04f), tint);
                    Bolt(HumanBodyBones.Head, PrimitiveType.Cube, new Vector3(0, .04f, .1f), new Vector3(.18f, .035f, .03f), tint);
                    break;
                case Role.Ember:    // 背负燃料罐
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(0, -.02f, -.22f), new Vector3(.15f, .2f, .15f), metal);
                    Bolt(HumanBodyBones.UpperChest, PrimitiveType.Cylinder, new Vector3(0, -.02f, -.22f), new Vector3(.155f, .04f, .155f), tint);
                    break;
                case Role.Phantom:  // 全息面罩 + 腕部投影
                    Bolt(HumanBodyBones.Head, PrimitiveType.Cube, new Vector3(0, .03f, .1f), new Vector3(.2f, .06f, .025f), tint);
                    Bolt(HumanBodyBones.LeftLowerArm, PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(.1f, .05f, .1f), tint);
                    break;
            }
        }

        public void MuzzleFlash() { flashUntil = Time.time + .05f; if (flashLight != null) flashLight.intensity = 3f; }
        public void Flinch()
        {
            flinch = 1f; flashUntil2 = Time.time + .08f;
            if (bodyRenderers == null) return;
            foreach (var r in bodyRenderers) { r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", new Color(1f, .9f, .85f) * 1.6f); mpb.SetColor("emissiveFactor", new Color(1f, .9f, .85f) * 1.2f); r.SetPropertyBlock(mpb); }
            hitUntil = Time.time + .28f;
            if (toon && clipPlayer != null && deathT < 0) { clipPlayer.Play("HitReact", .05f, 1.2f, true); hitUntil = Time.time + .35f; }
            if (fpsRig && clipPlayer != null && deathT < 0 && owner != null)
            {
                Vector3 from = owner.lastDamageFrom - owner.transform.position; from.y = 0;
                Vector3 l = owner.transform.InverseTransformDirection(from.normalized);
                string r = Mathf.Abs(l.z) > Mathf.Abs(l.x) ? (l.z > 0 ? "Reaction_N" : "Reaction_S") : (l.x > 0 ? "Reaction_E" : "Reaction_W");
                clipPlayer.Play(r, .05f, 1.2f, true); hitUntil = Time.time + .45f;
            }
        }

        public void Die(Vector3 away)
        {
            deathT = 0; deathStart = body.localRotation;
            if (toon && clipPlayer != null) { clipPlayer.Play("Death", .08f, 1f, true); return; }
            if (fpsRig && clipPlayer != null)
            {
                Vector3 l = owner.transform.InverseTransformDirection(away.sqrMagnitude > .01f ? away.normalized : -owner.transform.forward);
                string d = Mathf.Abs(l.z) > Mathf.Abs(l.x) ? (l.z > 0 ? "Death_N" : "Death_S") : (l.x > 0 ? "Death_E" : "Death_W");
                clipPlayer.Play(d, .08f, 1f, true); return;
            }
            if (gltf && clipPlayer != null) { clipPlayer.Play("Death", .1f, 1f, true); return; }
            Vector3 axis = Vector3.Cross(Vector3.up, away.sqrMagnitude > .01f ? away : -owner.transform.forward);
            deathAxis = owner.transform.InverseTransformDirection(axis.normalized);
            if (anim != null) anim.speed = .25f;
        }

        void Vanish()
        {
            vanished = true; hiddenOnDeath.Clear();
            foreach (var r in body.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hiddenOnDeath.Add(r); }
            FoodFx.KillPop(owner.transform.position + Vector3.up * .6f);
            var pf = Resources.Load<GameObject>("Toon/Kitchen/Plate");
            if (pf != null) { plate = Instantiate(pf); foreach (var c in plate.GetComponentsInChildren<Collider>()) Destroy(c); plate.transform.position = Skills.GroundAt(owner.transform.position) + Vector3.up * .02f; plate.transform.localScale = Vector3.one * 1.4f; }
        }

        public void Revive()
        {
            if (vanished) { foreach (var r in hiddenOnDeath) if (r != null) r.enabled = true; hiddenOnDeath.Clear(); vanished = false; }
            if (plate != null) Destroy(plate);
            deathT = -1; body.localRotation = Quaternion.identity; body.localPosition = Vector3.zero;
            if (anim != null) anim.speed = 1f;
            if (clipPlayer != null) clipPlayer.Play(fpsRig || toon ? "Idle" : "Idle_Gun", .01f, 1f, true);
            gameObject.SetActive(true);
        }

        void Update()
        {
            if (owner == null) return;
            if (flashLight != null && Time.time > flashUntil) flashLight.intensity = 0;
            if (flashUntil2 > 0 && Time.time > flashUntil2 && bodyRenderers != null)
            {
                flashUntil2 = 0;
                foreach (var r in bodyRenderers) { r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", teamEmission); mpb.SetColor("emissiveFactor", teamEmission); r.SetPropertyBlock(mpb); }
            }
            if (bombPack != null) bombPack.SetActive(Match.I != null && Match.I.BombCarrier == owner && owner.alive);
            if (toon) { UpdateToon(); return; }
            if (fpsRig) { UpdateFps(); return; }
            if (gltf) { UpdateGltf(); return; }
            if (deathT >= 0)
            {
                deathT += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(deathT / .55f));
                body.localRotation = Quaternion.AngleAxis(84f * k, deathAxis) * deathStart;
                body.localPosition = Vector3.up * .05f * k;
                return;
            }
            Vector3 v = (owner.transform.position - lastPos) / Mathf.Max(Time.deltaTime, .0001f);
            lastPos = owner.transform.position;
            v.y = 0;
            if (v.magnitude > 20) v = Vector3.zero;
            float speed = v.magnitude;
            float forward = Vector3.Dot(v, owner.transform.forward);
            float lateral = Vector3.Dot(v, owner.transform.right);
            speedSmoothed = Mathf.Lerp(speedSmoothed, Mathf.Clamp01(speed / 1.5f), Time.deltaTime * 10f);
            dirSmoothed = Mathf.Lerp(dirSmoothed, speed < .3f ? 1f : (forward < -.3f ? -1f : 1f) * Mathf.Max(.55f, speed / 5.6f), Time.deltaTime * 10f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, owner.crouching ? 1 : 0, Time.deltaTime * 6f);
            if (anim != null)
            {
                anim.SetFloat("Speed", speedSmoothed);
                anim.SetFloat("MoveDir", dirSmoothed);
                anim.SetBool("Grounded", owner.grounded);
            }
            // Turn the legs toward strafing direction (upper body is brought back by look-at IK).
            float legYaw = 0;
            if (speed > .5f) legYaw = Mathf.Clamp(Mathf.Atan2(lateral, Mathf.Abs(forward) + .01f) * Mathf.Rad2Deg * (forward < -.3f ? -1 : 1), -60, 60);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(-flinch * 9f, legYaw, flinch * 4f), Time.deltaTime * 14f);
            body.localPosition = Vector3.up * (-.42f * crouchBlend);
            flinch = Mathf.MoveTowards(flinch, 0, Time.deltaTime * 5f);
        }

        void UpdateGltf()
        {
            if (clipPlayer == null) return;
            clipPlayer.Tick(Time.deltaTime);
            if (deathT >= 0) { deathT += Time.deltaTime; return; }
            Vector3 v = (owner.transform.position - lastPos) / Mathf.Max(Time.deltaTime, .0001f);
            lastPos = owner.transform.position; v.y = 0;
            if (v.magnitude > 20) v = Vector3.zero;
            float speed = v.magnitude, fwd = Vector3.Dot(v, owner.transform.forward), side = Vector3.Dot(v, owner.transform.right);
            bool firing = owner.lastShotAt > Time.time - .25f;
            if (owner.Dashing) { if (Time.time > rollUntil) { clipPlayer.Play("Roll", .05f, 1.6f, true); rollUntil = Time.time + .5f; } }
            else if (owner.bombBusy || owner.usingInteract != null) clipPlayer.Play("Interact", .15f);
            else if (Time.time < hitUntil && speed < .5f) clipPlayer.Play("HitRecieve", .05f, 1.4f);
            else if (speed > .5f)
            {
                float rate = Mathf.Clamp(speed / 5.2f, .5f, 1.4f);
                if (Mathf.Abs(side) > Mathf.Abs(fwd)) clipPlayer.Play(side > 0 ? "Run_Right" : "Run_Left", .15f, rate);
                else if (fwd < 0) clipPlayer.Play("Run_Back", .15f, rate);
                else clipPlayer.Play(firing ? "Run_Shoot" : "Run", .15f, rate);
            }
            else clipPlayer.Play(firing ? "Idle_Gun_Shoot" : owner.aiming ? "Idle_Gun_Pointing" : "Idle_Gun", .15f);
            crouchBlend = Mathf.MoveTowards(crouchBlend, owner.crouching ? 1 : 0, Time.deltaTime * 6f);
            body.localPosition = Vector3.up * (-.38f * crouchBlend);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(-flinch * 9f, 0, flinch * 4f), Time.deltaTime * 14f);
            flinch = Mathf.MoveTowards(flinch, 0, Time.deltaTime * 5f);
            if (flashLight != null && Time.time > flashUntil) flashLight.intensity = 0;
            if (flashUntil2 > 0 && Time.time > flashUntil2 && bodyRenderers != null)
            {
                flashUntil2 = 0;
                foreach (var r in bodyRenderers) { r.GetPropertyBlock(mpb); mpb.SetColor("emissiveFactor", teamEmission); r.SetPropertyBlock(mpb); }
            }
            if (bombPack != null) bombPack.SetActive(Match.I != null && Match.I.BombCarrier == owner && owner.alive);
        }

        // After the animation pass: bend the chest toward the aim pitch and seat the rifle in the right hand.
        void LateUpdate()
        {
            if (toon)
            {
                if (owner == null || deathT >= 0) return;
                if (torso != null && !owner.IsReloading && !owner.sprinting) torso.rotation = Quaternion.AngleAxis(owner.pitch * .6f, owner.transform.right) * torso.rotation;
                if (torso != null && Mathf.Abs(owner.lean) > .01f) torso.rotation = Quaternion.AngleAxis(-owner.lean * 22f, owner.transform.forward) * torso.rotation;
                return;
            }
            if (fpsRig)
            {
                if (seatFrames >= 0 && gun != null && handR != null && handL != null && deathT < 0 && ++seatFrames >= 3 && clipPlayer != null && clipPlayer.Current.EndsWith("Idle"))
                { FPSAssets.Grip(gun, handR, handL, body); seatFrames = -1; }
                if (owner == null || deathT >= 0) return;
                if (chest != null && !owner.IsReloading && !owner.sprinting) chest.rotation = Quaternion.AngleAxis(owner.pitch * .7f, owner.transform.right) * chest.rotation;
                if (Mathf.Abs(owner.lean) > .01f)
                {
                    // bend the spine sideways so the head ends up where the camera / head hitbox is
                    var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                    if (spine != null) spine.rotation = Quaternion.AngleAxis(-owner.lean * 16f, owner.transform.forward) * spine.rotation;
                    if (chest != null) chest.rotation = Quaternion.AngleAxis(-owner.lean * 10f, owner.transform.forward) * chest.rotation;
                }
                return;
            }
            if (!gltf || owner == null || gun == null) return;
            if (deathT >= 0) { gun.gameObject.SetActive(deathT < .3f); return; }
            gun.gameObject.SetActive(true);
            Quaternion aimRot = Quaternion.LookRotation(owner.AimDir, Vector3.up);
            if (chest != null) chest.rotation = Quaternion.AngleAxis(owner.pitch * .75f, owner.transform.right) * chest.rotation;
            bool lowered = owner.sprinting || owner.IsReloading || owner.usingInteract != null || owner.bombBusy || owner.Dashing;
            Vector3 hand = wristR != null ? wristR.position : owner.transform.position + Vector3.up * 1.2f + owner.transform.right * .2f;
            gun.rotation = lowered ? aimRot * Quaternion.Euler(30, -20, 0) : aimRot * Quaternion.Euler(-flinch * 10f, 0, 0);
            gun.position = hand + gun.rotation * new Vector3(-.02f, .03f, .02f);
        }

        void OnAnimatorIK(int layer)
        {
            if (owner == null || anim == null || deathT >= 0 || fpsRig || toon) return;
            Vector3 aim = owner.AimDir;
            Vector3 shoulder = owner.transform.position + Vector3.up * (1.48f - .45f * crouchBlend) + owner.transform.right * .15f;
            Quaternion aimRot = Quaternion.LookRotation(aim, Vector3.up);
            bool lowered = owner.sprinting || owner.IsReloading || owner.usingInteract != null || owner.bombBusy;
            Quaternion gunRot = lowered ? aimRot * Quaternion.Euler(35, -25, 0) : aimRot * Quaternion.Euler(-flinch * 10f, 0, 0);
            gun.position = shoulder + aimRot * new Vector3(0, -.12f, .22f);
            gun.rotation = gunRot;
            anim.SetLookAtWeight(1f, .45f, .9f, 0f, .6f);
            anim.SetLookAtPosition(owner.Eye + aim * 10f);
            Vector3 grip = gun.position, fore = gun.TransformPoint(foreLocal);
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
            anim.SetIKPosition(AvatarIKGoal.RightHand, grip);
            anim.SetIKRotationWeight(AvatarIKGoal.RightHand, .7f);
            anim.SetIKRotation(AvatarIKGoal.RightHand, gunRot * Quaternion.Euler(0, 0, -90));
            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, lowered && owner.IsReloading ? .4f : 1f);
            anim.SetIKPosition(AvatarIKGoal.LeftHand, fore + gunRot * new Vector3(-.02f, -.03f, 0));
            anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, .6f);
            anim.SetIKRotation(AvatarIKGoal.LeftHand, gunRot * Quaternion.Euler(0, 0, 90));
            anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, .6f);
            anim.SetIKHintPosition(AvatarIKHint.LeftElbow, owner.transform.position + Vector3.up * (1.0f - .4f * crouchBlend) - owner.transform.right * .45f + owner.transform.forward * .1f);
            anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, .6f);
            anim.SetIKHintPosition(AvatarIKHint.RightElbow, owner.transform.position + Vector3.up * (1.0f - .4f * crouchBlend) + owner.transform.right * .5f - owner.transform.forward * .1f);
            if (crouchBlend > .01f && owner.grounded)
            {
                foreach (var goal in new[] { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot })
                {
                    Vector3 p = anim.GetIKPosition(goal);
                    p.y = Mathf.Max(p.y, owner.transform.position.y + .08f);
                    anim.SetIKPositionWeight(goal, crouchBlend);
                    anim.SetIKPosition(goal, p + owner.transform.forward * .12f * crouchBlend);
                }
                anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, crouchBlend);
                anim.SetIKHintPosition(AvatarIKHint.LeftKnee, owner.transform.position + Vector3.up * .5f + owner.transform.forward * .8f - owner.transform.right * .2f);
                anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, crouchBlend);
                anim.SetIKHintPosition(AvatarIKHint.RightKnee, owner.transform.position + Vector3.up * .5f + owner.transform.forward * .8f + owner.transform.right * .2f);
            }
        }
    }
}
