# 03 — Multi-Backend RAG Engine & `<2ms` Local Vector Cache

> **Scope**: `Packages/com.convai.openai/Runtime/RAG/**`, `Packages/com.convai.openai/Editor/RAG/**`, `Assets/Resources/RagConfig.asset`, `.env.vectorDB`.
> **Optional Sibling Sync Note**: If the standalone package repo (`../ConversationalAIOpenSource/com.convai.openai/`) is also present locally, mirror changes under `Packages/com.convai.openai/` there as well.
> **Update Rule**: If you modify vector providers, `IBulkExportProvider`, `RagLocalCache`, `RagLocalIndex`, `RagConversationBridge`, `RagConfig`, or Editor ingestion tools, update this file before completing your turn.

---

## 1. Architectural Overview (Zero-Network-Wait Local RAG)

To eliminate 1,000–2,500 ms of live remote vector DB + embedding round-trips during a voice conversation, the RAG subsystem pre-syncs the remote knowledge base into an in-memory + disk-persisted index (`RagLocalCache` + `RagLocalIndex`) at startup:

```text
Application Startup (RagConversationBridge.Register -> StartLocalCache)
   │
   ├──► Phase 1 (~5 ms): RagLocalCache loads local JSON snapshot from
   │                     Application.persistentDataPath/rag_cache_<backend>_<collection>.json
   │                     into RagLocalIndex (instant readiness before first user utterance!).
   │
   ├──► Phase 2 (Background): If _provider implements IBulkExportProvider (Qdrant, Pinecone,
   │                          Supabase, LocalBakedAsset), calls ExportAllAsync() to pull all
   │                          chunks + embeddings, atomic-swaps RagLocalIndex, and saves to disk.
   │
   └──► Phase 3 (Background): If warmUpEmbeddingsOnStart == true, batches up to 6 key phrases
                              in a single OpenAI /v1/embeddings call to prime the TLS connection
                              pool and populate the 64-entry LRU query embedding cache.

Runtime Query Execution (RagConversationBridge.ExecuteRetrieveContextAsync)
   │
   ├──► Path A — BM25 Lexical Fast-Path (< 0.5 ms, ZERO network calls):
   │             RagLocalIndex.SearchLexical(query) runs BM25 over tokenized chunks.
   │             If topBM25 >= lexicalFastPathThreshold (default 2.2), returns immediately!
   │
   ├──► Path B — Local Semantic Cosine Search (< 2 ms if query embedding cached, ~120 ms on warm socket):
   │             Gets query vector (from LRU cache or warm OpenAI /v1/embeddings socket) and runs
   │             L2-normalized dot-product over RagLocalIndex.SearchSemantic().
   │
   └──► Path C — Live Remote Provider Fallback:
                 Only used if enableLocalCache == false or the local index has 0 entries.
```

---

## 2. Core RAG Files & Exact Responsibilities

| File Path (under `Packages/com.convai.openai/`) | Class / Interface | Exact Responsibility |
| :--- | :--- | :--- |
| `Runtime/RAG/Models/RagConfig.cs` | `RagConfig : ScriptableObject` | Stores active `backendType` (`LocalBakedAsset`, `SupabasePgVector`, `Pinecone`, `Qdrant`), per-provider credential slots (`qdrant*`, `pinecone*`, `supabase*`), retrieval mode (`AgenticToolCall` vs `ProactiveContextInjection`), and local cache settings (`enableLocalCache`, `persistLocalCacheToDisk`, `warmUpEmbeddingsOnStart`, `lexicalFastPathThreshold`). |
| `Runtime/RAG/Core/IVectorStoreProvider.cs` | `IVectorStoreProvider` | Base provider contract: `ProviderName`, `InitializeAsync`, `UpsertChunksAsync`, `DeleteAllAsync`, `QueryAsync`, `TestConnectionAsync`. |
| `Runtime/RAG/Interfaces/IBulkExportProvider.cs` | `IBulkExportProvider` | Bulk sync contract: `Task<(List<DocumentChunk> chunks, string error)> ExportAllAsync(string apiKeyOverride = null)`. Implemented by **all 4 providers**. |
| `Runtime/RAG/Providers/RagLocalIndex.cs` | `RagLocalIndex`, `RagIndexEntry` | Thread-safe, zero-dependency in-memory index. L2-normalizes vectors on construction for fast dot-product cosine similarity (`SearchSemantic`) and builds an inverted token dictionary with IDF weights for `<0.5 ms` BM25 search (`SearchLexical`). |
| `Runtime/RAG/RuntimeBridge/RagLocalCache.cs` | `RagLocalCache` | Orchestrates disk snapshot load/save (`rag_cache_*.json`), background `IBulkExportProvider.ExportAllAsync()` sync, 64-entry LRU query embedding cache, startup batch warm-up (`WarmUpAsync`), and two-tier query routing (`QueryAsync`). |
| `Runtime/RAG/RuntimeBridge/RagConversationBridge.cs` | `RagConversationBridge : MonoBehaviour` | Bridges `RealtimeConversationManager` and the RAG engine. Registers the `search_knowledge_base` tool in `AgentToolRegistry` (Agentic mode) or hooks `onUserTranscript` with debounce (Proactive mode). Logs `[Latency] RAG search_knowledge_base: X ms`. |
| `Runtime/RAG/Core/OpenAIEmbeddingClient.cs` | `OpenAIEmbeddingClient` | Calls `POST https://api.openai.com/v1/embeddings` (`text-embedding-3-small`, 1536 dims) for single or batched texts. |
| `Runtime/RAG/Providers/QdrantVectorProvider.cs` | `QdrantVectorProvider` | REST client for Qdrant Cloud (`/collections/{name}/points/scroll` with `with_vector: true` for `ExportAllAsync`, `/points/search` for live query). |
| `Runtime/RAG/Providers/PineconeVectorProvider.cs` | `PineconeVectorProvider` | Dual-schema REST client for Pinecone Serverless: supports both standard Vector API (`2024-07`: `/vectors/list` + `/vectors/fetch`) and Document Schema API (`2026-07`: `/namespaces/{ns}/documents/list` + `/documents/fetch`). |
| `Runtime/RAG/Providers/SupabaseVectorProvider.cs` | `SupabaseVectorProvider` | PostgREST + RPC client for Supabase `pgvector`. `ExportAllAsync` paginates `GET /rest/v1/{table}?select=...&limit=500&offset=...` and parses both `pgvector` text strings (`"[0.1,-0.2,...]"`) and `JArray` embeddings. |
| `Runtime/RAG/Providers/LocalAssetVectorProvider.cs` | `LocalAssetVectorProvider` | Reads offline `LocalVectorDatabaseSO` assets. `ExportAllAsync` clones each `float[]` vector (`(float[])c.embedding.Clone()`) so `RagLocalIndex` in-place L2 normalization never mutates the Unity asset. |
| `Editor/RAG/RagKnowledgeBaseWindow.cs` | `RagKnowledgeBaseWindow : EditorWindow` | Unity Editor window (`Tools > OpenAI > RAG Knowledge Base`) for ingesting `.pdf`, `.docx`, `.md`, `.txt`, `.csv`, `.json` files, testing connections, and running live queries. |

---

## 3. Configured Vector Databases (`Assets/Resources/RagConfig.asset`)

- **Default Active Backend**: `Qdrant` (`backendType: 3`)
- **Active Qdrant Collection**: `arture-pump-manual` (1536-dim Cosine, seeded with 4 Centrifugal Water Pump manual chunks: Mechanical Seal, Impeller Clearance, Bearing Lubrication, Shaft Coupling Alignment).
- **Pinecone Host**: `https://rag-provider-tests-ieyzlld.svc.aped-4627-b74a.pinecone.io` (namespace `rag-provider-tests`, uses Pinecone `2026-07` Document Schema).
- **Supabase Table / RPC**: `documents` / `match_documents`.

---

## 4. Where to Butt In (Extension Hooks for Agents)

### Hook A: Adding a New Vector Store Backend (e.g., `Weaviate`, `Milvus`, `Chroma`)
1. Add the enum value to `RagBackendType` in `Packages/com.convai.openai/Runtime/RAG/Models/RagModels.cs`.
2. Create `Packages/com.convai.openai/Runtime/RAG/Providers/YourVectorProvider.cs` implementing **both** `IVectorStoreProvider` and `IBulkExportProvider`.
3. Register the new enum case in `Packages/com.convai.openai/Runtime/RAG/Providers/VectorProviderFactory.cs`.
4. Add credential fields and auto-save/load slot logic in `RagConfig.cs` (`SaveCurrentToSlot` / `LoadSlotIntoCurrent`).
5. If `../ConversationalAIOpenSource/com.convai.openai/` is present locally, mirror the changed package files there as well.

### Hook B: Tuning Local Cache & BM25 Fast-Path Behavior
- Adjust `lexicalFastPathThreshold` on `RagConfig.asset`:
  - Lower values (e.g., `1.5f`) favor `<0.5 ms` zero-network BM25 keyword matches more aggressively.
  - Higher values (e.g., `3.5f`) or `0f` force semantic cosine vector lookup for phrasing/synonym queries.
- Force a manual re-sync at runtime after uploading new documents by calling `ragConversationBridge.RefreshProvider()`.


### Conversation and walkthrough behavior

RagConversationBridge agent tool results distinguish retrieved, no_sources and unavailable. Existing public SearchAsync callers retain list semantics. Use a source name and section only for a claim supported by the returned passage. The direct HUD admits the existing cache-result latency line for inspection.
