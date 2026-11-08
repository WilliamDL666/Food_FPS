using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Lists clips, bones, renderers and bounds of the cartoon assets (Quaternius Toon Shooter / KayKit).
public static class ToonProbe
{
    public static void Run()
    {
        AssetDatabase.Refresh();
        var sb = new StringBuilder();
        foreach (var dir in new[] { "Assets/Resources/Toon/Chars", "Assets/Resources/Toon/Guns" })
            foreach (var f in Directory.GetFiles(dir))
            {
                if (f.EndsWith(".meta")) continue;
                string path = f.Replace('\\', '/');
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine("##### " + path + (go == null ? " (not a model)" : ""));
                if (go == null) continue;
                var clips = new List<string>();
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is AnimationClip c && !c.name.StartsWith("__")) clips.Add(c.name + "(" + c.length.ToString("0.0") + ")");
                sb.AppendLine("clips " + clips.Count + ": " + string.Join(", ", clips));
                var inst = (GameObject)Object.Instantiate(go);
                var rs = inst.GetComponentsInChildren<Renderer>(true);
                Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(); foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.AppendLine("bounds center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2") + " renderers=" + rs.Length + " animator=" + (inst.GetComponentInChildren<Animator>() != null));
                if (dir.EndsWith("Chars"))
                {
                    var names = new List<string>();
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
                    sb.AppendLine("nodes: " + string.Join(", ", names));
                    foreach (var r in rs) { var ms = new List<string>(); foreach (var m in r.sharedMaterials) ms.Add(m ? m.name : "null"); sb.AppendLine("  R " + r.name + " : " + string.Join(", ", ms)); }
                }
                Object.DestroyImmediate(inst);
            }
        File.WriteAllText(@"E:\Claude工作区\素材\Toon\probe.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("TOON_PROBE_DONE");
    }
}
