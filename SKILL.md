---
name: vrchat-mod
description: 给 VRChat 模型改模的工作流与坑位清单：接新衣服/配饰、用 MA Merge Armature 焊骨骼、重做 Expression Menu 与 FX 开关、改材质配色、NDMF bake、batchmode 渲染验证。任务涉及 Unity batchmode、Modular Avatar、NDMF、AnimatorController 手改、VRChat 上传前检查时加载。
---

# VRChat 改模（Unity batchmode 工作流）

改模的难点不在"怎么做"，而在"看不见结果怎么确认"和"Unity 的 API 有一半在 batchmode 下行为不同"。这份清单记录已经验证过的姿势和踩过的坑。

## 前置条件

- **Unity 2022.3.x**（VRChat 当前版本线）
- VCC 安装的包：VRChat SDK3 - Avatars、Modular Avatar、NDMF、lilToon
- **Windows + PowerShell**（第 1 节的启动姿势依赖 `Start-Process`）
- `scripts/` 里三个模板：`NdmfBake.cs`（bake）、`RenderShot.cs`（渲染验证）、`DumpScene.cs`（诊断）。按需复制进 `Assets/Editor/`，改掉文件头注释里的环境变量默认值即可用。

## 0. 先确认三件事

1. **编辑器版本和路径**。手动解压安装的 Unity 常常不在 Hub 目录下，用 `Get-Process Unity` 看进程启动参数能拿到真实路径。`unity.exe serve` 或 `--internal-auth-broker-serve` 是 Hub 的后台服务，不是编辑器在占工程。
2. **有没有 `Temp\UnityLockfile`**。上一次 batchmode 被中断会留下它，导致编辑器打不开。移到备份目录而不是删除。
3. **两个场景别搞混**。改东西用源场景（avatar 是 prefab 实例），bake 产物是展开版。永远在源场景上改，bake 会重新生成产物；改产物一重烤就丢。

## 1. 跑 batchmode 的唯一可靠姿势

```powershell
$unity = "D:\...\Editor\Unity.exe"
$log   = "D:\...\_backup\run.log"        # 路径不要带空格
Start-Process -FilePath $unity -ArgumentList @(
  "-batchmode","-quit","-projectPath",$proj,
  "-executeMethod","MyClass.Run","-logFile",$log
) | Out-Null                              # 不加 -Wait
Start-Sleep -Seconds 60                   # 再查进程和日志
Select-String -Path $log -Pattern "error CS|Exception|\[Done\]" | Select-Object -First 5
```

**关键点**：
- 不要用 `& Unity.exe`（立即返回、拿不到退出码），也不要加 `-Wait`（长任务会被中断）。
- 不要放在 `run_in_background` 的后台作业里 —— `Start-Process` 在那里会失败，进程根本不出现。
- 脚本入口结尾调 `EditorApplication.Exit(0)`，配合 `-quit` 双保险。
- **编译错误时 Unity 秒退且不写 `-logFile`**。日志没生成，第一反应应该是"编译挂了"，去人工查 `Assets/Editor/` 下的新脚本。

## 2. 标准改模流程

每一轮都是这五步，不要跳：

1. **备份**要动的资产到 `_backup/<日期>_<主题>/`（场景、controller、asset、动画、meta 一起）。
2. 写一个 `Assets/Editor/XxxTask.cs`，`public static void Run()`。
3. 跑 batchmode 把改动写进**源场景**并保存。
4. 调 bake 生成展示场景。
5. 渲染出图给用户看。

Editor 脚本的骨架：

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MyTask
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Main.unity", OpenSceneMode.Single);
        // ... 改动，记得 EditorUtility.SetDirty(obj)
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        MyBake.Run();   // 需要展示结果时
        EditorApplication.Exit(0);
    }
}
```

诊断脚本同样走这条路：dump 结果写到 `_backup/xxx.txt`，用 `File.WriteAllText(..., new UTF8Encoding(false))` 避免 BOM。

## 3. AnimatorController 手改配方（坑最密集）

这是改模里最容易翻车的地方，全部实测过：

### 新建 layer / state machine

- `new AnimatorStateMachine()` 之后调 `AddState` → **NullReferenceException**。
- `ScriptableObject.CreateInstance<AnimatorStateMachine>()` → **编译不过**（CS0311，它不是 ScriptableObject）。
- `Object.Instantiate(已有层的 stateMachine)` 看起来能用，但**会毁掉被当模板的那一层**：复制品和原层共享同一批 state 对象，你在复制品上 `DestroyImmediate(state)`，原层也跟着变空，而且 `m_Name` 还留着旧名字，表现为"层还在但 states=0"。
- **最稳的做法：改造已有层，不要新建**。找现成的同构层改 motion、改名、重建 transition。真要新建，先 dump 确认模板层事后还活着。

### 改参数表

`ctrl.AddParameter()` **调用后不落盘**，YAML 里查不到。必须整体重建：

```csharp
var list = ctrl.parameters.ToList();
list.RemoveAll(p => p.name == name);
list.Add(new AnimatorControllerParameter {
    name = name, type = AnimatorControllerParameterType.Bool, defaultBool = true });
ctrl.parameters = list.ToArray();
```

### 挂 StateMachineBehaviour（如 VRC_AvatarParameterDriver）

`state.AddStateMachineBehaviour<T>()` **返回 null，但对象其实挂上了**，且 `state.behaviours` 立刻读不到。全走 SerializedObject：

```csharp
var so  = new SerializedObject(state);
var arr = so.FindProperty("m_StateMachineBehaviours");
var copy = Object.Instantiate(templateBehaviour);
AssetDatabase.AddObjectToAsset(copy, ctrl);
arr.arraySize += 1;
arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = copy;
so.ApplyModifiedProperties();
```

写完 parameters 也要 `new SerializedObject(driver).FindProperty("parameters")` 逐个填 `name` / `type`（`type.enumValueIndex = (int)ChangeType.Set` 即 0）/ `value`。

**不要** `DestroyImmediate` 删 state 上的 behaviour，会坏 asset；改成复用第一个。

### 重建 transition

```csharp
foreach (var t in st.transitions.Where(x => x != null)) Object.DestroyImmediate(t, true);
st.transitions = new AnimatorStateTransition[0];

var t2 = off.AddTransition(on);           // 默认态出边用 defaultState.AddTransition
t2.hasExitTime = false; t2.exitTime = 0;
t2.hasFixedDuration = true; t2.duration = 0;
t2.AddCondition(AnimatorConditionMode.If, 0, "Param");
```

- **清空必须过滤 null**，直接遍历会遇到 null 元素报 NRE。
- **OR 条件**：多条单条件 transition。
- **AND 条件**：一条 transition 挂多个 `IfNot`。
- `anyStateTransitions` 是 `AnimatorStateTransition[]`，`entryTransitions` 是 **`AnimatorTransition[]`**，别混。

### 多部件一个开关

别为每个部件建层。把多条 `m_IsActive` 曲线写进同一个 clip：

```csharp
var b = EditorCurveBinding.FloatCurve("Path/To/Obj", typeof(GameObject), "m_IsActive");
AnimationUtility.SetEditorCurve(clip, b, AnimationCurve.Constant(0, 1f/60f, 1f));
```

PhysBone 开关是 `typeof(VRCPhysBone)` + `"m_Enabled"`。

### MA 生成的隐藏参数要手动喂

NDMF 会把 `ModularAvatarShapeChanger` / `ObjectToggle` 编译成 `__MA/ReadableProp/<物体>/UnityEngine.GameObject/m_IsActive#N` 这类 Animator 参数驱动的层，**默认值往往是 1**。一旦你删掉了源头的 MA 组件，这些参数就永远卡在 1，表现为"身体某些部位被收没"。

修法是给相关 clip 补参数曲线（binding 的 path 为空、type 为 `typeof(Animator)`）：

```csharp
var b = EditorCurveBinding.FloatCurve("", typeof(Animator), "__MA/ReadableProp/Obj/m_IsActive#0");
```

## 4. Modular Avatar

- `ModularAvatarMergeArmature` 要挂在**被合并物体的 `Armature` 子物体**上，`mergeTarget` 指向 avatar 的 `Armature`，这样衣服根节点保留原位、路径稳定，才能用动画控 `m_IsActive`。
- `LockMode` 是 `ArmatureLockMode` 枚举（namespace `nadena.dev.modular_avatar.core`），**不在类里**。常用值 `BaseToMerge`。
- 组件遇 prefab 实例会直接 throw，实例化后先 unpack。
- `AvatarObjectReference.Set(GameObject)` 一次性写 `referencePath` + `targetObject`，最省事。
- **`ModularAvatarMenuItem` + `ObjectToggle` 这条路有坑**：MA 生成的层只有"打开"动画，没有"关闭"态；而且它生成的参数在 FX 里是 Float、在 expression 里是 Bool，类型对不上；VirtualMenu 也不会自动进 descriptor 的菜单树（那只收 MenuInstaller 装的）。**要可控就自己手写 FX 层 + 改 `VRCExpressionsMenu` asset**。
- `VRCExpressionParameters.Parameter` **没有 `bitfield` 字段**，只有 name / valueType / networkSynced / saved / defaultValue。位数按 bool=1、int/float=8 估算。
- 菜单单页最多 8 项。

## 5. NDMF bake

- `AvatarProcessor.ProcessAvatar(avatar)` 返回 **void**，且**不能处理含 prefab 实例的 avatar**。
- 标准 bake：复制源场景 → 打开副本 → 用 `PrefabUtility.GetOutermostPrefabInstanceRoot` + `UnpackPrefabInstance(..., PrefabUnpackMode.Completely, InteractionMode.AutomatedAction)` 展开所有实例 → `ProcessAvatar` → 从名字含 NDMF 的场景取结果 → 改名 → 保存。
- bake 完读一下层数和参数总数，跟上一次对比，**层数突变通常意味着某个 MA 组件被删了但没接管**。

## 6. 渲染验证（看不见就改不了）

batchmode 里 `Camera.Render` + `RenderTexture` 是可行的，但有三条硬限制：

- **不吃动画**：不跑 animator，所以所有物体状态要自己 `SetActive`。
- **不吃 BlendShape 权重变化**（如果权重是动画驱动的）。被 MA ShapeChanger 烧成静态值的 blendshape 会一直生效，渲染前要显式归零。
- **渲染对比必须分目录**：两轮渲到同一个 `outDir` 同名文件，前一张会被后一张覆盖，得全部重来。

渲特写的可靠办法是**先渲整图再裁切**。batchmode 里手动对相机位置调半天很容易拍偏（拍到脸、拍到袖口），而"整图 + PIL crop + LANCZOS 放大"一次就对。

## 7. 材质与配色

- 改色**优先用素材包里自带的成品配色**。自己把灰度图压暗往往会损失纹理层次，用户一眼就能看出"平涂"。
- 压暗要保留明暗结构：`new = old * k`（k 从 0.4~0.55 起试），不要直接替换成纯色。
- **区域定位别靠肉眼框矩形**：袖口、口袋、下摆会漏。正确做法是把 mesh 的 `uv` 和每个 subMesh 的 `m.GetTriangles(si)` 导出来，用 PIL `ImageDraw.polygon` 逐三角形光栅化成像素级 mask（注意 UV 的 v 轴要翻转：`py = (1 - v) * H`）。
- 镜框这类没有 UV 贴图、颜色来自 `_BaseColor` 纯色 + MatCap 球的材质，改 `_BaseColor` 就够，不用动贴图。
- 贴图精修交给用户本人：区域语义判断不是 AI 的强项，做一份分区参考图给他更有效率。

## 8. 交互纪律

- 每改一轮**必渲染出图**让人看，不要只报"已修改"。
- 用户自己会开 Unity 验证，跑 batchmode 前确认没有编辑器在占工程。
- 用户给的数值（缩放系数、blendshape 值）通常是查过资料的，照做；他说"大一点"可能是"小一点"能解决，先按他说的试。
- 衣服穿模、腋下漏肤这类问题，**先试缩放和身体 blendshape 两条路**，都不行就承认是模型适配问题，别硬凑。
- 删除一律走回收站：

```powershell
Add-Type -AssemblyName Microsoft.VisualBasic
[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($p,'OnlyErrorDialogs','SendToRecycleBin')
```

## 9. 速查：症状 → 原因

| 症状 | 原因 |
|---|---|
| 编译错误后 Unity 秒退、无日志 | 脚本编译失败，人工查新写的 cs |
| 工程打不开 / 报锁 | 上次 batchmode 留下的 `Temp\UnityLockfile` |
| 某个 FX 层"存在但没效果" | states=0（Instantiate 模板被毁），dump 一下 `sm.states.Length` |
| 穿某件衣服身体变成细线 | `__MA/ReadableProp/...` 参数卡在 1，clip 缺参数曲线 |
| 换上新鞋后小腿消失 | 模型小腿靠袜子/靴子补，Body 只到大腿，得同时开袜子 |
| 开关点了只"打开"不"关闭" | MA ObjectToggle 只生成打开动画，要手写 OFF 态 |
| 材质变粉 | shader 引用丢失，检查 `liltoon` 是否在 Packages 里 |
| 贴图改了没生效 | 渲染读的是缓存，重新 import 或确认路径别指向副本 |

## 10. 免责

- 改模涉及第三方模型的著作权，务必遵守原作者的利用条款（多数 VRChat 模型禁止再配布）。
- 上传 VRChat 前请自行检查账号、内容与平台规则。
- 本仓库只收录方法论与脚本模板，不含任何模型、贴图或授权素材。
- 按本文档操作请先备份；作者不对数据丢失负责。

