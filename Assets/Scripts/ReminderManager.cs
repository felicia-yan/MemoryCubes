// ReminderManager - public methods to instantiate, edit, and delete reminders
// Called based on detected gestures. Reminders are world-space UI panels
// that spawn above the cube that triggered them and follow it each frame.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TryAR.MarkerTracking;

public class ReminderManager : MonoBehaviour
{
    [Serializable]
    public struct Reminder
    {
        public int cubeId;
        public Sprite icon;
        public string task;
        public DateTime time;
    }

    // Prefab to instantiate for each reminder.
    // Must have a Canvas (World Space), and child TMP text components for task + time.
    // See SetupReminderPrefab() comments below for expected hierarchy.
    [SerializeField] private GameObject reminderPrefab;

    // How far above the cube's position the panel floats (meters, world space)
    [SerializeField] private float heightOffset = 0.8f;

    // Active reminder instances, keyed by cube ID
    private Dictionary<int, GameObject> activeReminders = new Dictionary<int, GameObject>();

    // Reminder data, keyed by cube ID
    private Dictionary<int, Reminder> reminderData = new Dictionary<int, Reminder>();

    void Update()
    {
        // Keep each reminder panel anchored above its cube every frame,
        // and billboard it to face the main camera.
        foreach (var kvp in activeReminders)
        {
            int id = kvp.Key;
            GameObject panel = kvp.Value;

            if (panel == null) continue;

            // Look up the cube's current world position from the coordinator
            if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(id, out GameObject cubeObj)
                && cubeObj != null)
            {
                panel.transform.position = cubeObj.transform.position + Vector3.up * heightOffset;
            }

            // Billboard: rotate panel to face the camera (y-axis only keeps it upright)
            if (Camera.main != null)
            {
                Vector3 lookDir = panel.transform.position - Camera.main.transform.position;
                lookDir.y = 0f;
                if (lookDir != Vector3.zero)
                    panel.transform.rotation = Quaternion.LookRotation(lookDir);
            }
        }
    }

    public void CreateReminder(int cubeId)
    {
        if (reminderPrefab == null)
        {
            Debug.LogWarning("ReminderManager: reminderPrefab is not assigned.");
            return;
        }

        // If one already exists, just update it
        if (activeReminders.ContainsKey(cubeId))
        {
            EditReminder(cubeId, "Reminder", DateTime.Now);
            return;
        }

        // Placeholder data — swap for real user input later
        Reminder data = new Reminder
        {
            cubeId = cubeId,
            task   = "placeholder reminder text",
            time   = DateTime.Now,
            icon   = null
        };
        reminderData[cubeId] = data;

        // Spawn position: above the cube if it's on screen, else above this manager object
        Vector3 spawnPos = transform.position + Vector3.up * heightOffset;
        if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(cubeId, out GameObject cubeObj)
            && cubeObj != null)
        {
            spawnPos = cubeObj.transform.position + Vector3.up * heightOffset;
        }

        GameObject panel = Instantiate(reminderPrefab, spawnPos, Quaternion.identity);
        activeReminders[cubeId] = panel;

        // Force UI to appear on top of passthrough layer
        Canvas canvas = panel.GetComponent<Canvas>();
        canvas.sortingOrder = 10;

        ApplyDataToPanel(panel, data);

        Debug.Log($"ReminderManager: Created reminder for cube {cubeId}");
    }

    public void EditReminder(int cubeId, string newTask, DateTime newTime)
    {
        if (!activeReminders.TryGetValue(cubeId, out GameObject panel) || panel == null)
        {
            Debug.LogWarning($"ReminderManager: No active reminder for cube {cubeId} to edit.");
            return;
        }

        Reminder data = reminderData.ContainsKey(cubeId) ? reminderData[cubeId] : new Reminder { cubeId = cubeId };
        data.task = newTask;
        data.time = newTime;
        reminderData[cubeId] = data;

        ApplyDataToPanel(panel, data);

        Debug.Log($"ReminderManager: Edited reminder for cube {cubeId}");
    }

    public void DeleteReminder(int cubeId)
    {
        if (activeReminders.TryGetValue(cubeId, out GameObject panel))
        {
            if (panel != null) Destroy(panel);
            activeReminders.Remove(cubeId);
        }

        reminderData.Remove(cubeId);

        Debug.Log($"ReminderManager: Deleted reminder for cube {cubeId}");
    }

    public bool HasReminder(int cubeId)
    {
        return activeReminders.ContainsKey(cubeId) && activeReminders[cubeId] != null;
    }

    // ---------------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Writes reminder data onto the panel's child components.
    ///
    /// Expected prefab hierarchy:
    ///   ReminderPanel (Canvas, World Space)
    ///   └── Background (Image)
    ///       ├── IconImage   (Image)           ← name must be "IconImage"
    ///       ├── TaskText    (TextMeshProUGUI)  ← name must be "TaskText"
    ///       └── TimeText    (TextMeshProUGUI)  ← name must be "TimeText"
    /// </summary>
    private void ApplyDataToPanel(GameObject panel, Reminder data)
    {
        // Task label
        Transform taskObj = panel.transform.Find("CanvasRoot/Background/TaskText");
        if (taskObj != null)
        {
            var tmp = taskObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = data.task;
        }

        // Time label
        Transform timeObj = panel.transform.Find("CanvasRoot/Background/TimeText");
        if (timeObj != null)
        {
            var tmp = timeObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = data.time.ToString("hh:mm tt");
        }

        // Icon (optional — skipped if no sprite assigned)
        if (data.icon != null)
        {
            Transform iconObj = panel.transform.Find("CanvasRoot/Background/IconImage");
            if (iconObj != null)
            {
                var img = iconObj.GetComponent<Image>();
                if (img != null) img.sprite = data.icon;
            }
        }
    }
}