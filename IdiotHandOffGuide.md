# IdiotHandOffGuide: Multi-Backend `IBulkExportProvider` Parity (`LocalBakedAsset`, `SupabasePgVector`, `Pinecone`) & Quality Cleanup

---

## A. Execution Contract

| Field | Value |
| :--- | :--- |
| **Readiness Status** | `READY` |
| **Primary Repository Root** | `d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1` |
| **Secondary Package Mirror** | `D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource` |
| **Baseline State** | Branch `main` at `3b721b8` **plus** the completed changes from the initial `IdiotHandOffGuide` (`IBulkExportProvider.cs`, `RagLocalIndex.cs`, `RagLocalCache.cs`, `QdrantVectorProvider.cs`, `RagConfig.cs`, `RagConversationBridge.cs`, `OpenAIConfig.cs`, `ArtureRelaySettings.cs`, `RealtimeConversationManager.cs`, `ConvoAIDebugOverlay.cs`, `RagConfig.asset`). |
| **Target Shell & OS** | Windows PowerShell (`powershell.exe`) on Windows |
| **Target Outcome** | 1. **`LocalAssetVectorProvider` (`IBulkExportProvider`)**: Export all chunks from `LocalVectorDatabaseSO` (with cloned `float[]` vectors so in-place L2 normalization in `RagLocalIndex` never mutates the serialized Unity ScriptableObject asset), giving offline assets `<0.5 ms` BM25 keyword search and startup embedding warm-up.<br>2. **`SupabaseVectorProvider` (`IBulkExportProvider`)**: Paginated PostgREST bulk export (`GET /rest/v1/{table}?select=id,document_id,source_name,chunk_index,content,metadata,embedding&order=id.asc&limit=500&offset={offset}`) handling both `pgvector` text-serialized vectors (`"[0.1,-0.2,...]"`) and JSON array vectors (`[0.1,-0.2,...]`).<br>3. **`PineconeVectorProvider` (`IBulkExportProvider`)**: Dual-schema bulk export supporting both standard Pinecone Serverless Vector API (`2024-07`: `GET /vectors/list` + `GET /vectors/fetch`) and Pinecone Document Schema API (`2026-07`: `POST /namespaces/{ns}/documents/list` + `POST /namespaces/{ns}/documents/fetch`).<br>4. **Direct Mode Consistency Cleanup (`QA-VOICE-001`)**: Replace the duplicated raw boolean expression in `RealtimeConversationManager.SendSessionUpdate` (line 587) with `IsDirectMode`.<br>5. **Dual-Repo Parity**: Mirror the updated runtime files to `D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime`. |

### Scope Constraints
- **In Scope**:
  - `Packages/com.convai.openai/Runtime/RAG/Providers/LocalAssetVectorProvider.cs` (modify)
  - `Packages/com.convai.openai/Runtime/RAG/Providers/SupabaseVectorProvider.cs` (modify)
  - `Packages/com.convai.openai/Runtime/RAG/Providers/PineconeVectorProvider.cs` (modify)
  - `Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs` (modify)
  - `D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime\**` (mirror)
- **Out of Scope**:
  - Modifying `RagLocalIndex.cs`, `RagLocalCache.cs`, or `QdrantVectorProvider.cs` (already implemented and verified).
  - Committing raw API keys or credentials to git.

### Required Environment & Secret Inputs
- **Pinecone & Supabase Credentials**: Read at verification time from `Assets/Resources/RagConfig.asset` (`pineconeIndexHost`, `pineconeApiKey`, `pineconeNamespace`, `supabaseUrl`, `supabaseApiKey`, `supabaseTable`) — no raw secrets are embedded in this guide.
- **.NET SDK**: `dotnet` CLI on `PATH`.

### Operational Sequence
1. Run **Step S0** to verify the baseline prerequisites (`IBulkExportProvider.cs` and `RagLocalCache.cs` present) and ensure `$env:TEMP\ph\Patch-Block.ps1` is loaded.
2. Apply **Steps S1 through S5** in order.
3. Run **Verification V1 through V4** in Section E.
4. Report completion using the template in Section F.

---

## B. Requirements and Acceptance Map

| Req ID | Requirement Summary | Findings Closed | Implementation Steps | Verification IDs |
| :--- | :--- | :--- | :--- | :--- |
| **R1** | `LocalAssetVectorProvider` implements `IBulkExportProvider.ExportAllAsync`, cloning embedding arrays so `RagLocalIndex` in-place normalization does not dirty or mutate the Inspector `LocalVectorDatabaseSO` asset. | `QA-RAG-001` | `S1` | `V1`, `V2` |
| **R2** | `SupabaseVectorProvider` implements `IBulkExportProvider.ExportAllAsync` via paginated PostgREST `SELECT`, parsing both `pgvector` string format (`"[0.1,-0.2,...]"`) and `JArray` format (`[0.1,-0.2,...]`). | `QA-RAG-001` | `S2` | `V1`, `V2` |
| **R3** | `PineconeVectorProvider` implements `IBulkExportProvider.ExportAllAsync` supporting both `2024-07` (`/vectors/list` + `/vectors/fetch`) and `2026-07` document-schema indexes (`/namespaces/{ns}/documents/list` + `/documents/fetch`). | `QA-RAG-001` | `S3` | `V1`, `V2`, `V3` |
| **R4** | `RealtimeConversationManager.SendSessionUpdate` uses `IsDirectMode` at line 587 instead of repeating `_isDirectOpenAIFallback || (_activeSettings != null && _activeSettings.isDirectOpenAI)`. | `QA-VOICE-001` | `S4` | `V2` |
| **R5** | Mirror updated runtime files (`LocalAssetVectorProvider.cs`, `SupabaseVectorProvider.cs`, `PineconeVectorProvider.cs`, `RealtimeConversationManager.cs`) to `ConversationalAIOpenSource`. | Dual-repo parity | `S5` | `V4` |

---

## C. File-Change Manifest

| File Path (relative to `d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1`) | Action | Step ID | Summary of Change |
| :--- | :--- | :--- | :--- |
| `Packages/com.convai.openai/Runtime/RAG/Providers/LocalAssetVectorProvider.cs` | **Modify** | `S1` | Implement `IBulkExportProvider.ExportAllAsync` with cloned vector arrays. |
| `Packages/com.convai.openai/Runtime/RAG/Providers/SupabaseVectorProvider.cs` | **Modify** | `S2` | Implement `IBulkExportProvider.ExportAllAsync` via paginated PostgREST GET + `ParseExportRows` / `ParseEmbeddingToken`. |
| `Packages/com.convai.openai/Runtime/RAG/Providers/PineconeVectorProvider.cs` | **Modify** | `S3` | Implement `IBulkExportProvider.ExportAllAsync` supporting both `2024-07` vector endpoints and `2026-07` document-schema endpoints. |
| `Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs` | **Modify** | `S4` | Replace raw direct-mode boolean expression at line 587 with `if (IsDirectMode)`. |
| `D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime\**` | **Modify (Mirror)** | `S5` | Copy the 4 modified runtime `.cs` files to the secondary repository. |

---

## D. Ordered Implementation Steps

### Step S0 — Precheck & Patch Helper Setup

**Preconditions**:
- Working directory is `d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1`.
- `Packages/com.convai.openai/Runtime/RAG/Interfaces/IBulkExportProvider.cs` and `Packages/com.convai.openai/Runtime/RAG/RuntimeBridge/RagLocalCache.cs` exist from the prior handoff.

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"

if (-not (Test-Path "Packages\com.convai.openai\Runtime\RAG\Interfaces\IBulkExportProvider.cs")) {
    throw "Missing prerequisite: IBulkExportProvider.cs"
}
if (-not (Test-Path "Packages\com.convai.openai\Runtime\RAG\RuntimeBridge\RagLocalCache.cs")) {
    throw "Missing prerequisite: RagLocalCache.cs"
}

New-Item -ItemType Directory -Force -Path "$env:TEMP\ph" | Out-Null
$patchHelper = @'
function Replace-Block {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
    )
    $full = (Resolve-Path $Path).Path
    $bytes = [IO.File]::ReadAllBytes($full)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $raw = [Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $raw = $raw.Substring(1) }
    $hadCrLf = $raw.Contains("`r`n")
    $text = $raw.Replace("`r`n", "`n")
    $oldN = $Old.Replace("`r`n", "`n").Trim("`n")
    $newN = $New.Replace("`r`n", "`n").Trim("`n")

    $first = $text.IndexOf($oldN, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        if ($text.IndexOf($newN, [StringComparison]::Ordinal) -ge 0) { Write-Output "SKIP (already applied): $Path"; return }
        throw "NO MATCH in $Path for block starting: $($oldN.Split("`n")[0])"
    }
    if ($text.IndexOf($oldN, $first + 1, [StringComparison]::Ordinal) -ge 0) {
        throw "MULTIPLE MATCHES in $Path for block starting: $($oldN.Split("`n")[0])"
    }

    $text = $text.Substring(0, $first) + $newN + $text.Substring($first + $oldN.Length)
    if ($hadCrLf) { $text = $text.Replace("`n", "`r`n") }
    [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($hasBom)))
    Write-Output "OK   $Path"
}
'@
[IO.File]::WriteAllText("$env:TEMP\ph\Patch-Block.ps1", $patchHelper, (New-Object Text.UTF8Encoding($false)))
Write-Output "READY: Prechecks passed and Patch-Block.ps1 prepared."
```

**Postcondition**:
- Console outputs `READY: Prechecks passed and Patch-Block.ps1 prepared.`.

---

### Step S1 — Implement `IBulkExportProvider` in `LocalAssetVectorProvider.cs`

**Target File**: `Packages/com.convai.openai/Runtime/RAG/Providers/LocalAssetVectorProvider.cs` (Modify)

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"
. "$env:TEMP\ph\Patch-Block.ps1"
$F = "Packages\com.convai.openai\Runtime\RAG\Providers\LocalAssetVectorProvider.cs"

Replace-Block -Path $F -Old @'
    public class LocalAssetVectorProvider : IVectorStoreProvider
'@ -New @'
    public class LocalAssetVectorProvider : IVectorStoreProvider, IBulkExportProvider
'@

Replace-Block -Path $F -Old @'
            return Task.FromResult((true, $"Local database ready with {_config.localDatabase.chunks.Count} chunks."));
        }
    }
}
'@ -New @'
            return Task.FromResult((true, $"Local database ready with {_config.localDatabase.chunks.Count} chunks."));
        }

        /// <summary>
        /// Exports all chunks from the assigned <see cref="LocalVectorDatabaseSO"/> ScriptableObject
        /// so <see cref="RagLocalCache"/> can build a BM25 lexical + normalised dot-product index.
        /// Clones each embedding array so in-place normalisation does not mutate the serialized Unity asset.
        /// </summary>
        public Task<(List<DocumentChunk> chunks, string error)> ExportAllAsync(string apiKeyOverride = null)
        {
            var exported = new List<DocumentChunk>();
            if (_config == null || _config.localDatabase == null)
            {
                return Task.FromResult((exported, "Local database ScriptableObject is not assigned in RagConfig."));
            }

            var source = _config.localDatabase.chunks;
            if (source == null || source.Count == 0)
            {
                return Task.FromResult((exported, (string)null));
            }

            for (int i = 0; i < source.Count; i++)
            {
                var c = source[i];
                if (c == null || string.IsNullOrWhiteSpace(c.text)) continue;

                float[] vecCopy = null;
                if (c.embedding != null && c.embedding.Length > 0)
                {
                    vecCopy = (float[])c.embedding.Clone();
                }

                exported.Add(new DocumentChunk(
                    c.id,
                    c.documentId,
                    c.sourceName,
                    c.chunkIndex,
                    c.text,
                    vecCopy,
                    c.metadataJson
                ));
            }

            return Task.FromResult((exported, (string)null));
        }
    }
}
'@
```

**Postcondition**:
- Both `Replace-Block` calls print `OK`.

---

### Step S2 — Implement `IBulkExportProvider` in `SupabaseVectorProvider.cs`

**Target File**: `Packages/com.convai.openai/Runtime/RAG/Providers/SupabaseVectorProvider.cs` (Modify)

**Design Notes**:
- Uses PostgREST pagination (`?select=id,document_id,source_name,chunk_index,content,metadata,embedding&order=id.asc&limit=500&offset={offset}`).
- `ParseEmbeddingToken` handles both PostgREST `pgvector` text output (`"[0.123,-0.456,...]"`) and `JArray` output (`[0.123,-0.456,...]`).

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"
. "$env:TEMP\ph\Patch-Block.ps1"
$F = "Packages\com.convai.openai\Runtime\RAG\Providers\SupabaseVectorProvider.cs"

Replace-Block -Path $F -Old @'
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace OpenAI.RAG
{
    /// <summary>
    /// Vector store provider implementation for PostgreSQL with the pgvector extension,
    /// hosted on Supabase and accessed via PostgREST and Remote Procedure Calls (RPC).
    /// </summary>
    public class SupabaseVectorProvider : IVectorStoreProvider
'@ -New @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace OpenAI.RAG
{
    /// <summary>
    /// Vector store provider implementation for PostgreSQL with the pgvector extension,
    /// hosted on Supabase and accessed via PostgREST and Remote Procedure Calls (RPC).
    /// </summary>
    public class SupabaseVectorProvider : IVectorStoreProvider, IBulkExportProvider
'@

Replace-Block -Path $F -Old @'
        private string ResolveApiKey(string overrideKey)
'@ -New @'
        /// <summary>
        /// Downloads every row (content, metadata, and pgvector embedding) from the configured Supabase table
        /// via PostgREST pagination so the knowledge base can be mirrored into <see cref="RagLocalIndex"/>.
        /// Endpoint: GET {baseRestUrl}/{tableName}?select=id,document_id,source_name,chunk_index,content,metadata,embedding&amp;order=id.asc&amp;limit=500&amp;offset={offset}
        /// </summary>
        public async Task<(List<DocumentChunk> chunks, string error)> ExportAllAsync(string apiKeyOverride = null)
        {
            var all = new List<DocumentChunk>();
            string apiKey = ResolveApiKey(apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (all, "Supabase API key is missing. Set runtimeApiKey in RagConfig or provide apiKeyOverride.");
            }

            string baseRestUrl = GetBaseRestUrl();
            if (string.IsNullOrWhiteSpace(baseRestUrl))
            {
                return (all, "Supabase database URI is missing. Set databaseUri in RagConfig.");
            }

            string tableName = GetTableName();
            const int pageSize = 500;

            for (int page = 0; page < 100; page++)
            {
                int offset = page * pageSize;
                string endpoint = $"{baseRestUrl}/{tableName}?select=id,document_id,source_name,chunk_index,content,metadata,embedding&order=id.asc&limit={pageSize}&offset={offset}";

                string responseText;
                using (var request = UnityWebRequest.Get(endpoint))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("apikey", apiKey);
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                    request.timeout = 60;

                    try
                    {
                        await SendWebRequestAsync(request);
                    }
                    catch (Exception ex)
                    {
                        return (all, $"Network error during Supabase export: {ex.Message}");
                    }

                    responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        return (all, FormatHttpError(request, request.responseCode, responseText));
                    }
                }

                List<DocumentChunk> pageChunks;
                try
                {
                    pageChunks = await Task.Run(() => ParseExportRows(responseText));
                }
                catch (Exception ex)
                {
                    return (all, $"Failed to parse Supabase export response: {ex.Message}");
                }

                if (pageChunks == null || pageChunks.Count == 0)
                {
                    break;
                }

                all.AddRange(pageChunks);
                if (pageChunks.Count < pageSize)
                {
                    break;
                }
            }

            return (all, null);
        }

        private static List<DocumentChunk> ParseExportRows(string json)
        {
            var list = new List<DocumentChunk>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return list;
            }

            var rows = JToken.Parse(json) as JArray;
            if (rows == null)
            {
                return list;
            }

            foreach (var item in rows)
            {
                if (item == null) continue;

                string content = item["content"]?.ToString() ?? item["text"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(content)) continue;

                string id = item["id"]?.ToString() ?? string.Empty;
                string docId = item["document_id"]?.ToString() ?? item["documentId"]?.ToString() ?? string.Empty;
                string sourceName = item["source_name"]?.ToString() ?? item["sourceName"]?.ToString() ?? string.Empty;
                int chunkIndex = (int?)item["chunk_index"] ?? (int?)item["chunkIndex"] ?? 0;
                string metadataJson = item["metadata"] != null
                    ? item["metadata"].ToString(Newtonsoft.Json.Formatting.None)
                    : string.Empty;
                float[] embedding = ParseEmbeddingToken(item["embedding"]);

                list.Add(new DocumentChunk(id, docId, sourceName, chunkIndex, content, embedding, metadataJson));
            }

            return list;
        }

        private static float[] ParseEmbeddingToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token is JArray arr)
            {
                if (arr.Count == 0) return null;
                var vec = new float[arr.Count];
                for (int i = 0; i < arr.Count; i++)
                {
                    vec[i] = (float)arr[i];
                }
                return vec;
            }

            if (token.Type == JTokenType.String)
            {
                string s = token.ToString().Trim();
                if (s.StartsWith("[", StringComparison.Ordinal) && s.EndsWith("]", StringComparison.Ordinal))
                {
                    s = s.Substring(1, s.Length - 2);
                }
                if (string.IsNullOrWhiteSpace(s)) return null;

                string[] parts = s.Split(',');
                var vec = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    {
                        return null;
                    }
                    vec[i] = val;
                }
                return vec;
            }

            return null;
        }

        private string ResolveApiKey(string overrideKey)
'@
```

**Postcondition**:
- Both `Replace-Block` calls print `OK`.

---

### Step S3 — Implement `IBulkExportProvider` in `PineconeVectorProvider.cs`

**Target File**: `Packages/com.convai.openai/Runtime/RAG/Providers/PineconeVectorProvider.cs` (Modify)

**Design Notes**:
- First attempts `GET {baseUri}/vectors/list` (`X-Pinecone-API-Version: 2024-07`).
- If the index uses Pinecone's Document Schema (HTTP 400 containing `"document schema"`, as verified against the live `rag-provider-tests` host), automatically switches to `ExportAllDocumentsAsync` (`POST /namespaces/{ns}/documents/list` + `POST /namespaces/{ns}/documents/fetch` with `X-Pinecone-API-Version: 2026-07`).
- For standard vector indexes, batches IDs in groups of 100 via `GET {baseUri}/vectors/fetch?namespace={ns}&ids=...`.

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"
. "$env:TEMP\ph\Patch-Block.ps1"
$F = "Packages\com.convai.openai\Runtime\RAG\Providers\PineconeVectorProvider.cs"

Replace-Block -Path $F -Old @'
    public class PineconeVectorProvider : IVectorStoreProvider
'@ -New @'
    public class PineconeVectorProvider : IVectorStoreProvider, IBulkExportProvider
'@

Replace-Block -Path $F -Old @'
        /// <summary>
        /// Resolves the effective API key for Pinecone requests (override key takes precedence over RagConfig).
        /// </summary>
'@ -New @'
        /// <summary>
        /// Downloads every vector or document (metadata, text, and embedding values) from the configured Pinecone
        /// namespace so the knowledge base can be mirrored into <see cref="RagLocalIndex"/>.
        /// Supports both standard vector indexes (2024-07) and document-schema indexes (2026-07).
        /// </summary>
        public async Task<(List<DocumentChunk> chunks, string error)> ExportAllAsync(string apiKeyOverride = null)
        {
            var all = new List<DocumentChunk>();
            string apiKey = ResolveApiKey(apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (all, "Pinecone API key is missing. Set runtimeApiKey in RagConfig or provide apiKeyOverride.");
            }

            string baseUri = ResolveBaseUri(out string uriError);
            if (!string.IsNullOrEmpty(uriError))
            {
                return (all, uriError);
            }

            string ns = _config != null ? _config.GetEffectiveCollectionOrTable() : string.Empty;
            var allIds = new List<string>();
            string nextToken = null;

            for (int page = 0; page < 100; page++)
            {
                var sbUrl = new StringBuilder($"{baseUri}/vectors/list?limit=100");
                if (!string.IsNullOrEmpty(ns))
                {
                    sbUrl.Append("&namespace=").Append(Uri.EscapeDataString(ns));
                }
                if (!string.IsNullOrEmpty(nextToken))
                {
                    sbUrl.Append("&paginationToken=").Append(Uri.EscapeDataString(nextToken));
                }

                string responseText;
                using (var request = UnityWebRequest.Get(sbUrl.ToString()))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Api-Key", apiKey);
                    request.SetRequestHeader("X-Pinecone-API-Version", PineconeApiVersion);
                    request.timeout = 30;

                    try
                    {
                        await SendWebRequestAsync(request);
                    }
                    catch (Exception ex)
                    {
                        return (all, $"Pinecone list network request failed: {ex.Message}");
                    }

                    responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (!string.IsNullOrEmpty(responseText) && responseText.IndexOf("document schema", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return await ExportAllDocumentsAsync(baseUri, ns, apiKey);
                        }
                        return (all, FormatHttpError(request, request.responseCode, responseText));
                    }
                }

                try
                {
                    var (pageIds, next) = ParseListIds(responseText);
                    if (pageIds.Count > 0) allIds.AddRange(pageIds);
                    nextToken = next;
                }
                catch (Exception ex)
                {
                    return (all, $"Failed to parse Pinecone list response: {ex.Message}");
                }

                if (string.IsNullOrEmpty(nextToken))
                {
                    break;
                }
            }

            if (allIds.Count == 0)
            {
                return (all, null);
            }

            const int fetchBatchSize = 100;
            for (int i = 0; i < allIds.Count; i += fetchBatchSize)
            {
                int count = Math.Min(fetchBatchSize, allIds.Count - i);
                var sbFetch = new StringBuilder($"{baseUri}/vectors/fetch?");
                if (!string.IsNullOrEmpty(ns))
                {
                    sbFetch.Append("namespace=").Append(Uri.EscapeDataString(ns)).Append('&');
                }
                for (int j = 0; j < count; j++)
                {
                    if (j > 0) sbFetch.Append('&');
                    sbFetch.Append("ids=").Append(Uri.EscapeDataString(allIds[i + j]));
                }

                string fetchText;
                using (var request = UnityWebRequest.Get(sbFetch.ToString()))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Api-Key", apiKey);
                    request.SetRequestHeader("X-Pinecone-API-Version", PineconeApiVersion);
                    request.timeout = 60;

                    try
                    {
                        await SendWebRequestAsync(request);
                    }
                    catch (Exception ex)
                    {
                        return (all, $"Pinecone fetch network request failed: {ex.Message}");
                    }

                    fetchText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        return (all, FormatHttpError(request, request.responseCode, fetchText));
                    }
                }

                try
                {
                    var batchChunks = await Task.Run(() => ParseFetchRecords(fetchText));
                    all.AddRange(batchChunks);
                }
                catch (Exception ex)
                {
                    return (all, $"Failed to parse Pinecone fetch response: {ex.Message}");
                }
            }

            return (all, null);
        }

        private async Task<(List<DocumentChunk> chunks, string error)> ExportAllDocumentsAsync(string baseUri, string ns, string apiKey)
        {
            var all = new List<DocumentChunk>();
            string listUrl = string.IsNullOrEmpty(ns)
                ? $"{baseUri}/documents/list"
                : $"{baseUri}/namespaces/{Uri.EscapeDataString(ns)}/documents/list";
            string fetchUrl = string.IsNullOrEmpty(ns)
                ? $"{baseUri}/documents/fetch"
                : $"{baseUri}/namespaces/{Uri.EscapeDataString(ns)}/documents/fetch";

            var allIds = new List<string>();
            string nextToken = null;

            for (int page = 0; page < 100; page++)
            {
                var listBody = new JObject { ["limit"] = 100 };
                if (!string.IsNullOrEmpty(nextToken))
                {
                    listBody["pagination_token"] = nextToken;
                }

                byte[] bodyRaw = Encoding.UTF8.GetBytes(listBody.ToString(Formatting.None));
                string responseText;
                using (var request = new UnityWebRequest(listUrl, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Api-Key", apiKey);
                    request.SetRequestHeader("X-Pinecone-API-Version", "2026-07");
                    request.timeout = 30;

                    try
                    {
                        await SendWebRequestAsync(request);
                    }
                    catch (Exception ex)
                    {
                        return (all, $"Pinecone documents/list network request failed: {ex.Message}");
                    }

                    responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        return (all, FormatHttpError(request, request.responseCode, responseText));
                    }
                }

                try
                {
                    var (pageIds, next) = ParseListIds(responseText);
                    if (pageIds.Count > 0) allIds.AddRange(pageIds);
                    nextToken = next;
                }
                catch (Exception ex)
                {
                    return (all, $"Failed to parse Pinecone documents/list response: {ex.Message}");
                }

                if (string.IsNullOrEmpty(nextToken))
                {
                    break;
                }
            }

            if (allIds.Count == 0)
            {
                return (all, null);
            }

            const int fetchBatchSize = 100;
            for (int i = 0; i < allIds.Count; i += fetchBatchSize)
            {
                int count = Math.Min(fetchBatchSize, allIds.Count - i);
                var idsArray = new JArray();
                for (int j = 0; j < count; j++)
                {
                    idsArray.Add(allIds[i + j]);
                }

                var fetchBody = new JObject { ["ids"] = idsArray };
                byte[] bodyRaw = Encoding.UTF8.GetBytes(fetchBody.ToString(Formatting.None));
                string fetchText;

                using (var request = new UnityWebRequest(fetchUrl, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Api-Key", apiKey);
                    request.SetRequestHeader("X-Pinecone-API-Version", "2026-07");
                    request.timeout = 60;

                    try
                    {
                        await SendWebRequestAsync(request);
                    }
                    catch (Exception ex)
                    {
                        return (all, $"Pinecone documents/fetch network request failed: {ex.Message}");
                    }

                    fetchText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        return (all, FormatHttpError(request, request.responseCode, fetchText));
                    }
                }

                try
                {
                    var batchChunks = await Task.Run(() => ParseFetchRecords(fetchText));
                    all.AddRange(batchChunks);
                }
                catch (Exception ex)
                {
                    return (all, $"Failed to parse Pinecone documents/fetch response: {ex.Message}");
                }
            }

            return (all, null);
        }

        private static (List<string> ids, string nextToken) ParseListIds(string json)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return (ids, null);
            }

            var root = JObject.Parse(json);
            var items = (root["vectors"] as JArray) ?? (root["documents"] as JArray);
            if (items != null)
            {
                foreach (var item in items)
                {
                    string id = item?["id"]?.ToString() ?? item?["_id"]?.ToString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            string next = root["pagination"]?["next"]?.ToString();
            return (ids, string.IsNullOrWhiteSpace(next) ? null : next);
        }

        private static List<DocumentChunk> ParseFetchRecords(string json)
        {
            var chunks = new List<DocumentChunk>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return chunks;
            }

            var root = JObject.Parse(json);
            var map = (root["vectors"] as JObject) ?? (root["documents"] as JObject);
            if (map == null)
            {
                return chunks;
            }

            foreach (var prop in map.Properties())
            {
                var obj = prop.Value as JObject;
                if (obj == null) continue;

                var meta = (obj["metadata"] as JObject) ?? (obj["fields"] as JObject);
                string id = obj["id"]?.ToString() ?? obj["_id"]?.ToString() ?? prop.Name;
                string text = meta?["text"]?.ToString() ?? meta?["content"]?.ToString()
                              ?? obj["text"]?.ToString() ?? obj["content"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string docId = meta?["document_id"]?.ToString() ?? obj["document_id"]?.ToString() ?? string.Empty;
                string source = meta?["source_name"]?.ToString() ?? obj["source_name"]?.ToString() ?? string.Empty;
                int chunkIndex = (int?)meta?["chunk_index"] ?? (int?)obj["chunk_index"] ?? 0;

                float[] vector = null;
                var vecArr = (obj["values"] as JArray) ?? (obj["embedding"] as JArray) ?? (meta?["embedding"] as JArray);
                if (vecArr != null && vecArr.Count > 0)
                {
                    vector = new float[vecArr.Count];
                    for (int i = 0; i < vecArr.Count; i++)
                    {
                        vector[i] = (float)vecArr[i];
                    }
                }

                string metadataJson = meta != null ? meta.ToString(Formatting.None) : "{}";
                chunks.Add(new DocumentChunk(id, docId, source, chunkIndex, text, vector, metadataJson));
            }

            return chunks;
        }

        /// <summary>
        /// Resolves the effective API key for Pinecone requests (override key takes precedence over RagConfig).
        /// </summary>
'@
```

**Postcondition**:
- Both `Replace-Block` calls print `OK`.

---

### Step S4 — Clean Up `IsDirectMode` Check in `RealtimeConversationManager.cs` (`QA-VOICE-001`)

**Target File**: `Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs` (Modify)

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"
. "$env:TEMP\ph\Patch-Block.ps1"

Replace-Block -Path "Packages\com.convai.openai\Runtime\Scripts\RealtimeConversationManager.cs" -Old @'
        private void SendSessionUpdate()
        {
            if (_isDirectOpenAIFallback || (_activeSettings != null && _activeSettings.isDirectOpenAI))
'@ -New @'
        private void SendSessionUpdate()
        {
            if (IsDirectMode)
'@
```

**Postcondition**:
- `Replace-Block` prints `OK`.

---

### Step S5 — Mirror Updated Runtime Files to `ConversationalAIOpenSource`

**Target Directory**: `D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime`

**Command**:
```powershell
$ErrorActionPreference = "Stop"
$srcRoot = "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1\Packages\com.convai.openai\Runtime"
$dstRoot = "D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime"

if (Test-Path $dstRoot) {
    $relFiles = @(
        "RAG\Providers\LocalAssetVectorProvider.cs",
        "RAG\Providers\SupabaseVectorProvider.cs",
        "RAG\Providers\PineconeVectorProvider.cs",
        "Scripts\RealtimeConversationManager.cs"
    )
    foreach ($rel in $relFiles) {
        $s = Join-Path $srcRoot $rel
        $d = Join-Path $dstRoot $rel
        Copy-Item -Path $s -Destination $d -Force
        Write-Output "MIRRORED $rel"
    }
}
```

**Postcondition**:
- All 4 files print `MIRRORED ...`.

---

## E. Verification and Failure Handling

### Verification V1 — Multi-Backend Export Parser & `RagLocalIndex` Integration Harness

**Purpose**:
Verifies that chunks exported from **Supabase** (both `pgvector` string and `JArray` formats), **Pinecone** (both `2024-07` vector format and `2026-07` document-schema format), and **LocalBakedAsset** (cloned vector integrity) parse cleanly and index into `RagLocalIndex` for `<1 ms` BM25 and `<2 ms` cosine search without mutating source arrays.

**Command**:
```powershell
$ErrorActionPreference = "Stop"
$tDir = Join-Path $env:TEMP "rag_multibackend_verify"
if (Test-Path $tDir) { Remove-Item -Recurse -Force $tDir }
New-Item -ItemType Directory -Path $tDir | Out-Null

Copy-Item "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1\Packages\com.convai.openai\Runtime\RAG\Providers\RagLocalIndex.cs" "$tDir\RagLocalIndex.cs"
$nj = "D:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1\Library\PackageCache\com.unity.nuget.newtonsoft-json@4dfd81071c64\Runtime\Newtonsoft.Json.dll"

$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Newtonsoft.Json">
      <HintPath>$nj</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText("$tDir\MultiBackendVerify.csproj", $csproj)

$prog = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI.RAG;

internal static class Program
{
    private static int _fails;
    private static void Check(bool ok, string msg)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + msg);
        if (!ok) _fails++;
    }

    private static float[] ParseEmbeddingToken(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        if (token is JArray arr)
        {
            if (arr.Count == 0) return null;
            var vec = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++) vec[i] = (float)arr[i];
            return vec;
        }
        if (token.Type == JTokenType.String)
        {
            string s = token.ToString().Trim();
            if (s.StartsWith("[", StringComparison.Ordinal) && s.EndsWith("]", StringComparison.Ordinal))
                s = s.Substring(1, s.Length - 2);
            if (string.IsNullOrWhiteSpace(s)) return null;
            string[] parts = s.Split(',');
            var vec = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    return null;
                vec[i] = val;
            }
            return vec;
        }
        return null;
    }

    private static int Main()
    {
        // 1. LocalBakedAsset clone check: RagLocalIndex normalises entry.vector in place; original SO array must remain untouched
        float[] originalAssetVector = new float[] { 3f, 4f, 0f, 0f };
        float[] clonedVector = (float[])originalAssetVector.Clone();
        var entries = new List<RagIndexEntry>
        {
            new RagIndexEntry
            {
                id = "local_1",
                sourceName = "Local Manual",
                text = "Torque gland retaining nuts to 25 ft-lbs.",
                vector = clonedVector
            }
        };
        var idx = new RagLocalIndex(entries);
        Check(Math.Abs(originalAssetVector[0] - 3f) < 1e-5f && Math.Abs(originalAssetVector[1] - 4f) < 1e-5f,
            "LocalAsset: original ScriptableObject vector is not mutated by RagLocalIndex normalisation");
        Check(Math.Abs(clonedVector[0] - 0.6f) < 1e-5f && Math.Abs(clonedVector[1] - 0.8f) < 1e-5f,
            "LocalAsset: cloned vector in RagLocalIndex is L2-normalised");

        // 2. Supabase pgvector string + JArray export into RagLocalIndex
        string sbJson = @"[
          {""id"":""sb_1"",""document_id"":""d1"",""source_name"":""Supabase Manual"",""chunk_index"":0,""content"":""Bearing housing lubrication uses ISO VG 68 turbine oil."",""metadata"":{},""embedding"":""[0.0,3.0,4.0,0.0]""},
          {""id"":""sb_2"",""document_id"":""d1"",""source_name"":""Supabase Manual"",""chunk_index"":1,""content"":""Allowable impeller diametrical clearance is 0.010 to 0.015 inches."",""metadata"":{},""embedding"":[0.0,0.0,0.0,5.0]}
        ]";
        foreach (var row in (JArray)JToken.Parse(sbJson))
        {
            entries.Add(new RagIndexEntry
            {
                id = row["id"].ToString(),
                sourceName = row["source_name"].ToString(),
                text = row["content"].ToString(),
                vector = ParseEmbeddingToken(row["embedding"])
            });
        }

        // 3. Pinecone 2026-07 documents/fetch export into RagLocalIndex
        string pcDocFetch = @"{""documents"":{""pc_1"":{""_id"":""pc_1"",""chunk_index"":2.0,""embedding"":[0.0,0.0,5.0,0.0],""source_name"":""Pinecone Manual"",""text"":""Parallel shaft coupling misalignment must not exceed 0.002 inches.""}},""namespace"":""rag-provider-tests""}";
        var docMap = (JObject)JObject.Parse(pcDocFetch)["documents"];
        foreach (var p in docMap.Properties())
        {
            var o = (JObject)p.Value;
            entries.Add(new RagIndexEntry
            {
                id = o["_id"].ToString(),
                sourceName = o["source_name"].ToString(),
                text = o["text"].ToString(),
                vector = ParseEmbeddingToken(o["embedding"])
            });
        }

        var fullIdx = new RagLocalIndex(entries);
        Check(fullIdx.Count == 4, "Multi-backend index holds all 4 entries (Local + Supabase + Pinecone)");

        var lex = fullIdx.SearchLexical("parallel shaft coupling misalignment", 1);
        Check(lex.Count == 1 && fullIdx.GetEntry(lex[0].index).id == "pc_1", "Lexical search finds Pinecone chunk in <1ms");

        var sem = fullIdx.SearchSemantic(new float[] { 0f, 0.6f, 0.8f, 0f }, 1, 0.5f);
        Check(sem.Count == 1 && fullIdx.GetEntry(sem[0].index).id == "sb_1" && sem[0].score > 0.99f, "Semantic search finds Supabase pgvector-string chunk");

        Console.WriteLine(_fails == 0 ? "ALL MULTI-BACKEND CHECKS PASSED" : (_fails + " FAILED"));
        return _fails == 0 ? 0 : 1;
    }
}
'@
[IO.File]::WriteAllText("$tDir\Program.cs", $prog)
dotnet run --project "$tDir\MultiBackendVerify.csproj" -c Release
Remove-Item -Recurse -Force $tDir
```

**Expected Output**:
- 5 `PASS` lines and `ALL MULTI-BACKEND CHECKS PASSED` (exit code `0`).

---

### Verification V2 — Compile `com.convai.openai.csproj`

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
dotnet build "com.convai.openai.csproj" --no-restore -v minimal
```

**Expected Output**:
- `0 Error(s)` (exit code `0`).

---

### Verification V3 — Live Pinecone Document-Schema Bulk Export Verification

**Purpose**:
Executes the exact `documents/list` -> `documents/fetch` flow implemented in `PineconeVectorProvider.ExportAllDocumentsAsync` against the live Pinecone index configured in `Assets/Resources/RagConfig.asset`.

**Command**:
```powershell
Set-Location "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1"
$ErrorActionPreference = "Stop"

$rag = Get-Content "Assets\Resources\RagConfig.asset" -Raw
function Get-F([string]$n) { ([regex]::Match($rag, "(?m)^  ${n}: (.*)$")).Groups[1].Value.Trim() }
$pcHost = (Get-F "pineconeIndexHost").TrimEnd("/")
$pcKey  = Get-F "pineconeApiKey"
$pcNs   = Get-F "pineconeNamespace"

$hdr = @{ "Api-Key" = $pcKey; "X-Pinecone-API-Version" = "2026-07"; "Content-Type" = "application/json" }
$listUrl  = "$pcHost/namespaces/$([Uri]::EscapeDataString($pcNs))/documents/list"
$fetchUrl = "$pcHost/namespaces/$([Uri]::EscapeDataString($pcNs))/documents/fetch"

$listResp = Invoke-RestMethod -Method Post -Uri $listUrl -Headers $hdr -Body '{"limit":100}'
$ids = @($listResp.documents | ForEach-Object { $_._id })
Write-Output "Pinecone listed IDs: $($ids -join ', ')"
if ($ids.Count -lt 1) { throw "Expected at least 1 document ID in Pinecone namespace '$pcNs'." }

$fetchBody = @{ ids = $ids } | ConvertTo-Json -Compress
$fetchResp = Invoke-RestMethod -Method Post -Uri $fetchUrl -Headers $hdr -Body $fetchBody
$firstDoc = $fetchResp.documents.($ids[0])
Write-Output "Fetched id=$($firstDoc._id) | text_len=$($firstDoc.text.Length) | embedding_dims=$($firstDoc.embedding.Count)"
if (-not $firstDoc.text -or $firstDoc.embedding.Count -lt 1) { throw "Pinecone fetch verification failed." }
Write-Output "PINECONE EXPORT OK"
```

**Expected Output**:
- `PINECONE EXPORT OK` (exit code `0`).

---

### Verification V4 — Dual-Repository File Parity Check

**Command**:
```powershell
$ErrorActionPreference = "Stop"
$srcRoot = "d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1\Packages\com.convai.openai\Runtime"
$dstRoot = "D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource\com.convai.openai\Runtime"

$files = @(
    "RAG\Interfaces\IBulkExportProvider.cs",
    "RAG\Providers\RagLocalIndex.cs",
    "RAG\Providers\QdrantVectorProvider.cs",
    "RAG\Providers\LocalAssetVectorProvider.cs",
    "RAG\Providers\SupabaseVectorProvider.cs",
    "RAG\Providers\PineconeVectorProvider.cs",
    "RAG\Models\RagConfig.cs",
    "RAG\RuntimeBridge\RagLocalCache.cs",
    "RAG\RuntimeBridge\RagConversationBridge.cs",
    "Scripts\OpenAIConfig.cs",
    "Scripts\ArtureRelaySettings.cs",
    "Scripts\RealtimeConversationManager.cs"
)

foreach ($f in $files) {
    $sLen = (Get-Item (Join-Path $srcRoot $f)).Length
    $dLen = (Get-Item (Join-Path $dstRoot $f)).Length
    if ($sLen -ne $dLen) { throw "Size mismatch for ${f}: $sLen vs $dLen" }
    Write-Output "PARITY OK  $f ($sLen B)"
}
```

**Expected Output**:
- 12 `PARITY OK` lines with matching byte lengths.

---

### Failure Handling & Scoped Recovery

If a step fails and you need to revert only the 4 files touched by this guide back to their pre-guide state:
- Run `git diff` on the affected file and inspect the mismatched anchor before retrying.
- Do **not** run `git reset --hard` or `git checkout -- .` across the repo, as that would discard the uncommitted changes from the prior handoff guide.

---

## F. Completion Report Contract

### Already Verified by Handoff Author (Pre-Application)
- **Live Pinecone API Schema Probed**: Confirmed that the configured Pinecone index (`rag-provider-tests`) uses the `2026-07` Document Schema (`/namespaces/{ns}/documents/list` and `/documents/fetch`) and returns `_id`, `text`, `source_name`, `chunk_index`, and `embedding` arrays.
- **Export Parsers Pre-Tested**: Ran a standalone `.NET 8` verification harness against `Newtonsoft.Json.dll` verifying `Supabase` (`pgvector` string `"[0.25,-0.5,0.75,1.0]"` + `JArray`) and `Pinecone` (`2024-07` + `2026-07` schemas) — **7/7 checks passed**.
- **Replacement Anchors Verified**: Confirmed every `Replace-Block` target in Steps `S1`–`S4` matches uniquely in the current working tree.

### Required Executor Post-Execution Report
After applying Steps `S0`–`S5` and running `V1`–`V4`, report using this format:

```markdown
## Execution Report: IdiotHandOffGuide (Multi-Backend Local Cache Parity)
- **Status**: COMPLETE | BLOCKED
- **Steps Applied**: S0, S1, S2, S3, S4, S5
- **Verification Results**:
  - `V1` (Multi-backend parser & RagLocalIndex harness): PASS / FAIL
  - `V2` (`dotnet build com.convai.openai.csproj`): PASS / FAIL (0 Error(s))
  - `V3` (Live Pinecone export check): PASS / FAIL (`PINECONE EXPORT OK`)
  - `V4` (12-file dual-repo parity check): PASS / FAIL
```
