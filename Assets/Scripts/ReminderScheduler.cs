using System;
using System.Collections;
using UnityEngine;

public class ReminderScheduler : MonoBehaviour
{
    public static ReminderScheduler Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void ScheduleReminder(
        ReminderManager.Reminder reminder)
    {
        StartCoroutine(
            ReminderCoroutine(reminder));
    }

    private IEnumerator ReminderCoroutine(ReminderManager.Reminder reminder) {
        while (true) {
            TimeSpan now = DateTime.Now.TimeOfDay;

            TimeSpan delay = reminder.triggerTime - now;

            if (delay.TotalSeconds < 0)
                delay += TimeSpan.FromDays(1);

            yield return new WaitForSeconds(
                (float)delay.TotalSeconds);

            TriggerReminder(reminder);

            if (reminder.recurrence == "none")
                yield break;
        }
    }

    private void TriggerReminder(
        ReminderManager.Reminder reminder)
    {
        Debug.Log(
            $"REMINDER FIRED: {reminder.task}");

        // TODO:
        // play sound
        // pulse cube
        // vibration
        // Quest notification
    }
}