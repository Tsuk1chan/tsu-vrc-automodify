# vrchat-mod

> An AI-agent skill for modifying VRChat avatars — workflow playbook plus a catalog of the traps you'll hit along the way.

[中文](README_CN.md) | **English**

This is an [Agent Skill](https://docs.claude.com/en/docs/claude-code/skills): `SKILL.md` is the playbook. An agent loads it whenever a task involves Unity batchmode, Modular Avatar, NDMF, hand-editing AnimatorControllers, or pre-upload checks for VRChat, and follows the verified procedures instead of rediscovering the same pitfalls every time.

It is not a Unity plugin. Nothing gets installed into your project, and this repository contains no models, textures, or licensed assets.

---

## When it helps

**Good fit**

- Adding outfits or accessories to an existing avatar: bone merging, toggle wiring
- Rebuilding or slimming down the Expression Menu and FX layers (mutually exclusive outfits, global toggles)
- Material and color work, including UV-region-based texture edits
- Batch rendering for visual verification, so an agent can actually *see* what it changed
- Diagnosing the usual post-mod symptoms: clipping, vanishing body parts, dead toggles

**Not a fit**

- Modeling, sculpting, or weight painting from scratch
- Unity projects unrelated to VRChat (the workflow transfers, the component APIs don't)
- Looking for ready-made models or assets

---

## Installation

### DSH

Clone the repo into the user skills directory:

```bash
git clone https://github.com/Tsuk1chan/tsu-vrc-automodify.git ~/.dsh/skills/tsu-vrc-automodify
```

The skill name comes from the `name` field in `SKILL.md` (`vrchat-mod`), so the folder name doesn't have to match. Refresh or restart, and the skill appears in the available list.

### Claude Code / other Agent Skills runtimes

`SKILL.md` uses the common format (YAML front matter plus Markdown body). Copy it into the matching skills directory. Front matter requirements differ slightly between tools; fill in the extra fields according to their documentation.

---

## Requirements

- **Unity 2022.3.x** (the version line VRChat currently targets)
- Packages installed through VCC:
  - VRChat SDK3 - Avatars
  - [Modular Avatar](https://modular-avatar.nadena.dev/)
  - [NDMF](https://ndmf.nadena.dev/)
  - [lilToon](https://lilxyzw.github.io/liltoon/)
- **Windows + PowerShell** — the launch recipe in section 1 relies on `Start-Process`; other platforms need a different way to background the process and capture logs

The C# files under `scripts/` are templates meant to be copied into `Assets/Editor/`. Adjust the scene and object names at the top of each file.

---

## Contents

`SKILL.md` has ten sections:

| Section | Topic |
|---|---|
| Requirements | Dependencies, platform, where the template scripts go |
| 0 | Three pre-flight checks: real editor path, `Temp\UnityLockfile`, source scene vs. baked scene |
| 1 | The only reliable way to launch batchmode (`Start-Process` without `-Wait`, never from a background job, compile errors exit silently without a log) |
| 2 | The standard five-step loop and the Editor script skeleton |
| 3 | Hand-editing AnimatorControllers (densest pitfalls): creating layers, rebuilding parameters, attaching StateMachineBehaviours, transitions, merging multiple parts into one toggle |
| 4 | Modular Avatar: Merge Armature, LockMode, and the limits of ObjectToggle |
| 5 | NDMF bake |
| 6 | The three hard constraints of batchmode rendering |
| 7 | Materials and color, UV rasterization for texture edits |
| 8 | Working discipline: render every round, delete via recycle bin, etc. |
| 9 | Symptom → cause quick reference |
| 10 | Disclaimer |

Three templates under `scripts/`:

| File | Purpose |
|---|---|
| `NdmfBake.cs` | Copy the source scene → unpack every prefab instance → NDMF bake → emit layer count, menu, and parameter statistics |
| `RenderShot.cs` | Drive object visibility and camera placement from a job JSON, write PNGs |
| `DumpScene.cs` | One-shot dump of scene hierarchy, MA components, controller layers and parameters, expression parameter table |

---

## Why batchmode

Most modification work has to go through the Editor API, and the Editor API behaves differently in batchmode than it does interactively. The real problem is **you can't see the result**. The core claim of this workflow is:

> Every round runs the full loop — edit the source scene → bake → render — so the agent produces inspectable visual evidence instead of reporting "done".

It costs an extra minute or two per round. What you get back is not having to guess.

---

## Disclaimer

- Modifying avatars involves third-party copyright. Follow the original creator's terms of use; most VRChat models prohibit redistribution, and even personal use may carry conditions.
- Check your account, content, and platform rules before uploading to VRChat.
- This repository contains methodology and script templates only — **no models, textures, Unitypackages, or licensed assets**.
- Back up before following anything in `SKILL.md`. The author is not responsible for data loss.

---

## License

MIT
