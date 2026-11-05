# Implementation handoff — reliable pump walkthrough and conversation history

## A. Execution contract

**READY for the Unity changes.** The diff below was applied and compiled in isolation against main at 9f79fb3d9700327d8aa10d4ba08adab3e8bb5027. The exact mirror path in AGENTS.md is currently missing. The verified relocated checkout is D:\ThisPC\Documents\ArtureUnity\ConversationalAIOpenSource, branch feat/native-rag-knowledge-base at 4f30ceb, origin th-efool/Unity-QuestConversationalAI, with both affected source files initially byte-identical. Use the verified checkout as the package mirror. If the literal historical path is required by your environment, mark that location blocked until restored; do not create a second repository or overwrite another sample checkout.

Work in PowerShell from D:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1 with Unity 6000.4.0f1. Read Memory/00 first, then 01–04. Preserve untracked AGENTS.md, CLAUDE.md, Memory/ and both prior reports. No package install, database change, deployment, commit or push is part of execution. The app's configured direct connection is needed for the live acceptance checks. Keep credentials local.

1. Verify baseline and mirror with S1. Save the relevant working-tree diff if you have local edits. Stop on conflicts; never reset unrelated work.
2. Apply S2 once, then S3 and S4. For a partial application verify each file against the patch's resulting hunks and resume only missing file hunks using git apply --include with the exact path from the manifest. If neither forward nor reverse check succeeds on a file, stop and report it.
3. Run V1–V7. Report the exact failing step, command and error for unfamiliar failures. A written patch or successful build alone does not establish acceptance.

## B. Requirements and acceptance map

|ID|Observable result|Steps|Checks|
|---|---|---|---|
|R1|Separate typed and spoken turns display one full history message per completed assistant message item. Playback stops and function-only items cannot publish the prior answer.|S2 manager + HUD|V2,V3|
|R2|One request starts a hands-free visual tour. Unity shows one part at a time in profile order and stops after the last part. A replacement selection or interruption takes ownership immediately through normal clear behavior.|S2 schema, machine, instructions|V4,V5|
|R3|A source request gets the supporting section if available, or an explicit absence followed by clearly identified general knowledge where helpful.|S2 instructions, bridge, HUD|V6|
|R4|Search service failure is distinguishable from zero passages. Existing non-agent callers retain their list-returning API.|S2 bridge|V7|
|R5|Public delta stream, typed submissions, scene reflection, partial visual failures, microphone and package parity survive.|S1–S4|V1–V5,V7|

Chosen interaction: **automatic six-second visual steps** with a card per part. The first spoken response introduces the tour; narration of every subsequent card is outside this design. A single-part follow-up still uses Machine_ShowAnswer. The selected cadence is an implementation choice. Positive citations refer to source/section values actually returned by this collection; chunk indices are not PDF page numbers.

## C. File-change manifest

All paths below are relative to the Unity root unless explicitly marked mirror. All operations modify existing files.

- S2 — `Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs`
- S2 — `Assets/ConvoAI/ConvoAIDebugOverlay.cs`
- S2 — `Assets/Scripts/MachineInteraction.cs`
- S2 — `Assets/MachineTools.json`
- S2 — `Assets/ConvoAI/ConvoAIMicPermissionStarter.cs`
- S2 — `Packages/com.convai.openai/Runtime/RAG/RuntimeBridge/RagConversationBridge.cs`
- S3 — mirror the two S2 files under `Packages/com.convai.openai/` to the same suffix under the mirror's `com.convai.openai/`.
- S4 — append the supplied invariants to `Memory/01-UNITY-AR-SPATIAL-SYSTEM.md`, `Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md`, and `Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md`. No new Unity assets, serialized scene references or .meta files.

## D. Ordered implementation

### S1 — verify paths and baseline before editing

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'D:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1'
git status -sb
git rev-parse HEAD
$mirror = 'D:\ThisPC\Downloads\Compressed\ConversationalAIOpenSource'
if (!(Test-Path "$mirror/.git")) { $mirror = 'D:\ThisPC\Documents\ArtureUnity\ConversationalAIOpenSource' }
git -C $mirror status -sb
git -C $mirror remote get-url origin
$rels = @('Runtime/Scripts/RealtimeConversationManager.cs','Runtime/RAG/RuntimeBridge/RagConversationBridge.cs')
foreach ($r in $rels) {
    $a = (Get-FileHash "Packages/com.convai.openai/$r").Hash
    $b = (Get-FileHash "$mirror/com.convai.openai/$r").Hash
    if ($a -ne $b) { throw "Mirror mismatch for $r. Compare with the S2 preimage or postimage before continuing." }
}
```

Expected on a fresh application: revision and remote above, both hashes match. On a resume after S2, the mirror may still match the preimage. Verify its SHA256 against A6CF224E304473964D9BCB780944B067C23FAEB9EC22F674702D6C0DC274B88A (manager) and EE82A38A6839AB0BA12CB78B12E52DF7CEBB206FAC65DC5EDB42C63AE08D6061 (bridge) before copying.

### S2 — apply this unified diff

From the same PowerShell session extract the single diff block in this file verbatim. Preserve blank context lines (one space). The patch is a set of exact block edits; git checks their context before mutation.

```powershell
$guide = Get-Content -Raw -LiteralPath 'IdiotHandOffGuide-PumpConversation.md'
$block = [regex]::Match($guide, '(?ms)^\x60{3}diff\r?\n(.*?)^\x60{3}\r?$')
if (!$block.Success) { throw 'Missing diff block' }
$patch = Join-Path $env:TEMP ('arture-pump-' + [guid]::NewGuid() + '.patch')
[IO.File]::WriteAllText($patch, $block.Groups[1].Value.Replace("`r`n","`n"), [Text.UTF8Encoding]::new($false))
git apply --check $patch
if ($LASTEXITCODE) { throw 'Inspect forward/reverse state per file before continuing' }
git apply $patch
if ($LASTEXITCODE) { throw 'Application failed' }
git apply --reverse --check $patch
if ($LASTEXITCODE) { throw 'Unexpected postimage' }
```

```diff
--- a/Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs
+++ b/Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs
@@ -51,6 +51,8 @@
 
         [Header("Unity Events")]
         public UnityEvent<string> onAgentTranscript;
+        public event Action<string, string> AgentItemDelta;
+        public event Action<string, string, bool> AgentItemFinished;
         public UnityEvent<string> onUserTranscript;
         public UnityEvent<bool>   onUserSpeaking;
         public UnityEvent<bool>   onAgentSpeaking;
@@ -485,7 +487,7 @@
                     break;
 
                 case "conversation.item.input_audio_transcription.delta":
-                    HandleInputTranscriptDelta(text);
+                    // Only completed input transcripts are conversation turns.
                     break;
 
                 case "conversation.item.input_audio_transcription.completed":
@@ -1009,16 +1011,6 @@
 
         private void HandleInputCommitted() { MarkTurnStart("voice"); }
 
-        private void HandleInputTranscriptDelta(string json)
-        {
-            try
-            {
-                var jo = JObject.Parse(json);
-                var delta = jo["delta"]?.ToString();
-                if (!string.IsNullOrEmpty(delta)) onUserTranscript?.Invoke(delta);
-            } catch {}
-        }
-
         private void HandleInputTranscriptCompleted(string json)
         {
             try
@@ -1058,9 +1050,13 @@
         {
             try
             {
-                var tx = JsonConvert.DeserializeObject<ResponseAudioTranscriptDelta>(json);
-                if (!string.IsNullOrEmpty(tx?.DeltaText))
-                    onAgentTranscript?.Invoke(tx.DeltaText);
+                var part = JObject.Parse(json);
+                string delta = (string)part["delta"];
+                if (!string.IsNullOrEmpty(delta))
+                {
+                    onAgentTranscript?.Invoke(delta);
+                    AgentItemDelta?.Invoke((string)part["item_id"], delta);
+                }
             } catch {}
         }
 
@@ -1119,6 +1115,18 @@
             {
                 var obj  = JObject.Parse(json);
                 var item = obj["item"] as JObject;
+                if (item?.Value<string>("role") == "assistant" && item["content"] is JArray parts)
+                {
+                    var transcript = new System.Text.StringBuilder();
+                    foreach (var part in parts)
+                    {
+                        string value = (string)part["transcript"] ?? (string)part["text"];
+                        if (!string.IsNullOrWhiteSpace(value))
+                            transcript.AppendLine(value.Trim());
+                    }
+                    AgentItemFinished?.Invoke((string)item["id"], transcript.ToString().Trim(),
+                        (string)item["status"] == "incomplete");
+                }
                 if (item?["type"]?.ToString() == "function_call")
                 {
                     var callId = item["call_id"]?.ToString();
--- a/Assets/ConvoAI/ConvoAIDebugOverlay.cs
+++ b/Assets/ConvoAI/ConvoAIDebugOverlay.cs
@@ -1,7 +1,6 @@
 using System;
 using System.Collections.Generic;
 using System.Reflection;
-using System.Text;
 using NativeWebSocket;
 using Newtonsoft.Json.Linq;
 using OpenAI;
@@ -53,7 +52,7 @@
     private int _micChunkCount;
     private float _lastMicChunkTime = -999f;
     private string _lastUserTranscript = "";
-    private readonly StringBuilder _agentTranscriptBuilder = new StringBuilder();
+    private string _previewItemId;
     private string _lastAgentTranscript = "";
 
     private GUIStyle _boxStyle;
@@ -132,6 +131,7 @@
             condition.IndexOf("[MachineInteraction]", StringComparison.OrdinalIgnoreCase) >= 0 ||
             condition.IndexOf("[WorldSpaceUI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
             condition.IndexOf("[Arture]", StringComparison.OrdinalIgnoreCase) >= 0 ||
+            condition.StartsWith("[Latency] RAG ", StringComparison.Ordinal) ||
             condition.IndexOf("ToolBindings", StringComparison.OrdinalIgnoreCase) >= 0 ||
             condition.IndexOf("Machine_", StringComparison.OrdinalIgnoreCase) >= 0 ||
             condition.IndexOf("Cube_", StringComparison.OrdinalIgnoreCase) >= 0 ||
@@ -204,46 +204,34 @@
         {
             _eventsBound = true;
 
-            _manager.onUserSpeaking.AddListener(speaking =>
-            {
-                _userSpeaking = speaking;
-                if (speaking)
-                {
-                    _agentTranscriptBuilder.Clear();
-                }
-            });
-
-            _manager.onAgentSpeaking.AddListener(speaking =>
-            {
-                _agentSpeaking = speaking;
-                if (!speaking && _agentTranscriptBuilder.Length > 0)
-                {
-                    string reply = _agentTranscriptBuilder.ToString().Trim();
-                    if (!string.IsNullOrEmpty(reply))
-                    {
-                        string shortReply = reply.Length > 110 ? reply.Substring(0, 107) + "..." : reply;
-                        AddLog($"🤖 AI: \"{shortReply}\"");
-                    }
-                }
-            });
+            _manager.onUserSpeaking.AddListener(speaking => _userSpeaking = speaking);
+            _manager.onAgentSpeaking.AddListener(speaking => _agentSpeaking = speaking);
 
             _manager.onUserTranscript.AddListener(text =>
             {
                 if (!string.IsNullOrEmpty(text))
                 {
+                    _lastAgentTranscript = "";
+                    _previewItemId = null;
                     _lastUserTranscript = text.Trim();
                     AddLog($"💬 You: \"{_lastUserTranscript}\"");
                 }
             });
 
-            _manager.onAgentTranscript.AddListener(delta =>
+            _manager.AgentItemDelta += (id, delta) =>
             {
-                if (!string.IsNullOrEmpty(delta))
-                {
-                    _agentTranscriptBuilder.Append(delta);
-                    _lastAgentTranscript = _agentTranscriptBuilder.ToString();
-                }
-            });
+                if (string.IsNullOrEmpty(id)) return;
+                if (_previewItemId != id) _lastAgentTranscript = "";
+                _previewItemId = id;
+                _lastAgentTranscript += delta;
+            };
+            _manager.AgentItemFinished += (id, text, interrupted) =>
+            {
+                if (string.IsNullOrWhiteSpace(text)) return;
+                if (_previewItemId == id) _lastAgentTranscript = text;
+                string label = interrupted ? "AI (interrupted)" : "AI";
+                AddLog($"{label}: {text}");
+            };
         }
 
         if (_micStreamer != null)
@@ -534,7 +522,7 @@
             string aiPart = "";
             if (!string.IsNullOrEmpty(_lastAgentTranscript))
             {
-                string shortAi = _lastAgentTranscript.Length > 75 ? _lastAgentTranscript.Substring(_lastAgentTranscript.Length - 75) : _lastAgentTranscript;
+                string shortAi = _lastAgentTranscript.Length > 75 ? "…" + _lastAgentTranscript.Substring(_lastAgentTranscript.Length - 74) : _lastAgentTranscript;
                 aiPart = $"  <color=#81D4FA><b>AI:</b> \"{shortAi}\"</color>";
             }
             GUILayout.Label(userPart + aiPart, _statusStyle);
--- a/Assets/Scripts/MachineInteraction.cs
+++ b/Assets/Scripts/MachineInteraction.cs
@@ -170,6 +170,55 @@
         return focusShader;
     }
 
+    // The timer owns the next visual step. Each ordinary selection cancels it via ClearAnswer.
+    private JArray tourParts;
+    private int tourIndex;
+    private float nextTourStep;
+
+    [Preserve]
+    public JObject Machine_StartComponentTour(JObject args)
+    {
+        ClearAnswer();
+        JObject inventory = Machine_GetComponents(args);
+        if (inventory["components"] is not JArray all) return inventory;
+        var parts = new JArray();
+        foreach (JObject part in all)
+            if (part.Value<bool>("canHighlight")) parts.Add(part.DeepClone());
+        if (parts.Count == 0) return Error("No highlightable components on this machine.");
+        return ShowTourStep(parts, 0);
+    }
+
+    [Preserve]
+    public JObject Machine_StopComponentTour(JObject args)
+    {
+        bool running = tourParts != null;
+        if (running) ClearAnswer();
+        return new JObject { ["ok"] = true, ["stopped"] = running };
+    }
+
+    private JObject ShowTourStep(JArray parts, int index)
+    {
+        var part = parts[index];
+        JObject result = Machine_ShowAnswer(new JObject {
+            ["component"] = (string)part["component"],
+            ["answer"] = (string)part["description"]
+        });
+        result["step"] = index + 1;
+        result["total"] = parts.Count;
+        if (result.Value<bool>("highlighted") && result.Value<bool>("uiDisplayed"))
+        {
+            tourParts = parts;
+            tourIndex = index + 1;
+            nextTourStep = Time.unscaledTime + 6f;
+        }
+        else
+        {
+            tourParts = null;
+            Debug.LogWarning($"[MachineInteraction] Tour stopped at {part["component"]}: {result}");
+        }
+        return result;
+    }
+
     // AI reads the real component data before answering.
     [Preserve]
     public JObject Machine_GetComponents(JObject args)
@@ -652,6 +701,7 @@
 
     public void ClearAnswer()
     {
+        tourParts = null;
         if (activeLabel != null)
         {
             activeLabel.gameObject.SetActive(false);
@@ -1053,7 +1103,12 @@
 
     private void LateUpdate()
     {
-        if (autoClearSeconds > 0f && selectedObject != null && Time.unscaledTime - lastAnswerShownUnscaledTime > autoClearSeconds)
+        if (tourParts != null && Time.unscaledTime >= nextTourStep)
+        {
+            if (tourIndex < tourParts.Count) ShowTourStep(tourParts, tourIndex);
+            else ClearAnswer();
+        }
+        else if (tourParts == null && autoClearSeconds > 0f && selectedObject != null && Time.unscaledTime - lastAnswerShownUnscaledTime > autoClearSeconds)
             ClearAnswer();
         UpdateLabel();
     }
--- a/Assets/MachineTools.json
+++ b/Assets/MachineTools.json
@@ -2,6 +2,18 @@
   "tools": [
     {
       "type": "function",
+      "name": "Machine_StartComponentTour",
+      "description": "Show the individual components one by one in profile order, one highlight and card every six seconds. Call once for a walkthrough.",
+      "parameters": { "type": "object", "properties": {}, "additionalProperties": false }
+    },
+    {
+      "type": "function",
+      "name": "Machine_StopComponentTour",
+      "description": "Stop the current automatic component walkthrough.",
+      "parameters": { "type": "object", "properties": {}, "additionalProperties": false }
+    },
+    {
+      "type": "function",
       "name": "Machine_GetComponents",
       "description": "Read this machine's component names, descriptions, and whether each component has an assigned GameObject. Call before answering machine component questions. Use the returned names exactly.",
       "parameters": {
--- a/Assets/ConvoAI/ConvoAIMicPermissionStarter.cs
+++ b/Assets/ConvoAI/ConvoAIMicPermissionStarter.cs
@@ -575,7 +575,9 @@
             "\n5. Inspect Machine_ShowMessage's 'uiDisplayed' and 'uiError' fields before claiming its card appeared." +
             "\n6. When asked to resize/scale, rotate, or move/translate the machine, call Machine_SetScale, Machine_Rotate, or Machine_Translate immediately." +
             "\n7. Speak briskly, clearly, and concisely in English (1-2 short punchy sentences) with an energetic, natural conversational pace." +
-            "\n8. For technical questions, troubleshooting procedures, repair manuals, lubrication specs, or maintenance steps, call search_knowledge_base to retrieve factual domain documentation before answering, and summarize the retrieved facts concisely.";
+            "\n8. For technical questions, troubleshooting procedures, repair manuals, lubrication specs, or maintenance steps, call search_knowledge_base to retrieve factual domain documentation before answering, and summarize the retrieved facts concisely." +
+            "\n9. For show individual parts, a walkthrough, or one by one, call Machine_StartComponentTour once. It handles discovery and display. Check highlighted and uiDisplayed, introduce the first part in your own words and explain that the other cards advance automatically. Use Machine_StopComponentTour for stop. Keep single-part questions on Machine_ShowAnswer." +
+            "\n10. When asked where a fact came from, search the named subject. Cite the returned source name and section only if the passage supports that claim. If the search finds no support, say so first. You may then explain a general concept, explicitly labeled as general knowledge. If search failed, say lookup was unavailable. Never invent a document or imply that a pump manual covers Unity. Explain what a part does in context using the profile facts instead of reciting its description.";
 
         string voiceStr = _resolveVoiceMethod?.Invoke(_conversationManager, null) as string ?? "alloy";
 
--- a/Packages/com.convai.openai/Runtime/RAG/RuntimeBridge/RagConversationBridge.cs
+++ b/Packages/com.convai.openai/Runtime/RAG/RuntimeBridge/RagConversationBridge.cs
@@ -276,13 +276,14 @@
 
             try
             {
-                var results = await SearchAsync(query);
+                var results = await SearchCoreAsync(query, true);
                 results = LimitForTool(results);
                 string formattedContext = FormatContext(results);
 
                 return new JObject
                 {
                     ["ok"] = true,
+                    ["status"] = results.Count == 0 ? "no_sources" : "retrieved",
                     ["results_count"] = results.Count,
                     ["context"] = formattedContext
                 };
@@ -294,7 +295,8 @@
                 {
                     ["ok"] = false,
                     ["results_count"] = 0,
-                    ["context"] = $"Error searching knowledge base: {ex.Message}"
+                    ["status"] = "unavailable",
+                    ["context"] = "The knowledge lookup failed. Please retry."
                 };
             }
         }
@@ -422,11 +424,14 @@
         /// </summary>
         /// <param name="query">The text query to embed and search for.</param>
         /// <returns>A list of ranked <see cref="RagSearchResult"/> objects matching the query above the similarity threshold.</returns>
-        public async Task<List<RagSearchResult>> SearchAsync(string query)
+        public Task<List<RagSearchResult>> SearchAsync(string query) => SearchCoreAsync(query, false);
+
+        private async Task<List<RagSearchResult>> SearchCoreAsync(string query, bool reportFailure)
         {
             if (ragConfig == null)
             {
                 Debug.LogWarning("[OpenAI.RAG] SearchAsync called but ragConfig is null.");
+                if (reportFailure) throw new InvalidOperationException("[OpenAI.RAG] SearchAsync called but ragConfig is null.");
                 return new List<RagSearchResult>();
             }
 
@@ -455,6 +460,7 @@
             if (_embeddingClient == null)
             {
                 Debug.LogError("[OpenAI.RAG] OpenAIEmbeddingClient could not be initialized.");
+                if (reportFailure) throw new InvalidOperationException("[OpenAI.RAG] OpenAIEmbeddingClient could not be initialized.");
                 return new List<RagSearchResult>();
             }
 
@@ -462,12 +468,14 @@
             if (!string.IsNullOrEmpty(embedErr) || embedding == null)
             {
                 Debug.LogWarning($"[OpenAI.RAG] Failed to generate embedding for query '{query}': {embedErr}");
+                if (reportFailure) throw new InvalidOperationException($"[OpenAI.RAG] Failed to generate embedding for query '{query}': {embedErr}");
                 return new List<RagSearchResult>();
             }
 
             if (_provider == null)
             {
                 Debug.LogError("[OpenAI.RAG] Vector store provider could not be created.");
+                if (reportFailure) throw new InvalidOperationException("[OpenAI.RAG] Vector store provider could not be created.");
                 return new List<RagSearchResult>();
             }
 
@@ -480,6 +488,7 @@
             if (!string.IsNullOrEmpty(queryErr))
             {
                 Debug.LogWarning($"[OpenAI.RAG] Vector store query returned error: {queryErr}");
+                if (reportFailure) throw new InvalidOperationException($"[OpenAI.RAG] Vector store query returned error: {queryErr}");
                 return new List<RagSearchResult>();
             }
 
```

Postcondition: the package publishes per-item assistant text, history uses final message content, input partials stay private, Unity registers start/stop tools and the model receives explicit tour and source policies. The public search method still returns lists; the agent path distinguishes unavailable from no matches.

### S3 — mirror only the changed package files

```powershell
foreach ($r in $rels) {
    $src = "Packages/com.convai.openai/$r"
    $dst = "$mirror/com.convai.openai/$r"
    Copy-Item -LiteralPath $src -Destination $dst
    if ((Get-FileHash $src).Hash -ne (Get-FileHash $dst).Hash) { throw "Mirror mismatch after copy: $r" }
}
```

### S4 — record the resulting invariants

```powershell
$notes = [ordered]@{
 'Memory/01-UNITY-AR-SPATIAL-SYSTEM.md' = 'MachineInteraction and Assets/MachineTools.json expose Machine_StartComponentTour and Machine_StopComponentTour. Start enumerates highlightable profile entries and shows one existing highlight/card pair every six unscaled seconds. ClearAnswer, interruption, disable and replacement clear tour state. A partial visual failure ends progression. Extend this through the profile inventory and Machine_ShowAnswer.'
 'Memory/02-REALTIME-VOICE-AND-AUDIO-PIPELINE.md' = 'RealtimeConversationManager publishes assistant item-keyed preview deltas and final assistant message content for ConvoAIDebugOverlay history. Agent speaking is an audio status only. Typed submissions and completed voice transcripts publish onUserTranscript once; input deltas do not. Direct session instructions route walkthroughs to the tour tools and require explicit source/absence reporting.'
 'Memory/03-RAG-VECTOR-KNOWLEDGE-ENGINE.md' = 'RagConversationBridge agent tool results distinguish retrieved, no_sources and unavailable. Existing public SearchAsync callers retain list semantics. Use a source name and section only for a claim supported by the returned passage. The direct HUD admits the existing cache-result latency line for inspection.'
}
foreach ($entry in $notes.GetEnumerator()) {
    if (!(Test-Path -LiteralPath $entry.Key)) { throw "Missing memory file: $($entry.Key)" }
    if (!(Get-Content -Raw -LiteralPath $entry.Key).Contains($entry.Value)) {
        Add-Content -LiteralPath $entry.Key -Value ("`n### Conversation and walkthrough behavior`n`n" + $entry.Value)
    }
}
```

## E. Verification and failure handling

- **V1 — integration and parity.** Run `dotnet build .\com.convai.openai.csproj --no-restore -v minimal`, expect exit 0. Open SampleScene in the specified Unity Editor, wait for successful script compilation, run **Tools → Arture → Validate Machine Interaction System**, expect zero failures. The existing validator writes its normal report. Enter Play Mode in the same direct configuration and verify the two additional machine tools register (nine total if the original seven are active). Recheck S3 hashes. Never infer tool execution from the bound count alone.
- **V2 — original typed sequence.** Without speaking into the mic, type “Tell me about XR Origin”, then “Show me individual components of the machine one by one”, then “Tell me about AR Session Manager and which document you got it from.” Let each spoken introduction finish. Inspect full history after each. Each final assistant message must match its own content; the previous XR text must not prefix either later answer. The compact preview may show a suffix prefixed by an ellipsis. Check with a response longer than 110 characters. Function-call-only items should create zero AI history rows.
- **V3 — voice, barge-in and recovery.** Speak a multiword question in Auto-VAD and repeat in PTT. Expect one final user message per completed utterance. Interrupt a long reply, then send a different text prompt. Completed assistant items appear once, incomplete items with returned text are marked interrupted, and socket close/audio stop never replay a completed row. Reconnect and repeat. Listen for the actual answer separately from reading history; an item transcript describes generated audio and may exceed the portion heard before interruption.
- **V4 — visual tour.** Use the original request hands-free, then repeat via text. On WaterPump verify ElectricMotor, then the exact second identifier in Machine_GetComponents, then the remaining eligible entries in that returned order. Observe one target and one legible card per step, about six seconds apart, ending with a cleared selection after the final dwell. Screenshot the first two and final step. Verify the first spoken reply describes the observed action. Run a second complete tour and confirm it restarts at the first part.
- **V5 — cancellation, failure and single selection.** During step two ask to show the Impeller; verify the chosen part alone persists until its ordinary clear and the old tour never advances. Repeat with “stop the tour”, disabling the interaction object, and starting a new tour. In Play Mode on a temporary instance unset labelPrefab, start the tour, and check the first result's highlighted/uiDisplayed flags and warning; the tour must stop. Restore by exiting Play Mode. Re-run single-part, rotate and warning-card controls.
- **V6 — provenance.** With the inspected four-chunk collection, ask “What lubricant does the CP-400 manual specify for oil-lubricated bearings, and which section?” Expect a supported answer citing the CP-400 manual §6.3, using the retrieved oil-lubricated-model qualifier. Then ask the AR Session Manager document question. Expect explicit absence of a supporting uploaded document; any general explanation must be labeled as such. No invented Unity document, page or pump citation. A hit with an irrelevant passage still cannot support a claim. Record actual question, matched count/source if available, complete assistant text and what was heard. If corpus contents have changed, first identify the current source set before interpreting the result.
- **V7 — outage versus genuine miss.** In a disposable Editor run keep the Realtime socket and local cache enabled. Use a debugger breakpoint at SearchCoreAsync immediately before testing embedErr after the cache fallback (to force that path, temporarily set the local cachedResults variable to null in the debugger before its null check). For a fresh test query, set embedErr to a nonempty simulated error at the live-embedding check. Inspect HandleAgentToolCallAsync's catch result: ok=false, status=unavailable, zero results. Resume and verify the spoken answer identifies lookup failure. Repeat the healthy AR question from V6; expect ok=true, status=no_sources when zero results. Avoid changing persistent config, stored credentials or remote records. If a debugger or service is unavailable, report this check as outstanding.
 
To undo only this implementation, first verify S2's reverse check on the same six files; reverse the diff only when no later edits would be lost. Restore only S4's three added paragraphs and copy the reverted package files to the previously verified mirror. Never discard an entire working tree to recover one step.

## F. Completion report

Report applied step IDs, actual changed files in both checkouts, exact Unity/runtime versions, V1–V7 outcomes, mirror parity, skipped checks and errors. Include one before/after transcript and observed part order. Completion requires passing visual and conversational checks. Author validation was limited to source inspection, actual cache/log inspection, patch applicability and isolated Unity Roslyn compilation of both proposed assemblies. Live acceptance remains outstanding.

