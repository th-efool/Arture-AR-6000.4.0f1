# Quality assessment — applied pump conversation handoff

## 1. Verdict and assessed scope

**Acceptance unmet. Five findings — one high, three medium, one low.** The six-file patch is applied, both assemblies compile, and the two changed package files match the sibling checkout. The original shared-buffer typed-history defect is corrected in an isolated event replay. The claimed COMPLETE status is unsupported by the recorded verification and there are reproducible remaining state defects.

Assessed November 5, 2026. Root is D:/ThisPC/Documents/ArtureUnity/Arture-AR-6000.4.0f1, main at 9f79fb3d9700327d8aa10d4ba08adab3e8bb5027 plus the existing uncommitted implementation. Unity 6000.4.0f1, PowerShell, .NET SDK 10.0.401. Initial modified files were the six S2 files and IdiotHandOffManager-status.md. AGENTS.md, CLAUDE.md, Memory/, the earlier incident assessment and handoff were untracked. Comparison baseline is HEAD and the guide's exact patch. The sibling is ../ConversationalAIOpenSource on feat/native-rag-knowledge-base at 4f30ceb with the two package changes and untracked instructions/memory.

Scope is the execution summarized in the attached Pasted text.txt, its ledger and handoff S1–S4/V1–V7. Standards are the guide's observable acceptance checks, the user's original incident and current AGENTS.md. The six-second automatic visual tour is the author's design choice already specified in the guide. Per-step spoken narration was excluded by that guide and is not scored as an implementation failure here.

**Coverage limits** — no live service conversation, microphone capture, Unity render/visual tour, debugger-injected service outage or new Editor validator run was performed in this assessment. Executable reproductions use extracted production methods with explicit test doubles. They establish the described state transitions, not service timing frequency or rendered appearance. Source validation cannot establish model adherence to a prompt.

## 2. Requirement and component coverage

All paths in this report resolve relative to the root above unless marked sibling.

| ID | Requirement and inventory | Inputs, outputs and state path | Review and disposition |
|---|---|---|---|
| R1 | Trustworthy assistant history and preview | SendUserTextMessage / input completed → HUD user callback; output delta/item done → new manager events → preview/history | Source and C4 replay. Ordinary typed sequence passes. F3 affects late voice preview. Full V2/V3 outstanding. |
| R2 | Tour progresses and stops on interruption/replacement | Tool schema → ToolBindings reflection → StartComponentTour → inventory → ShowTourStep → ShowAnswer/ClearAnswer → LateUpdate | Full added methods and affected clear/lifecycle paths inspected. Synthetic six-second advance works. F2 confirms interruption gap. Visual V4/V5 outstanding. |
| R3 | Accurate document attribution and empty result handling | Enhanced instructions → search tool → SearchCoreAsync → cached/remote result → tool status/context → response continuation | Source inspected, schemas parsed, package compiled. Expected statuses and error propagation are present. Actual spoken V6/V7 outstanding. |
| R4 | Input final-only events and callback compatibility | Socket input delta/completed → public events; legacy RealtimeTextChatProp and mic starter listeners; new HUD events | Delta emission removed, completed/typed paths retained. Source verified. F3/F4 affect HUD recovery. |
| R5 | Integration and independent package distribution | Game assembly, package assembly, reflected tool registration, scene references, public search API, two mirror files, memories | C1–C3 pass compile/applicability/parity. F5 affects memory ownership. No live nine-tool binding observation. |
| R6 | Honest completion evidence | Guide V1–V7 → ledger and supplied final summary | F1. Ledger substitutes inspections for required live checks. |

Additional connected coverage includes the existing MachineInteractionValidator (individual part/error assertions, no added tour sequence assertions), MachineProfile/WaterPump inventory, Machine_ShowMessage replacement behavior, OnDisable/OnDestroy cleanup, manager cancellation and response continuation guards, RagLocalCache lexical/semantic/fallback paths, RAG source formatting, existing public SearchAsync callers and serialized tool binding. Their unchanged internals were inspected to the depth needed to validate the change. Vendor/generated assemblies were used as compiler references and excluded from line-by-line audit.

All six finding categories were considered. A/B/C/D have findings below. E has no substantiated deletion candidate — new tools are reflectively bound by JSON and new events have callers; no public DTO is declared dead from a local-reference search. F has duplicated external-consumer knowledge in sibling memory (F5). No broad abstraction rewrite is justified by this change.

## 3. Prioritized findings

### F1 — High · A/D · Completion claims replace live acceptance with inspection

**Requirement and impact** — R6 and guide V1–V7. The user is told that the original symptoms, speech interruption, visible tour and source answers have all been verified, while the evidence establishes code presence and compilation. This is high severity because the core interaction is being declared accepted despite both missing acceptance evidence and reproduced failures.

**Locations** — IdiotHandOffManager-status.md:7,19–25,36–50. Guide IdiotHandOffGuide-PumpConversation.md:475–488 defines actual spoken/typed prompts, screenshots, observed selection order, interruption and failure injection. The attachment explicitly claims all V1–V7 applied, compiled and verified.

**Trigger / expected / actual** — an executor reads the guide, applies S2 and compiles, then marks inspection of the methods as V2–V7 pass. Expected evidence includes actual interaction outputs. Actual ledger entries cite only symbols, fields, policy text and timer logic. V1 also included Editor validation and live tool registration, which are absent from the recorded evidence. No Temp/MachineInteractionValidationReport.txt was available at audit time.

**Root cause and evidence** — confirmed verification-method substitution, C1/C5. The status ledger renames several checks to inspection and treats that narrower work as the original acceptance. Prompt rules encourage behavior; their presence cannot prove no unsupported answer will be spoken.

**Attribution** — introduced in the current execution report/ledger. The earlier handoff explicitly retained live checks.

**Correction / closure** — revise the ledger to distinguish source-verified, compiler-passed and live-unverified results. Run the original V1–V7 and attach their requested evidence. Record remaining failures and skipped checks explicitly. Passing compilation alone cannot close R1–R3.

### F2 — Medium · B/C · Tour cancellation inherits a guard that suppresses real interruptions

**Requirement and impact** — R2, V3/V5. A user starting to speak shortly after any part transition can leave the automatic tour active. The next card can appear while the user is still speaking or asking a question that does not replace the selection.

**Locations** — Assets/Scripts/MachineInteraction.cs:199–217,690–704,1104–1113; Assets/ConvoAI/ConvoAIMicPermissionStarter.cs:495–496 invokes user-speaking at PTT start; RealtimeConversationManager.cs:1001 emits the same signal for VAD.

**Trigger / expected / actual** — show a tour step at time 0, emit OnUserSpeaking(true) at 0.1 seconds and tick at 6.1 seconds. Expected tour cancellation. Actual guard returns before ClearAnswer; the second component is displayed and tourParts remains populated. The same window occurs after every successful step because ShowAnswer updates lastAnswerShownUnscaledTime.

**Root cause and evidence** — confirmed by extracted-method replay C4, output EARLY_INTERRUPTION running=True shown=Motor,Impeller. A control at 0.3 seconds cancels successfully. The guard intended to protect a fresh label also protects the newly introduced progression state. LateUpdate has no user-speaking check.

**Attribution** — introduced tour behavior interacting with a pre-existing guard. This defect is in the authored handoff as well as the applied code.

**Correction / closure** — terminate tour progression immediately on a genuine input-start transition, independently of any visual-clear grace period. Retain any justified same-turn visual protection separately. Test real PTT/VAD starts at 0.1 and 0.3 seconds after a step, including a non-component question, and verify no further advance.

### F3 — Medium · C/D · Late voice transcription clears the current assistant preview

**Requirement and impact** — R1/R4, V3. A valid voice event ordering leaves a blank or truncated preview while the assistant is replying. Final history can be correct at the same time.

**Locations** — Assets/ConvoAI/ConvoAIDebugOverlay.cs:210–234; Packages/com.convai.openai/Runtime/Scripts/RealtimeConversationManager.cs:1003–1019,1051–1059.

**Trigger / expected / actual** — assistant deltas arrive for item c, then the user's final input transcript arrives. HandleSpeechStopped requests the answer without waiting for that transcript, so local control flow admits this ordering. Expected the preview remains associated with assistant item c. Actual onUserTranscript clears both _lastAgentTranscript and _previewItemId. Further deltas only reconstruct a suffix. If the assistant item finished before the input transcript, the preview becomes empty and remains empty.

**Root cause and evidence** — confirmed conditional defect, C4. A replay emitting an assistant delta and completed item followed by the user transcript produced LATE_TRANSCRIPT preview length=0. The code uses delivery of user transcription as a new-turn boundary even though it is an asynchronous result. The old builder-prefix defect is fixed; the replacement reset creates this separate voice-path defect.

**Attribution** — introduced in the current HUD diff, already present in the author's handoff. Actual frequency in live sessions remains unmeasured.

**Correction / closure** — reset preview at an explicit user-input-start or accepted typed-input boundary, with response/item ownership, rather than every completed transcription callback. Test final input transcription arriving before the first assistant delta, between deltas and after the assistant item completes. Preserve full per-item history and the current preview in all cases.

### F4 — Medium · C · Persistent HUD never binds to a replacement manager

**Requirement and impact** — R1/R4 recovery. After scene reload/replacement, the persistent HUD may show a connection but receive no new conversation events.

**Locations** — Assets/ConvoAI/ConvoAIDebugOverlay.cs:78–88,190–205,228–234,266–269. The fallback lookup is also used at 628 and 639.

**Trigger / expected / actual** — auto-created HUD persists with DontDestroyOnLoad, its scene manager is destroyed, then a replacement scene creates a manager. Expected listeners attach to the replacement. Actual Update finds it, but _eventsBound remains true and prevents subscriptions. Anonymous listeners also have no explicit unbinding path.

**Root cause and evidence** — confirmed by source and C4 replacement replay. First manager produces user and assistant history. After simulated destroyed-manager null followed by a new manager, output REPLACEMENT_MANAGER new rows=0. Unity destruction and scene rendering were simulated; the retained Boolean and early return are production code.

**Attribution** — pre-existing guard and ownership defect, retained by the current callback change. An ordinary socket reconnect using the same manager is a different case and is not claimed to fail here.

**Correction / closure** — track the actual subscribed manager, detach named handlers when changing it and bind the replacement. Cover scene reload and same-instance socket reconnect separately. Verify new events arrive once after repeated replacements.

### F5 — Low · D/F · Sibling memory now duplicates Unity consumer behavior

**Requirement and impact** — R5 and current repository anti-staleness rules. Future package work can assume the standalone package owns the Unity HUD and tour prompt, or rely on consumer behavior that has changed independently.

**Locations** — sibling D:/ThisPC/Documents/ArtureUnity/ConversationalAIOpenSource/Memory/01-REALTIME-VOICE-AND-AUDIO-PIPELINE.md:85–87 and Memory/02-RAG-VECTOR-ENGINE-AND-PROVIDERS.md:88. Its Memory/00-META-OVERVIEW.md ownership rule excludes internal consumer-project details.

**Trigger / expected / actual** — execution copies the Unity memory append into the independent package memory. Expected package events, payloads and status semantics. Actual text names ConvoAIDebugOverlay history, direct-session tour routing and the consumer HUD filter, which live in the Unity application's Assets.

**Root cause and evidence** — direct duplication confirmed by C3/source comparison. The changes exist in both memories but describe different ownership scopes.

**Attribution** — introduced by the execution's memory mirror. The current user instructions explicitly establish independent memories.

**Correction / closure** — document AgentItemDelta/AgentItemFinished and RAG status semantics in the package memory, and retain application HUD/tour policy details in Unity memory. Verify each documented owner exists in that repository. This is a cleanup item distinct from functional acceptance.

## 4. Checks and evidence

### C1 — Baseline, claims and application

Commands from the Unity root in PowerShell included git status -sb, git log -4 --oneline, git diff --stat, git diff --check, full diff reads for the six files and Get-Content on the attachment, guide, ledger and local memories. Existing report headers were inspected. The default report covers an older RAG audit and the earlier PumpConversation assessment covers the original incident, so this audit has its own filename.

Extracted the guide's fenced diff using [regex]::Match with (?ms)^\x60{3}diff\r?\n(.*?)^\x60{3}\r?$ into the temporary directory below. Ran git apply --reverse --check against it. **Passed, exit 0**, confirming the applied source matches the handoff postimage. git diff --check also passed. No source patch was applied during this audit.

### C2 — Compile checks

Ran dotnet build ./com.convai.openai.csproj --no-restore -v minimal from the Unity root. **Passed, exit 0, four warnings and zero errors.** Warnings are assembly-version resolution warnings, including System.Net.Http and System.Security.Cryptography.Algorithms. This command creates ordinary build intermediates.

Also compiled the current package and Assembly-CSharp using the installed Unity compiler and existing Library/Bee/artifacts/1300b0aE.dag/*.rsp files. Only output/refout paths were redirected to the temporary directory; the game assembly referenced the just-built temporary package reference. Invocation for each was dotnet "D:/Program Files/Unity/6000.4.0f1/Editor/Data/DotNetSdkRoslyn/csc.dll" "@<temporary assembly rsp>". **Both exited 0.** This checks application code that the standalone package build excludes.

### C3 — Mirror and memory

For Runtime/Scripts/RealtimeConversationManager.cs and Runtime/RAG/RuntimeBridge/RagConversationBridge.cs, compared Get-FileHash values between Packages/com.convai.openai and ../ConversationalAIOpenSource/com.convai.openai. **Both passed byte parity**.

- Manager SHA256 D0A306218E5DBE022161F6D323101D96AC089D68330D2BDC2999853EFBC76A4D.
- Bridge SHA256 29981BFE6B3AEBAF292D37616576D8CA57EDAD5034F8FACD317F8152581F72A5.

Read the sibling's actual AGENTS.md and Memory/00-META-OVERVIEW.md, then inspected the two relevant appended memory sections. The initial assumption that its overview filename matched the Unity repo returned file-not-found; the actual index resolved it. No mirror files were changed.

### C4 — Executable isolated reproductions

Temporary files are under C:/Users/agrim/AppData/Local/Temp/arture-postimplementation-audit-1793847957289. Run:

~~~powershell
dotnet run --project 'C:/Users/agrim/AppData/Local/Temp/arture-postimplementation-audit-1793847957289/Audit.csproj' --verbosity quiet
~~~

Program.cs contains the exact current HUD BindSceneComponents method and exact Machine_StartComponentTour, ShowTourStep, OnUserSpeaking and LateUpdate methods. Test doubles supply a manager/event source, two highlightable components, clock, renderer-success result and minimal ClearAnswer state reset. The original changed method bodies and lambdas execute as C# against the installed Newtonsoft.Json assembly. No socket, Unity-native rendering, audio or database runs in this harness.

Observed output:

~~~text
CONTROL typed history=💬 You: "XR"|AI: XR answer|💬 You: "Motor"|AI: Motor answer
LATE_TRANSCRIPT preview length=0
REPLACEMENT_MANAGER new rows=0
EARLY_INTERRUPTION running=True shown=Motor,Impeller
CONTROL later interruption running=False
~~~

Process exit 0 means the reproductions executed successfully. The three named failure scenarios demonstrate defects; they are not passing acceptance tests.

### C5 — Remaining inspections and unavailable checks

Inspected registered JSON tools, reflection binding, Preserve attributes, existing machine validator, manager input/output/cancellation/continuation paths, legacy transcript listener, mic starter PTT/VAD events, RAG agent handler and public wrapper, cache fallback, source formatting, and prompt construction. RAG error-to-status code is consistent with the intended local API distinction. Its real outage behavior and resulting model response are unverified.

No new automated test files were added to the repository. Existing validators do not contain the added tour/event scenarios. No live V2–V7 evidence was found in the supplied attachment or ledger. Actual output quality, nine tools bound in the running scene, readable cards, full 14-part order, interruption audio behavior and server failure handling require the original live checks.

## 5. Unverified risks and blocked investigation

- Full assistant item text is generated content and can exceed audio actually played before interruption. Live V3 should distinguish generated transcript from heard speech. No claim of playback-accurate history is established.
- The new finish event has no duplicate-item suppression. A repeated completion event would duplicate a row, but no normal duplicate-delivery trigger was established here, so this remains a risk rather than an additional defect.
- Typed questions emit user transcript without user-speaking. A tour continues until an explicit replacement/stop handler runs. Immediate pause on every typed question is not explicitly specified by the guide; settle that interaction contract before treating it as another failure.
- Tour cards advance every six seconds independently of audio. The guide deliberately chose visual progression with an introduction. Whether that experience satisfies the user's desired explanation quality remains a live acceptance question.
- No services were queried and no live database contents were re-audited in this turn. The earlier four-chunk corpus observation remains historical evidence, not proof of current remote content.
- Rendered layout/readability and Android/Quest IL2CPP behavior are outstanding. No compiler result or synthetic replay proves those dimensions.

## 6. Correction order and completion criteria

1. Correct the ledger's acceptance classifications and retain the original V1–V7 actions as outstanding where evidence is absent.
2. Fix tour cancellation ownership (F2), late-transcription preview ownership (F3), then manager subscription lifecycle (F4). Use the three reproduced inputs and controls as regression criteria. Two of these defects originated in the author's handoff; faithful application alone cannot resolve them.
3. Rewrite only the misplaced sibling memory paragraphs to respect independent ownership (F5).
4. Run the guide's actual typed/voice sequence, visual full tour and cancellation matrix, supported/unsupported source questions and lookup failure exercise. Capture the requested transcripts, selected-part order and screenshots.
5. Recompile both affected assemblies and recheck package parity after corrections. Acceptance requires all required checks to pass or a clear incomplete verdict for the remaining blockers.

Audit deliverable is this assessment file. Application code, ledger and memory corrections require a subsequent implementation task.


## Applied correction verification

F2, F3 and F4 were corrected after this assessment. Isolated production-method replay now preserves the 23-character preview after delayed transcription, receives two rows from a replacement manager, and stops the tour before its second step on early interruption. Both Unity assemblies compile. F1 ledger classifications were corrected and F5 consumer-specific sibling memory was removed. Live V1–V7 acceptance remains outstanding.

