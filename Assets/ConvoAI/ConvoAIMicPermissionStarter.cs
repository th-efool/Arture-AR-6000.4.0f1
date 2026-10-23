using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
/// - 24 kHz mono PCM16 input audio (fixing the 16kHz -> 24kHz 1.5x pitch/speed distortion in MicrophoneStreamer),
/// - English-locked gpt-transcribe / gpt-live-transcribe with near_field noise reduction and tuned VAD threshold (0.65),
/// - Automatic mic gating while the AI is speaking to prevent speaker feedback loops,
/// - Optional Push-to-Talk mode via the on-screen UI.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ConvoAIMicPermissionStarter : MonoBehaviour
{
    public static ConvoAIMicPermissionStarter Instance { get; private set; }

    private const int OpenAISampleRate = 24_000;
    private const int ChunkSamples24k = 1_536; // 64 ms at 24 kHz

    private string _transcribeModel = "gpt-transcribe";
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
    private FieldInfo _modelField;
    private FieldInfo _systemPromptField;
    private MethodInfo _resolveVoiceMethod;

    private string _micDevice;
    private AudioClip _micClip24k;
    private int _micClipFreq;
    private int _chunkSamplesIn;
    private int _lastSamplePos;

    private bool _agentSpeaking;
    private float _agentFinishedSpeakingTime = -999f;
    private bool _customSessionConfigured;
    private bool _pushToTalkMode;
    private bool _isHoldingTalk;

    public bool PushToTalkMode => _pushToTalkMode;
    public bool IsHoldingTalk => _isHoldingTalk;
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
        _modelField = typeof(RealtimeConversationManager).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic);
        _systemPromptField = typeof(RealtimeConversationManager).GetField("systemPrompt", BindingFlags.Instance | BindingFlags.NonPublic);
        _resolveVoiceMethod = typeof(RealtimeConversationManager).GetMethod("ResolveVoiceString", BindingFlags.Instance | BindingFlags.NonPublic);

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

#if UNITY_ANDROID && !UNITY_EDITOR
        // Wait briefly for ARCore's initial CAMERA permission dialog to finish so Android
        // does not ignore our RECORD_AUDIO permission request while another dialog is open.
        float cameraWaitElapsed = 0f;
        while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && cameraWaitElapsed < 15f)
        {
            cameraWaitElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Wait one extra frame after returning from the camera dialog
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

        // Start the Realtime Conversation Agent if it hasn't connected yet.
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

        // As soon as the package's HandleSessionCreated() runs, intercept and replace
        // its 16kHz mic capture with clean 24kHz capture + English noise-reduced session settings.
        if (!_customSessionConfigured)
        {
            _customSessionConfigured = true;
            UpgradeSessionAndStart24kMic();
        }

        Stream24kMicrophoneChunks();
    }

    private void UpgradeSessionAndStart24kMic()
    {
        // Stop the package's 16kHz MicrophoneStreamer that HandleSessionCreated() just started
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
        _micClip24k = Microphone.Start(_micDevice, true, 2, OpenAISampleRate);
        if (_micClip24k != null)
        {
            _micClipFreq = _micClip24k.frequency;
            _chunkSamplesIn = Mathf.RoundToInt(ChunkSamples24k * (float)_micClipFreq / OpenAISampleRate);
            _lastSamplePos = 0;
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

        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var clear = new JObject { ["type"] = "input_audio_buffer.clear" };
            _ = ws.SendText(clear.ToString(Formatting.None));
            SendEnhancedSessionUpdate();
        }

        Debug.Log(_pushToTalkMode
            ? "[ConvoAI] Switched to PUSH-TO-TALK mode (Hold 'Hold to Talk' button to speak)."
            : "[ConvoAI] Switched to AUTO-VAD mode (English-locked + Noise Reduction).");
    }

    public void ToggleTranscribeModel()
    {
        _transcribeModel = _transcribeModel == "gpt-transcribe"
            ? "gpt-live-transcribe"
            : "gpt-transcribe";

        SendEnhancedSessionUpdate();
        Debug.Log($"[ConvoAI] Switched transcriber model to '{_transcribeModel}' (English).");
    }

    public void BeginPushToTalk()
    {
        if (!_pushToTalkMode)
            return;

        _isHoldingTalk = true;
        _conversationManager?.InterruptNow();

        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var clear = new JObject { ["type"] = "input_audio_buffer.clear" };
            _ = ws.SendText(clear.ToString(Formatting.None));
        }

        Debug.Log("[ConvoAI] Push-to-Talk: Recording user speech...");
    }

    public void EndPushToTalk()
    {
        if (!_pushToTalkMode || !_isHoldingTalk)
            return;

        _isHoldingTalk = false;

        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var commit = new JObject { ["type"] = "input_audio_buffer.commit" };
            _ = ws.SendText(commit.ToString(Formatting.None));

            var create = new JObject { ["type"] = "response.create" };
            _ = ws.SendText(create.ToString(Formatting.None));
            Debug.Log("[ConvoAI] Push-to-Talk released: Committed audio & requested AI response.");
        }
    }

    private void SendEnhancedSessionUpdate()
    {
        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws == null || ws.State != WebSocketState.Open)
            return;

        string basePrompt = _systemPromptField?.GetValue(_conversationManager) as string
                            ?? "You are an AR cube assistant. Be extremely concise. Keep spoken replies to 1-5 words or at most one short sentence unless the user explicitly asks for an explanation or longer detail.";
        string enhancedInstructions =
            basePrompt.Trim() +
            "\nCRITICAL RULES:" +
            "\n1. Be extremely brief. Speak only 1 to 5 words or at most a single short sentence (e.g. 'Spinning now', 'Stopped', 'Color changed', 'Done') unless the user explicitly demands a long answer or detailed explanation." +
            "\n2. When asked to control the cube (spin, stop, speed, color, opacity), trigger the tool immediately without unnecessary commentary." +
            "\n3. Always listen and speak strictly in English. Ignore background noise and non-English phonetic artifacts.";

        string voiceStr = _resolveVoiceMethod?.Invoke(_conversationManager, null) as string ?? "alloy";

        JToken turnDetectionToken = _pushToTalkMode
            ? JValue.CreateNull()
            : new JObject
            {
                ["type"] = "server_vad",
                ["threshold"] = 0.65,
                ["prefix_padding_ms"] = 300,
                ["silence_duration_ms"] = 600,
                ["create_response"] = false
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
                        ["prompt"] = "English voice commands in an AR app for controlling a 3D cube (spin, stop, speed, random color, opacity, transparent, opaque)."
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

        _ = ws.SendText(upd.ToString(Formatting.None));
        Debug.Log($"[ConvoAI] Session configured: 24kHz PCM16, transcriber={_transcribeModel} (lang=en), noise_reduction=near_field, mode={(_pushToTalkMode ? "Push-To-Talk" : "Auto-VAD (threshold=0.65)")}");
    }

    private void Stream24kMicrophoneChunks()
    {
        if (_micClip24k == null || string.IsNullOrEmpty(_micDevice))
            return;

        int currentPos = Microphone.GetPosition(_micDevice);
        if (currentPos < 0)
            return;

        int samplesAvailable = currentPos - _lastSamplePos;
        if (samplesAvailable < 0)
            samplesAvailable += _micClip24k.samples;

        if (samplesAvailable < _chunkSamplesIn)
            return;

        var inBuf = new float[_chunkSamplesIn];
        ReadCircular(_micClip24k, _lastSamplePos, inBuf);
        _lastSamplePos = (_lastSamplePos + _chunkSamplesIn) % _micClip24k.samples;

        // Calculate RMS volume for diagnostics
        float sumSq = 0f;
        for (int i = 0; i < inBuf.Length; i++)
            sumSq += inBuf[i] * inBuf[i];
        CurrentMicRms = Mathf.Sqrt(sumSq / Mathf.Max(1, inBuf.Length));

        // Do not stream speaker echo while the AI is playing audio (plus 350ms tail)
        bool aiPlayingAudio = _agentSpeaking || (_agentAudioSource != null && _agentAudioSource.isPlaying);
        if (aiPlayingAudio)
        {
            _agentFinishedSpeakingTime = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _agentFinishedSpeakingTime < 0.35f)
            return;

        // In Push-to-Talk mode, only stream audio while the user is holding the talk button
        if (_pushToTalkMode && !_isHoldingTalk)
            return;

        byte[] pcm16Bytes = ResampleAndConvertToPcm16(inBuf, _micClipFreq, OpenAISampleRate);
        string b64 = Convert.ToBase64String(pcm16Bytes);

        var ws = _wsField?.GetValue(_conversationManager) as WebSocket;
        if (ws != null && ws.State == WebSocketState.Open)
        {
            var payload = new Dictionary<string, object>
            {
                { "type", "input_audio_buffer.append" },
                { "audio", b64 }
            };
            _ = ws.SendText(JsonConvert.SerializeObject(payload));

            SentChunksCount++;
            LastChunkSentTime = Time.unscaledTime;
            if (SentChunksCount == 1)
            {
                Debug.Log("[ConvoAI] First 24kHz PCM16 audio chunk sent to OpenAI!");
            }
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
