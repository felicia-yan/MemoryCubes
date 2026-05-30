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
        public DateTime triggerTime;
        public string recurrence;
        public string reminderId;
    }

    [Header("Prefab")]
    [SerializeField]
    private GameObject reminderPrefab;

    [Header("World Space")]
    [SerializeField]
    private float heightOffset = 0.8f;

    private Dictionary<int, GameObject> activeReminders = new Dictionary<int, GameObject>();
    private Dictionary<int, Reminder> reminderData = new Dictionary<int, Reminder>();
    private HashSet<int> groupedCubes = new HashSet<int>();

    // Cube colors
    private Dictionary<int, Color> cubeColors = new Dictionary<int, Color>()
    {
        { 0, new Color32(255, 247, 0, 170) },
        { 1, new Color32(255, 0, 220, 170) },
        { 2, new Color32(0, 180, 225, 170) },
        { 3, new Color32(170, 255, 0, 170) },
    };

    void Update()
    {
        foreach (var kvp in activeReminders) {
            int id = kvp.Key;
            GameObject panel = kvp.Value;

            if (panel == null)
                continue;

            if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(id, out GameObject cubeObj) && cubeObj != null) {
                panel.transform.position = cubeObj.transform.position + Vector3.up * heightOffset;
            }

            if (Camera.main != null)
            {
                Vector3 lookDir = panel.transform.position - Camera.main.transform.position;
                lookDir.y = 0f;
                if (lookDir != Vector3.zero) {
                    panel.transform.rotation = Quaternion.LookRotation(lookDir);
                }
            }
        }
    }

    public void CreateReminder(int cubeId, string task, DateTime triggerTime, string recurrence) {
        if (reminderPrefab == null) {
            Debug.LogWarning(
                "Reminder prefab missing.");

            return;
        }

        if (activeReminders.ContainsKey(cubeId)) {
            DeleteReminder(cubeId);
        }

        Reminder data = new Reminder {
            cubeId = cubeId,
            task = task,
            triggerTime = triggerTime,
            recurrence = recurrence,
            reminderId = Guid.NewGuid().ToString(),
            icon = null
        };

        reminderData[cubeId] = data;

        Vector3 spawnPos =
            transform.position
            + Vector3.up * heightOffset;

        if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(cubeId, out GameObject cubeObj) && cubeObj != null) {
            spawnPos = cubeObj.transform.position + Vector3.up * heightOffset;
        }

        GameObject panel = Instantiate(reminderPrefab, spawnPos,Quaternion.identity);

        activeReminders[cubeId] = panel;

        Canvas canvas = panel.GetComponent<Canvas>();

        if (canvas != null) {
            canvas.sortingOrder = 10;
        }

        ApplyDataToPanel(panel, data);

        if (ReminderScheduler.Instance != null) {
            ReminderScheduler.Instance.ScheduleReminder(data);
        }

        Debug.Log($"Created reminder for cube {cubeId}");
    }

    public void EditReminder(int cubeId, string newTask, DateTime newTime, string recurrence) {
        if (!activeReminders.TryGetValue(cubeId, out GameObject panel) || panel == null) {
            Debug.LogWarning(
                $"No reminder for cube {cubeId}");

            return;
        }

        Reminder data =
            reminderData.ContainsKey(cubeId)
            ? reminderData[cubeId]
            : new Reminder
            {
                cubeId = cubeId
            };

        data.task = newTask;
        data.triggerTime = newTime;
        data.recurrence = recurrence;

        reminderData[cubeId] = data;

        ApplyDataToPanel(panel, data);

        Debug.Log(
            $"Edited reminder for cube {cubeId}");
    }

    public void DeleteReminder(int cubeId)
    {
        if (activeReminders.TryGetValue(
            cubeId,
            out GameObject panel))
        {
            if (panel != null)
            {
                Destroy(panel);
            }

            activeReminders.Remove(cubeId);
        }

        reminderData.Remove(cubeId);

        Debug.Log(
            $"Deleted reminder for cube {cubeId}");
    }

    public bool HasReminder(int cubeId)
    {
        return activeReminders.ContainsKey(cubeId)
            && activeReminders[cubeId] != null;
    }

    public void SetCubeGrouped(int cubeId, bool grouped) {
        if (grouped) {
            groupedCubes.Add(cubeId);
        }
        else {
            groupedCubes.Remove(cubeId);
        }

        if (activeReminders.TryGetValue(cubeId, out GameObject panel) && panel != null) {
            panel.SetActive(!grouped);
        }
    }


    private void ApplyDataToPanel(
        GameObject panel,
        Reminder data)
    {
        Transform taskObj =
            panel.transform.Find(
                "CanvasRoot/Background/TaskText");

        if (taskObj != null)
        {
            TextMeshProUGUI tmp =
                taskObj.GetComponent<TextMeshProUGUI>();

            if (tmp != null)
            {
                tmp.text = data.task;
            }
        }

        Transform timeObj =
            panel.transform.Find(
                "CanvasRoot/Background/TimeText");

        if (timeObj != null)
        {
            TextMeshProUGUI tmp =
                timeObj.GetComponent<TextMeshProUGUI>();

            if (tmp != null)
            {
                tmp.text =
                    $"{data.triggerTime:hh:mm tt} • {data.recurrence}";
            }
        }

        if (data.icon != null)
        {
            Transform iconObj =
                panel.transform.Find(
                    "CanvasRoot/Background/IconImage");

            if (iconObj != null)
            {
                Image img =
                    iconObj.GetComponent<Image>();

                if (img != null)
                {
                    img.sprite = data.icon;
                }
            }
        }
        // Match reminder background to cube color
        Image bgImage = panel.transform.Find("CanvasRoot/Background")?.GetComponent<Image>();
        if (bgImage != null) {
            if (cubeColors.TryGetValue(data.cubeId, out Color color)) {
                bgImage.color = color;
            }
        }
    }
}