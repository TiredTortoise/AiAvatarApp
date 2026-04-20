# AI Avatar Android App — Technical Design Document

**Project codename:** TBD
**Author:** Mason B.
**Date:** April 20, 2026
**Status:** Draft v1.1 — intended as source-of-truth for multi-agent implementation
**Target platform:** Android (primary); architecture kept portable to iOS
**Build stack:** Unity (LTS), C#, VRM / VRoid avatar pipeline

---

## 0. How to use this document

This spec is written to be consumed by multiple AI coding agents working in parallel. To minimize conflicts:

1. Every module has a **contract** (public interface, inputs, outputs, error modes). Agents must implement to the contract, not to each other's internals.
2. Every cross-module data structure has a **schema** (Section 7). If an agent needs to extend a schema, it must update this doc first.
3. Section 10 decomposes the work into **agent-sized tasks** with explicit file ownership. Two agents should never edit the same file concurrently without coordination.
4. Section 12 lists **open questions**. Agents should flag blockers there rather than making silent assumptions.

---

## 1. Vision & product summary

Build an Android app that acts as a conversational front end to a user-chosen LLM, rendered as an expressive 3D avatar. The avatar should feel like a character — not a chatbot with a face. It reacts, gestures, shifts posture, and modulates tone based on the content and emotion of its own replies.

The app's novelty is in the **middleware between the LLM and the renderer**: outgoing prompts are wrapped with a system prompt that asks the model to return *both* a spoken response *and* a structured "performance track" (emotion beats, gestures, gaze targets). The client parses that track and drives the VRM avatar in real time while TTS audio plays.

### 1.1 Elevator pitch

> Pick your model (Claude, GPT, Gemini, or a local model). Talk to it by voice or text. Watch it talk back as a 3D character that actually emotes — rolls its eyes, shrugs, leans in, looks away when thinking — instead of a motionless head with lip-sync.

### 1.2 Differentiators

Compared to existing voice-chat apps and existing VTuber tooling, this app combines: bring-your-own-model flexibility, LLM-driven (not pre-scripted) emotional expression, full mobile-native experience, and a personality layer that users can customize per avatar.

---

## 2. Goals & non-goals

### 2.1 Goals (v1)

The app must allow users to register API keys for at least Anthropic, OpenAI, Google, and one local-model endpoint, and switch between them at runtime. Users can converse via typed text or push-to-talk speech. The AI's text reply drives synchronized TTS audio and a real-time performance track on a VRM avatar, including facial emotion blendshapes, body gestures from a gesture library, gaze direction, and idle motion. Users can load any standard VRM 0.x / 1.0 model and assign it a personality preset that shapes the system prompt. All API keys and preferences are stored encrypted on-device.

### 2.2 Non-goals (v1)

We are not shipping: multiplayer or social features, user-authored avatar creation inside the app (users bring a VRM file), full-body motion capture from the camera, photo-real avatars, Android Auto or wearable variants, or a billing layer in front of the user's own API keys. We are also not building our own LLM, TTS, or STT — all are third-party.

### 2.3 Success criteria

For v1 to be considered shipped: a cold-started session produces an avatar speaking with synchronized lip-sync and at least one body gesture within 3 seconds of the LLM's first streamed token on a mid-tier Android device (Snapdragon 7-gen or equivalent). Emotion changes should feel coupled to content — in a 20-turn qualitative test, reviewers rate expressive alignment ≥ 4/5 on average. Provider switching should not require an app restart.

---

## 3. Personas & user stories

Our primary persona is a curious hobbyist or indie developer who already pays for at least one LLM API and wants a more engaging way to interact with it than a chat window. Secondary personas include VTuber-adjacent creators who want to "bring a character to life," and accessibility-oriented users who prefer voice conversation to reading walls of text.

Representative user stories:

- *As a user*, I want to paste my Anthropic API key once and have it remembered securely, so I don't enter it every session.
- *As a user*, I want to hold a button to talk and release it to send, so I can have a hands-busy conversation.
- *As a user*, I want the avatar to look surprised when I tell it something unexpected, so the interaction feels alive.
- *As a user*, I want to swap my avatar model and have the new one inherit the same personality, so I can experiment with looks without retraining.
- *As a user*, I want to see a transcript of what's being said, so I can scroll back or copy text.
- *As a power user*, I want to edit the system prompt / personality preset, so I can tune behavior.

---

## 4. High-level architecture

The app is organized into six layers, each with a narrow responsibility and a well-defined contract with its neighbors.

```
┌──────────────────────────────────────────────────────────────────┐
│                         UI Layer (Unity UGUI)                    │
│   Chat transcript · PTT button · Settings · Avatar viewport      │
└──────────────────────────────────────────────────────────────────┘
                 │                                 ▲
                 ▼                                 │
┌──────────────────────────────────────────────────────────────────┐
│                   Input Layer                                    │
│   TextInput  · SpeechToText (on-device + cloud fallback)         │
└──────────────────────────────────────────────────────────────────┘
                 │
                 ▼
┌──────────────────────────────────────────────────────────────────┐
│              Prompt Orchestration Layer                          │
│   SystemPromptBuilder · ConversationMemory · ToolRegistry        │
│   Wraps user input with persona + performance-track instructions │
└──────────────────────────────────────────────────────────────────┘
                 │
                 ▼
┌──────────────────────────────────────────────────────────────────┐
│           Provider Abstraction Layer                             │
│   IChatProvider: Anthropic · OpenAI · Google · LocalHTTP         │
│   Streaming token-by-token with unified event shape              │
└──────────────────────────────────────────────────────────────────┘
                 │
                 ▼
┌──────────────────────────────────────────────────────────────────┐
│            Response Pipeline                                     │
│   StreamParser → PerformanceTrack → (SpeechText, CueQueue)       │
└──────────────────────────────────────────────────────────────────┘
                 │                            │
                 ▼                            ▼
┌─────────────────────────────┐   ┌───────────────────────────────┐
│   TTS Layer                 │   │   Avatar Controller (VRM)     │
│   ITTSProvider: ElevenLabs  │   │   Lip-sync · Blendshapes      │
│   · OpenAI · Azure · local  │   │   · Gestures · Gaze · Idle    │
└─────────────────────────────┘   └───────────────────────────────┘
                 │                            ▲
                 └──── viseme stream ─────────┘
```

The critical insight is that the **Response Pipeline is the integration point**. The LLM returns a single stream that encodes both the speech text and the performance cues; the pipeline splits this into two synchronized queues — audio/visemes go to TTS, gestures/emotions go to the avatar controller, and both are timestamped so they stay aligned.

---

## 5. Tech stack

**Unity 6.3 LTS (version `6000.3.x`, latest patch at project start)** is the host engine, chosen for its best-in-class 3D rendering on mobile and the mature VRM ecosystem. Unity 6.3 LTS is supported through December 2027, giving us a ~20-month fix window through v1 and v1.1. Unity 2022.3 LTS was evaluated and rejected (EOL as of May 2025); Unity 6.0 LTS was evaluated and rejected (support ends October 2026, insufficient runway); Tech-stream releases (6.2, 6.4) were evaluated and rejected (not LTS — support ends when the next Tech release ships). All gameplay and middleware code is C#. Networking uses `UnityWebRequest` for non-streaming calls and `System.Net.Http.HttpClient` with `HttpCompletionOption.ResponseHeadersRead` for SSE/chunked streams, since `UnityWebRequest` does not stream cleanly.

For VRM we use **UniVRM `0.131.x`** (official VRM Consortium library) which supports both VRM 0.x and VRM 1.0, including SpringBones, BlendShapeProxy, and the standard facial expression clips (`Joy`, `Angry`, `Sorrow`, `Fun`, plus viseme A/I/U/E/O). UniVRM is officially tested against Unity 2022.3.62f2, not Unity 6; real-world reports on Unity 6000.x show fixable issues (animator controllers, IMGUI compile errors on certain patches, URP exporter gaps). Task T1.5 (see Section 10) is a mandatory day-one spike that verifies the combination works end-to-end on an Android device before downstream work begins.

Speech-to-text uses Android's `SpeechRecognizer` as the default (free, on-device on modern devices) with an optional cloud fallback (OpenAI Whisper via REST) for accuracy. Text-to-speech uses a pluggable provider: ElevenLabs for highest quality, OpenAI TTS and Azure Speech as alternates, and Android's native `TextToSpeech` as an offline fallback.

Secure storage uses the Android Keystore via a small JNI bridge, exposed to C# as a `ISecureStore` interface. Settings and conversation history use SQLite (via `sqlite-net`) for queryability. Analytics and crash reporting are explicitly out of scope for v1 — we do not phone home.

For build tooling: Gradle (Unity-generated), JDK 17, Android SDK 34+ target, min SDK 26 (Android 8.0) to keep modern audio APIs available.

---

## 6. Module breakdown

Each module below is owned by a single logical component. File paths use Unity convention (`Assets/Scripts/<Module>/`).

### 6.1 UI Layer (`Assets/Scripts/UI/`)

Provides the chat transcript view, push-to-talk button, send button, settings screens, and the 3D avatar viewport (a `RawImage` bound to a `RenderTexture` from the avatar camera). The UI is event-driven: it subscribes to `ConversationEvents` and `AvatarEvents` from the domain layer, and publishes `UserInputEvents`. It owns no business logic. Use UGUI for v1; UI Toolkit is a future consideration.

### 6.2 Input Layer (`Assets/Scripts/Input/`)

Contains `ITextInput` (trivial wrapper around the text field) and `ISpeechToText` with two implementations: `AndroidSpeechRecognizer` (JNI) and `WhisperCloudStt` (REST to OpenAI). A `UserInputRouter` chooses which source is active and emits a single `UserUtterance` event downstream. Push-to-talk state is handled here, including VAD (voice activity detection) for auto-send.

### 6.3 Prompt Orchestration (`Assets/Scripts/Prompt/`)

The heart of the app's "personality" is here. `SystemPromptBuilder` composes the outgoing system prompt from three sources: the active personality preset (user-editable), the performance-track instructions (Section 7.1 — a stable block describing the emotion/action schema the model must emit), and any runtime context (current time, user name, recent memory summary). `ConversationMemory` keeps a rolling window of turns plus a summarized long-term memory; it is responsible for deciding what to include in each request to stay under the provider's context limit.

### 6.4 Provider Abstraction (`Assets/Scripts/Providers/`)

The key contract:

```csharp
public interface IChatProvider {
    string ProviderId { get; }                 // "anthropic" | "openai" | "google" | "local"
    IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        CancellationToken ct);
}

public record ChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    string SystemPrompt,
    ModelParams Params,                        // temp, max_tokens, etc.
    IReadOnlyList<ToolDef> Tools);             // optional tool/function defs

public record ChatDelta(
    string? TextChunk,                          // streamed text token(s)
    string? ToolCallJson,                       // partial tool call JSON
    FinishReason? Finish);                      // null until stream ends
```

Each provider translates this contract to its native API:

- `AnthropicProvider` → `/v1/messages` with `stream: true`, SSE parsing for `content_block_delta`.
- `OpenAIProvider` → `/v1/chat/completions` with `stream: true`, SSE `delta.content`.
- `GoogleProvider` → Gemini `generateContent` with `streamGenerateContent`.
- `LocalHttpProvider` → OpenAI-compatible endpoint (Ollama, llama.cpp server, LM Studio all expose this shape), configurable base URL.

A `ProviderRegistry` resolves `ProviderId` → instance and is the only place `new AnthropicProvider(...)` is called.

### 6.5 Response Pipeline (`Assets/Scripts/Response/`)

`StreamParser` consumes `ChatDelta` events and splits the text stream into two channels using the performance-track syntax (Section 7.1). As each complete spoken sentence is identified, it's emitted as a `SpeechSegment` with any preceding cues attached. This lets us start TTS synthesis on the first sentence while the model is still generating the rest — this is the main latency win.

`PerformanceTrack` is the data model carrying `{ segments: SpeechSegment[], cues: Cue[] }`. `Scheduler` advances the track in sync with the audio clock: when audio for segment N starts, any cues anchored to segment N fire.

### 6.6 TTS Layer (`Assets/Scripts/Tts/`)

```csharp
public interface ITTSProvider {
    string ProviderId { get; }
    Task<TtsResult> SynthesizeAsync(
        string text,
        VoiceParams voice,
        CancellationToken ct);
}

public record TtsResult(
    byte[] AudioPcm,            // 16-bit PCM, 24kHz mono preferred
    float[] VisemeTimeline);    // optional viseme ids + timestamps
```

`ElevenLabsTts` is the premium default. `OpenAiTts` and `AzureTts` are alternatives. `AndroidNativeTts` is the offline fallback but produces no viseme timeline — when using it, we fall back to amplitude-driven lip-sync via oVR Lip Sync or a simple RMS threshold on A/I/U/E/O blendshapes.

### 6.7 Avatar Controller (`Assets/Scripts/Avatar/`)

Loads a VRM file (from app bundle or user-selected location via Storage Access Framework) through UniVRM. Exposes:

- `SetEmotion(EmotionId id, float intensity, float fadeMs)` — maps to VRM BlendShapeProxy presets.
- `PlayGesture(GestureId id)` — plays an AnimationClip from the gesture library on an override layer.
- `LookAt(Vector3 worldPos)` or `LookAt(GazeTarget.Camera | User | Nothing)` — IK-based gaze.
- `SetVisemes(float[] timeline)` — drives `Aa/Ih/Ou/Ee/Oh` blendshapes in sync with audio.
- `SetIdle(IdleProfile profile)` — picks an idle breathing / weight-shift loop.

A `GestureLibrary` ScriptableObject maps `GestureId` enum values to AnimationClips. v1 ships with ~20 gestures: `Nod`, `ShakeHead`, `Shrug`, `WaveHello`, `PointAtSelf`, `PointAtUser`, `HandOnChin` (thinking), `Facepalm`, `Clap`, `ThumbsUp`, `ArmsOut` (welcoming), `CrossArms`, `LeanIn`, `LeanBack`, `TiltHead`, `Laugh`, `Sigh`, `Yawn`, `Stretch`, `LookAway`. Agents should treat this list as extensible — adding a gesture is a 3-file change (enum, library entry, prompt's gesture vocabulary).

### 6.8 Settings & Storage (`Assets/Scripts/Settings/`)

SQLite-backed store for conversations, provider configs, and personality presets. API keys go through `ISecureStore` (Android Keystore-backed) and are *never* written to the SQLite file or logs. A `SettingsService` exposes a clean API to the UI layer.

---

## 7. Data contracts & schemas

These are the schemas every agent must respect. Changes require updating this section first.

### 7.1 Performance-track wire format

The LLM is instructed via system prompt to return its reply in a lightweight tagged format that streams well and degrades gracefully. Two equivalent encodings are supported; **v1 uses Option A** because it survives partial streaming cleanly.

**Option A (chosen): inline tags**

```
[emotion: curious 0.6]
That's a good question — let me think.
[gesture: HandOnChin]
[emotion: thinking 0.8]
I think the answer depends on what you mean by "free will."
[gesture: PointAtUser]
[gaze: user]
Do you mean the libertarian sense, or just the absence of coercion?
[emotion: curious 0.7]
```

Grammar:
- `[emotion: <id> <intensity 0..1>]` — sets emotion until next emotion tag
- `[gesture: <GestureId>]` — fires once, anchored to the next spoken token
- `[gaze: user | camera | away | up | down]` — sets gaze target
- `[pause: <ms>]` — forces an audible beat
- Everything outside `[...]` is spoken text

Parser rule: partial tags (e.g. `[emotion: curi`) are buffered until closed or until 200 ms elapses, at which point they're treated as literal text. This prevents the renderer from ever stalling on a malformed stream.

**Option B (not used in v1, documented for reference): JSON structured output.** Requires provider-side structured output support, which not all local models have. Revisit in v1.1.

### 7.2 Emotion vocabulary

v1 emotion IDs map to VRM blendshapes via a per-avatar config file (`emotion_map.json`) so different avatars can redirect emotions to their rigs:

`neutral, joy, amused, curious, thinking, surprised, confused, sad, sympathetic, frustrated, angry, proud, shy, embarrassed, sleepy, excited`

The map file looks like:

```json
{
  "curious":   { "primary": "BrowUp", "weight": 0.8, "secondary": { "SmileSlight": 0.3 } },
  "thinking":  { "primary": "BrowFurrow", "weight": 0.6, "secondary": { "MouthClose": 0.4 } }
}
```

### 7.3 Conversation persistence schema

SQLite tables (abbreviated):

```
conversations(id, title, created_at, provider_id, model_id, personality_id)
messages(id, conversation_id, role, content, created_at, performance_track_json)
providers(id, provider_id, base_url, model_default, last_used_at)
personalities(id, name, system_prompt, voice_params_json, emotion_map_path)
```

API keys are *not* in SQLite. They live in Android Keystore and are referenced by provider_id only.

### 7.4 Provider config shape

```json
{
  "providerId": "anthropic",
  "displayName": "Claude",
  "baseUrl": "https://api.anthropic.com",
  "model": "claude-opus-4-6",
  "maxTokens": 2048,
  "temperature": 0.8,
  "apiKeyRef": "keystore://anthropic"
}
```

---

## 8. Key flows

### 8.1 End-to-end "user speaks → avatar responds"

1. User holds PTT button. `AndroidSpeechRecognizer` streams partial transcripts to the UI for live feedback.
2. User releases. Final transcript emitted as `UserUtterance`.
3. `PromptOrchestrator` appends utterance to `ConversationMemory`, composes the request (system prompt + history + tools), hands off to the active `IChatProvider`.
4. Provider opens a streaming connection. As `ChatDelta` events arrive, `StreamParser` splits tags from speech.
5. At the first completed sentence, `TtsService` requests synthesis. As soon as audio bytes arrive, playback begins and visemes are forwarded to the avatar.
6. Concurrently, each `[emotion: ...]` and `[gesture: ...]` cue is queued and fired by `Scheduler` at the right audio-clock moment.
7. When the provider stream ends, the final `PerformanceTrack` is persisted to SQLite alongside the full message.

The target budget: ≤ 800 ms from PTT-release to first avatar audio on a warm connection, ≤ 3 s cold. This requires streaming TTS (synthesizing sentence 1 while sentence 2 is still being generated by the LLM).

### 8.2 Provider switch mid-conversation

The user opens Settings, selects a different provider, and returns. The active `IChatProvider` reference is swapped atomically on the next turn. Conversation history is trimmed/rewrapped to fit the new provider's context window. The personality preset is unchanged.

### 8.3 Avatar swap

User picks a new VRM file via Storage Access Framework. The current avatar is gracefully unloaded (speech interrupts cleanly); the new one loads; its `emotion_map.json` (bundled with the VRM or derived from a default heuristic) is applied; idle profile is started; conversation resumes.

### 8.4 Offline / degraded mode

If no network: STT falls back to on-device, TTS falls back to `AndroidNativeTts` (amplitude-driven lip-sync), and only `LocalHttpProvider` is selectable for chat. The app should show a clear "Offline — local model only" banner, not silently fail.

---

## 9. Project structure

```
AiAvatarApp/
├── Assets/
│   ├── Scripts/
│   │   ├── App/               # App bootstrapping, DI container
│   │   ├── UI/
│   │   ├── Input/
│   │   ├── Prompt/
│   │   ├── Providers/
│   │   │   ├── Anthropic/
│   │   │   ├── OpenAI/
│   │   │   ├── Google/
│   │   │   └── Local/
│   │   ├── Response/
│   │   ├── Tts/
│   │   ├── Avatar/
│   │   ├── Settings/
│   │   └── Common/            # Shared records, extensions, logging
│   ├── Avatars/                # Default VRM + emotion_map.json
│   ├── Animations/             # Gesture AnimationClips
│   ├── Prefabs/
│   └── Scenes/
│       ├── Boot.unity
│       └── Main.unity
├── Packages/                   # UniVRM, UniTask, sqlite-net, etc.
├── ProjectSettings/
└── docs/                       # This file and related specs
```

Assembly definitions (`.asmdef`) per module enforce one-way dependency edges: UI → Domain → Providers; no reverse references. This prevents agents from accidentally creating cycles.

---

## 10. Agent task decomposition

Tasks are sized so a single coding agent can complete each in one session without touching another task's files. The dependency column indicates which tasks must finish first.

| # | Task | Owns files | Depends on |
|---|------|-----------|------------|
| T1 | Repo + Unity 6.3 LTS project scaffold + asmdefs + CI skeleton | `AiAvatarApp/*` (initial) | — |
| T1.5 | **UniVRM × Unity 6.3 LTS compatibility spike** (see Section 10.1) | `spikes/univrm-unity63/*` | T1 |
| T2 | Common records: `ChatMessage`, `ChatRequest`, `ChatDelta`, `Cue`, `PerformanceTrack` | `Common/Models/*` | T1 |
| T3 | `ISecureStore` + Android Keystore JNI bridge | `Settings/Secure/*` | T1 |
| T4 | SQLite schema + `SettingsService` | `Settings/*` (not Secure) | T1, T2 |
| T5 | Provider: Anthropic streaming client | `Providers/Anthropic/*` | T2 |
| T6 | Provider: OpenAI streaming client | `Providers/OpenAI/*` | T2 |
| T7 | Provider: Google (Gemini) streaming client | `Providers/Google/*` | T2 |
| T8 | Provider: Local (OpenAI-compatible) client | `Providers/Local/*` | T2 |
| T9 | `ProviderRegistry` + provider switching | `Providers/Registry/*` | T5–T8 |
| T10 | STT: Android `SpeechRecognizer` JNI bridge | `Input/Stt/Android/*` | T1 |
| T11 | STT: Whisper cloud fallback | `Input/Stt/Whisper/*` | T2 |
| T12 | `UserInputRouter` + PTT state machine | `Input/*` (top-level) | T10, T11 |
| T13 | TTS: ElevenLabs client | `Tts/ElevenLabs/*` | T2 |
| T14 | TTS: OpenAI / Azure / Android native | `Tts/*` (other dirs) | T2 |
| T15 | `StreamParser` for performance-track grammar | `Response/Parser/*` | T2 |
| T16 | `Scheduler` (audio-clock-synced cue firing) | `Response/Scheduler/*` | T15 |
| T17 | VRM loader + `AvatarController` core | `Avatar/Core/*` | T1 |
| T18 | Gesture library + `GestureId` enum + 20 baseline clips | `Avatar/Gestures/*`, `Animations/*` | T17 |
| T19 | Lip-sync (viseme + RMS fallback) | `Avatar/LipSync/*` | T17, T14 |
| T20 | Gaze IK | `Avatar/Gaze/*` | T17 |
| T21 | `SystemPromptBuilder` + default persona preset + performance-track instruction block | `Prompt/*` | T2 |
| T22 | `ConversationMemory` (rolling + summary) | `Prompt/Memory/*` | T4 |
| T23 | Main scene wiring + DI | `Scenes/Main.unity`, `App/*` | Most of the above |
| T24 | UI: chat transcript + PTT + send | `UI/Chat/*` | T23 |
| T25 | UI: settings, provider picker, avatar picker | `UI/Settings/*` | T23 |
| T26 | End-to-end integration test: canned stream → avatar performance | `Tests/*` | T23 |

Critical path runs T1 → T1.5 → T2 → (T15, T17, T21) → T23 → T26. Providers (T5–T9), TTS (T13–T14), and STT (T10–T12) are parallelizable once T2 lands. Aim to ship T1–T4 day one so everything else unblocks. **T1.5 is a hard gate** — if it fails without workaround, do not proceed to T17 (VRM loader / AvatarController) until a fallback engine or loader is chosen.

### 10.1 T1.5 spec — UniVRM × Unity 6.3 LTS compatibility spike

**Goal:** prove that UniVRM 0.131.x works on Unity 6.3 LTS on a physical Android device before the team commits to the stack. Budget: 1–2 days.

**Deliverable:** a throwaway Unity project at `spikes/univrm-unity63/` plus a short `RESULT.md` documenting each check (pass / pass-with-workaround / fail). The code is disposable; the document is the artifact.

**Required checks:**

1. **VRM load** — A VRM 1.0 file loads at runtime from a file path (not just editor import) using `Vrm10.LoadPathAsync` (or equivalent). Avatar renders in scene.
2. **Blendshapes** — BlendShapeProxy / VRM10 expression API successfully drives `Joy`, `Angry`, and viseme `A` at runtime via script (not just inspector). Weights visibly take effect.
3. **Humanoid animation** — A humanoid `AnimationClip` (e.g. a wave from Mixamo retargeted) plays cleanly on the loaded avatar without the Issue #2646 animator-controller regression. Bones move; feet stay planted.
4. **Android build** — An Android APK builds (IL2CPP, ARM64) and launches on a physical device running Android 12+. The same VRM load + blendshape + animation sequence runs on device.
5. **SpringBones on device** — Hair / cloth SpringBones visibly animate when the avatar moves, on the physical device build.

**Exit criteria:**

- All five pass → close OQ-1 as confirmed; continue with T2+.
- One or more pass with documented workaround → workaround is committed and OQ-1 is closed with caveats noted.
- A blocking failure on check 1, 2, or 4 → escalate. Fallback candidates: (a) pin to latest Unity 6.0 LTS patch and plan a mid-project migration to 6.6 LTS when it ships; (b) evaluate an alternative VRM loader (e.g., `UniGLTF` alone with custom expression driver, or a commercial asset).

**Out of scope for the spike:** TTS integration, chat providers, UI polish. Use placeholder button clicks to trigger the checks.

---

## 11. MVP cut vs. future work

**MVP (v1)** is everything in Section 2.1 plus two providers shipped fully working (Anthropic + OpenAI), at least one premium TTS (ElevenLabs), one bundled VRM avatar, and the baseline gesture library.

**v1.1 targets:** Gemini and local-model providers polished; avatar emotion_map editor in-app; per-personality voice cloning; long-term memory via embeddings; camera-based user-attention detection (look at user when they look at screen).

**v1.5 / future:** iOS port, co-watching a YouTube video with the avatar reacting, tool-use / function-calling surfaced to the UI (avatar runs a web search, shows the result), real-time multi-modal (Gemini Live / OpenAI Realtime) for sub-500ms barge-in.

---

## 12. Open questions (OQ)

These must be resolved before the affected tasks start. Agents encountering a blocking OQ should escalate, not guess.

- ~~**OQ-1 (T1):** Unity 2022.3 LTS vs Unity 6 LTS? UniVRM compatibility with Unity 6 is the deciding factor.~~ **RESOLVED 2026-04-20:** Unity 6.3 LTS (`6000.3.x`, latest patch) + UniVRM `0.131.x`. Unity 2022.3 LTS is EOL (May 2025); Unity 6.0 LTS has insufficient runway (Oct 2026); Tech-stream releases (6.2, 6.4) are not LTS. UniVRM has reported-but-tractable issues on Unity 6000.x; Task T1.5 is a mandatory day-one spike that must pass before downstream VRM work begins. See Section 10.1 for spike spec.
- **OQ-2 (T5–T8):** Do we want a unified tool-use / function-calling surface in v1, or defer to v1.1? Affects `ChatRequest.Tools` contract.
- **OQ-3 (T21):** Default personality — should we ship one ("warm, curious, conversational") or force users to choose at first launch? UX question.
- **OQ-4 (T15):** Do we accept the inline-tag grammar's limitation that avatars cannot overlap two emotions? If yes, keep Option A. If no, we need a richer (and more fragile) schema.
- **OQ-5 (T13):** ElevenLabs pricing at scale — do we warn users about cost or just let them hit their own limit?
- **OQ-6 (T22):** Summary strategy — client-side with the active LLM (cheap, variable quality) vs dedicated summarization model (consistent, extra cost).

---

## 13. Security, privacy, and compliance

API keys never leave the device except to the provider they belong to. Keys are stored in Android Keystore and referenced by opaque handle elsewhere in the app. Conversations are stored locally in SQLite; there is no cloud sync in v1. Logs must scrub anything matching key patterns (regex for `sk-...`, `anthropic_*`, etc.). Network traffic to providers uses TLS 1.2+ with certificate validation enabled (no debug bypass in release builds).

Microphone permission is requested at first PTT use, not at install. Users can delete any conversation or all conversations from Settings; "delete all" must also purge the associated SQLite rows and any cached TTS audio files.

Because this app can be used to generate arbitrary speech and expressions, the default personality preset must include a short safety preamble discouraging harmful impersonation. Users can edit it, but the default should be defensible.

---

## 14. Testing strategy

Unit tests cover the `StreamParser` grammar (including malformed streams, partial tags, and adversarial input), the `ProviderRegistry` swapping logic, and the `ConversationMemory` trimming math. Provider clients are tested with recorded fixtures of real streaming responses so we don't hammer live APIs in CI.

Integration tests run the full pipeline against a `FakeChatProvider` that replays a canned stream of deltas, verifying that the correct cues fire at the correct audio-clock offsets on a headless Unity test runner. This is T26 on the task list and is the single most important guardrail against regressions.

Manual QA checklist for each release covers: cold start on a mid-tier device, provider switch under load, avatar swap mid-utterance, network drop mid-stream, and a 20-minute battery burn to watch for memory leaks in the VRM bone hierarchy.

---

## 15. Glossary

*Blendshape / morph target* — a named deformation of a 3D mesh (e.g. "smile"), driven by a 0–1 weight. VRM standardizes a set for emotions and visemes.
*Cue* — a timestamped instruction in the performance track (emotion change, gesture, gaze, pause).
*Performance track* — the full structured representation of what the avatar should do during a single LLM reply.
*Push-to-talk (PTT)* — hold a button to record, release to send.
*SSE* — Server-Sent Events, the HTTP streaming format used by most chat APIs.
*Viseme* — the mouth shape corresponding to a phoneme; VRM's standard set is A, I, U, E, O.
*VRM* — an open file format for 3D humanoid avatars, built on glTF, with standardized blendshapes and bone mappings.

---

*End of document. Version-control this file alongside the codebase. Amendments should bump the version at the top and leave a short changelog entry below.*

### Changelog
- **v1.1 (2026-04-20):** Resolved OQ-1 (Unity 6.3 LTS + UniVRM 0.131.x). Added Task T1.5 compatibility spike (Section 10.1). Pinned version numbers in Section 5. Updated critical path to include T1.5 as a hard gate.
- **v1 (2026-04-20):** Initial draft.
