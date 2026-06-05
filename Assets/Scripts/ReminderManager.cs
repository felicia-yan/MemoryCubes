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
    [SerializeField] private GameObject reminderCompletedPrefab;
    [SerializeField] private GameObject farReminderCompletedPrefab;


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
        { 7, new Color32(250, 122,   3, 255) },
    };

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
        public const string Kitchen = "\uf2e7";   // fa-utensils (best FA5 substitute for kitchen)
        public const string BlenderPhone = "\ue2eb"; // fa-blender (cooking/kitchen)
        public const string Mortar = "\uf5a1";    // fa-mortar-pestle

        // Water / Hydration
        public const string GlassWater = "\ue4f4"; // fa-glass (FA5 solid, closest to glass of water)
        public const string Tint = "\uf043";       // fa-tint (water drop)
        public const string Water = "\uf773";      // fa-water

        // Health
        public const string Heart = "\uf004";
        public const string Pill = "\uf484";
        public const string Syringe = "\uf48e";
        public const string Stethoscope = "\uf0f1";
        public const string BandAid = "\uf462";
        public const string Heartbeat = "\uf21e";  // fa-heartbeat

        // Fitness
        public const string Dumbbell = "\uf44b";
        public const string Bicycle = "\uf206";
        public const string Walk = "\uf554";
        public const string Run = "\uf70c";
        public const string Swimming = "\uf5c4";   // fa-swimmer

        // Sleep
        public const string Bed = "\uf236";
        public const string Moon = "\uf186";
        public const string AlarmClock = "\uf017";

        // Washing
        public const string Shower = "\uf2cc";
        public const string Soap = "\ue05e"; 
        public const string HandSparkles = "\ue05d"; // fa-hand-sparkles (FA5.13+)
        public const string Sink = "\uf2cc";       // no FA5 sink; reuse shower as closest substitute
        public const string Bath = "\uf2cd";       // fa-bath

        // Dishes
        public const string Utensils2 = "\uf2e7";  // reuse utensils for dishes

        // Work / School
        public const string Briefcase = "\uf0b1";
        public const string Laptop = "\uf109";
        public const string Book = "\uf02d";
        public const string GraduationCap = "\uf19d";
        public const string Pencil = "\uf303";
        public const string ChalkboardTeacher = "\uf51c"; // fa-chalkboard-teacher

        // Communication
        public const string Phone = "\uf095";
        public const string Envelope = "\uf0e0";
        public const string Comments = "\uf086";
        public const string Video = "\uf03d";
        public const string PhoneAlt = "\uf879";   // fa-phone-alt

        // Cleaning
        public const string Broom = "\uf51a";
        public const string Trash = "\uf1f8";
        public const string SprayCan = "\uf5bd";
        public const string Recycle = "\uf1b8";    // fa-recycle

        // Laundry
        public const string Shirt = "\uf553";      // fa-tshirt

        // Home Maintenance
        public const string House = "\uf015";
        public const string Wrench = "\uf0ad";
        public const string Hammer = "\uf6e3";
        public const string Screwdriver = "\uf54a";
        public const string Lightbulb = "\uf0eb";
        public const string Battery = "\uf240";
        public const string Plug = "\uf1e6";
        public const string Tools = "\uf7d9";      // fa-tools

        // Shopping & Errands
        public const string Cart = "\uf07a";
        public const string Bag = "\uf290";        // fa-shopping-bag
        public const string Store = "\uf54e";
        public const string Receipt = "\uf543";
        public const string Tags = "\uf02c";       // fa-tags

        // Finance
        public const string Dollar = "\uf155";
        public const string CreditCard = "\uf09d";
        public const string FileInvoiceDollar = "\uf571";
        public const string PiggyBank = "\uf4d3";
        public const string MoneyBill = "\uf0d6";  // fa-money-bill

        // Travel
        public const string Car = "\uf1b9";
        public const string GasPump = "\uf52f";
        public const string Plane = "\uf072";
        public const string Train = "\uf238";
        public const string Suitcase = "\uf0f2";
        public const string MapMarker = "\uf041";  // fa-map-marker

        // Pets
        public const string Paw = "\uf1b0";
        public const string Bone = "\uf5d7";
        public const string Fish = "\uf578";
        public const string Dog = "\uf6d3";        // fa-dog
        public const string Cat = "\uf6be";        // fa-cat

        // Plants & Garden
        public const string Seedling = "\uf4d8";
        public const string Leaf = "\uf06c";
        public const string Tree = "\uf1bb";
        public const string Sun = "\uf185";        // moved here, also fits garden

        // Technology
        public const string Mobile = "\uf3cd";
        public const string Wifi = "\uf1eb";
        public const string Desktop = "\uf108";    // fa-desktop (was wrong codepoint)
        public const string Keyboard = "\uf11c";
        public const string TabletAlt = "\uf3fa";  // fa-tablet-alt

        // Entertainment
        public const string Music = "\uf001";
        public const string Film = "\uf008";
        public const string Gamepad = "\uf11b";
        public const string Tv = "\uf26c";
        public const string Headphones = "\uf025"; // fa-headphones

        // Family & Social
        public const string User = "\uf007";
        public const string Users = "\uf0c0";
        public const string Child = "\uf1ae";
        public const string Gift = "\uf06b";
        public const string HandHoldingHeart = "\uf4be"; // fa-hand-holding-heart

        // Documents
        public const string Folder = "\uf07b";
        public const string File = "\uf15b";
        public const string Clipboard = "\uf328";
        public const string FileAlt = "\uf15c";    // fa-file-alt

        // Weather
        public const string Cloud = "\uf0c2";
        public const string Umbrella = "\uf0e9";
        public const string Snowflake = "\uf2dc";  // fa-snowflake

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
        public const string ThumbsUp = "\uf164";   // fa-thumbs-up
        public const string Fire = "\uf06d";       // fa-fire
    }

    void Update() {
        if (Camera.main == null)
            return;

        foreach (var kvp in activeReminders)
        {
            int cubeId = kvp.Key;
            ReminderUI ui = kvp.Value;

            if (groupedCubes.Contains(cubeId)) {
                ui.nearPanel.SetActive(false);
                ui.farPanel.SetActive(false);
                continue;
            }

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
        if (nearCanvas != null) {
            nearCanvas.overrideSorting = true;
            nearCanvas.sortingOrder = cubeId * 2;
        }

        Canvas farCanvas = farPanel.GetComponent<Canvas>();
        if (farCanvas != null) {
            farCanvas.overrideSorting = true;
            farCanvas.sortingOrder = cubeId * 2 + 1;
        }

        ApplyDataToPanel(nearPanel, data);
        ApplyFarDataToPanel(farPanel, data);
        nearPanel.SetActive(true);   // ensure near is visible by default
        farPanel.SetActive(false);   // far starts hidden until distance check kicks in

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
        groupedCubes.Remove(cubeId);

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

    public static bool ContainsAny(string text, params string[] keywords) {
        text = text.ToLowerInvariant();

        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword))
                return true;
        }

        return false;
    }

    public static string GetIconForTask(string task) {
        // Water / Hydration
        if (ContainsAny(task, "water", "drink", "hydrate", "hydration"))
            return ReminderIcons.GlassWater;

        // Dishes / Kitchen cleaning
        if (ContainsAny(task, "dishes", "wash dishes", "dishwasher", "unload dishwasher", "load dishwasher", "washing up"))
            return ReminderIcons.Utensils;

        // Cooking / Food prep
        if (ContainsAny(task, "cook", "cooking", "bake", "baking", "make dinner", "make lunch", "make breakfast", "meal prep", "recipe", "boil", "fry", "roast", "grill"))
            return ReminderIcons.BlenderPhone;

        // Shower / Bath
        if (ContainsAny(task, "shower", "take a shower", "take a bath", "bathe", "bathing"))
            return ReminderIcons.Shower;

        // Brush teeth / dental
        if (ContainsAny(task, "brush", "teeth", "floss", "dental", "dentist", "mouthwash"))
            return ReminderIcons.Bath;

        // Laundry
        if (ContainsAny(task, "laundry", "wash clothes", "dryer", "fold clothes", "iron", "ironing"))
            return ReminderIcons.Shirt;

        // Vacuum / Sweeping / Mopping
        if (ContainsAny(task, "vacuum", "vacuuming", "sweep", "sweeping", "mop", "mopping", "wipe", "scrub"))
            return ReminderIcons.Broom;

        // General cleaning
        if (ContainsAny(task, "clean", "cleaning", "tidy", "tidying", "declutter", "organize"))
            return ReminderIcons.SprayCan;

        // Trash / Recycling
        if (ContainsAny(task, "trash", "garbage", "recycle", "recycling", "bin", "take out"))
            return ReminderIcons.Trash;

        // Wash hands / Sanitize
        if (ContainsAny(task, "wash hands", "soap", "sanitize", "hand sanitizer"))
            return ReminderIcons.Soap;

        // Medicine
        if (ContainsAny(task, "medicine", "medication", "pill", "vitamin", "supplement", "pharmacy", "pharmacist", "dose", "inhaler"))
            return ReminderIcons.Pill;

        // Walk
        if (ContainsAny(task, "walk", "take a walk", "go outside", "outside", "hike", "stroll"))
            return ReminderIcons.Walk;

        // Run
        if (ContainsAny(task, "run", "running", "jog", "jogging"))
            return ReminderIcons.Run;

        // Gym / Exercise
        if (ContainsAny(task, "gym", "exercise", "workout", "lift weights", "weights", "yoga", "stretch", "pilates", "cycling"))
            return ReminderIcons.Dumbbell;

        // Sleep / Nap
        if (ContainsAny(task, "nap", "sleep", "go to bed", "sleeping", "napping", "rest", "bedtime"))
            return ReminderIcons.Bed;

        // Keys / Lock
        if (ContainsAny(task, "key", "keys", "lock", "unlock"))
            return ReminderIcons.Key;

        // Music
        if (ContainsAny(task, "music", "song", "listen", "playlist", "podcast"))
            return ReminderIcons.Music;

        // Movie / Film
        if (ContainsAny(task, "movie", "film", "theater", "cinema", "watch"))
            return ReminderIcons.Film;

        // TV / Shows
        if (ContainsAny(task, "tv", "watch tv", "television", "show", "episode", "netflix", "stream"))
            return ReminderIcons.Tv;

        // Pets
        if (ContainsAny(task, "dog", "cat", "pet", "feed pet", "walk dog", "litter", "fish", "bird", "hamster", "rabbit"))
            return ReminderIcons.Paw;

        // Plants / Garden
        if (ContainsAny(task, "water plant", "garden", "plants", "soil", "outdoors", "weed", "weeding", "mow", "lawn", "flowers"))
            return ReminderIcons.Seedling;

        // Meeting / Call
        if (ContainsAny(task, "meeting", "call", "zoom", "teams", "interview", "meet", "conference", "webinar"))
            return ReminderIcons.Video;

        // Study / Homework
        if (ContainsAny(task, "homework", "study", "class", "exam", "assignment", "test", "quiz", "lecture", "read", "reading"))
            return ReminderIcons.Book;

        // Work
        if (ContainsAny(task, "work", "office", "project", "deadline", "email", "report", "presentation"))
            return ReminderIcons.Briefcase;

        // Bills / Finance
        if (ContainsAny(task, "pay", "bill", "rent", "mortgage", "invoice", "payment", "tax", "insurance", "bank", "budget"))
            return ReminderIcons.FileInvoiceDollar;

        // Shopping / Groceries
        if (ContainsAny(task, "shopping", "groceries", "buy", "shop", "cart", "supermarket", "store", "market", "errands"))
            return ReminderIcons.Cart;

        // Package / Delivery
        if (ContainsAny(task, "package", "delivery", "pickup", "parcel", "mail", "post"))
            return ReminderIcons.Box;

        // Car / Travel
        if (ContainsAny(task, "car", "drive", "driving", "gas", "fuel", "oil change", "service", "parking"))
            return ReminderIcons.Car;

        // Doctor / Health appointment
        if (ContainsAny(task, "doctor", "appointment", "checkup", "hospital", "clinic", "therapy", "therapist"))
            return ReminderIcons.Stethoscope;

        // Phone
        if (ContainsAny(task, "phone", "call", "text", "message", "contact", "ring"))
            return ReminderIcons.Phone;

        // Home maintenance
        if (ContainsAny(task, "fix", "repair", "maintenance", "install", "replace", "battery", "lightbulb", "bulb", "plumber", "electrician"))
            return ReminderIcons.Wrench;

        return ReminderIcons.Star;
    }

    public bool TryGetReminderData(int cubeId, out Reminder reminder) {
        return reminderData.TryGetValue(cubeId, out reminder);
    }

    public void SetGroupVisible(IEnumerable<int> cubeIds, bool visible) {
        foreach (int id in cubeIds)
            SetCubeGrouped(id, !visible);
    }

    public void SetCubeDismissed(int cubeId) {
        if (!activeReminders.TryGetValue(cubeId, out ReminderUI ui))
            return;

        reminderData.TryGetValue(cubeId, out Reminder data);

        Vector3 spawnPos = transform.position + Vector3.up * heightOffset;

        if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(cubeId, out GameObject cubeObj) && cubeObj != null)
        {
            spawnPos = cubeObj.transform.position + Vector3.up * heightOffset;
        }

        // cache old panels
        GameObject oldNear = ui.nearPanel;
        GameObject oldFar = ui.farPanel;

        // instantiate replacements in same position/rotation
        GameObject newNear = null;
        GameObject newFar = null;

        if (reminderCompletedPrefab != null && oldNear != null)
        {
            newNear = Instantiate(reminderCompletedPrefab, oldNear.transform.position, oldNear.transform.rotation, oldNear.transform.parent);
            ApplyDataToPanel(newNear, data);
        }

        if (farReminderCompletedPrefab != null && oldFar != null)
        {
            newFar = Instantiate(farReminderCompletedPrefab, oldFar.transform.position, oldFar.transform.rotation, oldFar.transform.parent);
            ApplyFarDataToPanel(newFar, data);
        }

        // keep canvas sorting consistent
        if (newNear != null)
        {
            Canvas c = newNear.GetComponent<Canvas>();
            if (c != null) c.sortingOrder = 10;
        }

        if (newFar != null)
        {
            Canvas c = newFar.GetComponent<Canvas>();
            if (c != null) c.sortingOrder = 10;
        }

        // destroy old panels AFTER replacement
        if (oldNear != null) Destroy(oldNear);
        if (oldFar != null) Destroy(oldFar);

        // replace references in-place
        ui.nearPanel = newNear;
        ui.farPanel = newFar;

        // update canvas groups if used
        ui.nearGroup = newNear ? newNear.GetComponent<CanvasGroup>() : null;
        ui.farGroup = newFar ? newFar.GetComponent<CanvasGroup>() : null;
        ui.farPanel.SetActive(false);
    }

    public Color GetCubeColor(int cubeId) {
        return cubeColors.TryGetValue(cubeId, out Color c) ? c : Color.white;
    }

}