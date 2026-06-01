using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to your GroupUI prefab root.
/// Call Refresh() whenever group state changes.
/// </summary>
public class GroupUIManager : MonoBehaviour
{
    // ── Panel roots (activate one, deactivate the other) ──────────────
    [Header("Panel Roots")]
    [SerializeField] private GameObject toDoPanel;    // Unordered
    [SerializeField] private GameObject stepsPanel;   // Ordered

    // ── TO-DO panel references ─────────────────────────────────────────
    [Header("To-Do Panel")]
    [SerializeField] private Transform toDoActiveContainer;     // VerticalLayoutGroup for pending items
    [SerializeField] private Transform toDoCompletedContainer;  // VerticalLayoutGroup for done items
    [SerializeField] private GameObject toDoRowPrefab;          // See ToDoRow prefab spec below

    // ── STEPS panel references ─────────────────────────────────────────
    [Header("Steps Panel")]
    [SerializeField] private GameObject stepsNowSection;        // The full-width "Now" row root
    [SerializeField] private TextMeshProUGUI stepsNowIcon;
    [SerializeField] private TextMeshProUGUI stepsNowTask;
    [SerializeField] private TextMeshProUGUI stepsNowTime;
    [SerializeField] private TextMeshProUGUI stepsNowNumber;

    [SerializeField] private Transform stepsNextContainer;      // HorizontalLayoutGroup (up to 3 items)
    [SerializeField] private GameObject stepsNextItemPrefab;    // See StepsNextItem prefab spec below

    [SerializeField] private Transform stepsCompletedContainer; // VerticalLayoutGroup for done items
    [SerializeField] private GameObject stepsCompletedRowPrefab;

    [SerializeField] private GameObject stepsPlaceCueRoot;      // "Place cube at bottom" instruction
    [SerializeField] private TextMeshProUGUI stepsPlaceCueText;

    // ── Cube colors (match ReminderManager) ───────────────────────────
    private static readonly Dictionary<int, Color32> CubeColors = new()
    {
        { 0, new Color32(255, 247,   0, 255) },
        { 1, new Color32(255,   0, 220, 255) },
        { 2, new Color32(  0, 180, 225, 255) },
        { 3, new Color32(170, 255,   0, 255) },
    };

    // Pastel tint for item backgrounds (same hue, lighter)
    private static readonly Dictionary<int, Color32> CubeColorsPastel = new()
    {
        { 0, new Color32(255, 251, 153, 255) },
        { 1, new Color32(255, 179, 246, 255) },
        { 2, new Color32(153, 229, 245, 255) },
        { 3, new Color32(210, 255, 153, 255) },
    };

    // ─────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from GestureDetection.RefreshGroupUI().
    /// Pass all the data needed to rebuild both panel types.
    /// </summary>
    public void Refresh(GroupUIData data)
    {
        bool isOrdered = data.groupType == GestureDetection.GroupType.Ordered;

        toDoPanel.SetActive(!isOrdered);
        stepsPanel.SetActive(isOrdered);

        if (isOrdered)
            RefreshSteps(data);
        else
            RefreshToDo(data);

        // Hide "place cue" if nothing was just completed
        if (stepsPlaceCueRoot != null)
            stepsPlaceCueRoot.SetActive(data.justCompletedID >= 0 && isOrdered);
    }

    // ─────────────────────────────────────────────────────────────────
    // TO-DO
    // ─────────────────────────────────────────────────────────────────

    void RefreshToDo(GroupUIData data)
    {
        ClearChildren(toDoActiveContainer);
        ClearChildren(toDoCompletedContainer);

        foreach (var item in data.items)
        {
            bool done = data.completedIDs.Contains(item.cubeId);
            GameObject row = Instantiate(toDoRowPrefab,
                done ? toDoCompletedContainer : toDoActiveContainer);

            ApplyToDoRow(row, item, done);
        }
    }

    void ApplyToDoRow(GameObject row, GroupItemData item, bool completed)
    {
        // Expected children: Checkbox, IconText, TaskText, TimeText, Background
        SetTMP(row, "TaskText",  completed ? $"<s>{item.task}</s>" : item.task);
        SetTMP(row, "IconText",  item.icon);
        SetTMP(row, "TimeText",  FormatTime(item.triggerTime));

        // Checkbox
        Transform cb = row.transform.Find("Checkbox");
        if (cb != null)
        {
            // Use a checkmark image swap or toggle — assumes two child images:
            // "Unchecked" and "Checked"
            cb.Find("Unchecked")?.gameObject.SetActive(!completed);
            cb.Find("Checked")?.gameObject.SetActive(completed);
        }

        // Row tint
        Image bg = row.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null)
        {
            if (completed)
                bg.color = new Color32(220, 220, 220, 255);
            else if (CubeColorsPastel.TryGetValue(item.cubeId, out Color32 c))
                bg.color = c;
        }

        // Gray-out text when completed
        Color textColor = completed ? new Color32(160, 160, 160, 255) : Color.black;
        SetTMPColor(row, "TaskText", textColor);
        SetTMPColor(row, "TimeText", textColor);
        SetTMPColor(row, "IconText",
            completed ? new Color32(160, 160, 160, 255) :
            CubeColors.TryGetValue(item.cubeId, out Color32 ic) ? ic : Color.black);
    }

    // ─────────────────────────────────────────────────────────────────
    // STEPS
    // ─────────────────────────────────────────────────────────────────

    void RefreshSteps(GroupUIData data)
    {
        ClearChildren(stepsNextContainer);
        ClearChildren(stepsCompletedContainer);

        var pending   = data.items.Where(i => !data.completedIDs.Contains(i.cubeId)).ToList();
        var completed = data.items.Where(i =>  data.completedIDs.Contains(i.cubeId)).ToList();

        // ── NOW ──────────────────────────────────────────────────────
        if (pending.Count > 0)
        {
            stepsNowSection.SetActive(true);
            var now = pending[0];

            if (stepsNowNumber != null) stepsNowNumber.text = $"({now.orderIndex + 1})";
            if (stepsNowIcon   != null) stepsNowIcon.text   = now.icon;
            if (stepsNowTask   != null) stepsNowTask.text   = now.task;
            if (stepsNowTime   != null) stepsNowTime.text   = FormatTime(now.triggerTime);

            // Tint the Now row
            Image nowBg = stepsNowSection.GetComponent<Image>();
            if (nowBg != null && CubeColorsPastel.TryGetValue(now.cubeId, out Color32 c))
                nowBg.color = c;
        }
        else
        {
            stepsNowSection.SetActive(false);
        }

        // ── NEXT (up to 3) ───────────────────────────────────────────
        var nextItems = pending.Skip(1).Take(3).ToList();
        foreach (var item in nextItems)
        {
            GameObject cell = Instantiate(stepsNextItemPrefab, stepsNextContainer);
            ApplyStepsNextCell(cell, item);
        }

        // ── COMPLETED ────────────────────────────────────────────────
        foreach (var item in completed)
        {
            GameObject row = Instantiate(stepsCompletedRowPrefab, stepsCompletedContainer);
            ApplyStepsCompletedRow(row, item);
        }

        // ── PLACE CUE ────────────────────────────────────────────────
        if (stepsPlaceCueRoot != null && data.justCompletedID >= 0)
        {
            stepsPlaceCueRoot.SetActive(true);
            if (stepsPlaceCueText != null)
                stepsPlaceCueText.text = "Place this cube at the bottom of the stack";
        }
    }

    void ApplyStepsNextCell(GameObject cell, GroupItemData item)
    {
        SetTMP(cell, "NumberText", $"({item.orderIndex + 1})");
        SetTMP(cell, "IconText",   item.icon);
        SetTMP(cell, "TaskText",   item.task);

        Image bg = cell.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null && CubeColorsPastel.TryGetValue(item.cubeId, out Color32 c))
            bg.color = c;

        if (CubeColors.TryGetValue(item.cubeId, out Color32 accent))
        {
            SetTMPColor(cell, "NumberText", accent);
            SetTMPColor(cell, "IconText",   accent);
            SetTMPColor(cell, "TaskText",   accent);
        }
    }

    void ApplyStepsCompletedRow(GameObject row, GroupItemData item)
    {
        SetTMP(row, "NumberText", $"({item.orderIndex + 1})");
        SetTMP(row, "IconText",   item.icon);
        SetTMP(row, "TaskText",   $"<s>{item.task}</s>");
        SetTMP(row, "TimeText",   FormatTime(item.triggerTime));

        Color gray = new Color32(160, 160, 160, 255);
        SetTMPColor(row, "NumberText", gray);
        SetTMPColor(row, "IconText",   gray);
        SetTMPColor(row, "TaskText",   gray);
        SetTMPColor(row, "TimeText",   gray);

        Image bg = row.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null) bg.color = new Color32(220, 220, 220, 255);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
            Destroy(child.gameObject);
    }

    void SetTMP(GameObject root, string childName, string text)
    {
        Transform t = root.transform.Find(childName);
        if (t == null) return;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }

    void SetTMPColor(GameObject root, string childName, Color color)
    {
        Transform t = root.transform.Find(childName);
        if (t == null) return;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.color = color;
    }

    string FormatTime(TimeSpan t) =>
        DateTime.Today.Add(t).ToString("h:mm tt");
}

// ─────────────────────────────────────────────────────────────────────
// Data structs passed from GestureDetection → GroupUIManager
// ─────────────────────────────────────────────────────────────────────

[Serializable]
public class GroupItemData
{
    public int      cubeId;
    public string   task;
    public string   icon;
    public TimeSpan triggerTime;
    public int      orderIndex;   // position in orderedIDs list
}

[Serializable]
public class GroupUIData
{
    public GestureDetection.GroupType groupType;
    public List<GroupItemData> items = new();
    public HashSet<int> completedIDs  = new();
    public int justCompletedID = -1; // set briefly after a shake
}