# AI Agent Operating Manual (`AGENTS.md` — `Arture-AR-6000.4.0f1`)

> **MANDATORY FIRST STEP FOR ALL AI AGENTS:**
> Before exploring files, running searches, or writing code, **read [`Memory/00-META-ECOSYSTEM.md`](./Memory/00-META-ECOSYSTEM.md)** first, then read only the specific `Memory/0X-*.md` chunk(s) relevant to your task.

---

## 1. Modular Agent Memory Index (`./Memory/`)

All architectural knowledge for this Unity AR project (`Arture-AR-6000.4.0f1`) and its external wire contracts is stored in [`./Memory/`](./Memory/):

| Memory Chunk | Scope & When to Read / Update |
| :--- | :--- |
| [`Memory/00-META-ECOSYSTEM.md`](./Memory/00-META-ECOSYSTEM.md) | **Read First Always.** High-level 3-project ecosystem map (`Arture-AR-6000.4.0f1`, `ConversationalAIOpenSource`, `arture-appsite`), Dual Operating Modes (Direct OpenAI vs. WebSocket Relay), `arture.*` WebSocket wire contracts, and the Anti-Staleness Memory Protocol. |
| [`Memory/01-UNITY-AR-SPATIAL-SYSTEM.md`](./Memory/01-UNITY-AR-SPATIAL-SYSTEM.md) | 3D/AR machine visualization, `MachineInteraction`, `MachineProfile`, `MachineComponent`, `MachineDefinition`, `WorldSpaceUI`, `Arture/MachineFocus` shader, and the 6 `Machine_*` AI tool callbacks. |
| [`Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md`](./Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md) | `RealtimeConversationManager`, `ConvoAIMicPermissionStarter`, `PcmAudioPlayer`, `ConvoAIDebugOverlay`, `ArtureRelaySettings`, `OpenAIConfig`, 24kHz PCM16 streaming, virtual mic filtering, queue-aware echo gating, RMS silence gating, and `1.15x` voice speed. |
| [`Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md`](./Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md) | `Packages/com.convai.openai/Runtime/RAG/`, `RagConfig`, `RagConversationBridge`, `RagLocalCache`, `RagLocalIndex` (`<0.5 ms` BM25 + `<2 ms` cosine similarity), `IBulkExportProvider`, and the 4 vector providers (`Qdrant`, `Pinecone`, `Supabase`, `LocalAsset`). |

> **Note on Sibling Repositories (`ConversationalAIOpenSource` & `arture-appsite`)**:
> Both `ConversationalAIOpenSource` and `arture-appsite` maintain **their own independent `./Memory/` folders and `AGENTS.md`**. Never duplicate their internal file inventories or internal routes inside this repository's `./Memory/` so that updates in those repositories never leave this repository's memory outdated.

---

## 2. Mandatory Memory Maintenance Rule (Update When You Change Code)

Whenever you make **any architectural, behavioral, schema, tool, or file-level change** in this repository, you **MUST update the corresponding `./Memory/0X-*.md` file(s)** before finishing your task:

1. **Added, renamed, or deleted a script/prefab/shader in this project?**
   - Update the **"Key Files & Locations"** table in the matching `./Memory/0X-*.md` chunk.
2. **Changed system behavior, thresholds, protocol events, or invariants?**
   - Update the **"How It Works"** and **"Invariants"** sections in the matching `./Memory/0X-*.md` chunk.
3. **Added a new AI tool (`AgentTool`), vector provider, or extension hook?**
   - Document it in the **"Where to Butt In (Extension Points)"** section of the matching `./Memory/0X-*.md` chunk.
4. **Modified anything inside `Packages/com.convai.openai/`?**
   - Update [`Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md`](./Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md) or [`Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md`](./Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md). If the sibling repository `../ConversationalAIOpenSource/com.convai.openai` is present on disk, mirror the package changes and its `Memory/` update there as well.

---

## 3. Critical Guardrails & Quick Verification Commands

- **Fast Package Compile Check (`~2 seconds`)**:
  ```powershell
  dotnet build "./com.convai.openai.csproj" --no-restore -v minimal
  ```
- **Never commit raw API keys or tokens** (`sk-...`, `pcsk_...`, Supabase JWTs) into `Memory/*.md`, `AGENTS.md`, or tracked source files.
- **Preserve Audio & Console Anti-Spam Invariants**:
  - Never select virtual loopback audio devices (`FliFlik`, `VB-Audio`, `Voicemeeter`, `Stereo Mix`) as the default mic on PC (`ConvoAIMicPermissionStarter.FindPhysicalMicrophoneDevice`).
  - Keep `PcmAudioPlayer.IsPlaying` true across queued clips (`audioSource.isPlaying || _clipQueue.Count > 0`).
  - Do not fire `onUserTranscript` on per-word `input_audio_transcription.delta` events—only on `.completed`.
