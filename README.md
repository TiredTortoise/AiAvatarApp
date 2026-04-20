# AiAvatarApp

An Android app that acts as a conversational front end to a user-chosen LLM (Claude, GPT, Gemini, or a local model), rendered as an expressive 3D VRM avatar. The avatar reacts, gestures, shifts posture, and modulates tone based on the content and emotion of its own replies. The novelty is middleware between the LLM and the renderer: outgoing prompts are wrapped with a system prompt that asks the model to return *both* a spoken response *and* a structured performance track (emotion beats, gestures, gaze targets), which the client parses and drives on the avatar in real time while TTS audio plays.

Full spec: [docs/design/AI_Avatar_App_Design_Doc.md](docs/design/AI_Avatar_App_Design_Doc.md).

---

## Pinned versions

These two versions define the engine-and-library contract. Do **not** bump them without updating the design doc.

| Component | Version |
|---|---|
| Unity | `6000.3.13f1` (6.3 LTS) |
| UniVRM | `v0.131.0` |

Other package versions are in [`Packages/manifest.json`](Packages/manifest.json) and may be bumped freely for patches as long as the pinned two remain fixed.

## Open the project

1. Install **Unity Hub**.
2. In Unity Hub → *Installs* → install **Unity `6000.3.13f1`** with modules: *Android Build Support* (with OpenJDK, Android SDK & NDK, IL2CPP).
3. *Projects* → *Add* → select this repo folder (`AiAvatarApp/`). Hub should show Unity version `6000.3.13f1`.
4. Open. First launch resolves UPM packages (UniVRM pulls from GitHub — network required).
5. Verify the console is clean: zero errors, zero warnings.

## Run the scaffold

1. In the Project window, open `Assets/Scenes/Boot.unity`.
2. Press Play. Boot loads `Main.unity`, which renders a placeholder label: *"AI Avatar App — scaffold ready."*

That's all T1 ships. T2+ implement the actual functionality.

## Project layout

See design doc [Section 9](docs/design/AI_Avatar_App_Design_Doc.md#9-project-structure). Key facts:

- `Assets/Scripts/<Module>/` — one folder per module, each with its own `.asmdef`.
- `Assets/Editor/Scaffold/` — Editor-only utility that (re)generates Boot/Main scenes and applies Player settings. Menu: *Tools → AiAvatarApp*.
- `Assets/Tests/EditMode/` — placeholder EditMode test. CI runs these.
- `docs/design/` — design doc + T1.5 spike prompt.
- `spikes/univrm-unity63/` — reserved for T1.5 spike output.

## Assembly definitions / dependency rule

The module asmdefs enforce a one-way dependency graph: **UI → Domain → Providers, never the reverse.** Only `AiAvatarApp.App` (the composition root) is allowed to reference concrete providers. See [CONTRIBUTING.md](CONTRIBUTING.md) for the full rule and what to do if you think you need to cross it.

## For coding agents

If you're an AI agent picking up a task (T2+), **read [CONTRIBUTING.md](CONTRIBUTING.md) first.** It covers task ownership, branch naming, PR requirements, and the hard rules you must not break.

## CI setup

The CI workflow at [`.github/workflows/ci.yml`](.github/workflows/ci.yml) activates a Unity license and runs EditMode tests + an Android build verification pass. It requires one repo secret:

- **`UNITY_LICENSE`** — Unity Personal license XML. Generate it locally once:
  1. On your dev machine, activate Unity in batch mode:
     ```
     "/Applications/Unity/Hub/Editor/6000.3.13f1/Unity.app/Contents/MacOS/Unity" \
       -batchmode -createManualActivationFile -logFile -
     ```
     This creates `Unity_v6000.3.13f1.alf`.
  2. Upload the `.alf` at <https://license.unity3d.com/manual> and download the resulting `.ulf`.
  3. Copy the full `.ulf` contents and paste into the GitHub repo *Settings → Secrets and variables → Actions → New repository secret* named `UNITY_LICENSE`.

**Do not commit the `.alf` or `.ulf` files to git.** The `.gitignore` excludes `*.apikey` and `LocalSecrets/` as a general safety net, but Unity license files should be handled out-of-band.

On first CI run after adding new asmdefs, the license cache may need refreshing. Re-running the failed job usually suffices; if not, purge the Actions cache for this repo.

## Known gaps / follow-ups

- **T1.5 — UniVRM × Unity 6.3 LTS compatibility spike** has not yet been run. See [docs/design/T1.5_UniVRM_Unity63_Spike_Prompt.md](docs/design/T1.5_UniVRM_Unity63_Spike_Prompt.md). Do not start T17 (VRM loader) until T1.5 passes.
- The Main scene uses a **legacy UGUI `Text`** for the placeholder label, not TextMeshPro, so T1 doesn't require importing *TMP Essentials*. The UI task (T24) should switch to TMP.
- **No DI framework yet.** T23 decides between VContainer / Zenject / manual. `App/` has only a trivial `Bootstrap`.
- **Git LFS is not configured.** When real VRM avatars + animation assets land (T17/T18), add LFS tracking for `*.vrm`, `*.fbx`, `*.png`, `*.wav`.
