using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityAIAPITutorial
{
    public class ImageQuestionPanelController : MonoBehaviour
    {
        [Header("API")]
        [SerializeField] private GPTClient gptClient;

        [Header("Image")]
        [SerializeField] private Texture2D imageToAskAbout;
        [Tooltip("Optional. If set, the API uses this URL instead of encoding the local texture.")]
        [SerializeField] private string imageUrl;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private bool preservePreviewAspect = true;

        [Header("Vision Prompting")]
        [TextArea(4, 8)]
        [SerializeField] private string visionInstructions =
            "You are a careful image understanding assistant. Answer only from visible evidence in the image. " +
            "If something is unclear, say that you are not sure. Do not invent objects or context outside the image. " +
            "Be concrete and mention visual details that support your answer.";

        [Header("UI")]
        [SerializeField] private TMP_InputField questionInput;
        [SerializeField] private Button askButton;
        [SerializeField] private TMP_Text answerText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text imageDebugText;

        [Header("Demo Defaults")]
        [TextArea(2, 4)]
        [SerializeField] private string starterQuestion =
            "What objects are visible in this image? Answer in one short paragraph.";
        [SerializeField] private string emptyAnswerMessage = "Ask a question about the image.";

        private void Awake()
        {
            if (askButton != null)
            {
                askButton.onClick.AddListener(AskAboutImage);
            }
        }

        private void Start()
        {
            if (questionInput != null && string.IsNullOrWhiteSpace(questionInput.text))
            {
                questionInput.text = starterQuestion;
            }

            RefreshImagePreview();
            SetAnswer(emptyAnswerMessage);
            SetStatus("Ready");
            SetLoading(false);
        }

        private void OnValidate()
        {
            RefreshImagePreview();
        }

        private void OnDestroy()
        {
            if (askButton != null)
            {
                askButton.onClick.RemoveListener(AskAboutImage);
            }
        }

        public void AskAboutImage()
        {
            if (gptClient == null)
            {
                ShowError("GPTClient is not assigned.");
                return;
            }

            if (questionInput == null)
            {
                ShowError("Question input is not assigned.");
                return;
            }

            string prompt = questionInput.text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ShowError("Type a question about the image first.");
                return;
            }

            SetLoading(true);
            SetStatus("Asking model about image...");
            SetAnswer("Waiting for the image understanding response...");
            Debug.Log($"Sending image question. {BuildImageDebugMessage()}");

            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                gptClient.AskAboutImageUrl(imageUrl, prompt, OnAnswerReceived, ShowError, visionInstructions);
                return;
            }

            gptClient.AskAboutImage(imageToAskAbout, prompt, OnAnswerReceived, ShowError, visionInstructions);
        }

        public void RefreshImagePreview()
        {
            if (previewImage != null)
            {
                previewImage.texture = imageToAskAbout;

                if (preservePreviewAspect && imageToAskAbout != null)
                {
                    var aspectRatioFitter = previewImage.GetComponent<AspectRatioFitter>();
                    if (aspectRatioFitter != null && imageToAskAbout.height > 0)
                    {
                        aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                        aspectRatioFitter.aspectRatio = (float)imageToAskAbout.width / imageToAskAbout.height;
                    }
                }
            }

            if (imageDebugText != null)
            {
                imageDebugText.text = BuildImageDebugMessage();
            }
        }

        private void OnAnswerReceived(string answer)
        {
            SetLoading(false);
            SetStatus("Done");
            SetAnswer(answer);
        }

        private void ShowError(string message)
        {
            SetLoading(false);
            SetStatus("Error");
            SetAnswer(message);
            Debug.LogWarning(message);
        }

        private void SetLoading(bool isLoading)
        {
            if (askButton != null)
            {
                askButton.interactable = !isLoading;
            }

            if (questionInput != null)
            {
                questionInput.interactable = !isLoading;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void SetAnswer(string message)
        {
            if (answerText != null)
            {
                answerText.text = message;
            }
        }

        private string BuildImageDebugMessage()
        {
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                return $"Image source: URL override\n{imageUrl}";
            }

            if (imageToAskAbout == null)
            {
                return "Image source: none assigned";
            }

            return $"Image source: local Texture2D\nName: {imageToAskAbout.name}\nSize: {imageToAskAbout.width} x {imageToAskAbout.height}\nReadable: {imageToAskAbout.isReadable}";
        }
    }
}
