// DumpScene.cs — 一次性把场景结构、MA 组件、AnimatorController 图层与参数打到文本文件。
// 改模时九成的问题靠这份 dump 就能定位：层是不是空的、参数类型对不对、组件的引用路径写的是什么。
// 放进 Assets/Editor/ 后：
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod DumpScene.Run -logFile <log>
//
// 环境变量：
//   DUMP_SCENE  要打开的场景   默认 Assets/Main.unity
//   DUMP_OUT    输出文件       默认 dump.txt

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

public static class DumpScene
{
    public static void Run()
    {
        string scenePath = System.Environment.GetEnvironmentVariable("DUMP_SCENE") ?? "Assets/Main.unity";
        string outPath   = System.Environment.GetEnvironmentVariable("DUMP_OUT")   ?? "dump.txt";

        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();

        var avatar = Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
            .Select(d => d.gameObject).FirstOrDefault();

        // ---- 场景层级 ----
        sb.AppendLine("========== 场景层级 ==========");
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            DumpTree(root.transform, 0, sb, avatar == null ? null : avatar.transform);

        if (avatar == null)
        {
            sb.AppendLine("!! 没有 VRCAvatarDescriptor");
            Write(outPath, sb);
            return;
        }

        // ---- MA 组件 ----
        sb.AppendLine();
        sb.AppendLine("========== Modular Avatar 组件 ==========");
        foreach (var mb in avatar.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var tn = mb.GetType().Name;
            if (!tn.Contains("ModularAvatar")) continue;

            sb.AppendLine("-- " + tn + " @ " + PathOf(avatar.transform, mb.transform));
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            int guard = 0;
            while (it.NextVisible(true) && guard++ < 200)
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var refObj = it.objectReferenceValue;
                    sb.AppendLine("     " + it.name + " = " +
                        (refObj == null ? "(null)" : refObj.name + " [" + refObj.GetType().Name + "]"));
                }
                else if (it.propertyType == SerializedPropertyType.String
                      || it.propertyType == SerializedPropertyType.Enum
                      || it.propertyType == SerializedPropertyType.Boolean
                      || it.propertyType == SerializedPropertyType.Integer)
                {
                    sb.AppendLine("     " + it.name + " = " + it.ToStringValue(it.propertyType));
                }
            }
        }

        // ---- AnimatorController ----
        sb.AppendLine();
        sb.AppendLine("========== AnimatorController ==========");
        var desc = avatar.GetComponent<VRCAvatarDescriptor>();
        var controllers = new System.Collections.Generic.List<UnityEditor.Animations.AnimatorController>();
        void Collect(VRCAvatarDescriptor.CustomAnimLayer[] layers)
        {
            if (layers == null) return;
            foreach (var l in layers)
            {
                var ac = l.animatorController as UnityEditor.Animations.AnimatorController;
                if (ac != null && !controllers.Contains(ac)) controllers.Add(ac);
            }
        }
        Collect(desc.baseAnimationLayers);
        Collect(desc.specialAnimationLayers);

        foreach (var ac in controllers)
        {
            sb.AppendLine("-- controller " + ac.name);
            sb.AppendLine("   参数：");
            foreach (var p in ac.parameters)
                sb.AppendLine("     " + p.name + " : " + p.type + " default=" + DefaultOf(p));
            sb.AppendLine("   图层：");
            for (int i = 0; i < ac.layers.Length; i++)
            {
                var layer = ac.layers[i];
                int states = CountStates(layer.stateMachine);
                sb.AppendLine("     [" + i + "] " + layer.name + " weight=" + layer.defaultWeight +
                              " states=" + states + (states == 0 ? "   <== 空层！" : ""));
            }
        }

        // ---- 参数总览 ----
        sb.AppendLine();
        sb.AppendLine("========== Expression Parameters ==========");
        if (desc.expressionParameters != null)
        {
            var ps = desc.expressionParameters.parameters;
            int bits = 0;
            foreach (var p in ps)
            {
                int b = p.valueType == VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool ? 1 : 8;
                if (p.networkSynced) bits += b;
                sb.AppendLine("  " + p.name + " type=" + p.valueType + " synced=" + p.networkSynced +
                              " saved=" + p.saved + " default=" + p.defaultValue);
            }
            sb.AppendLine("  paramCount=" + ps.Length + " syncedBits=" + bits + " / 256");
        }

        Write(outPath, sb);
    }

    static void Write(string path, StringBuilder sb)
    {
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[Dump] " + path);
        EditorApplication.Exit(0);
    }

    static void DumpTree(Transform t, int depth, StringBuilder sb, Transform avatar)
    {
        if (depth > 6) return;
        sb.AppendLine(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [inactive]"));

        var parts = new System.Collections.Generic.List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            var n = c.GetType().Name;
            if (n == "Transform") continue;
            if (n == "SkinnedMeshRenderer")
            {
                var smr = c as SkinnedMeshRenderer;
                parts.Add("SMR(" + (smr.sharedMesh == null ? "null" : smr.sharedMesh.name) +
                          " bones=" + (smr.bones == null ? 0 : smr.bones.Length) + ")");
                continue;
            }
            if (n == "MeshRenderer") { parts.Add("MR"); continue; }
            parts.Add(n);
        }
        if (parts.Count > 0) sb.AppendLine(new string(' ', depth * 2 + 2) + "· " + string.Join(", ", parts));

        foreach (Transform child in t) DumpTree(child, depth + 1, sb, avatar);
    }

    static string PathOf(Transform root, Transform t)
    {
        var stack = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null && cur != root) { stack.Insert(0, cur.name); cur = cur.parent; }
        return string.Join("/", stack);
    }

    static int CountStates(UnityEditor.Animations.AnimatorStateMachine sm)
    {
        if (sm == null) return 0;
        int n = sm.states == null ? 0 : sm.states.Length;
        if (sm.stateMachines != null)
            foreach (var sub in sm.stateMachines)
                n += CountStates(sub.stateMachine);
        return n;
    }

    static string DefaultOf(AnimatorControllerParameter p)
    {
        switch (p.type)
        {
            case AnimatorControllerParameterType.Bool:  return p.defaultBool.ToString();
            case AnimatorControllerParameterType.Int:   return p.defaultInt.ToString();
            case AnimatorControllerParameterType.Float: return p.defaultFloat.ToString();
            default: return "-";
        }
    }
}

static class SerializedPropertyExt
{
    public static string ToStringValue(this SerializedProperty p, SerializedPropertyType t)
    {
        switch (t)
        {
            case SerializedPropertyType.Enum:    return p.enumValueIndex.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            default: return p.stringValue;
        }
    }
}
