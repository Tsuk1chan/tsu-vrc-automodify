# vrchat-mod

> 给 AI Agent 用的 VRChat 改模工作流与踩坑清单。

**中文** | [English](README.md)

一个 [Agent Skill](https://docs.claude.com/en/docs/claude-code/skills)：`SKILL.md` 里是操作手册，agent 在任务涉及 Unity batchmode、Modular Avatar、NDMF、AnimatorController 手改、VRChat 上传前检查时读它，按里面的姿势干活，不用每次从头摸坑。

这不是 Unity 插件，不装进工程，也不包含任何模型、贴图或授权素材。

---

## 适合 / 不适合

**适合**

- 给已有 avatar 接新衣服、新配饰，焊骨骼、配开关
- 重做或精简 Expression Menu 与 FX 开关（多套互斥、全局开关）
- 改材质配色、按 UV 区域批量改贴图
- 用 batchmode 批量渲染验证，让 agent 能"看见"改动结果
- 排查改模后的常见症状（穿模、身体消失、开关失灵）

**不适合**

- 从零建模、雕刻、权重绘制
- 非 VRChat 的 Unity 项目（思路通用，但组件 API 是 VRC SDK 的）
- 想找现成模型或素材的地方

---

## 安装

### DSH

把仓库 clone 到用户 skills 目录：

```bash
git clone https://github.com/Tsuk1chan/tsu-vrc-automodify.git ~/.dsh/skills/tsu-vrc-automodify
```

skill 名取自 `SKILL.md` 里的 `name` 字段（`vrchat-mod`），目录名不必和它一致。重启或刷新后，skill 会出现在可用列表里。

### Claude Code / 其他支持 Agent Skills 的工具

`SKILL.md` 用的是通用格式（YAML front matter + Markdown 正文），放进对应的 skills 目录即可。不同工具对 front matter 的字段要求略有差异，按各自的文档补齐。

---

## 前置条件

- **Unity 2022.3.x**（VRChat 当前使用的版本线）
- 通过 VCC 安装的包：
  - VRChat SDK3 - Avatars
  - [Modular Avatar](https://modular-avatar.nadena.dev/)
  - [NDMF](https://ndmf.nadena.dev/)
  - [lilToon](https://lilxyzw.github.io/liltoon/)
- **Windows + PowerShell**（示例脚本基于 Windows；放后台、拿日志的方式在其他平台要改写）

`scripts/` 下的 C# 文件是可直接放进 `Assets/Editor/` 的模板，需要按自己的场景名和物体名改几处常量。

---

## 内容

`SKILL.md` 共十章：

| 章节 | 内容 |
|---|---|
| 前置 | 依赖包、平台、模板脚本位置 |
| 0 | 开工前三检查：编辑器真实路径、`Temp\UnityLockfile`、源场景 vs bake 场景 |
| 1 | batchmode 的唯一可靠姿势（`Start-Process` 不加 `-Wait`、不放后台作业、编译错误秒退不写日志） |
| 2 | 标准五步流程与 Editor 脚本骨架 |
| 3 | AnimatorController 手改配方（坑最密集）：新建层、参数表、StateMachineBehaviour、transition、多部件合并 |
| 4 | Modular Avatar：Merge Armature、LockMode、ObjectToggle 的局限 |
| 5 | NDMF bake |
| 6 | batchmode 渲染验证的三条硬限制 |
| 7 | 材质与配色、UV 光栅化改色 |
| 8 | 交互纪律（每轮必出图、删除走回收站等） |
| 9 | 症状速查表 |
| 10 | 免责 |

`scripts/` 里三个模板：

| 文件 | 作用 |
|---|---|
| `NdmfBake.cs` | 复制源场景 → unpack 所有 prefab 实例 → NDMF bake → 输出层数/菜单/参数统计 |
| `RenderShot.cs` | 按 job JSON 控制物体显隐、摆相机、输出 PNG |
| `DumpScene.cs` | 一次 dump 场景层级、MA 组件、controller 图层与参数、expression 参数表 |

---

## 为什么用 batchmode

改模的多数操作要写 Editor API，而 Unity 的 Editor API 在 batchmode 下的行为和交互模式有差异；最大的问题是**看不见结果**。这套流程的核心主张是：

> 每一轮改动都跑一遍「改源场景 → bake → 渲染出图」，让 agent 拿到可检查的视觉证据，而不是报告"已修改"。

代价是每轮多花一两分钟，换来的是不用靠猜。

---

## 免责

- 改模涉及第三方模型的著作权，务必遵守原作者的利用条款（多数 VRChat 模型禁止再配布，个人使用也可能有条件）。
- 上传到 VRChat 前请自行检查账号、内容与平台规则。
- 本仓库只收录方法论与脚本模板，**不含任何模型、贴图、Unitypackage 或授权素材**。
- 按 `SKILL.md` 操作请在备份之后进行；作者不对数据丢失负责。

---

## License

MIT
