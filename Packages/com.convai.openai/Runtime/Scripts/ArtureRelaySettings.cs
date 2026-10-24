using System;
using UnityEngine;

namespace OpenAI
{
    [Serializable]
    public sealed class ArtureRelaySettings
    {
        public string url;
        public string deviceUrl;
        public string accessToken;
        public string assetCode;
        public string workerPhone;

        public static ArtureRelaySettings Load(OpenAIConfig config)
        {
            var settings = new ArtureRelaySettings
            {
                url = config != null ? config.relayWebsocketUrl : "ws://127.0.0.1:3101/realtime",
                accessToken = config != null ? config.relayAccessToken : "",
                assetCode = config != null ? config.assetCode : "ATR-DEMO-PUMP-001"
            };
            var local = Resources.Load<TextAsset>("ArtureRelayConnection");
            if (local != null)
            {
                var supplied = JsonUtility.FromJson<ArtureRelaySettings>(local.text);
                if (!string.IsNullOrWhiteSpace(supplied.url)) settings.url = supplied.url;
                if (!string.IsNullOrWhiteSpace(supplied.deviceUrl)) settings.deviceUrl = supplied.deviceUrl;
                if (!string.IsNullOrWhiteSpace(supplied.accessToken)) settings.accessToken = supplied.accessToken;
                if (!string.IsNullOrWhiteSpace(supplied.assetCode)) settings.assetCode = supplied.assetCode;
                if (!string.IsNullOrWhiteSpace(supplied.workerPhone)) settings.workerPhone = supplied.workerPhone;
            }
            if (Application.isMobilePlatform && !Application.isEditor && !string.IsNullOrWhiteSpace(settings.deviceUrl))
                settings.url = settings.deviceUrl;
#if UNITY_EDITOR
            var editorUrl = Environment.GetEnvironmentVariable("ARTURE_RELAY_URL");
            var editorToken = Environment.GetEnvironmentVariable("ARTURE_RELAY_ACCESS_KEY");
            if (!string.IsNullOrWhiteSpace(editorUrl)) settings.url = editorUrl;
            if (!string.IsNullOrWhiteSpace(editorToken)) settings.accessToken = editorToken;
#endif
            if (!Uri.TryCreate(settings.url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new InvalidOperationException("Configure an Arture ws:// or wss:// relay URL.");
            if (uri.Host.EndsWith("openai.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unity must connect to the Arture relay, not directly to OpenAI.");
            if (string.IsNullOrWhiteSpace(settings.accessToken) || settings.accessToken.Length < 24 || settings.accessToken.StartsWith("sk-"))
                throw new InvalidOperationException("Configure an Arture app credential. Do not put an OpenAI API key in Unity.");
            if (string.IsNullOrWhiteSpace(settings.assetCode))
                throw new InvalidOperationException("Configure the machine asset code.");
            return settings;
        }
    }
}
