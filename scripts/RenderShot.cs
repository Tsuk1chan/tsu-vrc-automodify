// RenderShot.cs — 在 batchmode 里按 job JSON 控制物体显隐、摆相机、输出 PNG。
// 放进 Assets/Editor/ 后：
//   set SHOT_JOB=<job.json 路径>  &&  Unity.exe -batchmode -quit -projectPath <proj> \
//        -executeMethod RenderShot.Run -logFile <log>
//
// 三条硬限制（踩过才知道）：
//   1. batchmode 不跑 animator —— 所有显隐必须自己 SetActive，别指望 FX 生效。
//   2. batchmode 不吃动画驱动的 BlendShape 权重；被 MA ShapeChanger 烧成静态值的
//      blendshape 会一直生效，需要的话在 job 里用 resetOffBlendshapes 把它们归零。
//   3. 对比实验必须分 outDir，同名文件会被覆盖，只能全部重渲。
//
// job JSON 示例：
// {
//   "scene":  "Assets/Main_baked.unity",
//   "outDir": "D:/tmp/shots",
//   "ambient": 0.20, "dirLight": 1.15,
//   "shots": [
//     { "name": "01_full", "show": ["Outfit_A", "Underwear_Bottom"], "hide": ["Outfit_B"],
//       "camX": 0, "camY": 1.29, "camZ": 2.0, "lookY": 1.27, "rotY": 0,
//       "width": 700, "height": 760, "resetOffBlendshapes": ["Body_base"] }
//   ]
// }

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

public static class RenderShot
{
    public static void Run()
    {
        string jobPath = System.Environment.GetEnvironmentVariable("SHOT_JOB");
        if (string.IsNullOrEmpty(jobPath) || !File.Exists(jobPath))
        {
            Debug.LogError("[Shot] 缺少 SHOT_JOB 或文件不存在：" + jobPath);
            EditorApplication.Exit(1);
            return;
        }

        var job = JsonUtility.FromJson<Job>(File.ReadAllText(jobPath));
        EditorSceneManager.OpenScene(job.scene, OpenSceneMode.Single);

        var avatar = Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
            .Select(d => d.gameObject).FirstOrDefault();
        if (avatar == null)
        {
            Debug.LogError("[Shot] 场景里没有 VRCAvatarDescriptor");
            EditorApplication.Exit(1);
            return;
        }

        Directory.CreateDirectory(job.outDir);
        RenderSettings.ambientLight = new Color(job.ambient, job.ambient, job.ambient * 1.08f);
        foreach (var l in Object.FindObjectsOfType<Light>())
            if (l.type == LightType.Directional) l.intensity = job.dirLight;

        var cam = Object.FindObjectsOfType<Camera>().FirstOrDefault();
        if (cam == null)
        {
            var go = new GameObject("ShotCam");
            cam = go.AddComponent<Camera>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
        cam.fieldOfView = 30f;

        var originalRot = avatar.transform.rotation;

        foreach (var s in job.shots)
        {
            ApplyVisibility(avatar, s.hide, false);
            ApplyVisibility(avatar, s.show, true);

            // 归零指定 renderer 上以 _OFF 结尾的 blendshape（MA ShapeChanger 烧的静态值）
            if (s.resetOffBlendshapes != null && s.resetOffBlendshapes.Length > 0)
            {
                foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!s.resetOffBlendshapes.Contains(smr.name)) continue;
                    var mesh = smr.sharedMesh;
                    if (!mesh) continue;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        if (mesh.GetBlendShapeName(i).EndsWith("_OFF"))
                            smr.SetBlendShapeWeight(i, 0f);
                }
            }

            avatar.transform.rotation = Quaternion.Euler(0f, s.rotY, 0f) * originalRot;
            cam.transform.position = new Vector3(s.camX, s.camY, s.camZ);
            cam.transform.LookAt(new Vector3(0f, s.lookY, 0f));

            var rt = new RenderTexture(s.width, s.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var tex = new Texture2D(s.width, s.height, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, s.width, s.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(job.outDir, s.name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log("[Shot] wrote " + s.name);
        }

        avatar.transform.rotation = originalRot;
        Debug.Log("[Shot] done");
        EditorApplication.Exit(0);
    }

    static void ApplyVisibility(GameObject avatar, string[] names, bool active)
    {
        if (names == null) return;
        foreach (var n in names)
        {
            var t = avatar.transform.Find(n);
            if (t == null) { Debug.LogWarning("[Shot] 找不到节点：" + n); continue; }
            t.gameObject.SetActive(active);
        }
    }

    [System.Serializable]
    public class Shot
    {
        public string name = "shot";
        public string[] show;
        public string[] hide;
        public string[] resetOffBlendshapes;
        public float camX = 0f, camY = 1.29f, camZ = 2.0f, lookY = 1.27f, rotY = 0f;
        public int width = 700, height = 760;
    }

    [System.Serializable]
    public class Job
    {
        public string scene = "Assets/Main_baked.unity";
        public string outDir = "shots";
        public float ambient = 0.20f;
        public float dirLight = 1.15f;
        public Shot[] shots = new Shot[0];
    }
}
