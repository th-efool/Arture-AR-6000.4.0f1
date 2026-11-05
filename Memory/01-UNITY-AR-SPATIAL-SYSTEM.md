# 01 — Unity AR Spatial System, Digital Twin & Machine Tools

> **Scope**: `d:\ThisPC\Documents\ArtureUnity\Arture-AR-6000.4.0f1\Assets\Scripts\**`, `Assets\Prefabs\WaterPump.prefab`, `Assets\Scenes\SampleScene.unity`, `Assets\Resources\MachineFocus.shader`.
> **Update Rule**: If you add/modify machine types, component enums, highlight/x-ray shaders, 3D floating UI cards, or `Machine_*` AI tool methods, update this file before completing your turn.

---

## 1. Architectural Overview

The spatial layer turns AI tool calls from OpenAI Realtime into physical 3D visual feedback on the industrial digital twin (`WaterPump.prefab`, asset code `ATR-DEMO-PUMP-001`):

```text
OpenAI Realtime Function Call
   │  (e.g. Machine_ShowAnswer, Machine_Rotate, Machine_GetComponents)
   ▼
ToolBindings.cs  +  AgentToolRegistry.cs
   │  Reflectively binds JSON tool definitions (WaterPumpTools.json) to MonoBehaviour methods
   ▼
MachineInteraction.cs  (on WaterPump root GameObject, requires MachineProfile.cs)
   ├──► Reads component registry from MachineProfile.cs + MachineDefinitions.cs
   ├──► Applies Focus Shader (Arture/MachineFocus) + inverted-hull outline to target part
   ├──► Fades all non-selected machine parts to 27% opacity (X-Ray reveal for internal parts like Impeller)
   └──► Spawns/updates WorldSpaceUI.cs floating 3D card anchored above the highlighted component's bounds
```

---

## 2. Core Scripts & Responsibilities

| File Path | Class / Type | Exact Responsibility |
| :--- | :--- | :--- |
| `Assets/Scripts/MachineDefinition.cs` | `MachineType`, `MachineDefinitions` | Defines supported machine types (`CentrifugalWaterPump`, `CNC`) and their component enums with `[Description("...")]` attributes read by the AI via `Machine_GetComponents`. |
| `Assets/Scripts/MachineProfile.cs` | `MachineProfile : MonoBehaviour` | Attached to the root of a machine prefab. Holds `MachineID` (`ATR-DEMO-PUMP-001`), `Name`, `MachineType`, and the serialized `MachineComponent[] Components` array. |
| `Assets/Scripts/MachineComponent.cs` | `MachineComponent` (Serializable) | Maps a single enum integer (`ComponentValue`) to a child `GameObjectRef` mesh in the prefab hierarchy and an optional custom description override. |
| `Assets/Scripts/MachineInteraction.cs` | `MachineInteraction : MonoBehaviour` | `[DefaultExecutionOrder(-120)]`. Implements all `Machine_*` AI tool methods, manages material state snapshots (`RendererState`), creates inverted-hull outline meshes, fades unselected renderers (`unselectedOpacity = 0.27f`), spawns `WorldSpaceUI` labels, and auto-resets after `autoClearSeconds = 12f` or when `OnUserSpeaking(true)` fires. |
| `Assets/Scripts/WorldSpaceUI.cs` | `WorldSpaceUI : MonoBehaviour` | 3D world-space canvas card that billboards toward `viewerCamera`, displays component title + AI answer + manual citations (`Retrieved references: ...`), and draws a spatial attachment to the target part. |
| `Assets/Scripts/WorldSpaceMessageIcon.cs` | `WorldSpaceMessageIcon : MonoBehaviour` | Manages visual status icons (`info`, `warning`, `alert`, `direction`) on `WorldSpaceUI` cards. |
| `Assets/Editor/MachineProfileEditor.cs` | `MachineProfileEditor : Editor` | Custom Unity Inspector for `MachineProfile` that auto-populates the component slots from the selected `MachineType` enum. |
| `Assets/Editor/MachineInteractionValidator.cs` | `MachineInteractionValidator` | Editor validation utility verifying that every enum value on the prefab has a valid `GameObjectRef` and `Renderer`. |

---

## 3. Registered AI Spatial Tools (`MachineInteraction.cs`)

All methods below are marked `[Preserve]` for IL2CPP/Quest builds, take a single `JObject args` parameter, and return a `JObject` result to OpenAI:

| Tool Name | Input Arguments (`JObject`) | Behavior & Return Payload |
| :--- | :--- | :--- |
| **`Machine_GetComponents`** | `{}` *(none)* | Returns `{ ok, machineId, machine, machineType, components: [{ component, description, canHighlight }] }`. **Prompt Rule 1** forces the AI to call this before answering part questions. |
| **`Machine_ShowAnswer`** | `{ "component": "<EnumName>", "answer": "<Plain text>" }` | Highlights `component` in amber (`highlightColor`), fades other parts to `0.27` opacity, spawns `WorldSpaceUI` above the component bounds, and returns `{ ok, component, highlighted, uiDisplayed, highlightError, uiError }`. **Note**: `RealtimeConversationManager.ExecuteToolCallIfReady` automatically appends `KnowledgeSourcesSummary` (when Relay RAG is active) to `args["answer"]`. |
| **`Machine_ShowMessage`** | `{ "type": "info\|warning\|alert\|direction", "message": "<Text>", "component": "<OptionalEnumName>" }` | Displays a standalone spatial warning/alert/direction card above a specific part or the whole machine without triggering a full component Q&A highlight. |
| **`Machine_SetScale`** | `{ "scale": 1.25 }` *(or `x, y, z`)* | Scales the 3D machine transform uniformly or per-axis (clamped to safe bounds). |
| **`Machine_Rotate`** | `{ "axis": "X\|Y\|Z", "degrees": 45 }` *(or `x, y, z`)* | Rotates the machine around its spatial pivot so the technician can inspect hidden angles. |
| **`Machine_Translate`** | `{ "x": 0.1, "y": 0.0, "z": -0.2 }` | Moves/offsets the machine in local/world space. |

### `CentrifugalWaterPump` Valid Component Identifiers (14 Parts)
`ElectricMotor`, `MotorTerminalBox`, `MotorCoolingFanCover`, `ShaftCoupling`, `CouplingGuard`, `PumpCasing`, `Impeller`, `PumpShaft`, `BearingHousing`, `SuctionFlange`, `DischargeFlange`, `Baseplate`, `MountingFoot`, `MountingBolt`.

---

## 4. Where to Butt In (Extension Hooks for Agents)

### Hook A: Adding a New Machine Digital Twin (e.g., `HydraulicPress` or `GasTurbine`)
1. Open `Assets/Scripts/MachineDefinition.cs`:
   - Add the new enum value to `public enum MachineType`.
   - Create a nested enum inside `MachineDefinitions` with `[Description("...")]` attributes on every part.
   - Add the switch arm in `MachineDefinitions.GetComponentEnumType(MachineType)`.
2. Attach `MachineProfile` + `MachineInteraction` + `ToolBindings` to the new 3D model prefab root.
3. In the Unity Inspector on `MachineProfile`, select the new `MachineType` and drag each child mesh into its matching `MachineComponent.GameObjectRef` slot.
4. Update the prompt Vocabulary list in `ConvoAIMicPermissionStarter.cs` (`transcription.prompt`) so `gpt-live-transcribe` recognizes the new part names accurately.

### Hook B: Adding a New Spatial AI Tool (e.g., `Machine_ExplodeView` or `Machine_AnimateFlow`)
1. Add a `[Preserve] public JObject Machine_YourToolName(JObject args)` method (or `public async Task<JObject>`) to `Assets/Scripts/MachineInteraction.cs`.
2. Add the JSON schema definition (name, description, parameters) to the `toolsJson` `TextAsset` assigned on `ToolBindings` (or register it programmatically via `AgentToolRegistry.Register(name, handler, spec)` before `SendEnhancedSessionUpdate()`).
3. Add a rule in `ConvoAIMicPermissionStarter.SendEnhancedSessionUpdate()` (`enhancedInstructions`) telling the model *when* to invoke your new tool.

### Hook C: Customizing Highlighting, X-Ray Opacity, or 3D Cards
- **Highlight / X-Ray tuning**: Adjust `highlightColor`, `outlineWidthPixels`, `bloomGlowIntensity`, `unselectedOpacity` (`0.27f`), or `autoClearSeconds` (`12f`) in `Assets/Scripts/MachineInteraction.cs`.
- **Card styling / citations**: Edit `Assets/Scripts/WorldSpaceUI.cs` where title, body text, and citation footers are formatted.


### Conversation and walkthrough behavior

MachineInteraction and Assets/MachineTools.json expose Machine_StartComponentTour and Machine_StopComponentTour. Start enumerates highlightable profile entries and shows one existing highlight/card pair every six unscaled seconds. ClearAnswer, interruption, disable and replacement clear tour state. A partial visual failure ends progression. Extend this through the profile inventory and Machine_ShowAnswer.

Tour progression is cancelled on user speech start before the visual-clear grace period is evaluated.
