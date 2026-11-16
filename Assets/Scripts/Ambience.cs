using UnityEngine;

namespace NeonSiege
{
    // Lockdown soundscape: wind bed, distant gunfire, far-off sirens, plus reverb zones for the
    // factory hall and the alleys. The wind and siren clips are synthesised at startup (no assets).
    public class Ambience : MonoBehaviour
    {
        AudioSource wind, music; bool food;
        public static AudioSource Music;   // V24：局势音乐（V24Audio 调速度 / 叠紧张节奏）
        float nextGunfire, nextSiren;
        AudioClip siren;
        static readonly string[] guns = { "gun_ar", "gun_lmg", "gun_smg", "gun_br", "gun_dmr" };

        void Start()
        {
            if (ToonAssets.Available)
            {
                // V18：美食街背景音乐（程序合成），不再有远处枪声和警报
                var clip = Resources.Load<AudioClip>("Music/food_street");
                if (clip != null) { music = gameObject.AddComponent<AudioSource>(); music.clip = clip; music.loop = true; music.spatialBlend = 0; music.volume = .2f; music.Play(); Music = music; }
                V24Audio.Ensure(gameObject);
                food = true;
            }
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = MakeWind(14f); wind.loop = true; wind.spatialBlend = 0; wind.volume = food ? .04f : .16f; wind.Play();
            siren = MakeSiren(7f);
            nextGunfire = Time.time + Random.Range(6f, 12f);
            nextSiren = Time.time + Random.Range(25f, 45f);

            Zone("B 车间混响", new Vector3(37, 3, -34), 9, 15, AudioReverbPreset.Hangar);
            Zone("北小巷混响", new Vector3(-21, 2, 20), 3, 8, AudioReverbPreset.Alley);
            Zone("南小巷混响", new Vector3(-33, 2, -20), 3, 8, AudioReverbPreset.Alley);
            Zone("市场楼顶", new Vector3(37, 9, 0), 6, 10, AudioReverbPreset.Plain);
            Zone("街区混响", new Vector3(0, 2, 0), 60, 120, AudioReverbPreset.City);
        }

        void Zone(string name, Vector3 p, float min, float max, AudioReverbPreset preset)
        {
            var z = new GameObject(name).AddComponent<AudioReverbZone>();
            z.transform.SetParent(transform); z.transform.position = p;
            z.minDistance = min; z.maxDistance = max; z.reverbPreset = preset;
        }

        void Update()
        {
            if (Match.I == null || food) return;
            if (Time.time >= nextGunfire)
            {
                // a short exchange somewhere outside the lockdown zone
                nextGunfire = Time.time + Random.Range(9f, 22f);
                StartCoroutine(Burst(FarPoint(), guns[Random.Range(0, guns.Length)], Random.Range(2, 7)));
            }
            if (Time.time >= nextSiren)
            {
                nextSiren = Time.time + Random.Range(45f, 80f);
                var go = new GameObject("远处警报"); go.transform.position = FarPoint();
                var s = go.AddComponent<AudioSource>(); s.clip = siren; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 40; s.maxDistance = 420; s.volume = .22f; s.dopplerLevel = 0; s.Play();
                var lp = go.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 2200;
                Destroy(go, siren.length + .5f);
            }
        }

        static Vector3 FarPoint()
        {
            float a = Random.value * Mathf.PI * 2;
            return new Vector3(Mathf.Cos(a) * 150f, 10f, Mathf.Sin(a) * 120f);
        }

        System.Collections.IEnumerator Burst(Vector3 p, string gun, int shots)
        {
            for (int i = 0; i < shots; i++)
            {
                Sfx.Play(gun, p, .55f, Random.Range(.85f, .95f), 1f, 320f);
                yield return new WaitForSeconds(Random.Range(.09f, .35f));
            }
        }

        // Brown noise with slow gusts.
        static AudioClip MakeWind(float seconds)
        {
            int rate = 22050, n = (int)(rate * seconds);
            var data = new float[n]; float b = 0, lp = 0;
            var rnd = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                b = Mathf.Clamp(b + ((float)rnd.NextDouble() * 2 - 1) * .02f, -1, 1) * .998f;
                lp += (b - lp) * .08f;
                float t = i / (float)rate;
                float gust = .55f + .3f * Mathf.Sin(t * Mathf.PI * 2 / seconds) + .15f * Mathf.Sin(t * 1.7f + 1);
                data[i] = lp * gust * 1.6f;
            }
            // crossfade the loop seam
            int fade = rate / 2;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; data[i] = data[i] * k + data[n - fade + i] * (1 - k); }
            var clip = AudioClip.Create("wind", n - fade, 1, rate, false);
            var trimmed = new float[n - fade]; System.Array.Copy(data, trimmed, n - fade); clip.SetData(trimmed, 0);
            return clip;
        }

        // Rising/falling civil-defence wail.
        static AudioClip MakeSiren(float seconds)
        {
            int rate = 22050, n = (int)(rate * seconds);
            var data = new float[n]; double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = 420f + 380f * (.5f - .5f * Mathf.Cos(t / seconds * Mathf.PI * 4));
                phase += 2 * Mathf.PI * f / rate;
                float env = Mathf.Clamp01(t / 1.2f) * Mathf.Clamp01((seconds - t) / 1.5f);
                data[i] = (float)(Mathf.Sin((float)phase) * .6f + Mathf.Sin((float)phase * 2) * .25f + Mathf.Sin((float)phase * 3) * .1f) * env * .6f;
            }
            var clip = AudioClip.Create("siren", n, 1, rate, false); clip.SetData(data, 0);
            return clip;
        }
    }
}
