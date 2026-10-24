using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json.Linq;
using OpenAI;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// High-signal live status panel and conversational event log overlay.
/// Displays clear backend relay state, 24kHz capture, tools, transcripts, and Tap-to-Speak.
/// Automatically filters noisy spam to maximize vertical screen real estate for vital events.
/// </summary>
[DefaultExecutionOrder(-90)]
public class ConvoAIDebugOverlay : MonoBehaviour
{
    public static ConvoAIDebugOverlay Instance { get; private set; }

    private const int MaxLogEntries = 60;

    private struct LogItem
    {
        public string Timestamp;
        public string Message;
        public LogType Type;
    }

    private readonly List<LogItem> _logs = new List<LogItem>();
    private Vector2 _scrollPos;
    private bool _minimized;
    public bool IsMinimized
    {
        get => _minimized;
        set => _minimized = value;
    }

    private RealtimeConversationManager _manager;
    private MicrophoneStreamer _micStreamer;
    private ToolBindings _toolBindings;

    private FieldInfo _wsField;
    private FieldInfo _sessionReadyField;
    private FieldInfo _responseActiveField;
    private FieldInfo _micClipField;
    private FieldInfo _micDeviceField;

    private bool _userSpeaking;
    private bool _agentSpeaking;
    private int _micChunkCount;
    private float _lastMicChunkTime = -999f;
    private string _lastUserTranscript = "";
    private readonly StringBuilder _agentTranscriptBuilder = new StringBuilder();
    private string _lastAgentTranscript = "";

    private GUIStyle _boxStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _statusStyle;
    private GUIStyle _logStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _pttActiveStyle;
    private Texture2D _bgTex;
    private Texture2D _pttActiveTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterEarlyLogListener()
    {
        Application.logMessageReceivedThreaded -= OnUnityLogReceived;
        Application.logMessageReceivedThreaded += OnUnityLogReceived;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindAnyObjectByType<ConvoAIDebugOverlay>() != null)
            return;

        var go = new GameObject("ConvoAIDebugOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<ConvoAIDebugOverlay>();
    }

    private static readonly Queue<LogItem> PendingLogs = new Queue<LogItem>();
    private static readonly object LogLock = new object();

    public static void AddLog(string message, LogType type = LogType.Log)
    {
        var item = new LogItem
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Message = message,
            Type = type
        };

        lock (LogLock)
        {
            PendingLogs.Enqueue(item);
        }
    }

    private static void OnUnityLogReceived(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition))
            return;

        // Filter out noisy spam to preserve screen height for real events
        if (condition.IndexOf("VAD: User speech", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Agent audio response", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("First 24kHz PCM16 audio chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Registered Tools (", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Loading SFX", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Tap to Speak: LISTENING", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Tap to speak finished", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Hooked into RealtimeConversationManager", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("dispatched message", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        bool isRelevant =
            condition.IndexOf("[OpenAI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[ConvoAI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[MachineInteraction]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[WorldSpaceUI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[Arture]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("ToolBindings", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Machine_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Cube_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type == LogType.Error ||
            type == LogType.Exception;

        if (!isRelevant)
            return;

        string clean = condition
            .Replace("[OpenAI] ", "")
            .Replace("[ConvoAI] ", "")
            .Replace("[MachineInteraction] ", "")
            .Replace("[WorldSpaceUI] ", "")
            .Replace("[Arture] ", "");

        // Prefix high-signal icons for fast glanceability
        if (clean.IndexOf("Machine_ShowAnswer", StringComparison.OrdinalIgnoreCase) >= 0)
            clean = "🔧 " + clean;
        else if (clean.IndexOf("Machine_ShowMessage", StringComparison.OrdinalIgnoreCase) >= 0)
            clean = "🔧 " + clean;
        else if (clean.IndexOf("WorldSpaceUI", StringComparison.OrdinalIgnoreCase) >= 0)
            clean = "🏷️ " + clean;
        else if (clean.IndexOf("Connecting directly to OpenAI Realtime", StringComparison.OrdinalIgnoreCase) >= 0)
            clean = "⚠️ Direct OpenAI (Fallback Active)";
        else if (clean.IndexOf("Connecting to Arture Voice Relay", StringComparison.OrdinalIgnoreCase) >= 0)
            clean = "🌐 Relay: " + clean;

        AddLog(clean, type);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _wsField = typeof(RealtimeConversationManager).GetField("_ws", BindingFlags.Instance | BindingFlags.NonPublic);
        _sessionReadyField = typeof(RealtimeConversationManager).GetField("_sessionReady", BindingFlags.Instance | BindingFlags.NonPublic);
        _responseActiveField = typeof(RealtimeConversationManager).GetField("_responseActive", BindingFlags.Instance | BindingFlags.NonPublic);

        _micClipField = typeof(MicrophoneStreamer).GetField("_microphoneClip", BindingFlags.Instance | BindingFlags.NonPublic);
        _micDeviceField = typeof(MicrophoneStreamer).GetField("_micDevice", BindingFlags.Instance | BindingFlags.NonPublic);

        AddLog("🚀 ConvoAI Debug Overlay initialized.");
    }

    private void Start()
    {
        BindSceneComponents();
    }

    private bool _eventsBound;

    private void BindSceneComponents()
    {
        if (_manager == null)
            _manager = FindAnyObjectByType<RealtimeConversationManager>();

        if (_micStreamer == null)
            _micStreamer = FindAnyObjectByType<MicrophoneStreamer>();

        if (_toolBindings == null)
            _toolBindings = FindAnyObjectByType<ToolBindings>();

        if (_manager != null && !_eventsBound)
        {
            _eventsBound = true;

            _manager.onUserSpeaking.AddListener(speaking =>
            {
                _userSpeaking = speaking;
                if (speaking)
                {
                    _agentTranscriptBuilder.Clear();
                }
            });

            _manager.onAgentSpeaking.AddListener(speaking =>
            {
                _agentSpeaking = speaking;
                if (!speaking && _agentTranscriptBuilder.Length > 0)
                {
                    string reply = _agentTranscriptBuilder.ToString().Trim();
                    if (!string.IsNullOrEmpty(reply))
                    {
                        string shortReply = reply.Length > 110 ? reply.Substring(0, 107) + "..." : reply;
                        AddLog($"🤖 AI: \"{shortReply}\"");
                    }
                }
            });

            _manager.onUserTranscript.AddListener(text =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    _lastUserTranscript = text.Trim();
                    AddLog($"💬 You: \"{_lastUserTranscript}\"");
                }
            });

            _manager.onAgentTranscript.AddListener(delta =>
            {
                if (!string.IsNullOrEmpty(delta))
                {
                    _agentTranscriptBuilder.Append(delta);
                    _lastAgentTranscript = _agentTranscriptBuilder.ToString();
                }
            });
        }

        if (_micStreamer != null)
        {
            _micStreamer.OnAudioChunk -= OnMicAudioChunk;
            _micStreamer.OnAudioChunk += OnMicAudioChunk;
        }

        LogRegisteredToolsSummary();
    }

    private void LogRegisteredToolsSummary()
    {
        try
        {
            JArray specs = AgentToolRegistry.GetToolsSpec();
            int count = specs != null ? specs.Count : 0;
            if (count > 0)
            {
                AddLog($"📦 Registered {count} AI Tools (Water Pump & Cube)");
            }
        }
        catch {}
    }

    private void OnMicAudioChunk(string b64)
    {
        _micChunkCount++;
        _lastMicChunkTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (_manager == null || _micStreamer == null)
            BindSceneComponents();

        lock (LogLock)
        {
            while (PendingLogs.Count > 0)
            {
                _logs.Add(PendingLogs.Dequeue());
                if (_logs.Count > MaxLogEntries)
                    _logs.RemoveAt(0);
                _scrollPos.y = float.MaxValue;
            }
        }
    }

    private void EnsureStyles(float scale)
    {
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.09f, 0.88f));
            _bgTex.Apply();
        }

        if (_pttActiveTex == null)
        {
            _pttActiveTex = new Texture2D(1, 1);
            _pttActiveTex.SetPixel(0, 0, new Color(0.12f, 0.62f, 0.24f, 0.95f));
            _pttActiveTex.Apply();
        }

        int fontSize = Mathf.RoundToInt(12f * scale);
        int headerSize = Mathf.RoundToInt(14f * scale);

        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _bgTex, textColor = Color.white },
                padding = new RectOffset(8, 8, 6, 6)
            };
        }

        if (_headerStyle == null)
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                richText = true
            };
        }
        _headerStyle.fontSize = headerSize;

        if (_statusStyle == null)
        {
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                margin = new RectOffset(0, 0, 1, 1)
            };
        }
        _statusStyle.fontSize = fontSize;

        if (_logStyle == null)
        {
            _logStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                margin = new RectOffset(0, 0, 1, 1),
                padding = new RectOffset(0, 0, 0, 0)
            };
        }
        _logStyle.fontSize = fontSize;

        if (_buttonStyle == null)
        {
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };
        }
        _buttonStyle.fontSize = fontSize;

        if (_pttActiveStyle == null)
        {
            _pttActiveStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = _pttActiveTex, textColor = Color.white },
                active = { background = _pttActiveTex, textColor = Color.white },
                hover = { background = _pttActiveTex, textColor = Color.white }
            };
        }
        _pttActiveStyle.fontSize = headerSize;
    }

    private void OnGUI()
    {
        float scale = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 160f : 1.5f, 1.0f, 2.5f);
        EnsureStyles(scale);

        Rect safe = Screen.safeArea;
        float x = Mathf.Max(12f, safe.xMin + 10f);
        float y = Mathf.Max(12f, Screen.height - safe.yMax + 10f);
        float width = Mathf.Min(Screen.width - x * 2f, 540f * scale);

        var starter = ConvoAIMicPermissionStarter.Instance ?? FindAnyObjectByType<ConvoAIMicPermissionStarter>();
        bool isListening = starter != null && starter.IsListening;
        bool isPtt = starter != null && starter.PushToTalkMode;

        if (_minimized)
        {
            float btnHeight = 44f * scale;
            float showBtnWidth = 96f * scale;
            float maxAvailableWidth = Screen.width - x * 2f;
            float speakBtnWidth = Mathf.Clamp(maxAvailableWidth - showBtnWidth - 14f, 180f * scale, 320f * scale);
            float totalW = speakBtnWidth + showBtnWidth + 10f;

            Rect minRect = new Rect(x, y, totalW + 12f, btnHeight + 12f);
            GUI.Box(minRect, GUIContent.none, _boxStyle);

            string btnText;
            if (isListening)
            {
                btnText = $"⏹️ LISTENING ({starter.CurrentSpeechDuration:F1}s) - STOP";
            }
            else if (_agentSpeaking)
            {
                btnText = "🔊 AGENT SPEAKING (TAP TO TALK)";
            }
            else
            {
                btnText = "🎤 TAP TO SPEAK";
            }

            GUIStyle activeBtnStyle = isListening ? _pttActiveStyle : _buttonStyle;
            if (GUI.Button(new Rect(x + 6f, y + 6f, speakBtnWidth, btnHeight), btnText, activeBtnStyle))
            {
                if (starter != null)
                {
                    starter.ToggleTapToSpeak();
                }
            }

            if (GUI.Button(new Rect(x + 6f + speakBtnWidth + 8f, y + 6f, showBtnWidth, btnHeight), "Logs ⚙️", _buttonStyle))
            {
                _minimized = false;
            }

            return;
        }

        float height = Mathf.Min(Screen.height * 0.62f, 460f * scale);
        Rect panelRect = new Rect(x, y, width, height);
        GUI.Box(panelRect, GUIContent.none, _boxStyle);

        GUILayout.BeginArea(new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, panelRect.height - 12f));        // Top bar with Mode Toggle + Clear + Hide
        GUILayout.BeginHorizontal();

        if (starter != null)
        {
            string modeLabel = isPtt ? "Mode: PTT" : "Mode: Auto-VAD";
            if (GUILayout.Button(modeLabel, _buttonStyle, GUILayout.Width(110f * scale), GUILayout.Height(28f * scale)))
            {
                starter.SetPushToTalkMode(!isPtt);
            }
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Clear", _buttonStyle, GUILayout.Width(60f * scale), GUILayout.Height(28f * scale)))
        {
            _logs.Clear();
        }
        if (GUILayout.Button("Hide", _buttonStyle, GUILayout.Width(60f * scale), GUILayout.Height(28f * scale)))
        {
            _minimized = true;
        }
        GUILayout.EndHorizontal();

        // Tap-to-Speak Button
        if (starter != null)
        {
            GUILayout.Space(2f);
            string btnText;
            if (isListening)
            {
                btnText = $"⏹️ LISTENING ({starter.CurrentSpeechDuration:F1}s) - TAP TO SEND";
            }
            else
            {
                btnText = isPtt
                    ? "🎤 TAP TO SPEAK"
                    : "🎤 TAP TO SPEAK (or speak hands-free)";
            }

            GUIStyle activeBtnStyle = isListening ? _pttActiveStyle : _buttonStyle;
            if (GUILayout.Button(btnText, activeBtnStyle, GUILayout.Width(width - 24f), GUILayout.Height(34f * scale)))
            {
                starter.ToggleTapToSpeak();
            }
            GUILayout.Space(2f);
        }

        // High-signal 2-line compact status summary
        string wsStatus = GetWebSocketStatusText();
        string micStatus = GetMicrophoneStatusText();
        string toolCount = GetToolCountText();
        bool userActive = _userSpeaking || (starter != null && starter.IsListening);
        string userSpeakingLabel = userActive
            ? (starter != null && starter.IsListening
                ? $"<color=#66BB6A>LISTENING ({starter.CurrentSpeechDuration:F1}s)</color>"
                : "<color=#66BB6A>SPEAKING</color>")
            : "<color=#B0BEC5>Idle</color>";
        string agentSpeakingLabel = _agentSpeaking ? "<color=#FFA726>SPEAKING</color>" : "<color=#B0BEC5>Idle</color>";

        GUILayout.Label(
            $"<b>Relay:</b> {wsStatus}  |  <b>Mic:</b> {micStatus}  |  <b>Tools:</b> {toolCount}\n" +
            $"<b>Voice:</b> User={userSpeakingLabel}  |  Agent={agentSpeakingLabel}",
            _statusStyle);

        // Single-line compact live conversation preview
        if (!string.IsNullOrEmpty(_lastUserTranscript) || !string.IsNullOrEmpty(_lastAgentTranscript))
        {
            string userPart = !string.IsNullOrEmpty(_lastUserTranscript) ? $"<color=#B0BEC5><b>You:</b> \"{_lastUserTranscript}\"</color>" : "";
            string aiPart = "";
            if (!string.IsNullOrEmpty(_lastAgentTranscript))
            {
                string shortAi = _lastAgentTranscript.Length > 75 ? _lastAgentTranscript.Substring(_lastAgentTranscript.Length - 75) : _lastAgentTranscript;
                aiPart = $"  <color=#81D4FA><b>AI:</b> \"{shortAi}\"</color>";
            }
            GUILayout.Label(userPart + aiPart, _statusStyle);
        }

        GUILayout.Space(2f);

        // Scrollable event log taking up maximum vertical space
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);
        for (int i = 0; i < _logs.Count; i++)
        {
            var entry = _logs[i];
            string colorHex = entry.Type switch
            {
                LogType.Error => "#FF5252",
                LogType.Exception => "#FF5252",
                LogType.Warning => "#FFD54F",
                _ => "#ECEFF1"
            };
            GUILayout.Label($"<color=#78909C>[{entry.Timestamp}]</color> <color={colorHex}>{entry.Message}</color>", _logStyle);
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    private string GetWebSocketStatusText()
    {
        if (_manager == null)
            return "<color=#FF5252>No Manager</color>";

        var ws = _wsField?.GetValue(_manager) as WebSocket;
        bool sessionReady = _manager.IsSessionReady;

        if (ws == null)
            return "<color=#FFD54F>Connecting...</color>";

        if (ws.State != WebSocketState.Open)
        {
            string stateColor = ws.State switch
            {
                WebSocketState.Connecting => "#FFD54F",
                _ => "#FF5252"
            };
            return $"<color={stateColor}>{ws.State}</color>";
        }

        string target = "OpenAI Direct";
        if (_manager.ActiveRelayUrl != null)
        {
            if (_manager.ActiveRelayUrl.Contains("onrender.com")) target = "Cloud (Render)";
            else if (_manager.ActiveRelayUrl.Contains("3101")) target = "Local (3101)";
            else if (_manager.ActiveRelayUrl.Contains("openai.com")) target = "OpenAI Direct";
        }

        return sessionReady
            ? $"<color=#66BB6A>Connected ({target})</color>"
            : $"<color=#FFD54F>Handshaking ({target})</color>";
    }

    private string GetMicrophoneStatusText()
    {
        var starter = ConvoAIMicPermissionStarter.Instance;
        if (starter != null && starter.IsCapturing24k)
        {
            if (starter.PushToTalkMode && !starter.IsListening)
                return "<color=#81D4FA>24kHz Ready (PTT)</color>";

            bool recent = (Time.unscaledTime - starter.LastChunkSentTime) < 1.0f;
            return recent
                ? "<color=#66BB6A>24kHz Streaming</color>"
                : "<color=#81D4FA>24kHz Gated</color>";
        }

        return "<color=#FFD54F>Waiting</color>";
    }

    private static string GetToolCountText()
    {
        try
        {
            var specs = AgentToolRegistry.GetToolsSpec();
            int count = specs != null ? specs.Count : 0;
            return count > 0 ? $"<color=#66BB6A>{count} bound</color>" : "<color=#FF5252>0</color>";
        }
        catch
        {
            return "?";
        }
    }
}
