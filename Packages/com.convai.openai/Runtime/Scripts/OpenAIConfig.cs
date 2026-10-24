using UnityEngine;

namespace OpenAI
{
    [CreateAssetMenu(menuName = "OpenAI/Config", fileName = "OpenAIConfig")]
    public class OpenAIConfig : ScriptableObject
    {
        [Tooltip("Arture backend address. Use wss:// for a deployed relay.")]
        public string relayWebsocketUrl = "ws://127.0.0.1:3101/realtime";
        [Tooltip("Arture app credential, never an OpenAI API key. Local builds can use Resources/ArtureRelayConnection.json.")]
        public string relayAccessToken;
        public string assetCode = "ATR-DEMO-PUMP-001";
    }
}
