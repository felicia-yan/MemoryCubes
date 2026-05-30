using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityAIAPITutorial
{
    public class GPTClient : MonoBehaviour
    {
        private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

        [SerializeField] private OpenAISettings settings;
        [SerializeField] private int timeoutSeconds = 30;
        [SerializeField] private bool logRawJson;

        public bool IsConfigured
        {
            get
            {
                return settings != null
                       && !string.IsNullOrWhiteSpace(settings.ApiKey)
                       && settings.ApiKey != "PASTE_YOUR_OPENAI_API_KEY_HERE";
            }
        }

        public void Ask(string userPrompt, Action<string> onSuccess, Action<string> onError)
        {
            if (!IsConfigured)
            {
                onError?.Invoke("No OpenAI API key configured. Assign an OpenAISettings asset first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                onError?.Invoke("Please enter a question before calling the API.");
                return;
            }

            StartCoroutine(SendPrompt(userPrompt, onSuccess, onError));
        }

        public void AskAboutImage(
            Texture2D image,
            string userPrompt,
            Action<string> onSuccess,
            Action<string> onError,
            string instructionsOverride = null)
        {
            if (!IsConfigured)
            {
                onError?.Invoke("No OpenAI API key configured. Assign an OpenAISettings asset first.");
                return;
            }

            if (image == null)
            {
                onError?.Invoke("No image assigned. Add a Texture2D image in the Inspector first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                onError?.Invoke("Please enter a question about the image.");
                return;
            }

            if (!TryBuildImageDataUrl(image, out string imageDataUrl, out string errorMessage))
            {
                onError?.Invoke(errorMessage);
                return;
            }

            AskAboutImageUrl(imageDataUrl, userPrompt, onSuccess, onError, instructionsOverride);
        }

        public void AskAboutImageUrl(
            string imageUrl,
            string userPrompt,
            Action<string> onSuccess,
            Action<string> onError,
            string instructionsOverride = null)
        {
            if (!IsConfigured)
            {
                onError?.Invoke("No OpenAI API key configured. Assign an OpenAISettings asset first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                onError?.Invoke("No image URL or image data URL provided.");
                return;
            }

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                onError?.Invoke("Please enter a question about the image.");
                return;
            }

            string jsonBody = BuildVisionRequestJson(userPrompt, imageUrl, instructionsOverride);
            StartCoroutine(SendRequestJson(jsonBody, onSuccess, onError));
        }

        private IEnumerator SendPrompt(string userPrompt, Action<string> onSuccess, Action<string> onError)
        {
            var requestBody = new ResponsesRequest
            {
                model = settings.Model,
                instructions = settings.Instructions,
                input = userPrompt,
                max_output_tokens = settings.MaxOutputTokens
            };

            string jsonBody = JsonUtility.ToJson(requestBody);
            yield return SendRequestJson(jsonBody, onSuccess, onError);
        }

        private IEnumerator SendRequestJson(string jsonBody, Action<string> onSuccess, Action<string> onError)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using (var request = new UnityWebRequest(ResponsesEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, timeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {settings.ApiKey}");

                yield return request.SendWebRequest();

                string responseText = request.downloadHandler.text;

                if (logRawJson)
                {
                    Debug.Log(responseText);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(BuildErrorMessage(request, responseText));
                    yield break;
                }

                string answer = TryExtractAnswer(responseText);
                if (string.IsNullOrWhiteSpace(answer))
                {
                    onError?.Invoke("The API returned a response, but no text answer was found.");
                    yield break;
                }

                onSuccess?.Invoke(answer.Trim());
            }
        }

        private string BuildVisionRequestJson(string userPrompt, string imageUrl, string instructionsOverride)
        {
            string instructions = string.IsNullOrWhiteSpace(instructionsOverride)
                ? settings.Instructions
                : instructionsOverride;

            var builder = new StringBuilder();
            builder.Append('{');
            builder.Append("\"model\":\"").Append(EscapeJson(settings.Model)).Append("\",");
            builder.Append("\"instructions\":\"").Append(EscapeJson(instructions)).Append("\",");
            builder.Append("\"max_output_tokens\":").Append(settings.MaxOutputTokens).Append(',');
            builder.Append("\"input\":[{");
            builder.Append("\"role\":\"user\",");
            builder.Append("\"content\":[");
            builder.Append('{');
            builder.Append("\"type\":\"input_text\",");
            builder.Append("\"text\":\"").Append(EscapeJson(userPrompt)).Append("\"");
            builder.Append("},");
            builder.Append('{');
            builder.Append("\"type\":\"input_image\",");
            builder.Append("\"image_url\":\"").Append(EscapeJson(imageUrl)).Append("\",");
            builder.Append("\"detail\":\"high\"");
            builder.Append('}');
            builder.Append(']');
            builder.Append("}]");
            builder.Append('}');

            return builder.ToString();
        }

        private static bool TryBuildImageDataUrl(Texture2D image, out string imageDataUrl, out string errorMessage)
        {
            imageDataUrl = string.Empty;
            errorMessage = string.Empty;

            try
            {
                byte[] imageBytes = EncodeTextureToJpg(image, 85);
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    errorMessage = "Could not encode the image.";
                    return false;
                }

                imageDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
                return true;
            }
            catch (Exception exception)
            {
                errorMessage =
                    "Could not prepare the image for the API request.\n" +
                    exception.Message;
                return false;
            }
        }

        private static byte[] EncodeTextureToJpg(Texture2D source, int quality)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                return Array.Empty<byte>();
            }

            RenderTexture previousActiveTexture = RenderTexture.active;
            RenderTexture renderTexture = null;
            Texture2D readableCopy = null;

            try
            {
                renderTexture = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);

                Graphics.Blit(source, renderTexture);
                RenderTexture.active = renderTexture;

                readableCopy = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                readableCopy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readableCopy.Apply();

                return readableCopy.EncodeToJPG(quality);
            }
            finally
            {
                RenderTexture.active = previousActiveTexture;

                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (readableCopy != null)
                {
                    Destroy(readableCopy);
                }
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);

            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private static string BuildErrorMessage(UnityWebRequest request, string responseText)
        {
            string message = $"HTTP {(long)request.responseCode}: {request.error}";

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                try
                {
                    var errorResponse = JsonUtility.FromJson<ErrorResponse>(responseText);
                    if (errorResponse?.error != null && !string.IsNullOrWhiteSpace(errorResponse.error.message))
                    {
                        return $"{message}\n{errorResponse.error.message}";
                    }
                }
                catch (Exception)
                {
                    // Fall through to the raw response text below.
                }

                return $"{message}\n{responseText}";
            }

            return message;
        }

        private static string TryExtractAnswer(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            try
            {
                var response = JsonUtility.FromJson<ResponsesApiResponse>(responseText);

                if (!string.IsNullOrWhiteSpace(response.output_text))
                {
                    return response.output_text;
                }

                if (response.output == null)
                {
                    return string.Empty;
                }

                foreach (var outputItem in response.output)
                {
                    if (outputItem?.content == null)
                    {
                        continue;
                    }

                    foreach (var contentItem in outputItem.content)
                    {
                        if (contentItem != null
                            && contentItem.type == "output_text"
                            && !string.IsNullOrWhiteSpace(contentItem.text))
                        {
                            return contentItem.text;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not parse OpenAI response: {exception.Message}");
            }

            return string.Empty;
        }

        [Serializable]
        private class ResponsesRequest
        {
            public string model;
            public string instructions;
            public string input;
            public int max_output_tokens;
        }

        [Serializable]
        private class ResponsesApiResponse
        {
            public string output_text;
            public OutputItem[] output;
        }

        [Serializable]
        private class OutputItem
        {
            public ContentItem[] content;
        }

        [Serializable]
        private class ContentItem
        {
            public string type;
            public string text;
        }

        [Serializable]
        private class ErrorResponse
        {
            public ApiError error;
        }

        [Serializable]
        private class ApiError
        {
            public string message;
            public string type;
            public string code;
        }
    }
}
