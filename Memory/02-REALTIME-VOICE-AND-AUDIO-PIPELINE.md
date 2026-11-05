# 02 — Realtime Voice, Direct OpenAI vs Relay, 24kHz Audio & Debug HUD

> **Scope**: `Packages/com.convai.openai/Runtime/Scripts/**`, `Packages/com.convai.openai/Runtime/Common/**`, `Assets/ConvoAI/ConvoAIMicPermissionStarter.cs`, `Assets/ConvoAI/ConvoAIDebugOverlay.cs`.
> **Optional Sibling Sync Note**: If the standalone package repo (`../ConversationalAIOpenSource/com.convai.openai/`) is also present locally, mirror changes under `Packages/com.convai.openai/` there as well.
> **Update Rule**: If you modify WebSocket connection logic, event handlers, microphone capture, virtual mic filtering, VAD/PTT behavior, voice speed, or the debug overlay, update this file before completing your turn.

---

## 1. Architectural Overview & Startup Handshake

```text
Unity Scene Load
   │
   ├──► 1. RealtimeConversationManager.Awake()
   │       ├── PrewarmCloudRelay() (skipped if settings.isDirectOpenAI == true)
   │       └── EnsureRagBridge() -> attaches & registers RagConversationBridge + starts RagLocalCache
   │
   ├──► 2. ConvoAIMicPermissionStarter.Awake()  [DefaultExecutionOrder(-100)]
   │       ├── Disables legacy 16kHz MicrophoneStreamer component (prevents 1.5x chipmunk pitch shift)
   │       └── Upgrades model field on RealtimeConversationManager to "gpt-realtime-2.1-mini"
   │
   ├──► 3. ConvoAIMicPermissionStarter.Start()
   │       ├── Requests Android RECORD_AUDIO / iOS Microphone permissions on device builds
   │       └── Calls RealtimeConversationManager.StartAgent()
   │
   ├──► 4. WebSocket Handshake (Direct OpenAI or Relay)
   │       ├── OpenAI sends `session.created` -> RealtimeConversationManager sends initial `session.update`
   │       ├── OpenAI replies `session.updated` -> sets `_sessionReady = true`
   │       └── On next Update(), ConvoAIMicPermissionStarter detects `_sessionReady == true`:
   │            ├── Calls UpgradeSessionAndStart24kMic() (filters virtual mics, opens 24kHz ring buffer)
   │            └── Calls SendEnhancedSessionUpdate() (injects AR + RAG tool specs, 1.15x voiceSpeed,
   │                near_field noise reduction, gpt-live-transcribe, and 350ms server_vad or null for PTT)
```

---

## 2. Core Files & Exact Responsibilities

| File Path | Class | Key Responsibilities & Invariants |
| :--- | :--- | :--- |
| `Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs` | `RealtimeConversationManager` | Central WebSocket client (`NativeWebSocket`), event router (`OnSocketMessage`), tool execution coordinator (`ExecuteToolCallIfReady`), text message sender (`SendUserTextMessage`), barge-in canceller (`InterruptNow` / `TryCancelAssistantResponse`), voice speed (`voiceSpeed = 1.15f`), and `[Latency]` first-audio telemetry logger. |
| `Packages/com.convai.openai/Runtime/Scripts/ArtureRelaySettings.cs` | `ArtureRelaySettings` | Resolves runtime connection config from `OpenAIConfig.asset`, `ArtureRelayConnection.json`, `RagConfig.asset`, and `.env` files. Exposes `isDirectOpenAI` (`true` by default when `useDirectOpenAI` is enabled on `OpenAIConfig`), `directOpenAIUrl` (`wss://api.openai.com/v1/realtime`), and `openAIApiKey`. |
| `Packages/com.convai.openai/Runtime/Scripts/OpenAIConfig.cs` | `OpenAIConfig : ScriptableObject` | Asset at `Assets/Resources/OpenAIConfig.asset`. Stores `useDirectOpenAI = true`, `directOpenAIUrl`, `apiKey`, `relayUrl`, `deviceRelayUrl`, `relayAccessToken`, `assetCode = "ATR-DEMO-PUMP-001"`, and `workerPhone`. |
| `Packages/com.convai.openai/Runtime/Common/PcmAudioPlayer.cs` | `PcmAudioPlayer` | Decodes incoming base64 24kHz PCM16 chunks (`response.output_audio.delta`) into `AudioClip`s, queues them, plays sequentially, `Destroy()`s finished clips to prevent native memory leaks, and exposes **`IsPlaying`** (`audioSource.isPlaying \|\| _clipQueue.Count > 0`). |
| `Packages/com.convai.openai/Runtime/Scripts/AgentToolRegistry.cs` | `AgentToolRegistry` | Static registry mapping tool names (`Machine_*`, `search_knowledge_base`) to async `Func<JObject, Task<object>>` handlers and `JArray` tool schemas for `session.update`. |
| `Assets/ConvoAI/ConvoAIMicPermissionStarter.cs` | `ConvoAIMicPermissionStarter` | 24kHz PCM16 microphone capture, virtual/loopback audio device blocker, RMS silence gate (`0.0025f` + `0.65s` hangover), AI speech echo gate (`_audioPlayer.IsPlaying` + `0.45s` tail), Auto-VAD vs Tap-to-Speak (PTT) controller, and enhanced session configurator (`voiceSpeed = 1.15f`). |
| `Assets/ConvoAI/ConvoAIDebugOverlay.cs` | `ConvoAIDebugOverlay` | `[DefaultExecutionOrder(-90)]`. Immediate-mode (`OnGUI`) HUD displaying connection status, mic state, active tool count, live transcripts, quick preset chips (`🔍 Parts`, `⚙️ Impeller`, `📚 Seal RAG`, `🔄 Rotate 45°`), text chat input field, and mute/PTT toggles. |

---

## 3. Critical Audio, Latency & Anti-Spam Invariants (DO NOT BREAK)

1. **Virtual / Loopback Microphone Protection (`ConvoAIMicPermissionStarter.cs`)**:
   - PCs without a physical microphone plugged in often expose virtual loopback drivers as `Microphone.devices[0]` (e.g., `FliFlik Virtual Audio Input`, `Virtual Audio`, `VB-Audio`, `CABLE Output`, `Voicemeeter`, `Stereo Mix`, `Loopback`).
   - `FindPhysicalMicrophoneDevice()` **filters out** all virtual/loopback devices. If no physical mic is found, `NoPhysicalMicDetected` is set to `true`, the mic is muted (`_isMicMuted = true`), `turn_detection` is set to `null`, and the virtual device is **never** opened.
2. **Queue-Aware Echo Gating (`PcmAudioPlayer.IsPlaying`)**:
   - OpenAI sends `response.output_audio.done` (`_agentSpeaking = false`) as soon as the last audio packet finishes *downloading*—which is 1–4 seconds before `PcmAudioPlayer` finishes playing the queued clips.
   - Furthermore, `audioSource.isPlaying` briefly turns `false` on the 1-frame boundary between two queued `AudioClip`s.
   - Therefore, `ConvoAIMicPermissionStarter.Stream24kMicrophoneChunks()` checks `_audioPlayer.IsPlaying` (which includes `_clipQueue.Count > 0`) **plus** a `0.45s` post-playback cooldown (`_agentFinishedSpeakingTime`) so speaker reverb never triggers a self-talking loop.
3. **Zero Idle WebSocket Upload Traffic (RMS Noise-Floor Gate)**:
   - In Auto-VAD mode, `SendAudioSamplesBuffer()` computes `CurrentMicRms`. When `CurrentMicRms < 0.0025f` for more than `VadSilenceHangoverSec` (`0.65s`, enough trailing silence for OpenAI's `350ms` server VAD to commit the turn), silent chunks are dropped locally instead of flooding the WebSocket at 10 packets/sec.
4. **No Per-Delta `onUserTranscript` or Per-Item `Debug.Log` Spam**:
   - `HandleInputTranscriptDelta` in `RealtimeConversationManager.cs` is intentionally a no-op; only `HandleInputTranscriptCompleted` invokes `onUserTranscript` once per finished user sentence.
   - `conversation.item.added` and `conversation.item.done` route to `HandleNoop()`. Never call `Debug.Log` inside high-frequency WebSocket message handlers (Unity Editor stack-trace capture costs `1–3 ms` per log on the main thread).
5. **Guarded `response.cancel`**:
   - `TryCancelAssistantResponse()` only sends `{"type":"response.cancel"}` over the WebSocket when `_responseActive == true`, and `HandleError` ignores `response_cancel_not_active` and `input_audio_buffer_commit_empty`.

---

## 4. Where to Butt In (Extension Hooks for Agents)

- **Changing AI Speaking Speed**:
  - Adjust `voiceSpeed` (default `1.15f`, range `0.5f–1.5f`) on `ConvoAIMicPermissionStarter` or `RealtimeConversationManager`. Setting `ConvoAIMicPermissionStarter.Instance.VoiceSpeed = 1.2f` at runtime immediately dispatches a `session.update` with `audio.output.speed`.
- **Changing System Instructions / Prompt Rules**:
  - Edit `enhancedInstructions` in `ConvoAIMicPermissionStarter.SendEnhancedSessionUpdate()` (for general AR + RAG sessions) and `RealtimeConversationManager.SendSessionUpdate()` (initial session setup).
- **Adding Quick Test Chips or HUD Controls**:
  - Edit `ConvoAIDebugOverlay.OnGUI()` around the `// Quick Preset Chips for Arture` section and call `SendTypedText("...")`.
- **Switching Between Direct OpenAI Mode and Arture Relay Mode**:
  - Toggle `useDirectOpenAI` on `Assets/Resources/OpenAIConfig.asset` (or call `RealtimeConversationManager.ForceReconnectRelay(url)`).


### Conversation and walkthrough behavior

RealtimeConversationManager publishes assistant item-keyed preview deltas and final assistant message content for ConvoAIDebugOverlay history. Agent speaking is an audio status only. Typed submissions and completed voice transcripts publish onUserTranscript once; input deltas do not. Direct session instructions route walkthroughs to the tour tools and require explicit source/absence reporting.
