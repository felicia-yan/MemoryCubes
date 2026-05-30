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

    private IEnumerator ReminderCoroutine(
        ReminderManager.Reminder reminder)
    {
        while (true)
        {
            TimeSpan delay =
                reminder.triggerTime - DateTime.Now;

            if (delay.TotalSeconds > 0)
            {
                Debug.Log(
                    $"Reminder scheduled in {delay.TotalSeconds} seconds");

                yield return new WaitForSeconds(
                    (float)delay.TotalSeconds);
            }

            TriggerReminder(reminder);

            switch (reminder.recurrence)
            {
                case "daily":
                    reminder.triggerTime =
                        reminder.triggerTime.AddDays(1);
                    break;

                case "weekly":
                    reminder.triggerTime =
                        reminder.triggerTime.AddDays(7);
                    break;

                case "monthly":
                    reminder.triggerTime =
                        reminder.triggerTime.AddMonths(1);
                    break;

                default:
                    yield break;
            }
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