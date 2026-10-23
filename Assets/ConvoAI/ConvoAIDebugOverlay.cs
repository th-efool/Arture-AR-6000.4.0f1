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
/// Renders a live status panel and conversational API log overlay in the top-left corner of the screen.
/// Tracks permission state, 24kHz microphone capture, WebSocket state, OpenAI Realtime API events,
/// transcripts, custom tool registrations/invocations, and provides Auto-VAD / Push-to-Talk controls.
/// </summary>
[DefaultExecutionOrder(-90)]
public class ConvoAIDebugOverlay : MonoBehaviour
{
    public static ConvoAIDebugOverlay Instance { get; private set; }

    private const int MaxLogEntries = 45;

    private struct LogItem
    {
        public string Timestamp;
        public string Message;
        public LogType Type;
    }

    private readonly List<LogItem> _logs = new List<LogItem>();
    private Vector2 _scrollPos;
    private bool _minimized;

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
            Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
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

        bool isRelevant =
            condition.IndexOf("[OpenAI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[ConvoAI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[MicrophoneStreamer]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("MicrophoneStreamer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("[TestTools]", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("ToolBindings", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("Cube_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type == LogType.Error ||
            type == LogType.Exception;

        if (!isRelevant)
            return;

        AddLog(condition, type);
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

        AddLog("[ConvoAI] Debug overlay initialized.");
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
                    AddLog("[ConvoAI] VAD: User speech STARTED");
                }
                else
                {
                    AddLog("[ConvoAI] VAD: User speech STOPPED (committing audio & requesting response)");
                }
            });

            _manager.onAgentSpeaking.AddListener(speaking =>
            {
                _agentSpeaking = speaking;
                AddLog(speaking ? "[ConvoAI] Agent audio response STARTED" : "[ConvoAI] Agent audio response FINISHED");
            });

            _manager.onUserTranscript.AddListener(text =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    _lastUserTranscript = text.Trim();
                    AddLog($"[ConvoAI] User Transcript: \"{_lastUserTranscript}\"");
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

            AddLog("[ConvoAI] Hooked into RealtimeConversationManager events.");
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
            if (specs != null && specs.Count > 0)
            {
                var names = new List<string>();
                foreach (var token in specs)
                {
                    var n = token["name"]?.ToString();
                    if (!string.IsNullOrEmpty(n))
                        names.Add(n);
                }
                AddLog($"[ConvoAI] Registered Tools ({names.Count}): {string.Join(", ", names)}");
            }
            else
            {
                AddLog("[ConvoAI] Warning: 0 tools registered in AgentToolRegistry!", LogType.Warning);
            }
        }
        catch (Exception ex)
        {
            AddLog($"[ConvoAI] Failed to inspect AgentToolRegistry: {ex.Message}", LogType.Warning);
        }
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

        // If Push-To-Talk button is held and user releases touch/mouse anywhere on screen, commit the speech
        var starter = ConvoAIMicPermissionStarter.Instance;
        if (starter != null && starter.PushToTalkMode && starter.IsHoldingTalk)
        {
            bool anyPressed = Input.GetMouseButton(0) || Input.touchCount > 0;
            if (!anyPressed)
            {
                starter.EndPushToTalk();
            }
        }

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
            _bgTex.SetPixel(0, 0, new Color(0.05f, 0.07f, 0.1f, 0.84f));
            _bgTex.Apply();
        }

        if (_pttActiveTex == null)
        {
            _pttActiveTex = new Texture2D(1, 1);
            _pttActiveTex.SetPixel(0, 0, new Color(0.15f, 0.65f, 0.25f, 0.95f));
            _pttActiveTex.Apply();
        }

        int fontSize = Mathf.RoundToInt(13f * scale);
        int headerSize = Mathf.RoundToInt(15f * scale);

        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _bgTex, textColor = Color.white },
                padding = new RectOffset(10, 10, 8, 8)
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
                wordWrap = true
            };
        }
        _statusStyle.fontSize = fontSize;

        if (_logStyle == null)
        {
            _logStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                margin = new RectOffset(0, 0, 1, 1)
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

        if (_minimized)
        {
            Rect minRect = new Rect(x, y, 220f * scale, 38f * scale);
            GUI.Box(minRect, GUIContent.none, _boxStyle);
            if (GUI.Button(new Rect(x + 6f, y + 4f, minRect.width - 12f, minRect.height - 8f), "Show ConvoAI Logs", _buttonStyle))
            {
                _minimized = false;
            }
            return;
        }

        float height = Mathf.Min(Screen.height * 0.52f, 410f * scale);
        Rect panelRect = new Rect(x, y, width, height);
        GUI.Box(panelRect, GUIContent.none, _boxStyle);

        GUILayout.BeginArea(new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, panelRect.height - 12f));

        var starter = ConvoAIMicPermissionStarter.Instance;
        bool isPtt = starter != null && starter.PushToTalkMode;

        // Top bar with Mode Toggle + Clear + Hide
        GUILayout.BeginHorizontal();
        if (starter != null)
        {
            string sttLabel = $"STT: {starter.TranscribeModel}";
            if (GUILayout.Button(sttLabel, _buttonStyle, GUILayout.Width(135f * scale), GUILayout.Height(28f * scale)))
            {
                starter.ToggleTranscribeModel();
            }

            string modeLabel = isPtt ? "Mode: Push-To-Talk" : "Mode: Auto-VAD (EN)";
            if (GUILayout.Button(modeLabel, _buttonStyle, GUILayout.Width(135f * scale), GUILayout.Height(28f * scale)))
            {
                starter.SetPushToTalkMode(!isPtt);
            }
        }

        if (GUILayout.Button("Clear", _buttonStyle, GUILayout.Width(55f * scale), GUILayout.Height(28f * scale)))
        {
            _logs.Clear();
        }
        if (GUILayout.Button("Hide", _buttonStyle, GUILayout.Width(55f * scale), GUILayout.Height(28f * scale)))
        {
            _minimized = true;
        }
        GUILayout.EndHorizontal();

        // Push-to-Talk Hold Button when PTT mode is enabled
        if (starter != null && isPtt)
        {
            GUILayout.Space(4f);
            Rect pttRect = GUILayoutUtility.GetRect(width - 24f, 42f * scale);
            bool holding = starter.IsHoldingTalk;
            string btnText = holding ? "LISTENING... (Release to Send to AI)" : "HOLD TO TALK (English)";
            GUI.Box(pttRect, btnText, holding ? _pttActiveStyle : _buttonStyle);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && pttRect.Contains(e.mousePosition))
            {
                starter.BeginPushToTalk();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && holding)
            {
                starter.EndPushToTalk();
                e.Use();
            }
            GUILayout.Space(4f);
        }

        // Live stage status summary
        string permStatus = GetPermissionStatusText();
        string wsStatus = GetWebSocketStatusText();
        string micStatus = GetMicrophoneStatusText();
        string toolCount = GetToolCountText();
        bool userActive = _userSpeaking || (starter != null && starter.IsHoldingTalk);

        GUILayout.Label(
            $"<b>1. Mic Perm:</b> {permStatus}   |   <b>2. 24kHz Mic:</b> {micStatus}\n" +
            $"<b>3. WebSocket:</b> {wsStatus}   |   <b>4. Tools:</b> {toolCount}\n" +
            $"<b>5. Voice State:</b> User={( userActive ? "<color=#66BB6A>SPEAKING</color>" : "Idle" )} | Agent={( _agentSpeaking ? "<color=#FFA726>SPEAKING</color>" : "Idle" )}",
            _statusStyle);

        if (!string.IsNullOrEmpty(_lastUserTranscript))
            GUILayout.Label($"<color=#B0BEC5><b>User (EN):</b> {_lastUserTranscript}</color>", _statusStyle);

        if (!string.IsNullOrEmpty(_lastAgentTranscript))
        {
            string preview = _lastAgentTranscript.Length > 110
                ? "..." + _lastAgentTranscript.Substring(_lastAgentTranscript.Length - 110)
                : _lastAgentTranscript;
            GUILayout.Label($"<color=#81D4FA><b>AI:</b> {preview}</color>", _statusStyle);
        }

        GUILayout.Space(4f);

        // Scrollable event log
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);
        for (int i = 0; i < _logs.Count; i++)
        {
            var entry = _logs[i];
            string colorHex = entry.Type switch
            {
                LogType.Error => "#FF5252",
                LogType.Exception => "#FF5252",
                LogType.Warning => "#FFD54F",
                _ => "#E0E0E0"
            };
            GUILayout.Label($"<color=#90A4AE>[{entry.Timestamp}]</color> <color={colorHex}>{entry.Message}</color>", _logStyle);
        }
        GUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    private string GetPermissionStatusText()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool granted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
        return granted ? "<color=#66BB6A>GRANTED</color>" : "<color=#FF5252>NOT GRANTED</color>";
#else
        return "<color=#66BB6A>READY</color>";
#endif
    }

    private string GetWebSocketStatusText()
    {
        if (_manager == null)
            return "<color=#FF5252>No Manager</color>";

        var ws = _wsField?.GetValue(_manager) as WebSocket;
        bool sessionReady = _sessionReadyField != null && (bool)_sessionReadyField.GetValue(_manager);

        if (ws == null)
            return "<color=#FFD54F>Not Initialized</color>";

        string stateColor = ws.State switch
        {
            WebSocketState.Open => "#66BB6A",
            WebSocketState.Connecting => "#FFD54F",
            _ => "#FF5252"
        };

        string sessionStr = sessionReady ? " (Session Ready)" : " (Waiting Session)";
        return $"<color={stateColor}>{ws.State}{sessionStr}</color>";
    }

    private string GetMicrophoneStatusText()
    {
        var starter = ConvoAIMicPermissionStarter.Instance;
        if (starter != null && starter.IsCapturing24k)
        {
            bool recent = (Time.unscaledTime - starter.LastChunkSentTime) < 1.0f;
            if (starter.PushToTalkMode && !starter.IsHoldingTalk)
                return $"<color=#FFD54F>PTT Ready ({starter.SentChunksCount} pkts)</color>";

            return recent
                ? $"<color=#66BB6A>Streaming 24kHz ({starter.SentChunksCount} pkts)</color>"
                : $"<color=#FFD54F>Gated/Idle ({starter.SentChunksCount} pkts)</color>";
        }

        if (_micStreamer == null)
            return "<color=#FF5252>No Streamer</color>";

        var dev = _micDeviceField?.GetValue(_micStreamer) as string;
        var clip = _micClipField?.GetValue(_micStreamer) as AudioClip;
        bool isRec = !string.IsNullOrEmpty(dev) && Microphone.IsRecording(dev);
        bool recentChunk = (Time.unscaledTime - _lastMicChunkTime) < 1.0f;

        if (isRec && clip != null)
        {
            return recentChunk
                ? $"<color=#66BB6A>Streaming ({_micChunkCount} pkts)</color>"
                : $"<color=#FFD54F>Rec ({_micChunkCount} pkts)</color>";
        }

        return "<color=#FFD54F>Waiting Session</color>";
    }

    private static string GetToolCountText()
    {
        try
        {
            var specs = AgentToolRegistry.GetToolsSpec();
            int count = specs != null ? specs.Count : 0;
            return count > 0 ? $"<color=#66BB6A>{count} bound</color>" : "<color=#FF5252>0 bound</color>";
        }
        catch
        {
            return "?";
        }
    }
}
