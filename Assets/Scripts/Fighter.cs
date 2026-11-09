using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    public class Stats
    {
        public int kills, deaths, assists, headshots, shots, hits, skillUses, skillEffective, doorsOpened, coverDestroyed, plants, defuses;
        public float damage, captureTime;
        public void Clear() { kills = deaths = assists = headshots = shots = hits = skillUses = skillEffective = doorsOpened = coverDestroyed = plants = defuses = 0; damage = captureTime = 0; }
        public void Add(Stats o)
        {
            kills += o.kills; deaths += o.deaths; assists += o.assists; headshots += o.headshots; shots += o.shots; hits += o.hits;
            skillUses += o.skillUses; skillEffective += o.skillEffective; doorsOpened += o.doorsOpened; coverDestroyed += o.coverDestroyed;
            plants += o.plants; defuses += o.defuses; damage += o.damage; captureTime += o.captureTime;
        }
    }

    [RequireComponent(typeof(CharacterController))]
    public class Fighter : MonoBehaviour, IDamageable
    {
        public int team, slot;
        public bool human, alive;
        public Role role;
        public HeroSpec Hero { get { return Config.Hero(role); } }
        public WeaponSpec weapon;            // bought this round (null = hero's signature weapon, used by tests)
        public int credits = Config.StartCredits, lossStreak;
        WeaponSpec builtFor;
        public WeaponSpec W { get { return weapon ?? Hero.weapon; } }

        public bool Buy(WeaponSpec w)
        {
            if (Match.I == null || Match.I.phase != Phase.Prep || w == W) return false;
            int price = Config.Price(Hero, w);
            if (credits < price) return false;
            credits -= price; Equip(w);
            if (human) Sfx.Ui("confirmation", .45f, 1.1f);
            return true;
        }
        public void Equip(WeaponSpec w)
        {
            weapon = w; ammo = W.magazine; reloadEndAt = 0; bloom = 0; sprayShots = 0;
            BuildVisuals();
        }
        public float hp = Config.MaxHp, armor = Config.MaxArmor;
        public int ammo, skillCharges;
        public float skillRechargeAt, guardUntil;
        public readonly Stats stats = new Stats();
        public readonly Stats matchStats = new Stats();
        public CharacterController cc;
        public Camera cam;
        public FighterAvatar avatar;
        public float yaw, pitch;
        public bool crouching, aiming, sprinting, grounded;
        public Vector3 velocity;
        public float revealedUntil, hiddenFromScanUntil, jammedUntil;
        // V18：美食大作战（食材、变身、大招……都在 FoodState 里）
        public float freezeUntil; FoodState food;
        public FoodState Food { get { if (food == null) { food = GetComponent<FoodState>() ?? gameObject.AddComponent<FoodState>(); food.f = this; } return food; } }
        public bool FoodAim { get { return ToonAssets.Available && aiming && aimedAt > 0 && Time.time - aimedAt > .1f; } }
        public void Freeze(float t) { freezeUntil = Mathf.Max(freezeUntil, Time.time + t); }
        public float lastDamageAt; public Vector3 lastDamageFrom; public float lastArmorBreakAt = -10;
        public float hitMarkerUntil; public bool hitMarkerKill, hitMarkerHead; public float armorBreakMarkAt = -10; float fovKick;
        public float killBannerUntil; public string killBanner = "";
        public Fighter lastVictim; public bool lastKillHead; public int roundKills; public float hitPunchAt = -10;
        public float lastShotAt = -10f, lastSeenByEnemyAt = -10f;
        public float shieldActiveUntil, dashUntil, aimRecoveryUntil;
        public Interactable usingInteract;
        public bool bombBusy;            // planting / defusing — locks movement
        public string routeName = "";
        public readonly Dictionary<Fighter, float> damagedBy = new Dictionary<Fighter, float>();
        public bool IsAttacker { get { return Match.I != null && team == Match.I.AttackingTeam; } }

        // Bot input (set every frame by Bot)
        public Vector3 botMove; public bool botSprint, botCrouch, botTrigger, botAim, botJump, botUse;
        public bool scriptUse;   // test harness: hold E regardless of input source

        float verticalSpeed, nextShotAt, reloadEndAt, bloom, footstepAt;
        int burstLeft; float nextBurstAt; bool triggerWasHeld;
        float camHeight = 1.62f, viewBob, punchPitch, punchYaw;
        Transform viewPivot, viewGun, muzzle, ejectPort, bolt; Light muzzleLight; Renderer muzzleFlash; GunParts viewParts;
        Vector3 viewHip = new Vector3(.19f, -.2f, .36f), viewAds = new Vector3(0, -.115f, .26f);
        float viewKick, boltKick; Vector3 viewSway;
        public float mouseSensitivity = 2.0f;
        public bool adsToggle;            // V4：右键切换开镜
        float aimedAt = -1;
        public bool Scoped { get { return aiming && W.sight == "scope" && aimedAt > 0 && Time.time - aimedAt > .12f; } }

        public bool IsReloading { get { return reloadEndAt > 0; } }
        public float ReloadProgress { get { return IsReloading ? 1f - (reloadEndAt - Time.time) / W.reload : 0f; } }
        public Vector3 Eye { get { return transform.position + Vector3.up * camHeight + LeanOffset; } }
        // V13：Q/E 探头 —— 头和枪向侧面探出 0.38 m 并倾斜 14°，身体留在掩体后
        public float lean; public int botLean;   // -1 左 · 0 · +1 右
        public Vector3 LeanOffset { get { return transform.right * (lean * .38f) + Vector3.down * (Mathf.Abs(lean) * .06f); } }
        SphereCollider leanHead;
        void UpdateLean(int want)
        {
            if (sprinting || Sliding || mantling || !grounded || bombBusy) want = 0;
            if (want != 0)
            {
                // don't lean the head into a wall
                Vector3 side = transform.right * want;
                if (Physics.SphereCast(transform.position + Vector3.up * camHeight, .15f, side, out RaycastHit wallHit, .45f, ~0, QueryTriggerInteraction.Ignore) && wallHit.collider.GetComponentInParent<Fighter>() == null) want = 0;
            }
            lean = Mathf.MoveTowards(lean, want, Time.deltaTime * 6f);
            if (leanHead == null)
            {
                var hg = new GameObject("探头判定"); hg.transform.SetParent(transform, false);
                leanHead = hg.AddComponent<SphereCollider>(); leanHead.radius = .2f;
                Physics.IgnoreCollision(cc, leanHead);
            }
            leanHead.enabled = Mathf.Abs(lean) > .2f;
            leanHead.transform.localPosition = new Vector3(lean * .38f, camHeight - Mathf.Abs(lean) * .06f, 0);
        }
        public Vector3 Chest { get { return transform.position + Vector3.up * (crouching ? .9f : 1.32f); } }
        public Vector3 Head { get { return transform.position + Vector3.up * (crouching ? 1.15f : 1.72f); } }
        public Vector3 AimDir { get { return Quaternion.Euler(pitch, yaw, 0) * Vector3.forward; } }
        public bool Dashing { get { return Time.time < dashUntil; } }
        public bool ShieldUp { get { return Time.time < shieldActiveUntil; } }
        public bool Revealed { get { return Time.time < revealedUntil && Time.time >= hiddenFromScanUntil; } }

        public void Init(int t, bool isHuman, Role r, int s)
        {
            team = t; human = isHuman; role = r; slot = s;
            if (ToonAssets.Available) Food.f = this;
            cc = GetComponent<CharacterController>();
            cc.height = 1.92f; cc.radius = .4f; cc.center = new Vector3(0, .96f, 0); cc.stepOffset = .45f; cc.slopeLimit = 45;
            if (human)
            {
                cam = Camera.main;
                if (cam == null) { cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
                if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
                cam.nearClipPlane = .02f; cam.fieldOfView = GameSettings.Fov; cam.allowHDR = true;
                cam.renderingPath = RenderingPath.DeferredShading;
                AttachCamera();
            }
            BuildVisuals();
        }

        public void AttachCamera()
        {
            if (cam == null) return;
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0, camHeight, 0);
            cam.transform.localRotation = Quaternion.identity;
        }

        public void BuildVisuals()
        {
            if (avatar != null) Destroy(avatar.transform.parent != null ? avatar.transform.parent.gameObject : avatar.gameObject);
            avatar = FighterAvatar.Create(this, !human);
            if (human) BuildViewModel();
            builtFor = W;
        }

        // V12：FPSSample 第一人称手臂 + 武器（真实持枪、换弹、开镜、冲刺动画）
        public FPSViewModel fpsView;
        void BuildViewModel()
        {
            if (viewPivot != null) Destroy(viewPivot.gameObject);
            viewPivot = new GameObject("第一人称武器").transform;
            viewPivot.SetParent(cam.transform, false);
            fpsView = FPSAssets.Available && !ToonAssets.Available ? FPSViewModel.Create(this, viewPivot) : null;
            if (fpsView != null)
            {
                viewHip = new Vector3(.01f, -.035f, .07f); viewAds = new Vector3(0, -.01f, .04f); viewPivot.localPosition = viewHip;   // push the arms a little forward/down so the stock stays out of the face
                viewGun = fpsView.transform;
                viewParts = new GunParts { muzzle = fpsView.muzzle, eject = fpsView.slide, laser = fpsView.muzzle };
                muzzle = fpsView.muzzle != null ? fpsView.muzzle : fpsView.gun; ejectPort = fpsView.slide; bolt = null;
                muzzleLight = muzzle.gameObject.AddComponent<Light>(); muzzleLight.type = LightType.Point; muzzleLight.range = 5;
                muzzleLight.color = new Color(1f, .8f, .5f); muzzleLight.intensity = 0; muzzleLight.shadows = LightShadows.None;
                var fl = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(fl.GetComponent<Collider>());
                fl.transform.SetParent(muzzle, false); fl.transform.localScale = Vector3.one * (W.muzzleDevice == "suppressor" ? .08f : .2f);
                muzzleFlash = fl.GetComponent<Renderer>(); muzzleFlash.material = new Material(Fx.Additive);
                muzzleFlash.material.SetColor("_TintColor", new Color(1f, .75f, .4f));
                muzzleFlash.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; muzzleFlash.enabled = false;
                return;
            }
            viewHip = ToonAssets.Available ? new Vector3(.2f, -.2f, .42f) : new Vector3(.19f, -.2f, .36f);
            viewPivot.localPosition = viewHip;
            viewGun = GunModel.Create(W, out viewParts, Hero.tint, true);
            viewGun.SetParent(viewPivot, false);
            var vAnim = viewGun.GetComponent<FoodGunAnim>(); if (vAnim != null) vAnim.owner = this;
            if (ToonAssets.Available && viewPan == null)   // V17：近战改成平底锅
            {
                var pf = Resources.Load<GameObject>("Toon/FoodGuns/Pan");
                if (pf != null)
                {
                    viewPan = Instantiate(pf, viewPivot.parent, false).transform; viewPan.name = "平底锅";
                    foreach (var r in viewPan.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    viewPan.gameObject.SetActive(false);
                }
            }
            foreach (var r in viewGun.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            muzzle = viewParts.muzzle; ejectPort = viewParts.eject; bolt = viewParts.bolt;
            viewAds = -viewParts.sightLocal + new Vector3(0, 0, W.sight == "scope" ? .2f : .26f);
            muzzleLight = muzzle.gameObject.AddComponent<Light>(); muzzleLight.type = LightType.Point; muzzleLight.range = 5;
            muzzleLight.color = Color.Lerp(Hero.tint, new Color(1f, .8f, .5f), .5f); muzzleLight.intensity = 0; muzzleLight.shadows = LightShadows.None;
            var flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(flash.GetComponent<Collider>());
            flash.transform.SetParent(muzzle, false); flash.transform.localScale = Vector3.one * (W.muzzleDevice == "suppressor" ? .08f : .2f);
            muzzleFlash = flash.GetComponent<Renderer>(); muzzleFlash.material = new Material(Fx.Additive);
            muzzleFlash.material.SetColor("_TintColor", Color.Lerp(Hero.tint, new Color(1f, .75f, .4f), .6f));
            muzzleFlash.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; muzzleFlash.enabled = false;
        }

        public void ChangeRole(Role r)
        {
            role = r; ammo = W.magazine; skillCharges = Hero.skill.maxCharges; skillRechargeAt = 0;
            BuildVisuals();
        }

        public void Respawn(Vector3 pos, float faceYaw)
        {
            // survivors keep their gun; the fallen start the next round with the free pistol
            bool survived = alive;
            if (Match.I != null && Match.I.testMode) weapon = null;
            else if (!survived || weapon == null) weapon = Config.Pistol;
            cc.enabled = false; transform.position = pos; cc.enabled = true;
            yaw = faceYaw; pitch = 0; transform.rotation = Quaternion.Euler(0, yaw, 0);
            alive = true; hp = Config.MaxHp; armor = Config.MaxArmor; ammo = W.magazine; skillCharges = Hero.skill.maxCharges; skillRechargeAt = 0; guardUntil = 0;
            reloadEndAt = 0; bloom = 0; verticalSpeed = 0; velocity = Vector3.zero; mantling = false; slideUntil = 0; recoilDebt = 0; landDip = 0; lean = 0; botLean = 0; grenades = 1; sprayShots = 0; meleeUntil = 0; crouching = aiming = sprinting = false; adsToggle = false;
            revealedUntil = hiddenFromScanUntil = jammedUntil = shieldActiveUntil = dashUntil = aimRecoveryUntil = 0;
            damagedBy.Clear(); usingInteract = null; burstLeft = 0; bombBusy = false; roundKills = 0;
            stats.Clear();
            if (avatar != null) avatar.Revive();
            if (builtFor != W) BuildVisuals();
            if (human) { AttachCamera(); if (viewPivot != null) viewPivot.gameObject.SetActive(true); }
            SetHeight(false); freezeUntil = 0;
            if (ToonAssets.Available) Food.OnRespawn();
        }

        void SetHeight(bool crouch)
        {
            crouching = crouch;
            cc.height = crouch ? 1.28f : 1.92f; cc.center = new Vector3(0, cc.height / 2, 0);
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (!alive || Match.I == null) return;
            bool canAct = Match.I.CanAct;
            bool frozen = Match.I.phase == Phase.RoundEnd || Match.I.phase == Phase.MatchEnd || (human && Match.I.ui != null && Match.I.ui.Blocking);
            if (IsReloading && Time.time >= reloadEndAt) { ammo = W.magazine; reloadEndAt = 0; }
            if (skillCharges < Hero.skill.maxCharges && Match.I.CanAct)
            {
                if (skillRechargeAt <= 0) skillRechargeAt = Time.time + Hero.skill.cooldown;
                else if (Time.time >= skillRechargeAt)
                {
                    skillCharges++; skillRechargeAt = skillCharges < Hero.skill.maxCharges ? Time.time + Hero.skill.cooldown : 0;
                    if (human) Sfx.Ui("maximize", .35f, 1.5f);
                }
            }
            bloom = Mathf.MoveTowards(bloom, 0, Time.deltaTime * (W.maxBloom > 0 ? W.maxBloom * 2.2f : 1));

            Vector3 wish = Vector3.zero; bool wantSprint = false, wantCrouch = false, jump = false, trigger = false, ads = false, use = false;
            if (!frozen)
            {
                if (human)
                {
                    mouseSensitivity = GameSettings.Sensitivity;
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        float adsMul = aiming ? .6f / Mathf.Max(1, W.adsZoom * .7f) * GameSettings.AdsSensitivity : 1f;
                        float my = Input.GetAxis("Mouse Y") * mouseSensitivity * adsMul;
                        yaw += Input.GetAxis("Mouse X") * mouseSensitivity * adsMul;
                        pitch = Mathf.Clamp(pitch - my, -85, 85);
                        if (my < 0) recoilDebt = Mathf.Max(0, recoilDebt + my);   // player pulled down: they compensated themselves
                    }
                    float x = Input.GetAxisRaw("Horizontal"), z = Input.GetAxisRaw("Vertical");
                    wish = Quaternion.Euler(0, yaw, 0) * new Vector3(x, 0, z);
                    wantSprint = Input.GetKey(KeyCode.LeftShift) && z > .1f;
                    wantCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
                    jump = Input.GetKeyDown(KeyCode.Space);
                    trigger = Input.GetMouseButton(0) && canAct;
                    if (Input.GetMouseButtonDown(1) && !Skills.Previewing(this)) { adsToggle = !adsToggle; if (adsToggle) Sfx.Ui("click", .25f, 1.6f); }
                    if (wantSprint && wish.sqrMagnitude > .1f) adsToggle = false;
                    ads = adsToggle;
                    use = (Input.GetKey(KeyCode.G) || scriptUse) && canAct;
                    if (Input.GetKeyDown(KeyCode.R)) Reload();
                    UpdateLean((Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0));
                    if (canAct && grenades > 0 && Input.GetKey(KeyCode.Alpha4)) PreviewGrenade(true);
                    if (Input.GetKeyUp(KeyCode.Alpha4)) { PreviewGrenade(false); if (canAct && grenades > 0) ThrowGrenade(Grenade.HumanVelocity(this)); }
                    if (canAct && Input.GetKeyDown(KeyCode.V)) Melee();
                    if (ToonAssets.Available)
                    {
                        if (canAct && Input.GetKeyDown(KeyCode.X)) Food.TryUlt();   // V18：大招
                        if (Input.GetKeyDown(KeyCode.Alpha1)) Food.Emote("Wave");    // 表情
                        if (Input.GetKeyDown(KeyCode.Alpha2)) Food.Emote("Yes");
                        if (Input.GetKeyDown(KeyCode.Alpha3)) Food.Emote("No");
                    }
                    Skills.HumanInput(this, canAct);
                }
                else
                {
                    wish = botMove; wantSprint = botSprint; wantCrouch = botCrouch; jump = botJump; trigger = botTrigger && canAct; ads = botAim; use = (botUse || scriptUse) && canAct;
                    botJump = false;
                    UpdateLean(botLean);
                }
            }
            HandleUse(use);
            if (bombBusy || usingInteract != null) { wish = Vector3.zero; trigger = false; }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (wantCrouch != crouching && grounded && !Dashing)
            {
                if (wantCrouch || !Physics.SphereCast(transform.position + Vector3.up * .5f, .3f, Vector3.up, out _, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                    SetHeight(wantCrouch);
            }
            // Slide: crouch while sprinting. Mantle: jump into a ledge between 0.55 m and 2 m.
            if (human && !frozen && wantCrouch && sprinting && grounded && !Sliding && Time.time >= slideReadyAt && !bombBusy) StartSlide();
            if (Sliding && jump) slideUntil = 0;
            if (human && !frozen && jump && !mantling && TryMantle()) jump = false;
            if (mantling) { UpdateMantle(); wish = Vector3.zero; trigger = false; jump = false; }
            bool wasAiming = aiming;
            aiming = ads && !Dashing && !IsReloading && grounded && !bombBusy && !Sliding && !mantling;
            if (aiming && !wasAiming) aimedAt = Time.time;
            if (!aiming) aimedAt = -1;
            if (human && (IsReloading || bombBusy || Dashing)) adsToggle = false;
            sprinting = wantSprint && !aiming && (!crouching || Sliding) && !trigger && wish.sqrMagnitude > .1f && !ShieldUp && !mantling;
            Move(wish, jump);
            if (Dashing) trigger = false;
            HandleTrigger(trigger);
            if (human) UpdateView();
        }

        void Move(Vector3 wish, bool jump)
        {
            if (Dashing || mantling) return;
            float speed = sprinting ? Config.SprintSpeed : crouching ? Config.CrouchSpeed : Config.WalkSpeed;
            if (aiming) speed *= .6f;
            speed *= W.moveMultiplier;
            if (ShieldUp) speed *= Config.ShieldMoveMultiplier;
            if (Time.time < jammedUntil) speed *= Config.NoiseSlow;
            if (Time.time < creamUntil) speed *= .85f;
            if (ToonAssets.Available) speed *= Food.SpeedMul();
            if (Time.time < freezeUntil) speed *= .1f;   // 被冰淇淋冻住
            int sauce = grounded ? SauceZone.At(transform.position) : SauceZone.None;
            if (sauce == SauceZone.Chocolate) speed *= .7f;   // V17：巧克力酱黏脚
            else if (sauce == SauceZone.Butter) speed *= 1.1f;
            if (Match.I.BombCarrier == this) speed *= .96f;
            Vector3 target = Vector3.ClampMagnitude(wish, 1f) * speed;
            float accel = grounded ? (sauce == SauceZone.Butter ? (SauceZone.SlickAt(transform.position) ? 2f : 4.5f) : 40f) : 6f;   // 黄油地面打滑
            Vector3 planar = new Vector3(velocity.x, 0, velocity.z);
            // 弹射板飞行中保持惯性（不按方向键就不减速），保证能落到高台上
            if (launched && !grounded) { if (wish.sqrMagnitude > .01f) planar = Vector3.MoveTowards(planar, planar + target * .25f, 2f * Time.deltaTime); }
            else planar = Vector3.MoveTowards(planar, target, accel * Time.deltaTime);
            if (Sliding && grounded)
            {
                float k = Mathf.Clamp01((slideUntil - Time.time) / .8f);
                Vector3 steer = Vector3.ClampMagnitude(wish, 1f) * .25f;
                planar = (slideDir + steer).normalized * Mathf.Lerp(Config.WalkSpeed * .8f, slideSpeed, Mathf.Sqrt(k));
            }
            grounded = cc.isGrounded;
            if (grounded && verticalSpeed < 0)
            {
                verticalSpeed = -2f;
                if (jump && !crouching && !bombBusy) verticalSpeed = 5.6f;
            }
            verticalSpeed -= 17f * Time.deltaTime;
            velocity = planar + Vector3.up * verticalSpeed;
            var flags = cc.Move(velocity * Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            bool wasGrounded = grounded;
            grounded = cc.isGrounded;
            if (grounded && verticalSpeed <= 0) launched = false;
            if (!wasGrounded && grounded && human) { landDip = Mathf.Clamp(-verticalSpeedBeforeLand * .012f, .02f, .12f); punchPitch += 1.5f + landDip * 10f; Footstep(.45f, .8f); }
            if (!grounded) verticalSpeedBeforeLand = verticalSpeed;
            if (grounded && planar.magnitude > 3.2f && !crouching && Time.time >= footstepAt)
            {
                footstepAt = Time.time + (sprinting ? .32f : .42f);
                Footstep(human ? .25f : .6f, sprinting ? 1.05f : .95f);
            }
        }

        public void ApplyDash(Vector3 delta) { cc.Move(delta); }
        float verticalSpeedBeforeLand;

        // Surface-aware footsteps: metal floors / plates ring, everything else is concrete.
        void Footstep(float vol, float pitchMul)
        {
            string group = "footstep_concrete"; float pitchK = 1f;
            if (Physics.Raycast(transform.position + Vector3.up * .3f, Vector3.down, out RaycastHit h, .8f, ~0, QueryTriggerInteraction.Ignore))
            {
                var r = h.collider.GetComponent<Renderer>();
                string m = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
                if (m.Contains("Metal") || m.Contains("Plate") || m.Contains("Sheet") || m.Contains("Corrugated") || m.Contains("Rusty")) { group = "impactMetal_light"; pitchK = 1.35f; vol *= .55f; }
            }
            Sfx.Play(group, transform.position, vol, pitchMul * pitchK, human ? 0f : 1f, 28f);
        }

        // Raycast that ignores this fighter and other fighters.
        bool EnvCast(Vector3 o, Vector3 d, float len, out RaycastHit best)
        {
            best = default; bool any = false;
            foreach (var h in Physics.RaycastAll(o, d, len, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<Fighter>() != null) continue;
                if (!any || h.distance < best.distance) { best = h; any = true; }
            }
            return any;
        }

        public void ThrowGrenade(Vector3 velocity)
        {
            if (grenades <= 0 || !alive) return;
            grenades--;
            Grenade.Throw(this, velocity);
            if (Match.I != null) Match.I.Feed(Config.TeamName(team) + " " + Hero.name + " 投出了手雷", new Color(1f, .6f, .3f));
        }

        void PreviewGrenade(bool on)
        {
            if (!on) { if (grenadeArc != null) grenadeArc.enabled = false; return; }
            if (grenadeArc == null)
            {
                grenadeArc = new GameObject("手雷轨迹").AddComponent<LineRenderer>();
                grenadeArc.material = Fx.Additive; grenadeArc.startWidth = .04f; grenadeArc.endWidth = .02f;
                grenadeArc.startColor = grenadeArc.endColor = new Color(1f, .55f, .2f, .8f);
                grenadeArc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            grenadeArc.enabled = true;
            var pts = new List<Vector3>(); Vector3 pos = Eye + AimDir * .5f + Vector3.down * .15f, v = Grenade.HumanVelocity(this);
            pts.Add(pos);
            for (int i = 0; i < 60; i++)
            {
                Vector3 next = pos + v * .04f; v += Physics.gravity * .04f;
                if (Physics.Linecast(pos, next, out RaycastHit h, ~0, QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(transform)) { pts.Add(h.point); break; }
                pts.Add(next); pos = next;
            }
            grenadeArc.positionCount = pts.Count; grenadeArc.SetPositions(pts.ToArray());
        }

        // 枪托近战：1.9 m 内 55 伤害，从背后打 120（背刺）
        public bool Melee()
        {
            if (Time.time < meleeReadyAt || !alive) return false;
            meleeReadyAt = Time.time + .75f; meleeUntil = Time.time + .3f;
            Sfx.Play("impactGeneric_light", transform.position, .4f, .7f, human ? 0f : 1f, 15f);
            Fighter best = null; float bestD = 1.9f;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == team) continue;
                Vector3 to = e.Chest - Eye; float d = to.magnitude;
                if (d > bestD || Vector3.Angle(AimDir, to) > 35f) continue;
                if (!Skills.LineClear(Eye, e.Chest, e)) continue;
                best = e; bestD = d;
            }
            if (best == null) return false;
            bool back = Vector3.Angle(best.transform.forward, transform.forward) < 60f;
            float dmg = back ? 120f : 55f;
            bool killed = best.ReceiveDamage(dmg, this, transform.position, false);
            Sfx.Play(ToonAssets.Available ? "food_bonk" : "impactPunch_heavy", best.Chest, 1f, back ? .8f : 1f, human ? 0f : 1f, 20f);
            Fx.Burst(best.Chest, -transform.forward, new Color(1f, .2f, .15f), 14, 3f, .06f, .3f, .8f);
            if (human)
            {
                hitMarkerUntil = Time.time + (killed ? .4f : .2f); hitMarkerHead = back; hitMarkerKill = killed; hitPunchAt = Time.time;
                Fx.AddNumber(best.Chest, dmg, back, false, killed, best); punchPitch -= 3f; Fx.shake = Mathf.Max(Fx.shake, .25f);
                if (killed) { killBanner = (back ? "背刺击杀 " : "近战击杀 ") + best.Hero.name; killBannerUntil = Time.time + 2.2f; lastVictim = best; lastKillHead = back; roundKills++; }
            }
            return true;
        }

        public void StartSlide()
        {
            Vector3 flat = new Vector3(velocity.x, 0, velocity.z);
            slideDir = flat.sqrMagnitude > .5f ? flat.normalized : transform.forward;
            slideSpeed = Mathf.Max(flat.magnitude, Config.SprintSpeed) * 1.3f;
            slideUntil = Time.time + .8f; slideReadyAt = Time.time + 1.1f;
            if (!crouching) SetHeight(true);
            Sfx.Play("impactGeneric_light", transform.position, .5f, .55f, 0f); fovKick += 2f;
        }
        public bool DebugMantle() { return TryMantle(); }
        public void StopMantle() { if (mantling) { mantling = false; cc.enabled = true; } }
        // V17：AI 翻越美食方块
        public bool BotMantle(Vector3 dir)
        {
            if (mantling || !grounded) return false;
            float oldYaw = yaw; yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (TryMantle()) return true;
            yaw = oldYaw; return false;
        }

        bool TryMantle()
        {
            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward, feet = transform.position;
            bool wall = EnvCast(feet + Vector3.up * .9f, fwd, 1.1f, out RaycastHit w) || EnvCast(feet + Vector3.up * .6f, fwd, 1.1f, out w) || EnvCast(feet + Vector3.up * 1.5f, fwd, 1.1f, out w);
            if (!wall) return false;
            Vector3 probe = new Vector3(w.point.x, feet.y + 2.4f, w.point.z) + fwd * .45f;
            if (!EnvCast(probe, Vector3.down, 2.4f, out RaycastHit top)) return false;
            float h = top.point.y - feet.y;
            if (h < .55f || h > 2.05f || top.normal.y < .7f) return false;
            Vector3 dest = top.point + Vector3.up * .04f;
            float r = cc.radius * .9f;
            if (Physics.CheckCapsule(dest + Vector3.up * (r + .1f), dest + Vector3.up * (1.8f - r), r, ~0, QueryTriggerInteraction.Ignore)) return false;
            mantling = true; mantleT = 0; mantleFrom = feet; mantleTo = dest;
            cc.enabled = false; velocity = Vector3.zero; verticalSpeed = 0; slideUntil = 0;
            if (human) { Sfx.Play("impactSoft_heavy", feet, .45f, .9f, 0f); punchPitch -= 3f; }
            return true;
        }

        void UpdateMantle()
        {
            mantleT += Time.deltaTime / .38f;
            float k = Mathf.Clamp01(mantleT);
            float up = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k / .6f)), fwd = Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - .35f) / .65f));
            Vector3 p = new Vector3(Mathf.Lerp(mantleFrom.x, mantleTo.x, fwd), Mathf.Lerp(mantleFrom.y, mantleTo.y + .08f, up), Mathf.Lerp(mantleFrom.z, mantleTo.z, fwd));
            transform.position = p;
            if (k >= 1f)
            {
                mantling = false; cc.enabled = true; grounded = true;
                velocity = Quaternion.Euler(0, yaw, 0) * Vector3.forward * 2f;
                if (human) Footstep(.4f, .9f);
            }
        }

        bool launched;
        // V9 movement feel: slide, mantle, recoil recovery, camera tilt
        float slideUntil, slideReadyAt, slideSpeed; Vector3 slideDir;
        public bool Sliding { get { return Time.time < slideUntil; } }
        bool mantling; float mantleT; Vector3 mantleFrom, mantleTo;
        public bool Mantling { get { return mantling; } }
        float recoilDebt, camRoll, landDip;
        // V11（借鉴 AssaultCube）：连射后坐递增、开镜稳定时间、手雷、近战
        public int sprayShots; public int grenades = 1;
        float meleeReadyAt; public float meleeUntil; public float creamUntil; Transform viewPan; LineRenderer grenadeArc;
        public float ScopeSettle { get { return aiming && W.adsZoom >= 2f && aimedAt > 0 ? Mathf.Clamp01((Time.time - aimedAt) / .35f) : 1f; } }
        public void Launch(Vector3 v)
        {
            velocity = new Vector3(v.x, 0, v.z); verticalSpeed = v.y; grounded = false; launched = true;
            cc.Move(Vector3.up * .1f);
            if (human) { punchPitch -= 4f; Fx.shake = Mathf.Max(Fx.shake, .3f); }
        }

        void HandleUse(bool held)
        {
            // Bomb first (plant / defuse), then world interactables.
            if (Match.I.BombAction(this, held, Time.deltaTime)) { if (usingInteract != null) { usingInteract.Release(this); usingInteract = null; } return; }
            Interactable near = held ? Interactable.Nearest(this) : null;
            if (near != null) { usingInteract = near; if (near.Hold(this, Time.deltaTime)) usingInteract = null; }
            else if (usingInteract != null) { usingInteract.Release(this); usingInteract = null; }
        }

        // ------------------------------------------------------------------ weapon
        void HandleTrigger(bool held)
        {
            bool pressed = held && !triggerWasHeld;
            triggerWasHeld = held;
            if (burstLeft > 0 && Time.time >= nextBurstAt) { FireBullet(); burstLeft--; nextBurstAt = Time.time + W.burstInterval; }
            if (!held || Time.time < nextShotAt || IsReloading || burstLeft > 0 || Skills.Previewing(this)) return;
            if (!W.automatic && !pressed) return;
            if (ammo <= 0) { Reload(); if (human) Sfx.Ui("click", .4f, 1.6f); return; }
            nextShotAt = Time.time + W.ShotInterval;
            if (W.burst > 1) { burstLeft = Mathf.Min(W.burst, ammo) - 1; FireBullet(); nextBurstAt = Time.time + W.burstInterval; }
            else FireBullet();
        }

        public float CurrentSpread()
        {
            float s = aiming ? Mathf.Lerp(W.hipSpread * .5f, W.adsSpread, ScopeSettle) : W.hipSpread;
            float move = new Vector2(velocity.x, velocity.z).magnitude;
            if (move > .6f) s += aiming ? .5f : 1.3f;
            if (!grounded) s += 3f;
            if (crouching) s *= .75f;
            if (Time.time < aimRecoveryUntil) s += 3f;
            if (Time.time < jammedUntil) s *= Config.NoiseSpread;
            return s + bloom;
        }

        void FireBullet()
        {
            if (ammo <= 0) return;
            if (Time.time - lastShotAt > .28f) sprayShots = 0;
            sprayShots++;
            ammo--; stats.shots++; lastShotAt = Time.time;
            if (ToonAssets.Available && Food.InfiniteAmmo) ammo++;   // 奶茶杯形态：开火不耗弹药
            Vector3 origin = human ? cam.transform.position : Eye;   // Eye/camera include the lean offset
            Vector3 dir = human ? cam.transform.forward : AimDir;
            float spread = CurrentSpread();
            Vector3 visualStart = human && muzzle != null ? muzzle.position : (avatar != null ? avatar.MuzzlePosition : origin);
            Color tc = ToonAssets.Available ? DessertGuns.Candy(stats.shots) : Color.Lerp(Hero.tint, new Color(1f, .85f, .5f), .35f);   // candy-coloured rounds
            for (int p = 0; p < W.pellets; p++)
            {
                Vector2 rnd = Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
                Vector3 d = (dir + Quaternion.LookRotation(dir) * new Vector3(rnd.x, rnd.y, 0)).normalized;
                Bullet.Fire(this, origin, visualStart, d, tc);
            }
            bloom = Mathf.Min(W.maxBloom, bloom + W.bloomPerShot * (W.automatic && sprayShots <= 2 ? .4f : 1f));
            bool suppressed = W.muzzleDevice == "suppressor";
            if (ToonAssets.Available) Sfx.Play(FoodFx.For(W).sound, visualStart, human ? .7f : .9f, Random.Range(.94f, 1.08f), human ? .1f : 1f, 70f);   // V17：噗、啵、咕叽
            else Sfx.Play(W.sound, visualStart, (human ? .55f : .85f) * (suppressed ? .55f : 1f), W.soundPitch * (suppressed ? 1.15f : 1f), human ? .1f : 1f, suppressed ? 45f : 90f);
            // V13：远处听到的是带回声的远场枪声（Sonniss 远距离录音），近处的干声随距离被盖过
            if (!human && Camera.main != null && !suppressed && !ToonAssets.Available)
            {
                float d = Vector3.Distance(Camera.main.transform.position, visualStart);
                if (d > 28f) Sfx.Play("gun_far", visualStart, Mathf.Clamp01((d - 28f) / 30f) * .8f, Random.Range(.92f, 1.05f) * (W.pellets > 1 ? .85f : 1f), 1f, 260f);
            }
            if (human && W.pellets == 1 && !ToonAssets.Available && Random.value < .35f) Sfx.Play("shell", transform.position + Vector3.down * .2f, .18f, Random.Range(.9f, 1.15f), 0f);
            Match.I.NoiseAt(this, transform.position);
            if (human)
            {
                float ramp = W.automatic ? Mathf.Min(Mathf.Pow(sprayShots / 4f, 2f) + .45f, 2.2f) : 1f;   // first shots stay on target, long sprays climb
                float settle = Mathf.Lerp(1f, .33f, W.sight == "scope" && aiming ? ScopeSettle : 0f);      // a settled scope kicks a third as much
                float kick = W.recoil * ramp * settle * Random.Range(.8f, 1.2f) * (aiming ? .7f : 1f) * (crouching ? .8f : 1f);
                pitch -= kick; yaw += Random.Range(-.35f, .35f) * W.recoil;
                recoilDebt += kick * .6f;   // Apex-style: most of the climb settles back after the spray
                punchPitch -= kick * .6f;
                viewKick = Mathf.Min(viewKick + .025f + W.recoil * .015f, .12f); boltKick = 1f;
                fovKick += .35f + W.recoil * .25f; punchYaw += Random.Range(-.3f, .3f) * W.recoil;
                if (muzzleLight != null) { muzzleLight.intensity = suppressed ? 1f : 4f; muzzleFlash.enabled = true; muzzleFlash.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0, 360)); }
                if (ejectPort != null && W.pellets == 1 && !ToonAssets.Available) Fx.Shell(ejectPort.position, cam.transform.right, cam.transform.up);
                if (fpsView != null) fpsView.OnShot();
                if (ToonAssets.Available) Fx.Burst(muzzle.position, muzzle.forward, FoodFx.For(W).splat, 6, 3.5f, .03f, .3f, .8f);   // 枪口喷出一点酱汁
                else Fx.Burst(muzzle.position, muzzle.forward, new Color(1f, .7f, .35f), suppressed ? 2 : 6, 4, .03f, .12f, 0);
            }
            else if (avatar != null)
            {
                avatar.MuzzleFlash();
                if (W.pellets == 1 && !ToonAssets.Available && Camera.main != null && Vector3.Distance(Camera.main.transform.position, transform.position) < 15f)
                    Fx.Shell(avatar.MuzzlePosition - transform.forward * .4f, transform.right, Vector3.up);
            }
        }

        // Called by Bullet for each collider on its path. Returns true if the bullet stops here.
        public bool Resolve(RaycastHit h, Vector3 dir, float distance, WeaponSpec w)
        {
            var f = h.collider.GetComponentInParent<Fighter>();
            if (f != null)
            {
                if (f == this || f.team == team || !f.alive) return false;
                if (ToonAssets.Available && Time.time < f.meleeUntil + .05f && Vector3.Angle(f.transform.forward, -dir) < 70f)
                {
                    // V17：平底锅挥动的瞬间能挡住正面打来的子弹
                    Sfx.Play("food_bonk", h.point, .8f, Random.Range(1.05f, 1.25f), 1f, 30f);
                    Fx.Sparks(h.point, -dir, new Color(1f, .95f, .6f), 8);
                    return true;
                }
                float falloff = Falloff(distance, w);
                bool head = h.point.y >= f.transform.position.y + (f.crouching ? 1.02f : 1.55f);
                float dmg = w.bodyDamage * falloff * (head ? (w.pellets > 1 ? 1.4f : Config.HeadMultiplier) : 1f);
                if (!FoodState.Incoming(f, this, dir, ref dmg)) return true;   // 幻频甩糖 / 汉堡盾
                bool armorHit = f.armor > 0;
                bool killed = f.ReceiveDamage(dmg, this, transform.position, head);
                stats.hits++; if (head) stats.headshots++;
                FoodState.OnHit(this, f, dmg);
                if (killed && ToonAssets.Available) { if (human) Collection.AddKill(); else if (Random.value < .35f) Food.Emote("Wave"); }
                if (ToonAssets.Available && w.id == "BR-5") f.creamUntil = Time.time + .8f;   // 奶油糊在身上：短暂减速
                if (human)
                {
                    hitMarkerUntil = Time.time + (killed ? .4f : .18f); hitMarkerHead = head; hitMarkerKill = killed;
                    bool broke = armorHit && f.armor <= 0 && !killed;
                    // Apex-style layered hit audio: shield tick vs flesh thud, headshot ping, armor shatter.
                    float heavy = Mathf.Clamp01(dmg / 60f);   // Xonotic-style: bigger hits sound lower
                    if (armorHit) Sfx.Ui("impactMetal_light", .42f, Mathf.Lerp(1.9f, 1.25f, heavy)); else Sfx.Ui(ToonAssets.Available ? "food_squish" : "hit_body", .6f, Mathf.Lerp(1.2f, .85f, heavy));
                    if (head) Sfx.Ui("impactBell_heavy", .28f, 1.9f);
                    if (broke) { Sfx.Ui("impactGlass_heavy", .6f, 1.25f); armorBreakMarkAt = Time.time; Fx.Burst(f.Chest, -dir, new Color(.35f, .75f, 1f), 40, 6f, .05f, .6f, 1.2f); Fx.Flash(f.Chest, new Color(.3f, .7f, 1f), 3f, 4f, .2f); }
                    if (killed) Sfx.Ui("confirmation", .5f, 1f);
                    fovKick += head ? 1.4f : .7f;
                    Fx.AddNumber(h.point, dmg, head, armorHit, killed, f); hitPunchAt = Time.time;
                    if (killed) { killBanner = (head ? "爆头击杀 " : "击杀 ") + f.Hero.name; killBannerUntil = Time.time + 2.2f; lastVictim = f; lastKillHead = head; roundKills++; Match.I.RequestHitstop(); Sfx.Ui("impactBell_heavy", .45f, .7f); punchPitch -= 2f; }
                }
                if (ToonAssets.Available)
                {
                    var th = FoodFx.For(w);
                    Fx.Burst(h.point, -dir, th.splat, head ? 20 : 12, 3.5f, .07f, .45f, 1.1f);
                    if (armorHit) Fx.Burst(h.point, -dir, new Color(.5f, .85f, 1f), 5, 2.5f, .04f, .25f, .6f);
                    if (!human) Sfx.Play("food_squish", h.point, .45f, Random.Range(.9f, 1.1f), 1f, 25f);
                }
                else Fx.Burst(h.point, -dir, armorHit ? new Color(.4f, .8f, 1f) : new Color(1f, .15f, .15f), head ? 16 : 9, 3f, .06f, .3f, .8f);
                return true;
            }
            var target = h.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                if (ToonAssets.Available && w.id == "BR-5" && target is WallPiece wp && wp.wall != null && wp.wall.layers != null)
                {   // 奶油打到蛋糕墙：把旁边打掉的块补回来
                    wp.wall.RepairNear(wp); FoodFx.Impact(h.point, h.normal, w); return true;
                }
                float coverDmg = w.bodyDamage * Falloff(distance, w) * (role == Role.Ironwall ? 1.25f : 1f);
                if (target.TakeDamage(coverDmg, this, false))
                {
                    if (human) { hitMarkerUntil = Time.time + .1f; hitMarkerHead = false; hitMarkerKill = false; }
                    if (ToonAssets.Available) FoodFx.Impact(h.point, h.normal, w); else Fx.Sparks(h.point, h.normal, new Color(.5f, .9f, 1f), 6);
                    return true;
                }
                return false;
            }
            if (ToonAssets.Available) { var jg = h.collider.GetComponentInParent<FoodJiggle>(); if (jg != null) jg.Hit(); FoodReact.OnImpact(this, h, w); FoodFx.Impact(h.point, h.normal, w); return true; }   // V17：墙上地上留下酱汁
            Fx.Sparks(h.point, h.normal, new Color(1f, .75f, .4f), 7);
            Fx.Burst(h.point, h.normal, new Color(.5f, .5f, .52f, .5f), 4, 1.2f, .12f, .6f, .1f, Fx.Smoke);
            Fx.BulletHole(h.point, h.normal, Random.value < .3f);
            if (Random.value < .55f)
            {
                var hr = h.collider.GetComponent<Renderer>(); string mn = hr != null && hr.sharedMaterial != null ? hr.sharedMaterial.name : "";
                bool metal = mn.Contains("Metal") || mn.Contains("Plate") || mn.Contains("Sheet") || mn.Contains("Corrugated") || mn.Contains("Rusty") || mn.Contains("Gun");
                Sfx.Play(metal ? "hit_metal" : "hit_concrete", h.point, metal ? .45f : .5f, Random.Range(.9f, 1.12f), 1f, 30f);
            }
            return true;
        }

        static float Falloff(float d, WeaponSpec w)
        {
            if (d <= w.fullRange) return 1f;
            return Mathf.Lerp(1f, w.minFalloff, Mathf.InverseLerp(w.fullRange, w.falloffRange, d));
        }

        public void Reload()
        {
            if (IsReloading || ammo >= W.magazine || !alive) return;
            reloadEndAt = Time.time + W.reload; burstLeft = 0;
            Sfx.Play(W.pellets > 1 ? "reload_shotgun" : W.id == "SMG-7" || W.id == "P-9" ? "reload_pistol" : W.id == "DMR-3" ? "reload_dmr" : "reload_rifle", transform.position, human ? .6f : .7f, W.reload < 2f ? 1.1f : 1f, human ? 0 : 1, 18f);
            if (human && viewParts != null && viewParts.mag != null)
            {
                var drop = Instantiate(viewParts.mag.gameObject, viewParts.mag.position, viewParts.mag.rotation);
                drop.transform.localScale = viewParts.mag.lossyScale;
                foreach (var r in drop.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var b = drop.AddComponent<Ballistic>(); b.velocity = -cam.transform.up * 1.5f + cam.transform.forward * .5f; b.life = 3f; b.bounce = true;
                Invoke(nameof(ReloadClick), W.reload * .6f);
            }
        }
        void ReloadClick() { Sfx.Ui("click", .45f, .8f); }

        // ------------------------------------------------------------------ damage
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            return ReceiveDamage(amount, source, source != null ? source.transform.position : transform.position, false, explosive);
        }

        // Armor absorbs damage first (有效生命 150). Returns true if this hit killed.
        public bool ReceiveDamage(float amount, Fighter source, Vector3 from, bool head, bool explosive = false)
        {
            if (!alive || Match.I.phase != Phase.Live) return false;
            if (Time.time < guardUntil) amount *= 1f - Config.DashDamageReduction;
            if (explosive && ToonAssets.Available && role == Role.Ironwall) amount *= .5f;   // 铁幕被动
            float total = amount;
            float toArmor = Mathf.Min(armor, amount);
            armor -= toArmor; amount -= toArmor;
            if (toArmor > 0 && armor <= 0) { lastArmorBreakAt = Time.time; Sfx.Play("impactGlass_heavy", Chest, human ? .5f : .7f, 1.6f, human ? 0 : 1, 25f); }
            float dealt = Mathf.Min(hp, amount) + toArmor;
            hp -= amount;
            lastDamageAt = Time.time; lastDamageFrom = from;
            if (source != null && source != this)
            {
                source.stats.damage += dealt; damagedBy[source] = Time.time;
                Skills.NotifyDamage(source, this, dealt);
                Match.I.OnHit(source, this);
                if (TryGetComponent(out Bot bot)) bot.Hear(source, from, 2f);
            }
            if (avatar != null) avatar.Flinch();
            if (human)
            {
                Sfx.Ui(toArmor > 0 ? "impactPlate_heavy" : "hit_body", toArmor > 0 ? .5f : .75f, toArmor > 0 ? 1.2f : .9f);
                if (fpsView != null) fpsView.OnHit();
                Vector3 local = transform.InverseTransformDirection((transform.position - from).normalized);
                punchPitch -= 2.5f + total * .06f; punchYaw += local.x * 3f;
                Fx.shake = Mathf.Max(Fx.shake, explosive ? .7f : .18f);
            }
            if (hp > 0) return false;
            Die(source, head);
            return true;
        }

        void Die(Fighter killer, bool head)
        {
            hp = 0; alive = false; stats.deaths++; bombBusy = false;
            if (ToonAssets.Available) Food.OnDeath();
            if (ToonAssets.Available) FoodFx.KillPop(Chest);   // V17：噗——变成一团彩带
            else
            {
                Fx.Burst(Chest, Vector3.up, Config.TeamColor(team), 60, 5, .1f, 1.1f, -.2f);
                Fx.Sparks(Chest, Vector3.up, Color.Lerp(Config.TeamColor(team), Color.white, .4f), 30);
            }
            Fx.Flash(Chest, Config.TeamColor(team), 4f, 6f, .4f);
            if (usingInteract != null) { usingInteract.Release(this); usingInteract = null; }
            cc.enabled = false;
            if (avatar != null) avatar.Die(killer != null ? (transform.position - killer.transform.position).normalized : -transform.forward);
            if (human && viewPivot != null) viewPivot.gameObject.SetActive(false);
            foreach (var kv in damagedBy)
                if (kv.Key != killer && kv.Key.team != team && Time.time - kv.Value < 6f) kv.Key.stats.assists++;
            if (killer != null && killer.team != team) killer.stats.kills++;
            Skills.NotifyKill(killer, this);
            Match.I.OnKilled(this, killer, head);
        }

        // ------------------------------------------------------------------ first-person presentation
        void UpdateView()
        {
            landDip = Mathf.MoveTowards(landDip, 0, Time.deltaTime * .5f);
            float eye = (crouching ? 1.08f : 1.62f) - (Sliding ? .12f : 0f) - landDip;
            cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, new Vector3(lean * .38f, eye - Mathf.Abs(lean) * .06f, 0), Time.deltaTime * (mantling ? 20f : 14f));
            if (Time.time - lastShotAt > .14f && recoilDebt > 0)
            {
                float step = Mathf.Min(recoilDebt, Time.deltaTime * Mathf.Max(3f, recoilDebt * 7f));
                pitch = Mathf.Clamp(pitch + step, -85, 85); recoilDebt -= step;
            }
            Vector3 localVel = transform.InverseTransformDirection(velocity);
            float rollTarget = -Mathf.Clamp(localVel.x, -6, 6) * .35f + (Sliding ? 5f : 0f);
            camRoll = Mathf.Lerp(camRoll, rollTarget, Time.deltaTime * 8f);
            camHeight = cam.transform.localPosition.y;
            punchPitch = Mathf.Lerp(punchPitch, 0, Time.deltaTime * 9f); punchYaw = Mathf.Lerp(punchYaw, 0, Time.deltaTime * 9f);
            Fx.shake = Mathf.MoveTowards(Fx.shake, 0, Time.deltaTime * 1.6f);
            Vector3 shakeOff = Fx.shake > 0 ? new Vector3(Mathf.PerlinNoise(Time.time * 25, 0) - .5f, Mathf.PerlinNoise(0, Time.time * 25) - .5f, 0) * Fx.shake * 6f : Vector3.zero;
            cam.transform.localRotation = Quaternion.Euler(pitch + punchPitch + shakeOff.y, punchYaw + shakeOff.x, shakeOff.x * .5f + camRoll - lean * 14f);
            float baseFov = GameSettings.Fov;
            float targetFov = aiming ? baseFov / W.adsZoom : Sliding ? baseFov + 10f : sprinting ? baseFov + 6f : baseFov;
            if (Dashing) targetFov = 92f;
            fovKick = Mathf.Lerp(Mathf.Min(fovKick, 4f), 0, Time.deltaTime * 12f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * 14f) + fovKick * (aiming ? .3f : 1f) * Time.deltaTime * 10f;
            if (viewPivot == null) return;
            float move = new Vector2(velocity.x, velocity.z).magnitude;
            viewBob += Time.deltaTime * move * 1.6f;
            Vector3 bob = grounded ? new Vector3(Mathf.Sin(viewBob) * .008f, Mathf.Abs(Mathf.Cos(viewBob)) * .01f, 0) * (aiming ? .2f : 1f) * Mathf.Clamp01(move / 5f) : new Vector3(0, -.02f, 0);
            if (Cursor.lockState == CursorLockMode.Locked)
                viewSway = Vector3.Lerp(viewSway, new Vector3(-Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"), 0) * (aiming ? .002f : .006f), Time.deltaTime * 8f);
            viewKick = Mathf.MoveTowards(viewKick, 0, Time.deltaTime * .5f);
            boltKick = Mathf.MoveTowards(boltKick, 0, Time.deltaTime * 14f);
            if (bolt != null) bolt.localPosition = viewParts.boltRest - new Vector3(0, 0, boltKick * .05f);
            Vector3 basePos = aiming ? viewAds : viewHip;
            if (fpsView == null && sprinting) basePos += new Vector3(-.05f, -.05f, -.05f);
            if (fpsView == null && IsReloading) basePos += new Vector3(0, -.12f * Mathf.Sin(ReloadProgress * Mathf.PI), 0);
            if (bombBusy) basePos += new Vector3(0, -.3f, 0);
            if (mantling) basePos += new Vector3(.05f, -.22f, -.05f);
            if (Time.time < meleeUntil) { float mk = Mathf.Sin((1f - (meleeUntil - Time.time) / .3f) * Mathf.PI); basePos += viewPan != null ? new Vector3(.08f * mk, -.25f * mk, -.05f * mk) : new Vector3(-.12f * mk, .05f * mk, .28f * mk); }
            if (viewPan != null)
            {
                bool swing = Time.time < meleeUntil + .06f; viewPan.gameObject.SetActive(swing);
                if (swing)
                {
                    float k = Mathf.Clamp01(1f - (meleeUntil - Time.time) / .3f);
                    viewPan.localPosition = new Vector3(Mathf.Lerp(.35f, -.05f, k), Mathf.Lerp(-.05f, -.28f, k), Mathf.Lerp(.35f, .5f, Mathf.Sin(k * Mathf.PI)));
                    viewPan.localRotation = Quaternion.Euler(Mathf.Lerp(-70f, 30f, k), Mathf.Lerp(40f, -20f, k), Mathf.Lerp(60f, 90f, k)) * Quaternion.Euler(90, 0, 0);
                }
            }
            basePos += new Vector3(-localVel.x * .004f, -landDip * .6f, 0);   // weapon trails strafing and dips on landing
            viewPivot.localPosition = Vector3.Lerp(viewPivot.localPosition, basePos + bob + viewSway + new Vector3(0, 0, -viewKick * (aiming ? .5f : 1f)), Time.deltaTime * 18f);
            Quaternion rot = fpsView != null
                ? Quaternion.Euler(-viewKick * (aiming ? 12f : 35f), 0, 0)   // FPSSample clips already pose sprint / reload
                : Quaternion.Euler(-viewKick * (aiming ? 30f : 90f) + (IsReloading ? Mathf.Sin(ReloadProgress * Mathf.PI) * 25f : 0), sprinting ? -18f : 0, (sprinting ? 12f : 0) + (IsReloading ? Mathf.Sin(ReloadProgress * Mathf.PI) * -20f : 0));
            viewPivot.localRotation = Quaternion.Slerp(viewPivot.localRotation, rot, Time.deltaTime * 14f);
            if (viewParts != null && viewParts.mag != null) viewParts.mag.gameObject.SetActive(!IsReloading || ReloadProgress > .55f);
            // Through a magnified optic the weapon model is hidden and the UI draws the scope picture.
            if (viewGun != null) viewGun.gameObject.SetActive(!Scoped && !FoodAim);   // V18：开镜时枪模收起，换成美食准镜
            if (muzzleLight != null && muzzleLight.intensity > 0) { muzzleLight.intensity = Mathf.MoveTowards(muzzleLight.intensity, 0, Time.deltaTime * 70f); if (muzzleLight.intensity <= 0) muzzleFlash.enabled = false; }
        }

        public void SetViewModelVisible(bool v) { if (viewPivot != null) viewPivot.gameObject.SetActive(v); }
        public Transform LaserOrigin { get { return human && viewParts != null ? viewParts.laser : avatar != null ? avatar.LaserOrigin : null; } }
    }

    public class GunParts
    {
        public Transform muzzle, eject, bolt, mag, laser;
        public Vector3 sightLocal, foregrip, boltRest;
    }

    // Normalises the Quaternius gun FBX (muzzle +Z, grip at pivot, real length) and dresses it with
    // procedural attachments: rail, optic, muzzle device, magazine, bolt, tactical laser and neon accents.
    public static class GunModel
    {
        static Material metal, dark, polymer;

        public static float Length(WeaponSpec w)
        {
            switch (w.id) { case "SMG-7": return .62f; case "AR-9": return .88f; case "LMG-2": return 1.02f; case "DMR-3": return 1.1f; case "FS-12": return .98f; default: return .76f; }
        }

        static void Mats()
        {
            if (metal != null) return;
            var tri = Resources.Load<Material>("Materials/GunMetal");
            metal = tri != null ? tri : new Material(Shader.Find("Standard")) { color = new Color(.18f, .19f, .21f) };
            dark = new Material(Shader.Find("Standard")) { color = new Color(.05f, .05f, .06f) }; dark.SetFloat("_Glossiness", .35f); dark.SetFloat("_Metallic", .3f);
            polymer = new Material(Shader.Find("Standard")) { color = new Color(.12f, .13f, .14f) }; polymer.SetFloat("_Glossiness", .2f);
        }

        static Transform Part(Transform p, PrimitiveType t, Vector3 lp, Vector3 s, Material m, Vector3? euler = null)
        {
            var o = GameObject.CreatePrimitive(t); Object.Destroy(o.GetComponent<Collider>());
            o.transform.SetParent(p, false); o.transform.localPosition = lp; o.transform.localScale = s;
            if (euler.HasValue) o.transform.localRotation = Quaternion.Euler(euler.Value);
            o.GetComponent<Renderer>().sharedMaterial = m;
            return o.transform;
        }

        // V5: high-detail downloaded gun (glTF). Any axis/units: find the long axis, the thinner (muzzle) end,
        // rotate it to +Z, scale to real length, keep the model's own PBR materials, derive sight/muzzle points.
        // V10：卡通色枪模 → 写实 PBR。按原色分类：深色=黑色聚合物，灰/白=磷化枪钢，棕/黄=沙色护木，其它鲜艳色=军绿配件。
        static readonly Dictionary<int, Material> gunMats = new Dictionary<int, Material>();
        static void RealisticGunMaterials(GameObject model)
        {
            var baseMetal = Resources.Load<Material>("Materials/GunMetal");
            Material Get(int kind)
            {
                if (gunMats.TryGetValue(kind, out var m) && m != null) return m;
                m = baseMetal != null ? new Material(baseMetal) : new Material(Shader.Find("Standard"));
                switch (kind)
                {
                    case 0: m.color = new Color(.045f, .047f, .052f); m.SetFloat("_Metallic", .15f); m.SetFloat("_Glossiness", .32f); break;  // black polymer
                    case 1: m.color = new Color(.12f, .125f, .13f); m.SetFloat("_Metallic", .5f); m.SetFloat("_Glossiness", .42f); break;   // parkerized steel
                    case 2: m.color = new Color(.2f, .2f, .17f); m.SetFloat("_Metallic", 0f); m.SetFloat("_Glossiness", .22f); break;       // FDE furniture
                    default: m.color = new Color(.2f, .22f, .16f); m.SetFloat("_Metallic", .05f); m.SetFloat("_Glossiness", .25f); break;  // olive accessories
                }
                gunMats[kind] = m; return m;
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (src == null) continue;
                    Color c = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_Color") ? src.color : Color.gray;
                    Color.RGBToHSV(c, out float h, out float sat, out float v);
                    int kind = v < .2f ? 0 : sat < .25f ? 1 : (h > .04f && h < .17f) ? 2 : 3;
                    mats[i] = Get(kind);
                }
                r.sharedMaterials = mats;
            }
        }

        static Transform CreateDetailed(WeaponSpec w, Transform pivot, GunParts parts, Color accent, bool firstPerson)
        {
            var model = Arena.Model("Guns2/" + w.model2);
            RealisticGunMaterials(model);
            float len = Length(w);
            model.transform.position = Vector3.zero; model.transform.rotation = Quaternion.identity;
            var rs = model.GetComponentsInChildren<Renderer>();
            Bounds b = VertexBounds(model, true);  // glTF accessor min/max can be wrong in older exports
            var pts = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh; var mr = mf.GetComponent<Renderer>();
                if (mesh == null || !mesh.isReadable || (mr != null && !mr.enabled)) continue;
                var verts = mesh.vertices; int step = Mathf.Max(1, verts.Length / 3000);
                for (int i = 0; i < verts.Length; i += step) { var wv = mf.transform.TransformPoint(verts[i]) - b.center; if (b.Contains(wv + b.center)) pts.Add(wv); }
            }
            // 1) barrel axis = longest extent; muzzle = the slimmer end
            Vector3 sz = b.size; Vector3 ax = sz.x >= sz.y && sz.x >= sz.z ? Vector3.right : sz.y >= sz.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Dot(sz, ax) / 2; float thinP = 0, thinM = 0;
            foreach (var v in pts)
            {
                float t = Vector3.Dot(v, ax); float rad = (v - ax * t).magnitude;
                if (t > half * .8f) thinP = Mathf.Max(thinP, rad); else if (t < -half * .8f) thinM = Mathf.Max(thinM, rad);
            }
            Quaternion q = Quaternion.FromToRotation(thinP <= thinM ? ax : -ax, Vector3.forward);
            // 2) roll around the barrel so the weapon is taller (grip / magazine) than it is wide
            Quaternion bestQ = q; float bestScore = float.MinValue;
            for (int r = 0; r < 4; r++)
            {
                Quaternion qr = Quaternion.AngleAxis(r * 90, Vector3.forward) * q;
                float minY = 0, maxY = 0, minX = 0, maxX = 0, sumY = 0; int n = 0;
                foreach (var v in pts) { var w2 = qr * v; minY = Mathf.Min(minY, w2.y); maxY = Mathf.Max(maxY, w2.y); minX = Mathf.Min(minX, w2.x); maxX = Mathf.Max(maxX, w2.x); if (w2.z > half * .75f) { sumY += w2.y; n++; } }
                float barrelY = n > 0 ? sumY / n : 0;
                // taller than wide, and the barrel sits in the upper part (stuff hangs below it)
                float score = (maxY - minY) - (maxX - minX) + ((barrelY - minY) - (maxY - barrelY)) * .5f;
                if (score > bestScore) { bestScore = score; bestQ = qr; }
            }
            var holder = new GameObject("model").transform;
            model.transform.SetParent(holder, true);
            model.transform.localPosition = -b.center;
            float scale = len / Vector3.Dot(sz, ax);
            holder.localScale = Vector3.one * scale;
            holder.localRotation = bestQ;
            holder.SetParent(pivot, false);
            Bounds rb0 = VertexBounds(model);
            holder.localPosition = new Vector3(-rb0.center.x, -rb0.center.y + rb0.size.y * .12f, len * .12f - rb0.center.z);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            // bounds in pivot space (pivot is at the origin with identity rotation while building)
            Bounds pb = VertexBounds(model);
            parts.sightLocal = new Vector3(pb.center.x, pb.max.y + .004f, pb.center.z - len * .08f);
            parts.muzzle = new GameObject("muzzle").transform; parts.muzzle.SetParent(pivot, false);
            parts.muzzle.localPosition = new Vector3(pb.center.x, pb.center.y + pb.extents.y * .35f, pb.max.z + .01f);
            parts.eject = new GameObject("抛壳口").transform; parts.eject.SetParent(pivot, false); parts.eject.localPosition = new Vector3(pb.max.x, pb.center.y + pb.extents.y * .3f, pb.center.z);
            parts.mag = new GameObject("弹匣").transform; parts.mag.SetParent(pivot, false); parts.mag.localPosition = new Vector3(pb.center.x, pb.min.y, pb.center.z);
            parts.bolt = new GameObject("枪栓").transform; parts.bolt.SetParent(pivot, false); parts.bolt.localPosition = new Vector3(pb.max.x, pb.center.y, pb.center.z);
            parts.boltRest = parts.bolt.localPosition;
            parts.foregrip = new Vector3(pb.center.x, pb.center.y - pb.extents.y * .2f, pb.center.z + len * .22f);
            // neon accent so each hero's weapon still reads at a glance
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(pivot, false); strip.transform.localPosition = new Vector3(pb.max.x + .002f, pb.center.y, pb.center.z + len * .1f);
            strip.transform.localScale = new Vector3(.003f, .006f, len * .3f); strip.GetComponent<Renderer>().sharedMaterial = Arena.Glow(accent, 2f);
            if (w.laser)
            {
                parts.laser = new GameObject("激光").transform; parts.laser.SetParent(pivot, false);
                parts.laser.localPosition = new Vector3(pb.max.x + .01f, pb.center.y - pb.extents.y * .2f, pb.center.z + len * .25f);
                var beam = parts.laser.gameObject.AddComponent<LaserBeam>(); beam.color = new Color(1f, .15f, .2f); beam.firstPerson = firstPerson;
            }
            return pivot;
        }

        // Robust bounds from vertices (1st–99th percentile per axis). Parts whose centre lies outside the
        // robust box (stray props bundled in some downloaded kits) are hidden.
        static Bounds VertexBounds(GameObject go, bool hideOutliers = false)
        {
            var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
            var mfs = go.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in mfs)
            {
                var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
                var verts = mesh.vertices; int step = Mathf.Max(1, verts.Length / 3000);
                for (int i = 0; i < verts.Length; i += step) { Vector3 wv = mf.transform.TransformPoint(verts[i]); xs.Add(wv.x); ys.Add(wv.y); zs.Add(wv.z); }
            }
            if (xs.Count == 0) { var rs = go.GetComponentsInChildren<Renderer>(); Bounds rb = rs[0].bounds; foreach (var r in rs) rb.Encapsulate(r.bounds); return rb; }
            xs.Sort(); ys.Sort(); zs.Sort();
            int lo = (int)(xs.Count * .01f), hi = Mathf.Max(lo, (int)(xs.Count * .99f) - 1);
            var min = new Vector3(xs[lo], ys[lo], zs[lo]); var max = new Vector3(xs[hi], ys[hi], zs[hi]);
            var b = new Bounds((min + max) / 2, max - min);
            if (hideOutliers)
            {
                var grow = new Bounds(b.center, b.size * 1.3f);
                foreach (var mf in mfs)
                {
                    var r = mf.GetComponent<Renderer>(); if (r == null) continue;
                    if (!grow.Contains(r.bounds.center)) r.enabled = false;
                }
            }
            return b;
        }

        public static Transform Create(WeaponSpec w, out GunParts parts, Color accent, bool firstPerson)
        {
            Mats();
            parts = new GunParts();
            if (ToonAssets.Available) return DessertGuns.Create(w, parts, firstPerson);   // V14：甜品风武器（代码建模）
            var pivot = new GameObject("武器 " + w.id).transform;
            if (!string.IsNullOrEmpty(w.model2) && Resources.Load<GameObject>("Models/Guns2/" + w.model2) != null)
                return CreateDetailed(w, pivot, parts, accent, firstPerson);
            var model = Arena.Model("Weapons/" + w.model);
            float len = Length(w);
            float h = .16f;
            if (model != null)
            {
                model.transform.position = Vector3.zero; model.transform.rotation = Quaternion.identity;
                var rs = model.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float thinPlus = 0, thinMinus = 0; int nPlus = 0, nMinus = 0;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
                    foreach (var v in mesh.vertices)
                    {
                        Vector3 wv = mf.transform.TransformPoint(v);
                        float t = Mathf.InverseLerp(b.min.x, b.max.x, wv.x);
                        if (t > .9f) { thinPlus = Mathf.Max(thinPlus, Mathf.Abs(wv.y - b.center.y)); nPlus++; }
                        if (t < .1f) { thinMinus = Mathf.Max(thinMinus, Mathf.Abs(wv.y - b.center.y)); nMinus++; }
                    }
                }
                bool muzzlePlusX = nPlus > 0 && nMinus > 0 ? thinPlus < thinMinus : b.center.x < 0;
                float scale = len / b.size.x;
                var holder = new GameObject("model").transform;
                model.transform.SetParent(holder, true);
                model.transform.localPosition = -b.center;
                holder.localScale = Vector3.one * scale;
                holder.localRotation = Quaternion.Euler(0, muzzlePlusX ? -90 : 90, 0);
                holder.SetParent(pivot, false);
                h = b.size.y * scale;
                holder.localPosition = new Vector3(0, h * .12f, len * .12f);
                foreach (var r in rs)
                {
                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i];
                        var m = new Material(metal);
                        float lum = src.color.grayscale;
                        m.SetColor("_Color", lum > .06f ? new Color(.2f, .15f, .11f) : new Color(.09f, .095f, .105f));
                        mats[i] = m;
                    }
                    r.materials = mats;
                }
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            }
            else Part(pivot, PrimitiveType.Cube, new Vector3(0, 0, len * .3f), new Vector3(.06f, .1f, len), metal);

            float top = h * .55f;
            float muzzleZ = len * .62f;
            // Picatinny rail with teeth
            Part(pivot, PrimitiveType.Cube, new Vector3(0, top, len * .12f), new Vector3(.028f, .012f, len * .42f), dark);
            for (int i = 0; i < 9; i++) Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .009f, -len * .07f + i * len * .045f), new Vector3(.03f, .006f, .01f), dark);
            // Optic
            Vector3 sight = new Vector3(0, top + .045f, len * .05f);
            if (w.sight == "scope")
            {
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, top + .05f, len * .05f), new Vector3(.042f, len * .2f, .042f), dark, new Vector3(90, 0, 0));
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, top + .05f, len * .05f + len * .2f), new Vector3(.055f, .025f, .055f), dark, new Vector3(90, 0, 0));
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, top + .05f, len * .05f - len * .19f), new Vector3(.05f, .02f, .05f), dark, new Vector3(90, 0, 0));
                var lens = Part(pivot, PrimitiveType.Cylinder, new Vector3(0, top + .05f, len * .05f + len * .222f), new Vector3(.046f, .002f, .046f), Arena.Glow(new Color(.3f, .6f, 1f), 1.4f), new Vector3(90, 0, 0));
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .02f, len * .0f), new Vector3(.02f, .025f, .02f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .02f, len * .1f), new Vector3(.02f, .025f, .02f), dark);
                sight = new Vector3(0, top + .05f, len * .05f);
            }
            else if (w.sight == "holo")
            {
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .02f, len * .06f), new Vector3(.045f, .02f, .07f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(-.022f, top + .05f, len * .06f + .025f), new Vector3(.006f, .05f, .02f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(.022f, top + .05f, len * .06f + .025f), new Vector3(.006f, .05f, .02f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .076f, len * .06f + .025f), new Vector3(.05f, .006f, .02f), dark);
                var glass = Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .05f, len * .06f + .03f), new Vector3(.04f, .045f, .002f), new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive));
                glass.GetComponent<Renderer>().material.SetColor("_Color", new Color(.15f, .3f, .35f));
                Part(pivot, PrimitiveType.Sphere, new Vector3(0, top + .05f, len * .06f + .031f), new Vector3(.004f, .004f, .001f), Arena.Glow(new Color(1f, .15f, .15f), 8f));
                sight = new Vector3(0, top + .05f, len * .06f);
            }
            else if (w.sight == "reddot")
            {
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .018f, len * .08f), new Vector3(.03f, .016f, .05f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(-.016f, top + .042f, len * .08f), new Vector3(.004f, .032f, .012f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(.016f, top + .042f, len * .08f), new Vector3(.004f, .032f, .012f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(0, top + .059f, len * .08f), new Vector3(.036f, .004f, .012f), dark);
                Part(pivot, PrimitiveType.Sphere, new Vector3(0, top + .042f, len * .08f + .021f), new Vector3(.004f, .004f, .001f), Arena.Glow(new Color(1f, .15f, .15f), 8f));
                sight = new Vector3(0, top + .042f, len * .08f);
            }
            parts.sightLocal = sight;
            // Muzzle device
            if (w.muzzleDevice == "suppressor")
            {
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, h * .2f, muzzleZ + .07f), new Vector3(.034f, .08f, .034f), dark, new Vector3(90, 0, 0));
                muzzleZ += .16f;
            }
            else if (w.muzzleDevice == "brake")
            {
                Part(pivot, PrimitiveType.Cube, new Vector3(0, h * .2f, muzzleZ + .03f), new Vector3(.035f, .028f, .06f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(0, h * .2f, muzzleZ + .03f), new Vector3(.045f, .008f, .012f), metal);
                muzzleZ += .06f;
            }
            else
            {
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, h * .2f, muzzleZ + .025f), new Vector3(.026f, .025f, .026f), dark, new Vector3(90, 0, 0));
                muzzleZ += .05f;
            }
            parts.muzzle = new GameObject("muzzle").transform; parts.muzzle.SetParent(pivot, false); parts.muzzle.localPosition = new Vector3(0, h * .2f, muzzleZ);
            // Magazine (separate transform so it can drop on reload)
            var magRoot = new GameObject("弹匣").transform; magRoot.SetParent(pivot, false); magRoot.localPosition = new Vector3(0, -h * .32f, len * .1f);
            if (w.pellets > 1) Part(magRoot, PrimitiveType.Cube, Vector3.zero, new Vector3(.03f, .05f, .12f), polymer);
            else if (w.id == "LMG-2") { Part(magRoot, PrimitiveType.Cube, new Vector3(-.03f, -.02f, 0), new Vector3(.08f, .1f, .12f), polymer); }
            else { Part(magRoot, PrimitiveType.Cube, new Vector3(0, -.04f, 0), new Vector3(.026f, .11f, .05f), polymer, new Vector3(-12, 0, 0)); Part(magRoot, PrimitiveType.Cube, new Vector3(0, -.095f, .01f), new Vector3(.03f, .01f, .055f), dark); }
            parts.mag = magRoot;
            // Bolt / charging handle and ejection port
            parts.bolt = Part(pivot, PrimitiveType.Cube, new Vector3(.024f, h * .15f, len * .02f), new Vector3(.012f, .018f, .05f), metal);
            parts.boltRest = parts.bolt.localPosition;
            parts.eject = new GameObject("抛壳口").transform; parts.eject.SetParent(pivot, false); parts.eject.localPosition = new Vector3(.03f, h * .15f, len * .04f);
            parts.foregrip = new Vector3(0, h * .02f, len * .32f);
            // V4 detail pass: handguard vents, vertical grip, trigger guard, stock pad, sling loops, per-class extras.
            for (int i = 0; i < 5; i++)
            {
                Part(pivot, PrimitiveType.Cube, new Vector3(.026f, h * .12f, len * (.24f + i * .055f)), new Vector3(.004f, .012f, .028f), dark);
                Part(pivot, PrimitiveType.Cube, new Vector3(-.026f, h * .12f, len * (.24f + i * .055f)), new Vector3(.004f, .012f, .028f), dark);
            }
            if (w.id != "FS-12" && w.id != "DMR-3")
            {
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, -h * .2f, len * .34f), new Vector3(.026f, .045f, .026f), polymer);
                Part(pivot, PrimitiveType.Cylinder, new Vector3(0, -h * .2f - .045f, len * .34f), new Vector3(.03f, .004f, .03f), Arena.Glow(accent, 2f));
            }
            Part(pivot, PrimitiveType.Cube, new Vector3(0, -h * .2f, -len * .02f), new Vector3(.006f, .006f, .07f), dark);
            Part(pivot, PrimitiveType.Cube, new Vector3(0, -h * .05f, -len * .38f), new Vector3(.04f, h * .55f, .018f), polymer);
            Part(pivot, PrimitiveType.Cylinder, new Vector3(-.026f, 0, -len * .3f), new Vector3(.018f, .003f, .018f), metal, new Vector3(0, 0, 90));
            Part(pivot, PrimitiveType.Cylinder, new Vector3(-.026f, 0, len * .42f), new Vector3(.018f, .003f, .018f), metal, new Vector3(0, 0, 90));
            if (w.id == "LMG-2")
            {
                for (int i = -1; i <= 1; i += 2)
                    Part(pivot, PrimitiveType.Cube, new Vector3(i * .03f, -h * .3f, len * .5f), new Vector3(.008f, .2f, .008f), dark, new Vector3(-25, 0, i * 18));
                Part(pivot, PrimitiveType.Cube, new Vector3(-.045f, -h * .05f, len * .08f), new Vector3(.05f, .07f, .1f), polymer);
            }
            if (w.id == "FS-12")
                for (int i = 0; i < 4; i++)
                {
                    Part(pivot, PrimitiveType.Cylinder, new Vector3(.032f, h * .02f, len * (.0f + i * .035f)), new Vector3(.016f, .02f, .016f), Arena.Glow(new Color(1f, .3f, .15f), 1.2f), new Vector3(90, 0, 0));
                }
            if (w.id == "DMR-3")
                for (int i = -1; i <= 1; i += 2)
                    Part(pivot, PrimitiveType.Cube, new Vector3(i * .02f, h * .2f, muzzleZ - .03f), new Vector3(.006f, .03f, .05f), dark);
            // Neon accents
            Part(pivot, PrimitiveType.Cube, new Vector3(.027f, h * .05f, len * .25f), new Vector3(.003f, .006f, len * .3f), Arena.Glow(accent, 2f));
            Part(pivot, PrimitiveType.Cube, new Vector3(-.027f, h * .05f, len * .25f), new Vector3(.003f, .006f, len * .3f), Arena.Glow(accent, 2f));
            // Tactical laser
            if (w.laser)
            {
                Part(pivot, PrimitiveType.Cube, new Vector3(.032f, h * .02f, len * .38f), new Vector3(.018f, .022f, .06f), dark);
                parts.laser = new GameObject("激光").transform; parts.laser.SetParent(pivot, false); parts.laser.localPosition = new Vector3(.032f, h * .02f, len * .41f);
                var beam = parts.laser.gameObject.AddComponent<LaserBeam>(); beam.color = new Color(1f, .15f, .2f); beam.firstPerson = firstPerson;
            }
            return pivot;
        }
    }

    public class LaserBeam : MonoBehaviour
    {
        public Color color; public bool firstPerson; LineRenderer lr; Light dot;
        void Start()
        {
            lr = gameObject.AddComponent<LineRenderer>(); lr.material = Fx.Additive; lr.positionCount = 2;
            lr.startWidth = firstPerson ? .004f : .012f; lr.endWidth = .006f;
            lr.startColor = color; lr.endColor = new Color(color.r, color.g, color.b, .1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dot = new GameObject("激光点").AddComponent<Light>(); dot.type = LightType.Point; dot.color = color; dot.range = .5f; dot.intensity = 3; dot.shadows = LightShadows.None;
        }
        void LateUpdate()
        {
            if (lr == null) return;
            Vector3 a = transform.position, dir = transform.forward;
            var f = GetComponentInParent<Fighter>();
            Vector3 b = a + dir * 40f;
            var hits = Physics.RaycastAll(a, dir, 40f, ~0, QueryTriggerInteraction.Ignore);
            float best = 40f;
            foreach (var h in hits) { if (f != null && h.collider.transform.IsChildOf(f.transform)) continue; if (h.distance < best) { best = h.distance; b = h.point; } }
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            dot.transform.position = b - dir * .05f;
            bool on = f == null || f.alive;
            lr.enabled = on; dot.enabled = on;
        }
        void OnDestroy() { if (dot != null) Destroy(dot.gameObject); }
    }
}
