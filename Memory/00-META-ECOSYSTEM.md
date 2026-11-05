# 00 — Arture Meta-Memory & 3-Project Ecosystem Map

> **FOR ALL AI AGENTS**: Read this file FIRST before exploring or modifying code in `Arture-AR-6000.4.0f1`. It explains the 3-repository Arture ecosystem, the runtime wire contracts between them, which local `./Memory/` file covers each Unity subsystem, and the **Anti-Staleness & Memory Maintenance Protocol**.

---

## 1. The 3-Repository Ecosystem & Ownership Boundaries

Each repository in the Arture ecosystem has **its own self-contained `./Memory/` folder and `AGENTS.md`** so any repository can be cloned and worked on independently without relying on the other two being accessible:

| # | Repository | Git Remote | Role & Memory Ownership Boundary |
| :- | :--- | :--- | :--- |
| **1** | **`Arture-AR-6000.4.0f1`** *(This Repo)* | `https://github.com/th-efool/Arture-AR-6000.4.0f1.git` (`main`) | Unity `6000.4.0f1` AR digital twin client (Android, iOS, Quest, Editor). Owns `./Memory/00`–`03` covering the meta-ecosystem contracts, 3D/AR machine spatial UI (`Assets/Arture UI From Scratch/`), 24kHz voice I/O (`Assets/ConvoAI/`), and the embedded `Packages/com.convai.openai/` runtime. |
| **2** | **`ConversationalAIOpenSource`** *(Standalone UPM Repo)* | `https://github.com/th-efool/Unity-QuestConversationalAI.git` (`feat/native-rag-knowledge-base`) | Standalone open-source distribution repo for `com.convai.openai` and `com.convai.elevenlabs`. Maintains its **own** self-contained `Memory/` folder. *(Note: in this Unity repo the package is at `Packages/com.convai.openai/`; in the open-source repo it is at root `com.convai.openai/`).* |
| **3** | **`arture-appsite`** *(Web Workspace & Voice Relay)* | `https://github.com/th-efool/arture-appsite.git` (`master`) | Next.js operator workspace (`:3100`) and WebSocket Realtime Voice Relay (`:3101/realtime`). Maintains its **own** self-contained `Memory/` folder. |

> **CRITICAL ANTI-STALENESS RULE**:
> - **Never duplicate volatile internal file lists, internal routes, or internal schemas of `arture-appsite` or `ConversationalAIOpenSource` inside this repository's `./Memory/`**, because those two repositories can be updated independently without access to this repo.
> - This `00-META-ECOSYSTEM.md` file intentionally documents only **stable cross-project wire contracts and high-level topology**. If you need internal details of a sibling repository and it is present locally (e.g., `../arture-appsite/Memory/` or `../ConversationalAIOpenSource/Memory/`), read that repository's own `Memory/` folder directly.

---

## 2. End-to-End Runtime Topology (Dual Operating Modes)

`Arture-AR-6000.4.0f1` supports **two runtime connection topologies** controlled by `ArtureRelaySettings.cs` and `Assets/Resources/OpenAIConfig.asset`:

### Mode A: Direct OpenAI + Local In-Memory RAG Mode *(Default Low-Latency Path)*
```text
[Unity AR Client (Arture-AR-6000.4.0f1)]
   │
   ├──► 1. Startup: RagLocalCache loads disk snapshot (rag_cache_*.json) in ~5ms
   │       & background-exports all chunks from Qdrant / Pinecone / Supabase / LocalAsset
   │       via IBulkExportProvider.ExportAllAsync() + warms up OpenAI TLS & top query embeddings.
   │
   ├──► 2. Voice/Text Turn: Connects directly to wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini
   │       (no external relay required; saves 400–900ms RTT per audio chunk).
   │
   └──► 3. Grounding & Spatial Actions:
           ├── AI calls tool `search_knowledge_base(query)` ──► RagLocalCache answers in <0.5ms (BM25) or <2ms (cached cosine)
           └── AI calls tools `Machine_GetComponents`, `Machine_ShowAnswer`, `Machine_Rotate`, etc. ──► MachineInteraction.cs
```

### Mode B: Arture WebSocket Relay Mode *(Enterprise Audit & Server-Grounded Path)*
```text
[Unity AR Client (Arture-AR-6000.4.0f1)]
   │  wss://<relay-host>/realtime?assetCode=ATR-DEMO-PUMP-001
   │  Header: Authorization: Bearer <relayAccessToken>
   ▼
[Arture Voice Relay (:3101/realtime in arture-appsite)]
   ├──► Proxies OpenAI Realtime protocol events (`session.*`, `input_audio_buffer.*`, `response.*`)
   ├──► Performs server-side manual retrieval & session recording
   └──► Automatic Fallback: If the relay fails 5 times at startup and `openAIApiKey` is configured,
        `RealtimeConversationManager.RecordRelayFailureAndCheckFallback()` automatically switches to Mode A (Direct OpenAI).
```

---

## 3. Stable Wire Contract Between Unity & `arture-appsite` Relay

When running in **Mode B (Relay Mode)**, `RealtimeConversationManager.cs` exchanges standard OpenAI Realtime JSON events plus 4 custom `arture.*` JSON events over the WebSocket:

| Event `type` | Direction | Wire Payload & Unity Handling |
| :--- | :--- | :--- |
| **`arture.identify`** | Unity ➔ Relay | `{ "type": "arture.identify", "phone": "<workerPhone>" }` — Sent by `RealtimeConversationManager.IdentifyWorker()` on socket open (`!IsDirectMode`) to identify the technician. |
| **`arture.worker`** | Relay ➔ Unity | `{ "type": "arture.worker", "matched": true, "name": "..." }` — Handled in `RealtimeConversationManager.OnSocketMessage` to populate `WorkerName`. |
| **`arture.knowledge`** | Relay ➔ Unity | `{ "type": "arture.knowledge", "status": "searching\|retrieved\|no_sources\|unavailable\|cancelled", "sources": [{ "originalName": "Pump_Manual.pdf", "page": 14 }] }` — Handled in `RealtimeConversationManager.OnSocketMessage`; formats `KnowledgeSourcesSummary` (`Pump_Manual.pdf · Page 14`) and appends citations to `Machine_ShowAnswer` 3D floating cards. |
| **`arture.session.end`** | Unity ➔ Relay | `{ "type": "arture.session.end" }` — Sent by `RealtimeConversationManager.Shutdown()` before closing a relay WebSocket. |

---

## 4. Directory of Local `./Memory/` Chunks (This Repository)

Always read the specific memory file(s) matching your task before editing code:

| Memory File | Scope & Systems Covered | When to Read & Update |
| :--- | :--- | :--- |
| **[`00-META-ECOSYSTEM.md`](./00-META-ECOSYSTEM.md)** | 3-repo boundaries, Dual Operating Modes, `arture.*` wire contract, memory maintenance protocol. | Read on **every** session. Update only if cross-repo wire contracts or high-level modes change. |
| **[`01-UNITY-AR-SPATIAL-SYSTEM.md`](./01-UNITY-AR-SPATIAL-SYSTEM.md)** | `MachineInteraction.cs`, `MachineProfile.cs`, `MachineComponent.cs`, `MachineDefinition.cs`, `WorldSpaceUI.cs`, `Arture/MachineFocus` shader, `WaterPump.prefab`, AR Foundation setup. | Read/update when touching 3D models, component highlighting, x-ray fade, 3D floating cards, or `Machine_*` AI tools. |
| **[`02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md`](./02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md)** | `RealtimeConversationManager.cs`, `ConvoAIMicPermissionStarter.cs`, `ConvoAIDebugOverlay.cs`, `PcmAudioPlayer.cs`, `ArtureRelaySettings.cs`, `OpenAIConfig.cs`, `ToolBindings.cs`, `AgentToolRegistry.cs`. | Read/update when touching WebSocket events, microphone capture, virtual mic filtering, VAD/PTT, voice speed (`1.15x`), audio playback, or the debug HUD. |
| **[`03-RAG-VECTOR-KNOWLEDGE-ENGINE.md`](./03-RAG-VECTOR-KNOWLEDGE-ENGINE.md)** | `RagConfig.cs`, `RagConversationBridge.cs`, `RagLocalCache.cs`, `RagLocalIndex.cs`, `IBulkExportProvider.cs`, `Qdrant` / `Pinecone` / `Supabase` / `LocalAsset` providers, Editor ingestion window. | Read/update when touching vector DB providers, embeddings, local BM25/cosine cache, `RagConfig.asset`, or document chunking. |

---

## 5. Mandatory Agent Rules & Memory Maintenance Protocol

1. **Read Before Writing**:
   - Before modifying any subsystem in this repository, read its corresponding `./Memory/0X-*.md` file to understand existing invariants, performance budgets, and extension hooks ("Where to Butt In").
2. **Never Break Latency or Audio Safety Invariants**:
   - **Never** remove the virtual/loopback microphone filter in `ConvoAIMicPermissionStarter.cs` (`FliFlik`, `Virtual Audio`, `VB-Audio`, `Stereo Mix`, etc.) — doing so causes PC desktop audio to loop back into OpenAI in an infinite self-talking loop.
   - **Never** fire `onUserTranscript` on partial `conversation.item.input_audio_transcription.delta` tokens — only fire on `completed`.
   - **Never** add per-item or per-chunk `Debug.Log` calls on the WebSocket receive/send hot path.
   - **Never** mutate `LocalVectorDatabaseSO` embedding arrays in place — always `.Clone()` before passing to `RagLocalIndex`.
3. **Update `./Memory/` When You Make Changes**:
   - Whenever you add a feature, change a method signature, add a tool, modify a protocol event, or alter configuration in this repository, **you MUST update the relevant `./Memory/0X-*.md` file(s)** in the same session.
   - Do **not** add volatile internal details of external repos (`arture-appsite` or `ConversationalAIOpenSource`) into this repo's `./Memory/` so nothing here goes stale when those repos change independently.
4. **Optional Sibling Package Sync (When `../ConversationalAIOpenSource` is Present Locally)**:
   - If you modify `Packages/com.convai.openai/` and the sibling repo `../ConversationalAIOpenSource/com.convai.openai/` is accessible on disk, mirror your package changes (and any relevant `Memory/` updates) there as well.
5. **Fast Headless C# Compilation Check**:
   ```powershell
   dotnet build "./com.convai.openai.csproj" --no-restore -v minimal
   ```
