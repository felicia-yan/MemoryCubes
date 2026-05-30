using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityAIAPITutorial
{
    public class TutorPanelController : MonoBehaviour
    {
        [Header("API")]
        [SerializeField] private GPTClient gptClient;

        [Header("UI")]
        [SerializeField] private TMP_InputField questionInput;
        [SerializeField] private Button askButton;
        [SerializeField] private TMP_Text answerText;
        [SerializeField] private TMP_Text statusText;

        [Header("Demo Defaults")]
        [TextArea(2, 4)]
        [SerializeField] private string starterQuestion =
            "Explain one practical way to use AI in a Unity VR training app.";
        [SerializeField] private string emptyAnswerMessage = "Ask a question to see the model response here.";

        private void Awake()
        {
            if (askButton != null)
            {
                askButton.onClick.AddListener(AskCurrentQuestion);
            }
        }

        private void Start()
        {
            if (questionInput != null && string.IsNullOrWhiteSpace(questionInput.text))
            {
                questionInput.text = starterQuestion;
            }

            SetAnswer(emptyAnswerMessage);
            SetStatus("Ready");
            SetLoading(false);
        }

        private void OnDestroy()
        {
            if (askButton != null)
            {
                askButton.onClick.RemoveListener(AskCurrentQuestion);
            }
        }

        public void AskCurrentQuestion()
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
                ShowError("Type a question first.");
                return;
            }

            SetLoading(true);
            SetStatus("Asking model...");
            SetAnswer("Waiting for the AI response...");

            gptClient.Ask(prompt, OnAnswerReceived, ShowError);
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
    }
}
