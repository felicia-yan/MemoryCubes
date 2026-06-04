using System;
using System.Collections.Generic;
using Meta.XR;
using Oculus.Interaction.Input; 
using Oculus.Voice; 
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Meta.WitAi.Json;


public class VoiceManager : MonoBehaviour {
    [SerializeField] public AppVoiceExperience appVoice; 

    // Preview UI
    [SerializeField] private GameObject systemCanvas; 
    [SerializeField] private TextMeshProUGUI systemText; 
    [SerializeField] private TextMeshProUGUI parsedTaskText; 
    [SerializeField] private TextMeshProUGUI parsedTimeText; 
    [SerializeField] private TextMeshProUGUI parsedIconText; 
    [SerializeField] private Image background; 
    [SerializeField] private CanvasGroup reminderGroup; 

    // Save parsed information
    private string currentTask;
    private DateTimeOffset? currentReminderTime;
    private string currentIcon;
    private int currentCubeId; 

    // Cube colors
    private Dictionary<int, Color> cubeColors = new Dictionary<int, Color>()
    {
        { 0, new Color32(255, 247, 0, 170) },
        { 1, new Color32(255, 0, 220, 170) },
        { 2, new Color32(0, 180, 225, 170) },
        { 3, new Color32(170, 255, 0, 170) },
    };

    // Instantiating the reminder
    [SerializeField] private ReminderManager reminderManager;

    public event Action<int> OnReminderCreated;

    void Start() {
        appVoice.VoiceEvents.OnResponse.AddListener(OnWitResponse);
        systemCanvas.SetActive(false); 
    }

    void Update() {
        
    }

    // If no task captured, continue listening
    public void FailedParse() {
        ResetWit(); 
        systemText.text = "Sorry, I didn't catch that. Can you repeat it?";
        reminderGroup.alpha = 0f;  
    }

    // On parse, show the preview to the user to verify the parsed task and/or time
    private void OnWitResponse(WitResponseNode response) {
        string task = ""; 
        if (response["entities"]["task:task"].Count == 0) {
            FailedParse();
            return;
        }


        if (response["entities"]["task:task"].Count > 0)
        {
            task = response["entities"]["task:task"][0]["value"];
            if (!string.IsNullOrEmpty(task)) {
                task = char.ToUpper(task[0]) + task.Substring(1);
            }
            currentTask = task; 
        }

        string icon = ReminderManager.GetIconForTask(task); 
        currentIcon = icon; 

        string readableTime = "No time specified";

        if (response["entities"]["wit$datetime:datetime"].Count > 0)
        {
            string timeString = response["entities"]["wit$datetime:datetime"][0]["value"];

            DateTimeOffset dateTime = DateTimeOffset.Parse(timeString);
            currentReminderTime = dateTime; 

            // Format for readability
            readableTime = dateTime.LocalDateTime.ToString("h:mm tt");
        }

        parsedTaskText.text = task;
        parsedTimeText.text = readableTime;
        parsedIconText.text = icon; 

        reminderGroup.alpha = 1f;  

        systemText.text = "Give a thumbs up when you're happy with your reminder!";
    }

    public void ActivateWit() {
        systemCanvas.SetActive(true); 
        systemText.text = "Listening..."; 
        reminderGroup.alpha = 0f;  
        appVoice.Activate();
    }

    public void CancelListening() {
        if (appVoice.Active) {
            appVoice.Deactivate();
        }
        currentCubeId = -1; 
        systemCanvas.SetActive(false); 
    }

    // If user gave thumbs up and approved this reminder
    public void CompleteReminderCreation() {
        if (currentCubeId == -1)
            return;

        if (string.IsNullOrEmpty(currentTask))
            return;

        TimeSpan triggerTime = TimeSpan.Zero;

        if (currentReminderTime.HasValue) {
            triggerTime = currentReminderTime.Value.TimeOfDay;
        }

        reminderManager.CreateReminder(
            currentCubeId,
            currentTask,
            triggerTime,
            currentIcon);

        OnReminderCreated?.Invoke(currentCubeId);
        systemCanvas.SetActive(false);

        currentCubeId = -1;
        currentTask = "";
        currentIcon = "";
        currentReminderTime = null;
    }

    private void OnDestroy() {
        appVoice.VoiceEvents.OnResponse.RemoveListener(OnWitResponse);
    }

    public void BeginReminderCreation(int cubeId) {
        ActivateWit();
        currentCubeId = cubeId; 
        if (cubeColors.TryGetValue(cubeId, out Color color))
        {
            background.color = color;
        }
    }

    public void ResetWit() {
        appVoice.Deactivate();
        appVoice.Activate();
    }
}