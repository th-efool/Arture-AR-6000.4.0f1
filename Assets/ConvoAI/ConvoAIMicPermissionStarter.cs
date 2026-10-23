using System.Collections;
using System.Reflection;
using OpenAI;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Ensures Android runtime microphone permissions (RECORD_AUDIO) are granted
/// after ARCore's camera permission dialog completes before initializing
/// MicrophoneStreamer and starting RealtimeConversationManager.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ConvoAIMicPermissionStarter : MonoBehaviour
{
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

    private void Awake()
    {
        _conversationManager = GetComponent<RealtimeConversationManager>();
        if (_conversationManager == null)
            _conversationManager = FindAnyObjectByType<RealtimeConversationManager>();

        _micStreamer = GetComponent<MicrophoneStreamer>();
        if (_micStreamer == null)
            _micStreamer = FindAnyObjectByType<MicrophoneStreamer>();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Hold MicrophoneStreamer.Start() until RECORD_AUDIO is granted so Microphone.devices is valid.
        if (_micStreamer != null && !Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            _micStreamer.enabled = false;
        }
#endif
    }

    private IEnumerator Start()
    {
        if (_conversationManager == null)
        {
            Debug.LogWarning("[ConvoAI] RealtimeConversationManager not found in scene.");
            yield break;
        }

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

        // Ensure MicrophoneStreamer is enabled and has selected a valid microphone device.
        if (_micStreamer != null)
        {
            _micStreamer.enabled = true;

            var micDeviceField = typeof(MicrophoneStreamer).GetField("_micDevice", BindingFlags.Instance | BindingFlags.NonPublic);
            if (micDeviceField != null)
            {
                var currentDevice = micDeviceField.GetValue(_micStreamer) as string;
                if (string.IsNullOrEmpty(currentDevice) && Microphone.devices.Length > 0)
                {
                    micDeviceField.SetValue(_micStreamer, Microphone.devices[0]);
                }
            }
        }

        // Start the Realtime Conversation Agent if it hasn't connected yet.
        var wsField = typeof(RealtimeConversationManager).GetField("_ws", BindingFlags.Instance | BindingFlags.NonPublic);
        bool alreadyStarted = wsField != null && wsField.GetValue(_conversationManager) != null;

        if (!alreadyStarted)
        {
            Debug.Log("[ConvoAI] Microphone ready. Starting RealtimeConversationManager...");
            _conversationManager.StartAgent();
        }
    }
}
