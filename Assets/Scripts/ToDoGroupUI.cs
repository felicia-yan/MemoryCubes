using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ToDoGroupUI : GroupUIBase
{
    [Header("Containers")]
    [SerializeField] private Transform activeContainer;
    [SerializeField] private Transform completedContainer;

    [Header("Prefabs")]
    [SerializeField] private GameObject toDoRowPrefab;

    private static readonly Dictionary<int, Color32> CubeColors = new()
    {
        { 0, new Color32(255, 247,   0, 255) },
        { 1, new Color32(255,   0, 220, 255) },
        { 2, new Color32(  0, 180, 225, 255) },
        { 3, new Color32(170, 255,   0, 255) },
        { 7, new Color32(250, 122,   3, 255) },
    };

    private static readonly Dictionary<int, Color32> CubeColorsPastel = new()
    {
        { 0, new Color32(255, 251, 153, 255) },
        { 1, new Color32(255, 179, 246, 255) },
        { 2, new Color32(153, 229, 245, 255) },
        { 3, new Color32(210, 255, 153, 255) },
        { 7, new Color32(255, 186, 122, 255) },
    };

    public override void Refresh(GroupUIData data)
    {
        ClearChildren(activeContainer);
        ClearChildren(completedContainer);

        foreach (var item in data.items)
        {
            bool done = data.completedIDs.Contains(item.cubeId);
            GameObject row = Instantiate(toDoRowPrefab,
                done ? completedContainer : activeContainer);
            ApplyRow(row, item, done);
        }

        ForceRelayout();
    }

    void ApplyRow(GameObject row, GroupItemData item, bool completed)
    {
        SetTMP(row, "TaskText", completed ? $"<s>{item.task}</s>" : item.task);
        SetTMP(row, "IconText", item.icon);
        SetTMP(row, "TimeText", FormatTime(item.triggerTime));

        row.transform.Find("Strikethrough")?.gameObject.SetActive(completed);

        Transform cb = row.transform.Find("Checkbox");
        if (cb != null)
        {
            cb.Find("Unchecked")?.gameObject.SetActive(!completed);
            cb.Find("Checked")?.gameObject.SetActive(completed);
        }

        Image bg = row.GetComponent<Image>();
        if (bg != null)
        {
            if (completed)
                bg.color = new Color32(220, 220, 220, 255);
            else if (CubeColorsPastel.TryGetValue(item.cubeId, out Color32 c))
                bg.color = c;
        }

        Color textColor = completed ? new Color32(160, 160, 160, 255) : Color.black;
        SetTMPColor(row, "TaskText", textColor);
        SetTMPColor(row, "TimeText", textColor);
        SetTMPColor(row, "IconText",
            completed ? new Color32(160, 160, 160, 255) :
            CubeColors.TryGetValue(item.cubeId, out Color32 ic) ? ic : Color.black);
    }

    void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    void ForceRelayout()
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        StartCoroutine(RelayoutNextFrame());
    }

    IEnumerator RelayoutNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    void SetTMP(GameObject root, string childName, string text)
    {
        var t = root.transform.Find(childName);
        if (t == null) return;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }

    void SetTMPColor(GameObject root, string childName, Color color)
    {
        var t = root.transform.Find(childName);
        if (t == null) return;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.color = color;
    }

    string FormatTime(TimeSpan t) =>
        DateTime.Today.Add(t).ToString("h:mm tt");
}