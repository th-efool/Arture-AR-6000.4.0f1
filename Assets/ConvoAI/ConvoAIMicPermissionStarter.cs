using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Ensures Android runtime microphone permissions (RECORD_AUDIO) are granted,
/// upgrades the Realtime API session to use:
/// - 24 kHz mono PCM16 input audio (fixing the 16kHz -> 24kHz 1.5x pitch/speed distortion in MicrophoneStreamer)
///   with a full drain loop and non-blocking async WebSocket sends so zero audio backlog can ever build up,
/// - English-locked gpt-live-transcribe / gpt-transcribe with near_field noise reduction and responsive VAD,
/// - Automatic mic gating while the AI is speaking to prevent speaker feedback loops,
/// - Reliable Tap-to-Speak / Push-to-Talk mode with micro-tap guard and synchronized WebSocket delivery.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ConvoAIMicPermissionStarter : MonoBehaviour
{
    public static ConvoAIMicPermissionStarter Instance { get; private set; }

    private const int OpenAISampleRate = 24_000;
    private const int ChunkSamples24k = 2_400; // 100 ms at 24 kHz (10 packets/sec instead of 16)

    // Default to gpt-live-transcribe for instant low-latency continuous live transcription
    private string _transcribeModel = "gpt-live-transcribe";
    public string TranscribeModel => _transcribeModel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (FindAnyObjectByType<ConvoAIMicPermissionStarter>() != null)
            return;

        var manager = FindAnyObjectByType<RealtimeConversationManager>();
        if (manager == null)
            return;

        manager.gameObject.AddComponent<ConvoAIMicPermissionStarter>();
    }

    private RealtimeConversationManager _conversationManager;
    private MicrophoneStreamer _micStreamer;
    private PcmAudioPlayer _audioPlayer;
    private AudioSource _agentAudioSource;

    private FieldInfo _wsField;
    private FieldInfo _sessionReadyField;
    private FieldInfo _responseActiveField;
    private FieldInfo _responseCreateSentField;
    private FieldInfo _modelField;
    private FieldInfo _systemPromptField;
    private MethodInfo _resolveVoiceMethod;
    private FieldInfo _sendTextQueueField;

    private string _micDevice;
    private AudioClip _micClip24k;
    private int _micClipFreq;
    private int _chunkSamplesIn;
    private int _lastSamplePos;

    private bool _agentSpeaking;
    private float _agentFinishedSpeakingTime = -999f;
    private float _responseActiveSince = -999f;
    private bool _customSessionConfigured;
    private bool _pushToTalkMode;
    private bool _isHoldingTalk;
    private float _speechStartTime = -999f;
    private int _speechSamplesRecorded;

    public bool PushToTalkMode => _pushToTalkMode;
    public bool IsHoldingTalk => _isHoldingTalk;
    public bool IsListening => _isHoldingTalk;
    public float CurrentSpeechDuration => (_isHoldingTalk && _speechStartTime > 0f) ? (Time.unscaledTime - _speechStartTime) : 0f;
    public bool IsCapturing24k => _micClip24k != null && !string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice);
    public int SentChunksCount { get; private set; }
    public float LastChunkSentTime { get; private set; } = -999f;
    public float CurrentMicRms { get; private set; }

    private void Awake()
    {
        Instance = this;

        _conversationManager = GetComponent<RealtimeConversationManager>();
        if (_conversationManager == null)
            _conversationManager = FindAnyObjectByType<RealtimeConversationManager>();

        _micStreamer = GetComponent<MicrophoneStreamer>();
        if (_micStreamer == null)
            _micStreamer = FindAnyObjectByType<MicrophoneStreamer>();

        _audioPlayer = GetComponent<PcmAudioPlayer>();
        if (_audioPlayer == null)
            _audioPlayer = FindAnyObjectByType<PcmAudioPlayer>();

        if (_audioPlayer != null)
        {
            var audioSourceField = typeof(PcmAudioPlayer).GetField("audioSource", BindingFlags.Instance | BindingFlags.NonPublic);
            _agentAudioSource = audioSourceField?.GetValue(_audioPlayer) as AudioSource;
        }

        _wsField = typeof(RealtimeConversationManager).GetField("_ws", BindingFlags.Instance | BindingFlags.NonPublic);
        _sessionReadyField = typeof(RealtimeConversationManager).GetField("_sessionReady", BindingFlags.Instance | BindingFlags.NonPublic);
        _responseActiveField = typeof(RealtimeConversationManager).GetField("_responseActive", BindingFlags.Instance | BindingFlags.NonPublic);
        _responseCreateSentField = typeof(RealtimeConversationManager).GetField("_responseCreateSent", BindingFlags.Instance | BindingFlags.NonPublic);
        _modelField = typeof(RealtimeConversationManager).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic);
        _systemPromptField = typeof(RealtimeConversationManager).GetField("systemPrompt", BindingFlags.Instance | BindingFlags.NonPublic);
        _resolveVoiceMethod = typeof(RealtimeConversationManager).GetMethod("ResolveVoiceString", BindingFlags.Instance | BindingFlags.NonPublic);
        _sendTextQueueField = typeof(WebSocket).GetField("sendTextQueue", BindingFlags.Instance | BindingFlags.NonPublic);

        // Disable the package's 16kHz MicrophoneStreamer component so it does not send
        // 1.5x sped-up / pitch-shifted 16kHz audio to OpenAI's 24kHz Realtime API endpoint.
        if (_micStreamer != null)
        {
            _micStreamer.enabled = false;
        }

        // Ensure model is upgraded to gpt-realtime-2.1-mini
        if (_conversationManager != null && _modelField != null)
        {
            var currentModel = _modelField.GetValue(_conversationManager) as string;
            if (string.IsNullOrEmpty(currentModel) || currentModel == "gpt-realtime-mini" || currentModel == "gpt-realtime")
            {
                _modelField.SetValue(_conversationManager, "gpt-realtime-2.1-mini");
            }
        }
    }

    private IEnumerator Start()
    {
        if (_conversationManager == null)
        {
            Debug.LogWarning("[ConvoAI] RealtimeConversationManager not found in scene.");
            yield break;
        }

        _conversationManager.onAgentSpeaking.AddListener(speaking =>
        {
            _agentSpeaking = speaking;
            if (!speaking)
                _agentFinishedSpeakingTime = Time.unscaledTime;
        });

        _conversationManager.onUserSpeaking.AddListener(speaking =>
        {
            if (speaking)
            {
                // Ensure stuck _responseActive flags never block a new user turn
                _responseActiveField?.SetValue(_conversationManager, false);
                _responseCreateSentField?.SetValue(_conversationManager, false);
            }
        });

#if UNITY_ANDROID && !UNITY_EDITOR
        float cameraWaitElapsed = 0f;
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && cameraWaitElapsed < 15f)
        {
            cameraWaitElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return null;

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.Log("[ConvoAI] Requesting Android RECORD_AUDIO permission...");
            bool requestFinished = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => requestFinished = true;
            callbacks.PermissionDenied += _ => requestFinished = true;

            Permission.RequestUserPermission(Permission.Microphone, callbacks);

            float micWaitElapsed = 0f;
            while (!requestFinished && !Permission.HasUserAuthorizedPermission(Permission.Microphone) && micWaitElapsed < 30f)
            {
                micWaitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Debug.LogError("[ConvoAI] Microphone permission (RECORD_AUDIO) was not granted. Voice input cannot be sent.");
            yield break;
        }
        Debug.Log("[ConvoAI] Android RECORD_AUDIO permission granted.");
#elif UNITY_IOS && !UNITY_EDITOR
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }
#endif

        bool alreadyStarted = _wsField != null && _wsField.GetValue(_conversationManager) != null;
        if (!alreadyStarted)
        {
            var activeModel = _modelField?.GetValue(_conversationManager) as string ?? "gpt-realtime-2.1-mini";
            Debug.Log($"[ConvoAI] Starting RealtimeConversationManager (model={activeModel})...");
            _conversationManager.StartAgent();
        }
    }

    private void Update()
    {
        if (_conversationManager == null)
            return;

        bool sessionReady = _sessionReadyField != null && (bool)_sessionReadyField.GetValue(_conversationManager);
        if (!sessionReady)
        {
            _customSessionConfigured = false;
            return;
        }

        // Watchdog for stuck _responseActive state (if a response hangs for > 8s without speaking, unlock it)
        bool respActive = _responseActiveField != null && (bool)_responseActiveField.GetValue(_conversationManager);
        if (respActive)
        {
            if (_responseActiveSince < 0f)
                _responseActiveSince = Time.unscaledTime;
            else if (!_agentSpeaking && (Time.unscaledTime - _responseActiveSince) > 8f)
            {
                _responseActiveField.SetValue(_conversationManager, false);
                _responseCreateSentField?.SetValue(_conversationManager, false);
                _responseActiveSince = -999f;
            }
        }
        else
        {
            _responseActiveSince = -999f;
        }

        if (!_customSessionConfigured)
        {
            _customSessionConfigured = true;
            UpgradeSessionAndStart24kMic();
        }

        // Safety timeout: if Tap-to-Speak was left recording for > 35s, automatically finish and commit
        if (_isHoldingTalk && CurrentSpeechDuration > 35f)
        {
            EndTapToSpeak();
        }

        Stream24kMicrophoneChunks();
    }

    private void UpgradeSessionAndStart24kMic()
    {
        if (_micStreamer != null)
        {
            _micStreamer.StopStreaming();
            _micStreamer.enabled = false;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[ConvoAI] No microphone devices found.");
            return;
        }

        _micDevice = Microphone.devices[0];
        // Use a 10-second ring buffer so wrap-around edge cases never cause lag or lost speech
        _micClip24k = Microphone.Start(_micDevice, true, 10, OpenAISampleRate);
        if (_micClip24k != null)
        {
            _micClipFreq = _micClip24k.frequency;
            _chunkSamplesIn = Mathf.RoundToInt(ChunkSamples24k * (float)_micClipFreq / OpenAISampleRate);
            _lastSamplePos = Microphone.GetPosition(_micDevice);
            if (_lastSamplePos < 0) _lastSamplePos = 0;
            Debug.Log($"[ConvoAI] Started 24kHz Mic Stream: device='{_micDevice}', hardwareRate={_micClipFreq}Hz -> 24000Hz PCM16");
        }

        SendEnhancedSessionUpdate();
    }

    public void SetPushToTalkMode(bool enabled)
    {
        if (_pushToTalkMode == enabled)
            return;

        _pushToTalkMode = enabled;
        _isHoldingTalk = false;
        _speechStartTime = -999f;
        _speechSamplesRecorded = 0;

        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            SendWebSocketJson("{\"type\":\"input_audio_buffer.clear\"}");
            SendEnhancedSessionUpdate();
        }

        Debug.Log(_pushToTalkMode
            ? "[ConvoAI] Switched to TAP-TO-SPEAK / PTT mode (Tap or hold button to speak)."
            : "[ConvoAI] Switched to AUTO-VAD mode (Hands-free automatic detection).");
    }

    public void ToggleTranscribeModel()
    {
        _transcribeModel = _transcribeModel == "gpt-live-transcribe"
            ? "gpt-transcribe"
            : "gpt-live-transcribe";

        SendEnhancedSessionUpdate();
        Debug.Log($"[ConvoAI] Switched transcriber model to '{_transcribeModel}' (English).");
    }

    public void ToggleTapToSpeak()
    {
        if (_isHoldingTalk)
            EndTapToSpeak();
        else
            BeginTapToSpeak();
    }

    public void BeginTapToSpeak()
    {
        _isHoldingTalk = true;
        _speechStartTime = Time.unscaledTime;
        _speechSamplesRecorded = 0;

        _conversationManager?.InterruptNow();
        _conversationManager?.onUserSpeaking?.Invoke(true);

        if (_micClip24k != null && !string.IsNullOrEmpty(_micDevice))
        {
            // Jump read pointer to live mic head so no stale audio is sent
            int pos = Microphone.GetPosition(_micDevice);
            if (pos >= 0) _lastSamplePos = pos;
        }

        SendWebSocketJson("{\"type\":\"input_audio_buffer.clear\"}");
        Debug.Log("[ConvoAI] Tap to Speak: LISTENING... (Speak your command)");
    }

    public void EndTapToSpeak()
    {
        if (!_isHoldingTalk)
            return;

        // Flush any remaining mic samples immediately before committing
        Stream24kMicrophoneChunks(forceFlushRemaining: true);
        _isHoldingTalk = false;
        float duration = _speechStartTime > 0f ? (Time.unscaledTime - _speechStartTime) : 0f;
        _speechStartTime = -999f;

        // Guard against accidental micro-taps: OpenAI rejects commit if audio buffer < 100ms
        // 2400 samples at 24kHz = 100ms. If under 120ms, cleanly clear the buffer without error.
        if (_speechSamplesRecorded < 2400 || duration < 0.12f)
        {
            SendWebSocketJson("{\"type\":\"input_audio_buffer.clear\"}");
            Debug.Log("[ConvoAI] Tap duration too short (< 120ms). Audio discarded without sending.");
            return;
        }

        _responseActiveField?.SetValue(_conversationManager, false);
        _responseCreateSentField?.SetValue(_conversationManager, false);

        SendWebSocketJson("{\"type\":\"input_audio_buffer.commit\"}");
        SendWebSocketJson("{\"type\":\"response.create\"}");
        Debug.Log($"[ConvoAI] Tap to speak finished ({duration:F1}s, {_speechSamplesRecorded} samples): Committed & AI response requested.");
    }

    public void BeginPushToTalk() => BeginTapToSpeak();
    public void EndPushToTalk() => EndTapToSpeak();

    private void SendEnhancedSessionUpdate()
    {
        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws == null || ws.State != WebSocketState.Open)
            return;

        // Ensure ToolBindings has registered its tools before sending session.update
        var toolBindings = FindAnyObjectByType<ToolBindings>();
        if (toolBindings != null && AgentToolRegistry.GetToolsSpec().Count == 0)
        {
            toolBindings.RegisterAll();
        }

        string basePrompt = _systemPromptField?.GetValue(_conversationManager) as string;
        if (string.IsNullOrWhiteSpace(basePrompt) || basePrompt.IndexOf("magical 3D cube", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            basePrompt = "You are an interactive AR engineering assistant inspecting a 3D industrial machine.";
        }

        string enhancedInstructions =
            basePrompt.Trim() +
            "\nCRITICAL RULES:" +
            "\n1. Before answering questions about the machine or its parts, call Machine_GetComponents to read the real component identifiers, descriptions, and assigned status. Ground all machine-specific answers in the returned data." +
            "\n2. Whenever the user asks about a component or asks a follow-up question about a component, ALWAYS call Machine_ShowAnswer using the exact component identifier returned by Machine_GetComponents (e.g., Impeller, PumpCasing, ElectricMotor) and a clear, concise plain-text answer." +
            "\n3. Inspect the Machine_ShowAnswer tool response before speaking: check 'highlighted' and 'uiDisplayed'. Never claim the component was highlighted or the label was shown if the corresponding flag is false; if one failed, explain whether the highlight or the UI label had an issue using 'highlightError' or 'uiError'." +
            "\n4. When asked to resize/scale, rotate, or move/translate the machine, call Machine_SetScale, Machine_Rotate, or Machine_Translate immediately." +
            "\n5. Speak clearly and concisely in English (1-2 short sentences) to explain the answer aloud while the component is highlighted.";

        string voiceStr = _resolveVoiceMethod?.Invoke(_conversationManager, null) as string ?? "alloy";

        // Fast, responsive turn detection: 350ms silence duration & 0.55 threshold so commands execute immediately after speaking!
        // Note: create_response is false because RealtimeConversationManager.HandleSpeechStopped sends response.create itself.
        JToken turnDetectionToken = _pushToTalkMode
            ? JValue.CreateNull()
            : new JObject
            {
                ["type"] = "server_vad",
                ["threshold"] = 0.55,
                ["prefix_padding_ms"] = 250,
                ["silence_duration_ms"] = 350,
                ["create_response"] = false,
                ["interrupt_response"] = true
            };

        var session = new JObject
        {
            ["type"] = "realtime",
            ["instructions"] = enhancedInstructions,
            ["tools"] = AgentToolRegistry.GetToolsSpec(),
            ["audio"] = new JObject
            {
                ["input"] = new JObject
                {
                    ["noise_reduction"] = new JObject
                    {
                        ["type"] = "near_field"
                    },
                    ["transcription"] = new JObject
                    {
                        ["model"] = _transcribeModel,
                        ["language"] = "en",
                        ["prompt"] = "What does the impeller do, electric motor, motor terminal box, cooling fan cover, shaft coupling, coupling guard, pump casing, pump shaft, bearing housing, suction flange, discharge flange, baseplate, pump mounting foot, mounting bolt, scale, rotate, move, translate."
                    },
                    ["turn_detection"] = turnDetectionToken
                },
                ["output"] = new JObject
                {
                    ["voice"] = voiceStr
                }
            }
        };

        var upd = new JObject
        {
            ["type"] = "session.update",
            ["session"] = session
        };

        SendWebSocketJson(upd.ToString(Formatting.None));
        Debug.Log($"[ConvoAI] Session configured: 24kHz PCM16, transcriber={_transcribeModel} (lang=en), noise_reduction=near_field, mode={(_pushToTalkMode ? "Tap-To-Speak / PTT" : "Auto-VAD (threshold=0.55, silence=350ms)")}");
    }

    private void Stream24kMicrophoneChunks(bool forceFlushRemaining = false)
    {
        if (_micClip24k == null || string.IsNullOrEmpty(_micDevice))
            return;

        int currentPos = Microphone.GetPosition(_micDevice);
        if (currentPos < 0)
            return;

        int clipSamples = _micClip24k.samples;
        int samplesAvailable = currentPos - _lastSamplePos;
        if (samplesAvailable < 0)
            samplesAvailable += clipSamples;

        // If we aren't actively sending (e.g. AI is speaking or Tap-to-Speak is not active),
        // advance _lastSamplePos to live currentPos so zero old audio ever accumulates!
        bool aiPlayingAudio = _agentSpeaking || (_agentAudioSource != null && _agentAudioSource.isPlaying);
        if (aiPlayingAudio)
        {
            _agentFinishedSpeakingTime = Time.unscaledTime;
            _lastSamplePos = currentPos;
            return;
        }
        if (Time.unscaledTime - _agentFinishedSpeakingTime < 0.25f)
        {
            _lastSamplePos = currentPos;
            return;
        }
        if (_pushToTalkMode && !_isHoldingTalk)
        {
            _lastSamplePos = currentPos;
            return;
        }

        // Safety cap: if more than 0.6s of audio accumulated due to an AR frame hitch, skip stale history to keep real-time zero-lag
        int maxAllowableLagSamples = _micClipFreq / 2;
        if (samplesAvailable > maxAllowableLagSamples && !forceFlushRemaining)
        {
            _lastSamplePos = (currentPos - _chunkSamplesIn + clipSamples) % clipSamples;
            samplesAvailable = _chunkSamplesIn;
        }

        // Drain all available full chunks in a while loop so we NEVER fall behind real-time!
        int chunksProcessedThisFrame = 0;
        while (samplesAvailable >= _chunkSamplesIn && chunksProcessedThisFrame < 5)
        {
            chunksProcessedThisFrame++;
            var inBuf = new float[_chunkSamplesIn];
            ReadCircular(_micClip24k, _lastSamplePos, inBuf);
            _lastSamplePos = (_lastSamplePos + _chunkSamplesIn) % clipSamples;
            samplesAvailable -= _chunkSamplesIn;

            SendAudioSamplesBuffer(inBuf);
        }

        // When finishing Tap-to-Speak, flush any partial trailing samples (> 20ms)
        if (forceFlushRemaining && samplesAvailable > (_micClipFreq / 50))
        {
            var inBuf = new float[samplesAvailable];
            ReadCircular(_micClip24k, _lastSamplePos, inBuf);
            _lastSamplePos = currentPos;
            SendAudioSamplesBuffer(inBuf);
        }
    }

    private void SendAudioSamplesBuffer(float[] inBuf)
    {
        float sumSq = 0f;
        for (int i = 0; i < inBuf.Length; i++)
            sumSq += inBuf[i] * inBuf[i];
        CurrentMicRms = Mathf.Sqrt(sumSq / Mathf.Max(1, inBuf.Length));

        _speechSamplesRecorded += inBuf.Length;

        byte[] pcm16Bytes = ResampleAndConvertToPcm16(inBuf, _micClipFreq, OpenAISampleRate);
        string b64 = Convert.ToBase64String(pcm16Bytes);

        // Build JSON directly without Dictionary allocation overhead
        string json = "{\"type\":\"input_audio_buffer.append\",\"audio\":\"" + b64 + "\"}";
        SendWebSocketJson(json, isAudioChunk: true);

        SentChunksCount++;
        LastChunkSentTime = Time.unscaledTime;
        if (SentChunksCount == 1)
        {
            Debug.Log("[ConvoAI] First 24kHz PCM16 audio chunk sent to OpenAI!");
        }
    }

    private void SendWebSocketJson(string json, bool isAudioChunk = false)
    {
        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws == null || ws.State != WebSocketState.Open)
            return;

        // If this is an audio chunk, ensure NativeWebSocket's outgoing queue isn't backing up
        if (isAudioChunk && _sendTextQueueField != null)
        {
            var queue = _sendTextQueueField.GetValue(ws) as IList;
            if (queue != null && queue.Count > 6)
            {
                // Socket is busy with older audio packets; skip this chunk to maintain real-time zero lag
                return;
            }
        }

        try
        {
            _ = ws.SendText(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ConvoAI] SendText warning: {ex.Message}");
        }
    }

    private static void ReadCircular(AudioClip clip, int start, float[] buffer)
    {
        int len = buffer.Length;
        int clipSamples = clip.samples;
        int tail = clipSamples - start;

        if (len <= tail)
        {
            clip.GetData(buffer, start);
        }
        else
        {
            var tempTail = new float[tail];
            var tempHead = new float[len - tail];
            clip.GetData(tempTail, start);
            clip.GetData(tempHead, 0);
            Array.Copy(tempTail, 0, buffer, 0, tail);
            Array.Copy(tempHead, 0, buffer, tail, tempHead.Length);
        }
    }

    private static byte[] ResampleAndConvertToPcm16(float[] inBuf, int inRate, int outRate)
    {
        if (inRate == outRate)
        {
            var pcm = new byte[inBuf.Length * 2];
            for (int i = 0; i < inBuf.Length; i++)
            {
                short s = (short)Mathf.Clamp(inBuf[i] * 32767f, short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return pcm;
        }

        float ratio = (float)inRate / outRate;
        int outLen = Mathf.RoundToInt(inBuf.Length / ratio);
        var pcmOut = new byte[outLen * 2];

        float pos = 0f;
        for (int o = 0; o < outLen; o++, pos += ratio)
        {
            int i0 = Mathf.Clamp((int)pos, 0, inBuf.Length - 1);
            int i1 = Mathf.Min(i0 + 1, inBuf.Length - 1);
            float frac = pos - i0;

            float sample = Mathf.Lerp(inBuf[i0], inBuf[i1], frac);
            short s16 = (short)Mathf.Clamp(sample * 32767f, short.MinValue, short.MaxValue);

            pcmOut[o * 2] = (byte)(s16 & 0xFF);
            pcmOut[o * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        return pcmOut;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(_micDevice) && Microphone.IsRecording(_micDevice))
        {
            Microphone.End(_micDevice);
        }
    }
}
