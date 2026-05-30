using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAIReminderParser : MonoBehaviour
{
    [SerializeField] private string openAIApiKey;

    private const string ENDPOINT = "https://api.openai.com/v1/chat/completions";

    public IEnumerator ParseReminder(
        string transcript,
        Action<ReminderParseResponse> onComplete)
    {
        string prompt =
$@"Extract reminder information from this sentence.

Sentence:
""{transcript}""

Return ONLY valid JSON:
{{
    ""task"": ""string"",
    ""time"": ""ISO datetime"",
    ""recurrence"": ""none|daily|weekly|monthly""
}}

Examples:

Input:
""Remind me to take meds every day at 8 AM""

Output:
{{
    ""task"": ""take meds"",
    ""time"": ""2026-05-27T08:00:00"",
    ""recurrence"": ""daily""
}}";

        string body =
@"{
    ""model"": ""gpt-4.1-mini"",
    ""messages"": [
        {
            ""role"": ""user"",
            ""content"": """ + EscapeJson(prompt) + @"""
        }
    ],
    ""temperature"": 0
}";

        using UnityWebRequest request =
            new UnityWebRequest(ENDPOINT, "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(body);

        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(request.error);
            yield break;
        }

        string response = request.downloadHandler.text;

        ChatCompletionResponse parsed =
            JsonUtility.FromJson<ChatCompletionResponse>(response);

        string content =
            parsed.choices[0].message.content;

        ReminderParseResponse reminder =
            JsonUtility.FromJson<ReminderParseResponse>(content);

        onComplete?.Invoke(reminder);
    }

    private string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
    }
}