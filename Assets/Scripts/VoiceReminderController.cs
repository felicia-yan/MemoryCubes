using System;
using UnityEngine;

public class VoiceReminderController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private OpenAIReminderParser parser;

    [SerializeField]
    private ReminderManager reminderManager;

    [Header("Current Target Cube")]
    public int currentCubeId;

    // Call this from Meta Voice SDK transcription callback
    public void OnVoiceTranscript(string transcript)
    {
        Debug.Log($"Transcript: {transcript}");

        StartCoroutine(
            parser.ParseReminder(
                transcript,
                OnReminderParsed));
    }

    private void OnReminderParsed(
        ReminderParseResponse parsed)
    {
        if (parsed == null)
        {
            Debug.LogError(
                "Reminder parse returned null.");
            return;
        }

        Debug.Log(
            $"Parsed Reminder: {parsed.task}");

        DateTime parsedTime =
            DateTime.Parse(parsed.time);

        reminderManager.CreateReminder(
            currentCubeId,
            parsed.task,
            parsedTime,
            parsed.recurrence);
    }
}