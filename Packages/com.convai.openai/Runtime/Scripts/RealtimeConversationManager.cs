using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace OpenAI
{
    public class RealtimeConversationManager : MonoBehaviour
    {
        #region Inspector

        [Header("Model")]
        [SerializeField] private string model = "gpt-realtime";

        private enum Voice { Alloy, Verse, Custom }

        [Header("Voice")]
        [SerializeField] private Voice voice = Voice.Alloy;
        [SerializeField] private string customVoice = "";

        [Header("System prompt")]
        [TextArea(2, 8)]
        [SerializeField] private string systemPrompt = "You are a helpful assistant who answers briefly.";

        [Header("Startup")]
        [SerializeField] private bool startOnAwake = true;
        [SerializeField] private bool reconnectAutomatically = true;

        [Header("Dependencies")]
        [SerializeField] private OpenAIConfig       config;
        [SerializeField] private MicrophoneStreamer micStreamer;
        [SerializeField] private PcmAudioPlayer     audioPlayer;

        private enum ImageDetail { Low, High }
        [Header("Vision (Image Input)")]
        [SerializeField] private ImageDetail imageDetail = ImageDetail.High;

        [Header("Thinking SFX")]
        [SerializeField] private AudioSource thinkingAudioSource;
        [SerializeField] private AudioClip thinkingLoopClip;
        [Range(0f, 1f)]
        [SerializeField] private float thinkingLoopVolume = 0.25f;

        [Header("Unity Events")]
        public UnityEvent<string> onAgentTranscript;
        public UnityEvent<string> onUserTranscript;
        public UnityEvent<bool>   onUserSpeaking;
        public UnityEvent<bool>   onAgentSpeaking;
        public UnityEvent<string> onConnectionState;
        public UnityEvent<string> onKnowledgeStatus;

        #endregion

        #region Internals

        private WebSocket _ws;
        private bool      _sessionReady;
        private bool      _agentCurrentlySpeaking;
        private bool      _responseActive;
        private bool      _responseCreateSent;
        private double    _lastCreateSentTime;
        private const double MinCreateIntervalSec = 0.2;

        private readonly ToolCallState _toolCalls = new ();
        private int _thinkingDepth;
        private bool _starting;
        private bool _shuttingDown;
        private double _reconnectAt = double.PositiveInfinity;
        private int _reconnectAttempts;
        private readonly HashSet<string> _executingToolCalls = new ();
        private int _toolsInFlight;
        private bool _toolContinuationPending;
        private int _turnGeneration;
        private bool _terminalRelayError;
        private string _workerPhone;
        private bool _isDirectOpenAIFallback;
        private int _relayFailures = 0;
        private ArtureRelaySettings _activeSettings;
        public string WorkerName { get; private set; } = "Unassigned";
        public string KnowledgeStatus { get; private set; } = "idle";
        public string KnowledgeSourcesSummary { get; private set; } = string.Empty;
        public bool IsDirectOpenAIFallback => _isDirectOpenAIFallback;
        public string ActiveRelayUrl => _activeSettings?.url;
        public string CloudRelayUrl => _activeSettings?.cloudUrl ?? "wss://arture-voice-relay.onrender.com/realtime";
        public bool IsSessionReady => _sessionReady;

        #endregion

        #region Unity lifecycle

        private void Start()
        {
            if (startOnAwake) StartAgent();
        }

        private void Update()
        {
            _ws?.DispatchMessageQueue();
            TryContinueAfterTools();
            if (!_shuttingDown && !_starting && _ws == null && Time.realtimeSinceStartupAsDouble >= _reconnectAt)
                StartAgent();
        }

        private async void OnDisable()         => await Shutdown();
        private async void OnApplicationQuit() => await Shutdown();

        #endregion

        #region Public API
        
        /// <summary>
        /// Used to get image url added into conversation for image analysis
        /// Check: https://openai.com/index/introducing-gpt-realtime/
        /// </summary>
        public static event Func<string> OnRequestImage; 
        
        
        /// <summary>
        /// Call this from any GameObject to start talking to the agent
        /// Normally this is called on start if `startOnAwake` is set to true
        /// </summary>
        public async void StartAgent()
        {
            if (_starting || _ws != null) return;
            _starting = true;
            _shuttingDown = false;
            _terminalRelayError = false;
            _reconnectAt = double.PositiveInfinity;
            try
            {
                InitializeWebSocket();
                RegisterSocketCallbacks();
                if (micStreamer != null) { micStreamer.OnAudioChunk -= SendMicChunk; micStreamer.OnAudioChunk += SendMicChunk; }
                onConnectionState?.Invoke("connecting");
                await _ws.Connect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Arture] Connection attempt failed ({ex.GetType().Name}): {ex.Message}");
                _ws = null;
                _relayFailures++;
                if (!_isDirectOpenAIFallback && !(_activeSettings != null && _activeSettings.isDirectOpenAI) && _relayFailures >= 3 && !string.IsNullOrWhiteSpace(_activeSettings?.openAIApiKey))
                {
                    Debug.LogWarning("[Arture] Live backend relay unreachable after 3 attempts. Falling back directly to OpenAI Realtime API...");
                    _isDirectOpenAIFallback = true;
                    _starting = false;
                    StartAgent();
                    return;
                }
                ScheduleReconnect();
            }
            finally { _starting = false; }
        }

        public void StopAgent() => _ = Shutdown();
        public void RequestResponse() => SendResponseCreate();
        public async void ForceReconnectRelay(string overrideUrl = null)
        {
            _isDirectOpenAIFallback = false;
            if (_activeSettings != null && !string.IsNullOrWhiteSpace(overrideUrl))
            {
                _activeSettings.url = overrideUrl;
                _activeSettings.deviceUrl = overrideUrl;
            }
            await Shutdown();
            _starting = false;
            StartAgent();
        }

        public void IdentifyWorker(string phone)
        {
            _workerPhone = phone ?? "";
            if (_ws != null && _ws.State == WebSocketState.Open && !string.IsNullOrWhiteSpace(_workerPhone))
                _ = _ws.SendText(new JObject { ["type"] = "arture.identify", ["phone"] = _workerPhone }.ToString(Formatting.None));
        }

        private void ScheduleReconnect()
        {
            _sessionReady = false;
            _responseActive = false;
            _responseCreateSent = false;
            _toolContinuationPending = false;
            onConnectionState?.Invoke(_terminalRelayError ? "error" : "disconnected");
            if (_shuttingDown || _terminalRelayError || !reconnectAutomatically || !isActiveAndEnabled) return;
            _reconnectAt = Time.realtimeSinceStartupAsDouble + Math.Min(15, Math.Pow(2, Math.Min(_reconnectAttempts++, 4)));
        }

        /// <summary>
        /// Force stop agent
        /// Used it to cancel any interaction and close the socket immediately
        /// </summary>
        public void InterruptNow()
        {
            TryCancelAssistantResponse();
        }

        #endregion

        #region Shutdown

        private async Task Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            _reconnectAt = double.PositiveInfinity;
            _sessionReady = false;
            try
            {
                if (micStreamer != null) { micStreamer.OnAudioChunk -= SendMicChunk; micStreamer.StopStreaming(); }
                if (audioPlayer != null) audioPlayer.StopImmediately();
                StopThinkingSfx(true);

                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open)
                        await _ws.SendText("{\"type\":\"arture.session.end\"}");
                    await _ws.Close();
                    _ws = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenAI] Shutdown error: {ex}");
            }
        }

        #endregion

        #region WebSocket receive

        private void OnSocketMessage(byte[] raw)
        {
            var text = Encoding.UTF8.GetString(raw);

            BaseEvent baseEvt;
            try { baseEvt = JsonConvert.DeserializeObject<BaseEvent>(text); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAI] Failed to parse event: {ex}\n{text}");
                return;
            }

            switch (baseEvt?.Type)
            {
                case "arture.knowledge":
                    var knowledge = JObject.Parse(text);
                    KnowledgeStatus = knowledge.Value<string>("status") ?? "unavailable";
                    var sourceLabels = new System.Collections.Generic.List<string>();
                    if (knowledge["sources"] is JArray sources)
                        foreach (var source in sources)
                            sourceLabels.Add($"{(source.Value<string>("originalName") ?? "Manual").Replace("<", "‹").Replace(">", "›")} · Page {source.Value<int>("page")}");
                    KnowledgeSourcesSummary = string.Join("\n", sourceLabels);
                    onKnowledgeStatus?.Invoke(KnowledgeStatus);
                    break;
                case "arture.worker":
                    var worker = JObject.Parse(text);
                    WorkerName = worker.Value<bool>("matched") ? worker.Value<string>("name") ?? "Unassigned" : "Unassigned";
                    break;
                case "session.created":
                    HandleSessionCreated();
                    break;

                case "session.updated":
                    HandleSessionUpdated();
                    break;

                case "input_audio_buffer.speech_started":
                    HandleSpeechStarted();
                    break;

                case "input_audio_buffer.speech_stopped":
                    HandleSpeechStopped();
                    break;

                case "input_audio_buffer.committed":
                    HandleInputCommitted();
                    break;

                case "conversation.item.input_audio_transcription.delta":
                    HandleInputTranscriptDelta(text);
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    HandleInputTranscriptCompleted(text);
                    break;

                case "conversation.item.created":
                    HandleConversationItemCreated();
                    break;

                case "response.audio.delta":
                case "response.output_audio.delta":
                    HandleResponseAudioDelta(text);
                    break;

                case "response.audio.done":
                case "response.output_audio.done":
                    HandleResponseAudioDone();
                    break;

                case "response.audio_transcript.delta":
                case "response.output_audio_transcript.delta":
                    HandleResponseTranscriptDelta(text);
                    break;

                case "response.audio_transcript.done":
                case "response.output_audio_transcript.done":
                case "response.content_part.added":
                case "response.content_part.done":
                    HandleNoop();
                    break;

                case "response.created":
                    HandleResponseCreated();
                    break;

                case "response.done":
                case "response.cancelled":
                    HandleResponseFinished();
                    break;

                case "response.function_call_arguments.delta":
                    HandleFunctionArgsDelta(text);
                    break;

                case "response.function_call_arguments.done":
                    HandleNoop();
                    break;

                case "response.output_item.added":
                    HandleOutputItemAdded(text);
                    break;

                case "response.output_item.done":
                    HandleOutputItemDone(text);
                    break;

                case "rate_limits.updated":
                    HandleNoop();
                    break;

                case "error":
                    HandleError(text);
                    break;

                default:
                    Debug.Log($"[OpenAI] Unhandled event type: {baseEvt?.Type}");
                    break;
            }
        }

        #endregion

        #region WebSocket send

        private void SendMicChunk(string b64)
        {
            if (!_sessionReady || _ws == null || _ws.State != WebSocketState.Open) return;

            var payload = new Dictionary<string, object>
            {
                { "type",  "input_audio_buffer.append" },
                { "audio", b64 }
            };

            _ = _ws.SendText(JsonConvert.SerializeObject(payload));
        }

        private void SendSessionUpdate()
        {
            if (_isDirectOpenAIFallback || (_activeSettings != null && _activeSettings.isDirectOpenAI))
            {
                var enrichedPrompt = systemPrompt ?? string.Empty;
                if (!enrichedPrompt.Contains("Centrifugal Water Pump"))
                {
                    enrichedPrompt += "\nYou are the Arture AR assistant for the Centrifugal Water Pump (asset ATR-DEMO-PUMP-001). Guide the technician through maintenance, troubleshooting, and inspection. Use the available AR tools (Machine_HighlightComponent, Machine_ShowAnswer, Machine_ExplodeView, Machine_RotatePump) to visually illuminate and manipulate pump components in augmented reality whenever explaining them.";
                }

                var directSession = new JObject
                {
                    ["type"] = "realtime",
                    ["instructions"] = enrichedPrompt,
                    ["audio"] = new JObject
                    {
                        ["input"] = new JObject
                        {
                            ["format"] = new JObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                            ["transcription"] = new JObject { ["model"] = "whisper-1" },
                            ["turn_detection"] = new JObject
                            {
                                ["type"] = "server_vad",
                                ["threshold"] = 0.5,
                                ["prefix_padding_ms"] = 300,
                                ["silence_duration_ms"] = 500,
                                ["create_response"] = false,
                                ["interrupt_response"] = true
                            }
                        },
                        ["output"] = new JObject
                        {
                            ["format"] = new JObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                            ["voice"] = ResolveVoiceString()
                        }
                    },
                    ["tools"] = AgentToolRegistry.GetToolsSpec()
                };

                var directUpd = new JObject { ["type"] = "session.update", ["session"] = directSession };
                _ = _ws.SendText(directUpd.ToString(Formatting.None));
                return;
            }

            // server VAD on, but we create responses ourselves at speech stop
            var session = new JObject
            {
                ["type"] = "realtime",
                ["instructions"] = systemPrompt ?? string.Empty,
                ["audio"] = new JObject
                {
                    ["input"] = new JObject
                    {
                        ["format"] = new JObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                        ["transcription"] = new JObject { ["model"] = "gpt-live-transcribe", ["language"] = "en" },
                        ["turn_detection"] = new JObject { ["type"] = "server_vad", ["create_response"] = false, ["interrupt_response"] = true }
                    },
                    ["output"] = new JObject
                    {
                        ["format"] = new JObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                        ["voice"] = ResolveVoiceString()
                    }
                },
                ["tools"] = AgentToolRegistry.GetToolsSpec()
            };

            var upd = new JObject { ["type"] = "session.update", ["session"] = session };
            _ = _ws.SendText(upd.ToString(Formatting.None));
        }

        private void SendResponseCreate()
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            if (_responseActive || _responseCreateSent) return;

            var now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastCreateSentTime < MinCreateIntervalSec) return;

            _lastCreateSentTime = now;
            _responseCreateSent = true;

            var create = new JObject { ["type"] = "response.create" };
            _ = _ws.SendText(create.ToString(Formatting.None));
        }

        private void TryCancelAssistantResponse()
        {
            _turnGeneration++;
            _toolContinuationPending = false;
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            var cancel = new JObject { ["type"] = "response.cancel" };
            _ = _ws.SendText(cancel.ToString(Formatting.None));

            audioPlayer.StopImmediately();
            StopThinkingSfx(true);

            if (_agentCurrentlySpeaking)
            {
                _agentCurrentlySpeaking = false;
                onAgentSpeaking?.Invoke(false);
            }
            _responseActive = false;
            _responseCreateSent = false;
        }
        
        private static string GetImageDataUrlAsync()
        {
            if (OnRequestImage == null) return null;
            
            try
            {
                foreach (var d in OnRequestImage.GetInvocationList())
                {
                    var fn = (Func<string>)d;
                    var s = fn();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAI] OnRequestImage failed: {ex.Message}");
            }
            
            return null;
        }

        #endregion

        #region Tool execution

        private async Task ExecuteToolCallIfReady(string callId)
        {
            if (!_executingToolCalls.Add(callId)) return;
            var sourceSocket = _ws;
            var generation = _turnGeneration;
            _toolsInFlight++;
            try
            {
                if (!_toolCalls.TryGetName(callId, out var toolName))
                {
                    Debug.LogWarning($"[OpenAI] function_call done without known name (call_id={callId})");
                    StopThinkingSfx();
                    return;
                }

                var argsJson = _toolCalls.GetArgsJson(callId) ?? "{}";

                if (!AgentToolRegistry.TryGetHandler(toolName, out var handler))
                {
                    Debug.LogWarning($"[OpenAI] Tool not registered: {toolName}");
                    await SendToolOutputAndContinue(callId, "", sourceSocket, generation);
                    return;
                }

                JObject args;
                try { args = string.IsNullOrWhiteSpace(argsJson) ? new JObject() : JObject.Parse(argsJson); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OpenAI] Tool args parse failed for {toolName}: {ex.Message}\n{argsJson}");
                    args = new JObject();
                }

                if (toolName == "Machine_ShowAnswer")
                {
                    if (KnowledgeStatus == "retrieved" && !string.IsNullOrWhiteSpace(KnowledgeSourcesSummary))
                        args["answer"] = (args.Value<string>("answer") ?? string.Empty) + "\n\nRetrieved references:\n" + KnowledgeSourcesSummary;
                    else if (KnowledgeStatus == "no_sources" || KnowledgeStatus == "unavailable")
                        args["answer"] = KnowledgeStatus == "no_sources"
                            ? "The uploaded manuals do not provide supporting passages for this question. Add a relevant manual or clarify the question."
                            : "The manual search is unavailable. Please try again before relying on an operating procedure.";
                }

                object resultAny = await handler(args);

                string outputStr;
                if (resultAny == null) outputStr = string.Empty;
                else if (resultAny is string s) outputStr = s;
                else if (resultAny is JToken jt) outputStr = jt.ToString(Formatting.None);
                else outputStr = JsonConvert.SerializeObject(resultAny, Formatting.None);

                await SendToolOutputAndContinue(callId, outputStr, sourceSocket, generation);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OpenAI] Tool execution failed: {ex}");
                await SendToolOutputAndContinue(callId, JsonConvert.SerializeObject(new { ok = false, error = ex.Message }), sourceSocket, generation);
            }
            finally
            {
                _toolCalls.Clear(callId);
                StopThinkingSfx();
                _toolsInFlight--;
                TryContinueAfterTools();
            }
        }

        private async Task SendToolOutputAndContinue(string callId, string outputStr, WebSocket sourceSocket, int generation)
        {
            if (!_sessionReady || _ws != sourceSocket || generation != _turnGeneration || _ws == null || _ws.State != WebSocketState.Open) return;

            var toolResultItem = new JObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JObject
                {
                    ["type"]    = "function_call_output",
                    ["call_id"] = callId ?? string.Empty,
                    ["output"]  = outputStr ?? string.Empty
                }
            };
            await _ws.SendText(toolResultItem.ToString(Formatting.None));
            if (_ws == sourceSocket && generation == _turnGeneration) _toolContinuationPending = true;
        }

        private void TryContinueAfterTools()
        {
            if (!_toolContinuationPending || !_sessionReady || _toolsInFlight > 0 || _responseActive || _responseCreateSent) return;
            if (Time.realtimeSinceStartupAsDouble - _lastCreateSentTime < MinCreateIntervalSec) return;
            _toolContinuationPending = false;
            SendResponseCreate();
        }

        private async Task MaybeSendImageItemAsync()
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            var dataUrl = GetImageDataUrlAsync();
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                return;
            }

            var addImage = new JObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JObject
                {
                    ["type"]   = "message",
                    ["role"]   = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "input_image",
                            ["image_url"] = dataUrl,
                            ["detail"] = imageDetail == ImageDetail.High ? "high" : "low"
                        }
                    }
                }
            };

            await _ws.SendText(addImage.ToString(Formatting.None));
        }
        
        #endregion

        #region Helpers

        private string ResolveVoiceString()
        {
            switch (voice)
            {
                case Voice.Alloy:  return "alloy";
                case Voice.Verse:  return "verse";
                case Voice.Custom: return string.IsNullOrWhiteSpace(customVoice) ? "alloy" : customVoice.Trim();
                default:           return "alloy";
            }
        }

        private void StartThinkingSfx()
        {
            _thinkingDepth++;
            if (thinkingAudioSource == null || thinkingLoopClip == null) return;

            if (!thinkingAudioSource.isPlaying)
            {
                thinkingAudioSource.clip   = thinkingLoopClip;
                thinkingAudioSource.loop   = true;
                thinkingAudioSource.volume = thinkingLoopVolume;
                thinkingAudioSource.Play();
            }
        }

        private void StopThinkingSfx(bool forceStopAll = false)
        {
            if (forceStopAll) _thinkingDepth = 0;
            else _thinkingDepth = Mathf.Max(0, _thinkingDepth - 1);

            if (_thinkingDepth == 0 && thinkingAudioSource != null && thinkingAudioSource.isPlaying)
            {
                thinkingAudioSource.Stop();
            }
        }

        #endregion

        #region Socket helpers

        private void InitializeWebSocket()
        {
            _activeSettings = ArtureRelaySettings.Load(config);
            if (!string.IsNullOrWhiteSpace(_activeSettings.workerPhone)) _workerPhone = _activeSettings.workerPhone;

            string url;
            Dictionary<string, string> headers;

            if (_isDirectOpenAIFallback || _activeSettings.isDirectOpenAI)
            {
                var directUrl = !string.IsNullOrWhiteSpace(_activeSettings.directOpenAIUrl)
                    ? _activeSettings.directOpenAIUrl
                    : "wss://api.openai.com/v1/realtime";
                var separator = directUrl.Contains("?") ? "&" : "?";
                url = $"{directUrl}{separator}model={Uri.EscapeDataString(model)}";

                var key = !string.IsNullOrWhiteSpace(_activeSettings.openAIApiKey)
                    ? _activeSettings.openAIApiKey
                    : _activeSettings.accessToken;

                headers = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {key}" }
                };
                Debug.Log($"[Arture] Connecting directly to OpenAI Realtime: {directUrl} (Model: {model})");
                onKnowledgeStatus?.Invoke("direct_openai");
            }
            else
            {
                var separator = _activeSettings.url.Contains("?") ? "&" : "?";
                url = $"{_activeSettings.url}{separator}assetCode={Uri.EscapeDataString(_activeSettings.assetCode)}";
                headers = new Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {_activeSettings.accessToken}" }
                };
                Debug.Log($"[Arture] Connecting to Arture Voice Relay: {_activeSettings.url} (Asset: {_activeSettings.assetCode})");
            }

            _ws = new WebSocket(url, headers);
        }

        private void RegisterSocketCallbacks()
        {
            var socket = _ws;
            socket.OnOpen += () =>
            {
                _relayFailures = 0;
                WorkerName = "Unassigned";
                KnowledgeStatus = (_isDirectOpenAIFallback || (_activeSettings != null && _activeSettings.isDirectOpenAI)) ? "direct_openai" : "idle";
                KnowledgeSourcesSummary = string.Empty;
                if (!_isDirectOpenAIFallback && !(_activeSettings != null && _activeSettings.isDirectOpenAI))
                {
                    IdentifyWorker(_workerPhone);
                    Debug.Log("[Arture] Relay socket open; waiting for voice session setup.");
                }
                else
                {
                    Debug.Log("[Arture] Direct OpenAI socket open; waiting for voice session setup.");
                }
            };
            socket.OnClose += code =>
            {
                if (_ws != socket) return;
                _ws = null;
                if (audioPlayer != null) audioPlayer.StopImmediately();
                StopThinkingSfx(true);
                _agentCurrentlySpeaking = false;
                onAgentSpeaking?.Invoke(false);

                // Retry relay first so Render cold-starts and live logging work
                if (!_sessionReady && !_isDirectOpenAIFallback && !(_activeSettings != null && _activeSettings.isDirectOpenAI))
                {
                    _relayFailures++;
                    if (_relayFailures >= 3 && !string.IsNullOrWhiteSpace(_activeSettings?.openAIApiKey))
                    {
                        Debug.LogWarning("[Arture] Relay closed before session ready (3 attempts). Falling back directly to OpenAI Realtime API...");
                        _isDirectOpenAIFallback = true;
                        StartAgent();
                        return;
                    }
                }

                ScheduleReconnect();
                Debug.Log($"[Arture] Socket disconnected ({code}).");
            };
            socket.OnError += err =>
            {
                Debug.LogError($"[Arture] Transport error: {err}");
                if (!_sessionReady && !_isDirectOpenAIFallback && !(_activeSettings != null && _activeSettings.isDirectOpenAI))
                {
                    _relayFailures++;
                    if (_relayFailures >= 3 && !string.IsNullOrWhiteSpace(_activeSettings?.openAIApiKey))
                    {
                        Debug.LogWarning("[Arture] Relay transport error during connection (3 attempts). Falling back directly to OpenAI Realtime API...");
                        _isDirectOpenAIFallback = true;
                        _ws = null;
                        StartAgent();
                    }
                }
            };
            _ws.OnMessage += OnSocketMessage;
        }

        #endregion

        #region Event handlers (parsed)

        private void HandleSessionCreated()
        {
            SendSessionUpdate();
        }

        private void HandleSessionUpdated()
        {
            if (_sessionReady) return;
            _sessionReady = true;
            _turnGeneration++;
            _reconnectAttempts = 0;
            _executingToolCalls.Clear();
            _toolCalls.ClearAll();
            onConnectionState?.Invoke("connected");
            if (micStreamer != null && micStreamer.enabled) micStreamer.StartStreaming();
        }

        private void HandleSpeechStarted() => onUserSpeaking?.Invoke(true);

        private async void HandleSpeechStopped()
        {
            onUserSpeaking?.Invoke(false);

            // Server VAD already commits this turn. PTT commits once in the microphone starter.
            await MaybeSendImageItemAsync();
            SendResponseCreate();
        }

        private static void HandleInputCommitted() { }

        private void HandleInputTranscriptDelta(string json)
        {
            try
            {
                var jo = JObject.Parse(json);
                var delta = jo["delta"]?.ToString();
                if (!string.IsNullOrEmpty(delta)) onUserTranscript?.Invoke(delta);
            } catch {}
        }

        private void HandleInputTranscriptCompleted(string json)
        {
            try
            {
                var user = JsonConvert.DeserializeObject<InputAudioTranscriptDone>(json);
                onUserTranscript?.Invoke(user?.Transcript ?? user?.Text ?? "");
            } catch {}
        }

        private static void HandleConversationItemCreated() { }

        private void HandleResponseAudioDelta(string json)
        {
            if (!_agentCurrentlySpeaking)
            {
                _agentCurrentlySpeaking = true;
                onAgentSpeaking?.Invoke(true);
            }
            try
            {
                var audio = JsonConvert.DeserializeObject<ResponseAudioDelta>(json);
                if (!string.IsNullOrEmpty(audio?.DeltaBase64))
                    audioPlayer.EnqueueBase64Audio(audio.DeltaBase64);
            } catch { 
                // ignore
            }
        }

        private void HandleResponseAudioDone()
        {
            _agentCurrentlySpeaking = false;
            onAgentSpeaking?.Invoke(false);
        }

        private void HandleResponseTranscriptDelta(string json)
        {
            try
            {
                var tx = JsonConvert.DeserializeObject<ResponseAudioTranscriptDelta>(json);
                if (!string.IsNullOrEmpty(tx?.DeltaText))
                    onAgentTranscript?.Invoke(tx.DeltaText);
            } catch {}
        }

        private void HandleResponseCreated()
        {
            _responseActive = true;
            _responseCreateSent = false;
        }

        private void HandleResponseFinished()
        {
            _responseActive = false;
            _responseCreateSent = false;
        }

        private void HandleFunctionArgsDelta(string json)
        {
            try
            {
                var obj    = JObject.Parse(json);
                var callId = obj["call_id"]?.ToString();
                var delta  = obj["delta"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(callId)) _toolCalls.AppendArgs(callId, delta);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAI] parse arguments.delta failed: {ex}");
            }
        }

        private void HandleOutputItemAdded(string json)
        {
            try
            {
                var obj  = JObject.Parse(json);
                var item = obj["item"] as JObject;
                if (item?["type"]?.ToString() == "function_call")
                {
                    var callId = item["call_id"]?.ToString();
                    var name   = item["name"]?.ToString();
                    if (!string.IsNullOrEmpty(callId) && !string.IsNullOrEmpty(name))
                        _toolCalls.SetToolName(callId, name);

                    StartThinkingSfx();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAI] parse output_item.added failed: {ex}");
            }
        }

        private void HandleOutputItemDone(string json)
        {
            try
            {
                var obj  = JObject.Parse(json);
                var item = obj["item"] as JObject;
                if (item?["type"]?.ToString() == "function_call")
                {
                    var callId = item["call_id"]?.ToString();
                    if (!string.IsNullOrEmpty(callId))
                    {
                        _toolCalls.SetToolName(callId, item["name"]?.ToString());
                        if (item["arguments"] != null) _toolCalls.SetArgsJson(callId, item["arguments"].ToString());
                        _ = ExecuteToolCallIfReady(callId);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAI] parse output_item.done failed: {ex}");
            }
        }

        private void HandleError(string json)
        {
            var err = JsonConvert.DeserializeObject<ErrorEvent>(json);
            var code = err?.Error?.Code    ?? "(no code)";
            var msg  = err?.Error?.Message ?? "(no message)";
            if (code.Contains("invalid_api_key") || code.Contains("insufficient_quota") || code.Contains("model_not_found") || code == "openai_http_401" || code == "openai_http_403")
            {
                _terminalRelayError = true;
                _reconnectAt = double.PositiveInfinity;
                onConnectionState?.Invoke("error");
            }
            
            // Avoid the warnings while agent is speaking.
            if (code == "input_audio_buffer_commit_empty")
            {
                return;
            }
            
            Debug.LogError($"[OpenAI] ERROR {code}: {msg}");
            if (code == "conversation_already_has_active_response")
            {
                _responseActive = true;
                _responseCreateSent = false;
            }
            StopThinkingSfx();
        }

        private static void HandleNoop() { }

        #endregion
    }
}
