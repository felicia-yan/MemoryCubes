using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StepsGroupUI : GroupUIBase
{
    [Header("Now Section")]
    [SerializeField] private GameObject nowSection;
    [SerializeField] private TextMeshProUGUI nowNumber;
    [SerializeField] private TextMeshProUGUI nowIcon;
    [SerializeField] private TextMeshProUGUI nowTask;
    [SerializeField] private TextMeshProUGUI nowTime;

    [Header("Next Section")]
    [SerializeField] private Transform nextContainer;
    [SerializeField] private GameObject nextItemPrefab;

    [Header("Completed Section")]
    [SerializeField] private Transform completedContainer;
    [SerializeField] private GameObject completedRowPrefab;

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
        ClearChildren(nextContainer);
        ClearChildren(completedContainer);

        var pending   = data.items.Where(i => !data.completedIDs.Contains(i.cubeId)).ToList();
        var completed = data.items.Where(i =>  data.completedIDs.Contains(i.cubeId)).ToList();

        // NOW
        if (pending.Count > 0)
        {
            nowSection.SetActive(true);
            var now = pending[0];
            if (nowNumber != null) nowNumber.text = $"{now.orderIndex + 1}";
            if (nowIcon   != null) nowIcon.text   = now.icon;
            if (nowTask   != null) nowTask.text   = now.task;
            if (nowTime   != null) nowTime.text   = FormatTime(now.triggerTime);

            Image bg = nowSection.GetComponent<Image>();
            if (bg != null && CubeColorsPastel.TryGetValue(now.cubeId, out Color32 c))
                bg.color = c;
        }
        else
        {
            nowSection.SetActive(false);
        }

        // NEXT (up to 3)
        foreach (var item in pending.Skip(1).Take(3))
        {
            GameObject cell = Instantiate(nextItemPrefab, nextContainer);
            ApplyNextCell(cell, item);
        }

        // COMPLETED
        foreach (var item in completed)
        {
            GameObject row = Instantiate(completedRowPrefab, completedContainer);
            ApplyCompletedRow(row, item);
        }

        ForceRelayout();
    }

    void ApplyNextCell(GameObject cell, GroupItemData item) {
        // Number text (nested under Number/NumberText)
        SetTMP(cell, "Number/NumberText", $"{item.orderIndex + 1}");
        SetTMP(cell, "IconText",          item.icon);
        SetTMP(cell, "TaskText",          item.task);

        // Pastel background for the whole cell
        Image cellBg = cell.GetComponent<Image>();
        if (cellBg != null && CubeColorsPastel.TryGetValue(item.cubeId, out Color32 pastel))
            cellBg.color = pastel;

        // Vivid accent for icon + task text
        SetTMPColor(cell, "TaskText",          Color.black);
        SetTMPColor(cell, "Number/NumberText", Color.white);
        if (CubeColors.TryGetValue(item.cubeId, out Color32 accent))
        {
            SetTMPColor(cell, "IconText",          accent);
        }

        // Number circle background (the Image on the Number object itself)
        Transform numberObj = cell.transform.Find("Number");
        if (numberObj != null)
        {
            Image circleBg = numberObj.GetComponent<Image>();
            circleBg.color = Color.black;
        }
    }

    void ApplyCompletedRow(GameObject row, GroupItemData item)
    {
        SetTMP(row, "NumberText", $"{item.orderIndex + 1}");
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
