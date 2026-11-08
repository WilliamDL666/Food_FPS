using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace NeonSiege
{
    // Minimal cross-fading clip player on top of the Playables API. Used for the glTF heroes, whose
    // clips are generic (not Humanoid), so no AnimatorController asset is needed.
    public class ClipPlayer
    {
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly List<AnimationClipPlayable> playables = new List<AnimationClipPlayable>();
        readonly List<AnimationClip> clips = new List<AnimationClip>();
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        readonly HashSet<int> once = new HashSet<int>();
        float[] weights;
        int target = -1; float fadeRate = 8f;
        public string Current { get { return target >= 0 ? clips[target].name : ""; } }

        public static AnimationClip[] Load(string resourcePath)
        {
            var all = Resources.LoadAll<AnimationClip>(resourcePath);
            return all;
        }

        public ClipPlayer(Animator animator, AnimationClip[] source, params string[] oneShots)
        {
            graph = PlayableGraph.Create("hero-anim");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            mixer = AnimationMixerPlayable.Create(graph, source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var p = AnimationClipPlayable.Create(graph, c);
                p.SetApplyFootIK(false);
                graph.Connect(p, 0, mixer, i);
                playables.Add(p); clips.Add(c);
                string key = c.name.Contains("|") ? c.name.Substring(c.name.LastIndexOf('|') + 1) : c.name;
                index[key] = i;
            }
            foreach (var n in oneShots) if (index.TryGetValue(n, out int k)) { once.Add(k); playables[k].SetDuration(clips[k].length); }
            weights = new float[source.Length];
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        public bool Has(string name) { return index.ContainsKey(name); }

        public void Play(string name, float fade = .15f, float speed = 1f, bool restart = false)
        {
            if (!index.TryGetValue(name, out int i)) return;
            if (i != target || restart)
            {
                if (restart || once.Contains(i)) { playables[i].SetTime(0); playables[i].SetDone(false); }
                target = i;
            }
            playables[i].SetSpeed(speed);
            fadeRate = 1f / Mathf.Max(.01f, fade);
        }

        public bool Finished(string name)
        {
            return index.TryGetValue(name, out int i) && playables[i].GetTime() >= clips[i].length - .02f;
        }

        public void Tick(float dt)
        {
            if (!graph.IsValid()) return;
            float total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], i == target ? 1f : 0f, dt * fadeRate);
                total += weights[i];
                if (!once.Contains(i))
                {
                    double t = playables[i].GetTime(), len = clips[i].length;
                    if (len > 0 && t > len) playables[i].SetTime(t % len);
                }
            }
            for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, total > 0 ? weights[i] / total : 0);
        }

        // For portraits: snap to a clip and evaluate one frame immediately.
        public void Pose(string name, float time)
        {
            if (!index.TryGetValue(name, out int i)) return;
            for (int k = 0; k < weights.Length; k++) { weights[k] = k == i ? 1 : 0; mixer.SetInputWeight(k, weights[k]); }
            target = i; playables[i].SetTime(time); graph.Evaluate(0);
        }

        public void Destroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
