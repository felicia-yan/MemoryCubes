using UnityEngine;

namespace UnityAIAPITutorial
{
    [CreateAssetMenu(fileName = "OpenAISettings", menuName = "AI Tutorial/OpenAI Settings")]
    public class OpenAISettings : ScriptableObject
    {
        [Header("Teaching demo only")]
        [SerializeField] private string apiKey = "PASTE_YOUR_OPENAI_API_KEY_HERE";

        [Header("Model")]
        [SerializeField] private string model = "gpt-4.1-mini";
        [TextArea(3, 8)]
        [SerializeField] private string instructions =
            "You are a concise AI tutor inside a Unity VR app. Answer in 2-4 sentences. " +
            "Use beginner-friendly language and avoid long lists unless asked.";
        [SerializeField] private int maxOutputTokens = 300;

        public string ApiKey => apiKey;
        public string Model => model;
        public string Instructions => instructions;
        public int MaxOutputTokens => Mathf.Max(32, maxOutputTokens);
    }
}
