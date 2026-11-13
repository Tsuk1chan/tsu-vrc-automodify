// NdmfBake.cs — 把源场景复制一份、unpack 所有 prefab 实例、跑 NDMF bake。
// 放进 Assets/Editor/ 后：
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod NdmfBake.Run -logFile <log>
//
// 环境变量（都可省略，省略时用下面的默认值）：
//   BAKE_SRC   源场景          默认 Assets/Main.unity
//   BAKE_DST   产物场景        默认 Assets/Main_baked.unity
//   BAKE_INFO  统计输出文件    默认 bake_info.txt（相对工程根）
//
// 为什么不能直接用 AssetDatabase.CopyAsset 之外的省事写法：
//   NDMF 的 AvatarProcessor 不接受含 prefab 实例的 avatar，必须先 unpack 干净。

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

public static class NdmfBake
{
    public static void Run()
    {
        string src  = System.Environment.GetEnvironmentVariable("BAKE_SRC")  ?? "Assets/Main.unity";
        string dst  = System.Environment.GetEnvironmentVariable("BAKE_DST")  ?? "Assets/Main_baked.unity";
        string info = System.Environment.GetEnvironmentVariable("BAKE_INFO") ?? "bake_info.txt";

        AssetDatabase.DeleteAsset(dst);
        if (!AssetDatabase.CopyAsset(src, dst))
        {
            Debug.LogError("[Bake] 复制场景失败：" + src);
            EditorApplication.Exit(1);
            return;
        }
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.OpenScene(dst, OpenSceneMode.Single);

        // 找 avatar：取第一个带 VRCAvatarDescriptor 的根对象
        GameObject avatar = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponent<VRCAvatarDescriptor>() != null) { avatar = root; break; }
        if (avatar == null)
        {
            Debug.LogError("[Bake] 场景里没有 VRCAvatarDescriptor");
            EditorApplication.Exit(1);
            return;
        }

        // 展开所有 prefab 实例
        var roots = new List<GameObject>();
        foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) continue;
            var r = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            if (r != null && !roots.Contains(r)) roots.Add(r);
        }
        foreach (var r in roots)
            PrefabUtility.UnpackPrefabInstance(r, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Debug.Log("[Bake] 展开 prefab 实例：" + roots.Count);

        var before = new HashSet<int>();
        foreach (var r in scene.GetRootGameObjects()) before.Add(r.GetInstanceID());

        try { nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(avatar); }
        catch (System.Exception e)
        {
            Debug.LogError("[Bake] 异常：" + e);
            EditorApplication.Exit(1);
            return;
        }

        // NDMF 把结果生成在它自己的预览场景里；找不到就退回原对象
        GameObject baked = null;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            Debug.Log("[Bake] scene " + s.name + " roots=" + s.rootCount);
            if (!s.name.Contains("NDMF")) continue;
            foreach (var r in s.GetRootGameObjects())
                if (r.GetComponent<VRCAvatarDescriptor>() != null) baked = r;
        }
        if (baked == null)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (!before.Contains(r.GetInstanceID()) && r != avatar) baked = r;
        }
        if (baked == null)
        {
            baked = avatar;
            Debug.Log("[Bake] 未能找到独立产物，按原地 bake 处理");
        }

        baked.name = avatar.name + "_BAKED";

        // 统计：层数、菜单项、参数位数。跟上一轮对比，层数突变通常说明 MA 组件被删了没接管
        var sb = new StringBuilder();
        sb.Append("[Bake] name=").Append(baked.name).Append('\n');
        var d = baked.GetComponent<VRCAvatarDescriptor>();
        if (d != null)
        {
            DumpLayers(sb, "layer", d.baseAnimationLayers);
            DumpLayers(sb, "special", d.specialAnimationLayers);

            sb.Append("  menu=").Append(d.expressionsMenu == null ? "null" : d.expressionsMenu.name).Append('\n');
            if (d.expressionsMenu != null)
                foreach (var c in d.expressionsMenu.controls)
                    sb.Append("    - ").Append(c.name).Append(" type=").Append(c.type).Append('\n');

            sb.Append("  params=").Append(d.expressionParameters == null ? "null" : d.expressionParameters.name).Append('\n');
            if (d.expressionParameters != null)
            {
                var ps = d.expressionParameters.parameters;
                int bits = 0;
                foreach (var p in ps)
                    if (p.networkSynced)
                        bits += p.valueType == VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool ? 1 : 8;
                sb.Append("  paramCount=").Append(ps == null ? 0 : ps.Length)
                  .Append(" syncedBits=").Append(bits).Append('\n');
            }
        }
        File.WriteAllText(info, sb.ToString(), new UTF8Encoding(false));
        Debug.Log(sb.ToString());

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bake] done");
        EditorApplication.Exit(0);
    }

    static void DumpLayers(StringBuilder sb, string tag, VRCAvatarDescriptor.CustomAnimLayer[] layers)
    {
        if (layers == null) return;
        for (int i = 0; i < layers.Length; i++)
        {
            var ac = layers[i].animatorController as UnityEditor.Animations.AnimatorController;
            sb.Append("  ").Append(tag).Append(' ').Append(i).Append(' ').Append(layers[i].type)
              .Append(" ctrl=").Append(ac == null ? "null" : ac.name + "(" + ac.layers.Length + " layers)")
              .Append('\n');
        }
    }
}
