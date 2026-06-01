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
        public string icon;
        public string task;
        public TimeSpan triggerTime;
        public string recurrence;
        public string reminderId;
    }

    [Header("Prefab")]
    [SerializeField] private GameObject reminderPrefab;
    [SerializeField] private GameObject farReminderPrefab;

    [SerializeField]
    private float farDistance = 2.0f;

    [Serializable]
    public class ReminderUI
    {
        public GameObject nearPanel;
        public GameObject farPanel;

        public CanvasGroup nearGroup;
        public CanvasGroup farGroup;

        public bool currentlyFar;
    }

    [Header("World Space")]
    [SerializeField]
    private float heightOffset = 0.8f;

    private Dictionary<int, ReminderUI> activeReminders = new Dictionary<int, ReminderUI>();

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

    // Icon constants to customize each task
    public static class ReminderIcons {
        // Default
        public const string Bell = "\uf0f3";
        public const string Check = "\uf00c";
        public const string Clock = "\uf017";
        public const string Calendar = "\uf133";
        public const string Hourglass = "\uf254";

        // Food & Drink
        public const string Utensils = "\uf2e7";
        public const string MugHot = "\uf7b6";
        public const string WineGlass = "\uf4e3";
        public const string Apple = "\uf5d1";
        public const string ShoppingBasket = "\uf291";
        public const string Kitchen = "\ue51a"; 

        // Water / Hydration
        public const string GlassWater = "\ue4f4";
        public const string Water = "\uf0f4";

        // Health
        public const string Heart = "\uf004";
        public const string Pill = "\uf484";
        public const string Syringe = "\uf48e";
        public const string Stethoscope = "\uf0f1";
        public const string BandAid = "\uf462";

        // Fitness
        public const string Dumbbell = "\uf44b";
        public const string Bicycle = "\uf206";
        public const string Walk = "\uf554";
        public const string Run = "\uf70c"; 

        // Sleep
        public const string Bed = "\uf236";
        public const string Moon = "\uf186";
        public const string AlarmClock = "\uf017";

        // Washing
        public const string Shower = "\uf2cc"; 
        public const string Sink = "\ue06d"; 
        public const string Soap = "\ue06e"; 

        // Work / School
        public const string Briefcase = "\uf0b1";
        public const string Laptop = "\uf109";
        public const string Book = "\uf02d";
        public const string GraduationCap = "\uf19d";
        public const string Pencil = "\uf303";

        // Communication
        public const string Phone = "\uf095";
        public const string Envelope = "\uf0e0";
        public const string Comments = "\uf086";
        public const string Video = "\uf03d";

        // Cleaning
        public const string Broom = "\uf51a";
        public const string Trash = "\uf1f8";
        public const string SprayCan = "\uf5bd";

        // Laundry
        public const string Shirt = "\uf553";

        // Home Maintenance
        public const string House = "\uf015";
        public const string Wrench = "\uf0ad";
        public const string Hammer = "\uf6e3";
        public const string Screwdriver = "\uf54a";
        public const string Lightbulb = "\uf0eb";
        public const string Battery = "\uf240";
        public const string Plug = "\uf1e6";

        // Shopping & Errands
        public const string Cart = "\uf07a";
        public const string Bag = "\uf290";
        public const string Store = "\uf54e";
        public const string Receipt = "\uf543";

        // Finance
        public const string Dollar = "\uf155";
        public const string CreditCard = "\uf09d";
        public const string FileInvoiceDollar = "\uf571";
        public const string PiggyBank = "\uf4d3";

        // Travel
        public const string Car = "\uf1b9";
        public const string GasPump = "\uf52f";
        public const string Plane = "\uf072";
        public const string Train = "\uf238";
        public const string Suitcase = "\uf0f2";

        // Pets
        public const string Paw = "\uf1b0";
        public const string Bone = "\uf5d7";
        public const string Fish = "\uf578";

        // Plants & Garden
        public const string Seedling = "\uf4d8";
        public const string Leaf = "\uf06c";
        public const string Tree = "\uf1bb";

        // Technology
        public const string Mobile = "\uf3cd";
        public const string Wifi = "\uf1eb";
        public const string Desktop = "\uf390";
        public const string Keyboard = "\uf11c";

        // Entertainment
        public const string Music = "\uf001";
        public const string Film = "\uf008";
        public const string Gamepad = "\uf11b";
        public const string Tv = "\uf26c";

        // Family & Social
        public const string User = "\uf007";
        public const string Users = "\uf0c0";
        public const string Child = "\uf1ae";
        public const string Gift = "\uf06b";

        // Documents
        public const string Folder = "\uf07b";
        public const string File = "\uf15b";
        public const string Clipboard = "\uf328";

        // Weather
        public const string Sun = "\uf185";
        public const string Cloud = "\uf0c2";
        public const string Umbrella = "\uf0e9";

        // Security
        public const string Key = "\uf084";
        public const string Lock = "\uf023";
        public const string Shield = "\uf132";

        // Deliveries
        public const string Box = "\uf466";
        public const string Truck = "\uf0d1";

        // Important / Urgent
        public const string Exclamation = "\uf12a";
        public const string TriangleExclamation = "\uf071";
        public const string Flag = "\uf024";

        // Misc
        public const string Star = "\uf005";
        public const string Bookmark = "\uf02e";
        public const string LocationPin = "\uf3c5";
    }

    void Update() {
        if (Camera.main == null)
            return;

        foreach (var kvp in activeReminders)
        {
            int cubeId = kvp.Key;
            ReminderUI ui = kvp.Value;

            if (groupedCubes.Contains(cubeId))
                continue;

            if (!ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(cubeId, out GameObject cubeObj)) {
                continue;
            }

            if (cubeObj == null)
                continue;

            Vector3 pos = cubeObj.transform.position + Vector3.up * heightOffset;

            // Keep both panels attached to cube
            ui.nearPanel.transform.position = pos;
            ui.farPanel.transform.position = pos;

            float distance = Vector3.Distance(Camera.main.transform.position, cubeObj.transform.position);

            bool showFar = distance > farDistance;

            float targetNearAlpha = showFar ? 0f : 1f;
            float targetFarAlpha = showFar ? 1f : 0f;

            // Fade alpha
            ui.nearPanel.SetActive(!showFar);
            ui.farPanel.SetActive(showFar);

            GameObject visiblePanel = showFar ? ui.farPanel : ui.nearPanel;
            Vector3 lookDir = visiblePanel.transform.position - Camera.main.transform.position;
            lookDir.y = 0f;

            if (lookDir != Vector3.zero)
            {
                Quaternion rotation =
                    Quaternion.LookRotation(lookDir);

                ui.nearPanel.transform.rotation = rotation;
                ui.farPanel.transform.rotation = rotation;
            }

            // Refresh "in 5 min" text
            if (showFar && reminderData.TryGetValue(cubeId, out Reminder reminder)) {
                ApplyFarDataToPanel(ui.farPanel, reminder);
            }
        }
    }

    public void CreateReminder(int cubeId, string task, TimeSpan triggerTime, string recurrence) {
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
            icon = GetIconForTask(task)
        };

        reminderData[cubeId] = data;

        Vector3 spawnPos =
            transform.position
            + Vector3.up * heightOffset;

        if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(cubeId, out GameObject cubeObj) && cubeObj != null) {
            spawnPos = cubeObj.transform.position + Vector3.up * heightOffset;
        }

        GameObject nearPanel = Instantiate(reminderPrefab, spawnPos, Quaternion.identity);

        GameObject farPanel = Instantiate(farReminderPrefab, spawnPos, Quaternion.identity);

        activeReminders[cubeId] = new ReminderUI {
            nearPanel = nearPanel,
            farPanel = farPanel,
            nearGroup = nearPanel.GetComponent<CanvasGroup>(),
            farGroup = farPanel.GetComponent<CanvasGroup>(),
            currentlyFar = false
        };

        Canvas nearCanvas = nearPanel.GetComponent<Canvas>();
        if (nearCanvas != null)
            nearCanvas.sortingOrder = 10;

        Canvas farCanvas = farPanel.GetComponent<Canvas>();
        if (farCanvas != null)
            farCanvas.sortingOrder = 10;

        ApplyDataToPanel(nearPanel, data);
        ApplyFarDataToPanel(farPanel, data);

        if (ReminderScheduler.Instance != null) {
            ReminderScheduler.Instance.ScheduleReminder(data);
        }

        Debug.Log($"Created reminder for cube {cubeId}");
    }

    public void EditReminder(int cubeId, string newTask, TimeSpan newTime, string recurrence) {
        if (!activeReminders.TryGetValue(cubeId, out ReminderUI ui)) {
            Debug.LogWarning($"No reminder for cube {cubeId}");
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
        data.icon = GetIconForTask(newTask);

        reminderData[cubeId] = data;

        ApplyDataToPanel(ui.nearPanel, data);
        ApplyFarDataToPanel(ui.farPanel, data);

        Debug.Log(
            $"Edited reminder for cube {cubeId}");
    }

    public void DeleteReminder(int cubeId) {
        if (activeReminders.TryGetValue(cubeId, out ReminderUI ui)) {
            if (ui.nearPanel != null)
                Destroy(ui.nearPanel);

            if (ui.farPanel != null)
                Destroy(ui.farPanel);
            activeReminders.Remove(cubeId);
        }

        reminderData.Remove(cubeId);

        Debug.Log(
            $"Deleted reminder for cube {cubeId}");
    }

    public bool HasReminder(int cubeId) {
        return activeReminders.ContainsKey(cubeId);
    }

    public void SetCubeGrouped(int cubeId, bool grouped) {
        if (grouped) {
            groupedCubes.Add(cubeId);
        }
        else {
            groupedCubes.Remove(cubeId);
        }

        if (activeReminders.TryGetValue(cubeId, out ReminderUI ui)) {
            if (ui.nearPanel != null)
                ui.nearPanel.SetActive(!grouped);

            if (ui.farPanel != null)
                ui.farPanel.SetActive(!grouped);
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
                tmp.text = $"{DateTime.Today.Add(data.triggerTime):h:mm tt}";
            }
        }

        if (data.icon != null)
        {
            Transform iconObj = panel.transform.Find("CanvasRoot/Background/IconText");

            if (iconObj != null) {
                TextMeshProUGUI tmp = iconObj.GetComponent<TextMeshProUGUI>();

                if (tmp != null) {
                    tmp.text = data.icon;
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

    private string GetRelativeTime(TimeSpan triggerTime) {
        TimeSpan now = DateTime.Now.TimeOfDay;
        TimeSpan remaining = triggerTime - now;

        // Tomorrow rollover
        if (remaining.TotalSeconds < 0)
            remaining += TimeSpan.FromDays(1);

        if (remaining.TotalMinutes < 1)
            return "Now";

        if (remaining.TotalHours < 1)
            return $"in {Mathf.CeilToInt((float)remaining.TotalMinutes)} min";

        if (remaining.TotalDays < 1)
            return $"in {Mathf.CeilToInt((float)remaining.TotalHours)} hr";

        return $"in {Mathf.CeilToInt((float)remaining.TotalDays)} day";
    }

    private void ApplyFarDataToPanel(GameObject panel, Reminder data) {
        Transform timeObj = panel.transform.Find("CanvasRoot/Background/TimeRemainingText");
        if (!cubeColors.TryGetValue(data.cubeId, out Color cubeColor)) {
                cubeColor = Color.white;
        }
        cubeColor.a = 1f;

        if (timeObj != null) {
            TextMeshProUGUI tmp = timeObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null) {
                tmp.text = GetRelativeTime(data.triggerTime);
                tmp.color = cubeColor;
            }
        }

        Transform iconObj = panel.transform.Find("CanvasRoot/Background/IconText");

        if (iconObj != null) {
            TextMeshProUGUI tmp = iconObj.GetComponent<TextMeshProUGUI>();
            if (tmp != null) {
                tmp.text = data.icon;
                tmp.color = cubeColor;
            }
        }

    }

    private bool ContainsAny(string text, params string[] keywords) {
        text = text.ToLowerInvariant();

        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword))
                return true;
        }

        return false;
    }

    private string GetIconForTask(string task) {
        if (ContainsAny(task,"water", "drink", "hydrate"))
        return ReminderIcons.GlassWater;

        if (ContainsAny(task,
            "walk", "take a walk",
            "go outside", "outside", "hike"))
            return ReminderIcons.Walk;

        if (ContainsAny(task,
            "medicine", "medication", "pill",
            "vitamin", "supplement", "pharmacy", "pharmacist"))
            return ReminderIcons.Pill;

        if (ContainsAny(task,
            "laundry", "wash clothes",
            "dryer", "fold clothes"))
            return ReminderIcons.Shirt;
        
        if (ContainsAny(task,
            "shower", "take a shower",
            "take a bath", "take a bath"))
            return ReminderIcons.Shower;
        
        if (ContainsAny(task,
            "run", "running"))
            return ReminderIcons.Run;
        
        if (ContainsAny(task,
            "gym", "exercise", "lift weights"))
            return ReminderIcons.Dumbbell;
        
        if (ContainsAny(task,
            "nap", "sleep", "go to bed", "sleeping", "napping"))
            return ReminderIcons.Bed;

        if (ContainsAny(task,
            "wash hands", "soap", "sanitize", "hand sanitizer"))
            return ReminderIcons.Soap;
        
        if (ContainsAny(task,
            "key", "keys",
            "lock"))
            return ReminderIcons.Key;
        
        if (ContainsAny(task,
            "music", "song",
            "listen"))
            return ReminderIcons.Music;
        
        if (ContainsAny(task,
            "movie", "film",
            "theater"))
            return ReminderIcons.Film;
        
        if (ContainsAny(task,
            "fish", "fishes"))
            return ReminderIcons.Fish;
        
        if (ContainsAny(task,
            "cat", "dog", "pet"))
            return ReminderIcons.Paw;

        if (ContainsAny(task,
            "meeting", "call", "zoom",
            "teams", "interview", "meet"))
            return ReminderIcons.Video;

        if (ContainsAny(task,
            "homework", "study", "class",
            "exam", "assignment", "test", "quiz"))
            return ReminderIcons.Book;

        if (ContainsAny(task,
            "work", "office", "project",
            "deadline"))
            return ReminderIcons.Briefcase;

        if (ContainsAny(task,
            "pay", "bill", "rent",
            "mortgage", "invoice", "payment", "invoice"))
            return ReminderIcons.FileInvoiceDollar;

        if (ContainsAny(task,
            "vacuum", "clean", "mop",
            "sweep", "wipe"))
            return ReminderIcons.Broom;

        if (ContainsAny(task,
            "dog", "cat", "pet",
            "feed pet"))
            return ReminderIcons.Paw;

        if (ContainsAny(task,
            "water plant", "garden",
            "plants", "soil", "outdoors"))
            return ReminderIcons.Seedling;

        if (ContainsAny(task,
            "shopping", "groceries",
            "buy", "shop", "cart"))
            return ReminderIcons.Cart;

        if (ContainsAny(task,
            "package", "delivery",
            "pickup"))
            return ReminderIcons.Box;
        
        if (ContainsAny(task,
            "tv", "watch tv",
            "television", "show"))
            return ReminderIcons.Tv;

        return ReminderIcons.Star;
    }

    public bool TryGetReminderData(int cubeId, out Reminder reminder) {
        return reminderData.TryGetValue(cubeId, out reminder);
    }

    public void SetGroupVisible(IEnumerable<int> cubeIds, bool visible) {
        foreach (int id in cubeIds)
            SetCubeGrouped(id, !visible);
    }
}