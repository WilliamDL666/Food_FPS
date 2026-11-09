using UnityEngine;

namespace NeonSiege
{
    // Frag grenade (borrowed from AssaultCube's GUN_GRENADE): one per round, bounces, 2.5 s fuse,
    // falloff damage that needs line of sight. Teammates are not hurt; the thrower takes half.
    public class Grenade : MonoBehaviour
    {
        public const float Fuse = 2.5f, Radius = 6.5f, MaxDamage = 95f, Speed = 17f;
        public Fighter owner; public Vector3 velocity;
        float born; Light lamp; float nextBlink;

        public static Grenade Throw(Fighter f, Vector3 velocity)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "手雷";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(.11f, .14f, .11f);
            go.transform.position = f.Eye + f.AimDir * .5f + Vector3.down * .15f;
            var m = Resources.Load<Material>("Materials/GunMetal");
            var r = go.GetComponent<Renderer>(); if (m != null) r.sharedMaterial = m;
            if (ToonAssets.Available)   // V17：手雷 = 摇晃过的汽水罐
            {
                var can = Resources.Load<GameObject>("Toon/FoodGuns/Soda");
                if (can != null)
                {
                    r.enabled = false; go.transform.localScale = Vector3.one;
                    var c = Object.Instantiate(can, go.transform, false); c.transform.localScale = Vector3.one * 1.6f;
                }
            }
            var g = go.AddComponent<Grenade>(); g.owner = f; g.velocity = velocity;
            Sfx.Play("impactGeneric_light", go.transform.position, .45f, 1.6f, f.human ? .3f : 1f, 20f);
            return g;
        }

        // Same arc the preview line draws.
        public static Vector3 HumanVelocity(Fighter f) { return (f.AimDir + Vector3.up * .22f).normalized * Speed; }

        void Start()
        {
            born = Time.time;
            lamp = new GameObject("手雷指示灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false);
            lamp.color = new Color(1f, .25f, .1f); lamp.range = 2.5f; lamp.intensity = 0;
        }

        void Update()
        {
            float age = Time.time - born;
            velocity += Physics.gravity * Time.deltaTime;
            Vector3 next = transform.position + velocity * Time.deltaTime;
            foreach (var hit in Physics.RaycastAll(transform.position, next - transform.position, (next - transform.position).magnitude + .06f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner != null && hit.collider.transform.IsChildOf(owner.transform) && age < .3f) continue;
                next = hit.point + hit.normal * .07f;
                velocity = Vector3.Reflect(velocity, hit.normal) * .42f;
                if (hit.normal.y > .7f) { velocity.x *= .7f; velocity.z *= .7f; }   // ground friction
                if (velocity.magnitude > 1.2f) Sfx.Play("impactMetal_light", hit.point, .35f, 1.7f, 1f, 22f);
                break;
            }
            transform.position = next;
            transform.Rotate(velocity.magnitude * 90f * Time.deltaTime, 0, 0);
            // blinking fuse light speeds up near the end
            float interval = Mathf.Lerp(.5f, .08f, age / Fuse);
            if (Time.time >= nextBlink) { nextBlink = Time.time + interval; lamp.intensity = 3; Sfx.Play("tick", transform.position, .5f, 2.2f, 1f, 14f); }
            lamp.intensity = Mathf.MoveTowards(lamp.intensity, 0, Time.deltaTime * 30f);
            if (age >= Fuse) Explode();
        }

        void Explode()
        {
            Vector3 p = transform.position;
            Fx.Explosion(p, Radius * .6f);
            Fx.Flash(p, new Color(1f, .6f, .25f), 7f, 14f, .35f);
            Sfx.Play("boom", p, ToonAssets.Available ? .55f : 1f, Random.Range(.92f, 1.05f), 1f, 110f);
            if (ToonAssets.Available)
            {
                Sfx.Play("food_fizz", p, 1f, Random.Range(.95f, 1.05f), 1f, 60f);
                var soda = new Color(.35f, .85f, 1f);
                Fx.Burst(p, Vector3.up, soda, 50, 8f, .09f, 1f, 1.2f); Fx.Burst(p, Vector3.up, new Color(1f, .92f, .35f), 25, 6f, .07f, .9f, 1.2f);
                Fx.Burst(p, Vector3.up, Color.white, 20, 4f, .25f, .8f, -.3f, Fx.Smoke);
                for (int i = 0; i < 10; i++)   // 汽水溅一地
                {
                    Vector2 rr = Random.insideUnitCircle * Radius * .6f;
                    if (Physics.Raycast(p + new Vector3(rr.x, .8f, rr.y), Vector3.down, out RaycastHit gh, 3f, ~0, QueryTriggerInteraction.Ignore))
                        FoodFx.Splat(gh.point, gh.normal, i % 3 == 0 ? new Color(1f, .9f, .35f) : soda, Random.Range(.6f, 1.2f));
                }
            }
            Sfx.Play("boom_far", p, .7f, 1f, 1f, 400f);
            if (ToonAssets.Available) MentosPile.TryFountain(p, owner);   // 汽水 + 曼妥思
            float rad = Radius * (ToonAssets.Available && owner != null && owner.role == Role.Ember ? 1.2f : 1f);   // 焚芯被动
            if (Match.I != null)
                foreach (var f in Match.I.fighters)
                {
                    if (!f.alive) continue;
                    float d = Vector3.Distance(f.Chest, p);
                    if (d > rad) continue;
                    if (owner != null && f != owner && f.team == owner.team) continue;
                    if (!Skills.LineClear(p + Vector3.up * .2f, f.Chest, f)) continue;
                    float dmg = MaxDamage * Mathf.Clamp01(1f - d / rad) * (f == owner ? .5f : 1f);
                    if (dmg < 4f) continue;
                    f.ReceiveDamage(dmg, owner, p, false, true);
                    if (owner != null && owner.human && f != owner) { owner.hitMarkerUntil = Time.time + .25f; owner.hitMarkerHead = false; owner.hitMarkerKill = !f.alive; Fx.AddNumber(f.Chest, dmg, false, false, !f.alive, f); }
                }
            var hitProps = new System.Collections.Generic.HashSet<IDamageable>();
            foreach (var t in Physics.OverlapSphere(p, Radius * .7f))
            {
                if (t.GetComponent<WallPiece>() != null && Vector3.Distance(t.transform.position, p) > 1.9f) continue;   // ~2 m breach, not a whole wall
                var dmg = t.GetComponentInParent<IDamageable>();
                if (dmg != null && !(dmg is Fighter) && hitProps.Add(dmg)) dmg.TakeDamage(MaxDamage, owner, true);
            }
            if (Match.I != null) Match.I.NoiseAt(owner, p);
            Destroy(gameObject);
        }
    }
}
