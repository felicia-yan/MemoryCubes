// GestureDetection - detect user hand gestures with cubes (e.g. shake, spin, stack) to trigger interactions

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using Meta.XR;
using TryAR.MarkerTracking;
using Oculus.Interaction.Input; 
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GestureDetection : MonoBehaviour
{
    [System.Serializable]
    public class CubeState // Storing state of each cube for gesture detection
    {
        public Transform transform;

        public Vector3 lastPosition;
        public Quaternion lastRotation;

        public Vector3 velocity;
        public Vector3 angularVelocity;

        public bool isTouched;

        // Shake detection — uses raw (unsmoothed) position to avoid filter killing velocity reversals
        public Vector3 lastRawPosition;
        public Vector3 rawVelocity;
        public int shakeDirectionChanges;
        public float shakeTimer;
        public Vector3 lastRawVelocity;
        public float touchHeldTime;

        // Spin detection
        public float spinTimer;
    }

    // Storing current or last known cube states, by ID
    private Dictionary<int, CubeState> _cubes = new Dictionary<int, CubeState>();
    // List of IDs currently on screen
    private List<int> _detectedIDs = new List<int>();

    [SerializeField] public Material outlineMaterial; 
    private Dictionary<int, Material> _outlineMaterialInstances = new Dictionary<int, Material>();


    [SerializeField] float touchThreshold = 0.2f;
    [SerializeField] public OVRSkeleton leftSkeleton;
    [SerializeField] public OVRSkeleton rightSkeleton; 
    private Vector3 leftHandPos; 
    private Vector3 rightHandPos;
    private List<int> touchedCubeIDs = new List<int>(); 

    // Darken fade overlay effect on focus
    [SerializeField] public OVRPassthroughLayer passthroughLayer; 
    [SerializeField] private float fadeSpeed = 0.5f;
    private Coroutine fadeRoutine;

    [SerializeField] private TextMeshProUGUI debugText; 

    // Creating reminders
    [SerializeField] private ReminderManager reminderManager;
    private HashSet<int> _anchoredCubeIDs = new HashSet<int>();
    private Dictionary<int, OVRSpatialAnchor> _cubeAnchors = new Dictionary<int, OVRSpatialAnchor>();

    // Grouping cubes into routines
    public enum GroupType
    {
        Ordered,
        Unordered
    }

    [System.Serializable]
    public class CubeGroup
    {
        public int groupID;
        // all cubes in group
        public HashSet<int> cubeIDs = new HashSet<int>();
        // ordered representation
        public List<int> orderedIDs = new List<int>();
        public GroupType type;
        public GameObject groupUI;
    }


    [Header("Forming Routines")]
    [SerializeField] private float combineDistance = 0.12f;
    [SerializeField] private float disconnectDistance = 0.25f;
    [SerializeField] private GameObject groupUIPrefab;

    private Dictionary<int, CubeGroup> cubeToGroup = new();
    private Dictionary<int, CubeGroup> groups = new();

    private int nextGroupID = 1;

    private int lastTouchedCube = -1;

    private HashSet<int> completedOrderedTasks = new();

    // For testing purposes, possible tasks
    private List<string> tasks = new List<string> { "Take a walk", "Brush my teeth", "Vacuum", "Take my medication" };


    void Start()
    {
        SetDim(false); 
    }

    void Update()
    {
        // Track cube states + if they are being touched every frame
        UpdateDetectedIDs(); 
        UpdateCubeMotion(); 
        UpdateTouchedCubes(); 

        HandleCubeGrouping();
        HandleOrderedTraversalUpdate();

        // Check for gesture detections for touched cubes
        foreach (int id in _detectedIDs) {
            CubeState cube = _cubes[id];

            if (!cube.isTouched)
                continue;

            DetectShake(id, cube);
            DetectSpin(id, cube);
        }
    }

    // Get IDs of cubes currently detected on camera
    void UpdateDetectedIDs() {
        _detectedIDs.Clear();

        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            GameObject cubeObj = kvp.Value;
            _detectedIDs.Add(id);

            if (!_cubes.ContainsKey(id))
            {
                _cubes[id] = new CubeState
                {
                    transform = cubeObj.transform,
                    lastPosition = cubeObj.transform.position,
                    lastRotation = cubeObj.transform.rotation,
                    lastRawPosition = cubeObj.transform.position,
                    lastRawVelocity = Vector3.zero
                };
            }
            else
            {
                // If anchored, drive the cube's GameObject position from the anchor
               if (_cubeAnchors.TryGetValue(id, out OVRSpatialAnchor anchor) && anchor != null && anchor.Localized) {
                    // Drive BOTH the GameObject and the cached transform from the anchor
                    cubeObj.transform.position = anchor.transform.position;
                    cubeObj.transform.rotation = anchor.transform.rotation;
                    _cubes[id].transform = cubeObj.transform;  // keep in sync
               } else {
                _cubes[id].transform = cubeObj.transform;
               }
            }
        }
        
    }

    // Get IDs of cubes currently detected on camera
    void UpdateTouchedCubes() {
        // Detect if cubes were placed down and released
        List<int> previouslyTouched = new List<int>(touchedCubeIDs);
        foreach (int id in previouslyTouched) {
            if (!touchedCubeIDs.Contains(id)) { // was touched, now released
                _cubes[id].touchHeldTime = 0f;
            }
        }

        foreach (int id in _detectedIDs)
            _cubes[id].isTouched = false;
        touchedCubeIDs.Clear();

        // Guard: don't run at all if skeletons aren't ready
        bool leftReady = leftSkeleton.IsInitialized && leftSkeleton.Bones != null && leftSkeleton.Bones.Count > 0;
        bool rightReady = rightSkeleton.IsInitialized && rightSkeleton.Bones != null && rightSkeleton.Bones.Count > 0;

        if (!leftReady && !rightReady) {
            debugText.text = "Waiting for skeleton init...";
            SetDim(false);
            return;
        }

        foreach (int id in _detectedIDs) {
            Vector3 cubePos = _cubes[id].transform.position;
            bool touched = false;

            if (leftReady) {
                foreach (var bone in leftSkeleton.Bones) {
                    if (bone?.Transform == null) continue;
                    if (Vector3.Distance(bone.Transform.position, cubePos) < touchThreshold) {
                        touched = true;
                        break;
                    }
                }
            }

            if (!touched && rightReady) {
                foreach (var bone in rightSkeleton.Bones) {
                    if (bone?.Transform == null) continue;
                    if (Vector3.Distance(bone.Transform.position, cubePos) < touchThreshold) {
                        touched = true;
                        break;
                    }
                }
            }

            if (touched) {
                _cubes[id].isTouched = true;
                touchedCubeIDs.Add(id);
            }
        }

        foreach (int id in _detectedIDs) {
            if (_cubes[id].isTouched && !previouslyTouched.Contains(id)) {
                _anchoredCubeIDs.Remove(id);
                _cubeAnchors.Remove(id);
            }
        }
        // If cube was released, place spatial anchor there for stable pos
        foreach (int id in previouslyTouched) {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id)) {
                // Destroy old anchor if one exists
                if (_cubeAnchors.TryGetValue(id, out OVRSpatialAnchor old) && old != null)
                    Destroy(old.gameObject);
                
                var anchor = AnchorsManager.Instance.CreateAnchorAt(
                    _cubes[id].transform.position, 
                    _cubes[id].transform.rotation);
                _cubeAnchors[id] = anchor;
                _anchoredCubeIDs.Add(id);
            }
        }

        // Update outline
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary) {
            int id = kvp.Key;
            if (_cubes.ContainsKey(id))
                SetOutline(kvp.Value, _cubes[id].isTouched);
        }

        // Hide cube altogether if not touched or no active reminder
        foreach (int id in _detectedIDs) {
            bool touched = _cubes[id].isTouched;
    
            // Show/hide the cube mesh
            GameObject cubeObj = ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary[id];
            Transform cubeChild = cubeObj.transform.Find("Cube");
            if (cubeChild != null)
                cubeChild.GetComponent<MeshRenderer>().enabled = touched;
        }

        bool anyTouched = touchedCubeIDs.Count > 0;
        SetDim(anyTouched);
        // debugText.text = $"Touched: {string.Join(", ", touchedCubeIDs)}"; 
    }

    // // Helper function, used to place anchor when cube is released
    // void PlaceAnchorAtCube(int id) {
    //     if (!_cubes.ContainsKey(id)) return;
    //     if (AnchorsManager.Instance == null) {
    //         Debug.LogWarning("AnchorsManager instance not found");
    //         return;
    //     }

    //     // Temporarily move the anchor's spawn transform to the cube's position
    //     Transform spawnTransform = AnchorsManager.Instance.GetSpawnTransform();
    //     Vector3 cubePos = _cubes[id].transform.position;
    //     Quaternion cubeRot = _cubes[id].transform.rotation;

    //     AnchorsManager.Instance.CreateAnchorAt(cubePos, cubeRot);
    //     Debug.Log($"Placed anchor for cube {id} at {cubePos}");
    // }

    // Show outline on touched cubes
    void SetOutline(GameObject cube, bool show) {
        Transform cubeChild = cube.transform.Find("Cube");
        if (cubeChild == null) {
            Debug.Log($"No child named 'Cube' found on {cube.name}");
            return;
        }

        MeshRenderer renderer = cubeChild.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        // Get the cube's ID to look up cached material
        int id = -1;
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
            if (kvp.Value == cube) { id = kvp.Key; break; }

        Material[] mats = renderer.sharedMaterials; // sharedMaterials avoids copy leak

        if (show && mats.Length < 2) {
            if (!_outlineMaterialInstances.ContainsKey(id))
                _outlineMaterialInstances[id] = new Material(outlineMaterial); // instantiate once
            renderer.materials = new Material[] { mats[0], _outlineMaterialInstances[id] };
        }
        else if (!show && mats.Length >= 2) {
            renderer.materials = new Material[] { mats[0] };
        }
    }
    
    // Fade to darker camera overlay effect on cube focus
    public void SetDim(bool dim) {
        float targetBrightness = dim ? -0.6f : 0f; // -1f is fully black

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeTo(targetBrightness));
    }

    private IEnumerator FadeTo(float target) {
        float start = passthroughLayer.colorMapEditorBrightness;
        float t = 0f;

        while (t < 1f) {
            t += Time.deltaTime * fadeSpeed;
            float brightness = Mathf.Lerp(start, target, t);
            passthroughLayer.SetBrightnessContrastSaturation(brightness, 0f, 0f);
            yield return null;
        }

        // Ensure we land exactly on target
        passthroughLayer.SetBrightnessContrastSaturation(target, 0f, 0f);
    }

    // Update cube states, occurs every frame
    void UpdateCubeMotion() {
        foreach (int id in _detectedIDs)
        {
            CubeState cube = _cubes[id];
            Transform t = cube.transform;

            // Smoothed velocity — used for spin (angular) and anything that benefits from stability
            cube.velocity = (t.position - cube.lastPosition) / Time.deltaTime;

            // Angular velocity (from smoothed rotation is fine — spin is sustained, not abrupt)
            Quaternion delta = t.rotation * Quaternion.Inverse(cube.lastRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            cube.angularVelocity = axis * angle * Mathf.Deg2Rad / Time.deltaTime;

            cube.lastPosition = t.position;
            cube.lastRotation = t.rotation;

            // Raw velocity — pulled from the coordinator's unsmoothed pose, used for shake detection.
            // The smoothing filter in ArUcoMarkerTracking blurs rapid direction reversals, which
            // is exactly the signal shake detection relies on, so we bypass it here.
            if (ArUcoTrackingAppCoordinator.m_markerRawPositionDictionary != null &&
                ArUcoTrackingAppCoordinator.m_markerRawPositionDictionary.TryGetValue(id, out Vector3 rawPos))
            {
                cube.rawVelocity = (rawPos - cube.lastRawPosition) / Time.deltaTime;
                cube.lastRawPosition = rawPos;
            }
            else
            {
                // Fallback: if raw positions aren't exposed yet, use smoothed velocity.
                // Shake detection will be less sensitive but won't break.
                cube.rawVelocity = cube.velocity;
                cube.lastRawPosition = t.position;
            }
        }
    }   

    // GESTURES ------------------------------------------------------------------
    // ---------------------------------------------------------------------------
    void DetectShake(int id, CubeState cube) {
        if (cube.touchHeldTime < 0.3f) {
            cube.touchHeldTime += Time.deltaTime;
            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
            return;
        }

        // Use raw velocity so the pose smoothing filter doesn't suppress direction reversals
        Vector3 velocity = cube.rawVelocity;

        float speed = velocity.magnitude;

        if (speed < 1.0f) return;

        float dot = Vector3.Dot(velocity.normalized, cube.lastRawVelocity.normalized);
        if (dot < -0.6f)
            cube.shakeDirectionChanges++;
        cube.lastRawVelocity = velocity;

        cube.shakeTimer += Time.deltaTime;

        if (cube.shakeTimer > 0.5f) {
            if (cube.shakeDirectionChanges >= 5) {
                debugText.text = $"Cube {id} SHAKE DETECTED!";
                
                bool reminderExists = reminderManager.HasReminder(id);

                if (reminderExists)
                {
                    reminderManager.DeleteReminder(id);

                    debugText.text =
                        $"Deleted reminder for cube {id}";
                }
                else
                {
                    int task_idx = UnityEngine.Random.Range(0, tasks.Count);
                    string task = tasks[task_idx]; 
                    // Temporary placeholder until voice input is connected
                    reminderManager.CreateReminder(
                        id,
                        task,
                        DateTime.Now.AddMinutes(1),
                        "none");

                    debugText.text =
                        $"Created reminder for cube {id}";

                    // voiceReminderController.currentCubeId = id;
                    // voiceExperience.Activate();
                }
            }
            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void DetectSpin(int id, CubeState cube)
    {
        float horizontalSpin = Mathf.Abs(cube.angularVelocity.y);

        if (horizontalSpin > 4f) {
            cube.spinTimer += Time.deltaTime;

            if (cube.spinTimer > 0.25f) {
                Debug.Log($"Cube {id} SPIN");
                debugText.text = $"Cube {id} SPIN DETECTED!";
                cube.spinTimer = 0f;
            }
        }
        else {
            cube.spinTimer = 0f;
        }
    }   

    // =====================================================
    // GROUPING INTO ROUTINES
    // =====================================================

    void HandleCubeGrouping() {
        // -------------------------------------------------
        // COMBINE
        // -------------------------------------------------

        for (int i = 0; i < touchedCubeIDs.Count; i++) {
            for (int j = i + 1; j < touchedCubeIDs.Count; j++) {
                int idA = touchedCubeIDs[i];
                int idB = touchedCubeIDs[j];

                float dist = Vector3.Distance(
                    _cubes[idA].transform.position,
                    _cubes[idB].transform.position);

                bool cubeAHasTask = reminderManager.HasReminder(idA);
                bool cubeBHasTask = reminderManager.HasReminder(idB);

                if (!cubeAHasTask || !cubeBHasTask)
                    continue;

                if (dist < combineDistance) {
                    CombineCubes(idA, idB);
                }
            }
        }

        // -------------------------------------------------
        // DISCONNECT
        // -------------------------------------------------

        List<(int,int)> disconnects = new();

        foreach (var group in groups.Values) {
            List<int> ids = group.cubeIDs.ToList();

            for (int i = 0; i < ids.Count; i++) {
                for (int j = i + 1; j < ids.Count; j++) {
                    int idA = ids[i];
                    int idB = ids[j];

                    bool touching =
                        _cubes[idA].isTouched ||
                        _cubes[idB].isTouched;

                    if (!touching)
                        continue;

                    float dist = Vector3.Distance(
                        _cubes[idA].transform.position,
                        _cubes[idB].transform.position);

                    if (dist > disconnectDistance)
                    {
                        disconnects.Add((idA, idB));
                    }
                }
            }
        }

        foreach (var pair in disconnects) {
            DisconnectCubePair(pair.Item1, pair.Item2);
        }
    }
    void CombineCubes(int idA, int idB) {
        if (!reminderManager.HasReminder(idA) || !reminderManager.HasReminder(idB)) {
            return;
        }
        CubeGroup groupA = cubeToGroup.ContainsKey(idA)
            ? cubeToGroup[idA]
            : null;

        CubeGroup groupB = cubeToGroup.ContainsKey(idB)
            ? cubeToGroup[idB]
            : null;

        // already same group
        if (groupA != null && groupA == groupB)
            return;

        if (groupA == null && groupB == null)
        {
            CubeGroup newGroup = new CubeGroup();

            newGroup.groupID = nextGroupID++;

            newGroup.cubeIDs.Add(idA);
            newGroup.cubeIDs.Add(idB);

            groups[newGroup.groupID] = newGroup;

            cubeToGroup[idA] = newGroup;
            cubeToGroup[idB] = newGroup;

            UpdateGroupSemantics(newGroup);

            CreateGroupUI(newGroup);

            Debug.Log($"Created Group {newGroup.groupID}");
            foreach (int id in newGroup.cubeIDs) {
                reminderManager.SetCubeGrouped(id, true);
            }

            return;
        }

        if (groupA != null && groupB == null)
        {
            groupA.cubeIDs.Add(idB);

            cubeToGroup[idB] = groupA;

            UpdateGroupSemantics(groupA);

            RefreshGroupUI(groupA);

            Debug.Log($"Added cube {idB} to group {groupA.groupID}");
            foreach (int id in groupA.cubeIDs) {
                reminderManager.SetCubeGrouped(id, true);
            }

            return;
        }

        if (groupB != null && groupA == null)
        {
            groupB.cubeIDs.Add(idA);

            cubeToGroup[idA] = groupB;

            UpdateGroupSemantics(groupB);

            RefreshGroupUI(groupB);

            Debug.Log($"Added cube {idA} to group {groupB.groupID}");

            return;
        }

        foreach (int id in groupB.cubeIDs)
        {
            groupA.cubeIDs.Add(id);

            cubeToGroup[id] = groupA;
        }
        groups.Remove(groupB.groupID);

        if (groupB.groupUI != null)
            Destroy(groupB.groupUI);

        UpdateGroupSemantics(groupA);
        RefreshGroupUI(groupA);
        foreach (int id in groupA.cubeIDs) {
            reminderManager.SetCubeGrouped(id, true);
        }

        Debug.Log($"Merged groups into {groupA.groupID}");
        foreach (int id in groupA.cubeIDs) {
            reminderManager.SetCubeGrouped(id, true);
        }
    }

    void DisconnectCubePair(int idA, int idB) {
        if (!cubeToGroup.ContainsKey(idA))
            return;

        CubeGroup originalGroup = cubeToGroup[idA];
        if (!originalGroup.cubeIDs.Contains(idB))
            return;
        if (originalGroup.cubeIDs.Count <= 1)
            return;

        // remove cube
        originalGroup.cubeIDs.Remove(idB);
        // create singleton group
        CubeGroup newGroup = new CubeGroup();
        newGroup.groupID = nextGroupID++;
        newGroup.cubeIDs.Add(idB);
        groups[newGroup.groupID] = newGroup;
        cubeToGroup[idB] = newGroup;
        reminderManager.SetCubeGrouped(idB, false);

        UpdateGroupSemantics(originalGroup);
        UpdateGroupSemantics(newGroup);

        CreateGroupUI(newGroup);
        RefreshGroupUI(originalGroup);
        if (originalGroup.cubeIDs.Count == 1) {
            int remaining = originalGroup.cubeIDs.First();
            reminderManager.SetCubeGrouped(remaining, false);
        }

        Debug.Log($"Disconnected cube {idB}");
    }


    // =====================================================
    // GROUP SEMANTICS
    // =====================================================

    void UpdateGroupSemantics(CubeGroup group) {
        group.type = DetectGroupType(group);

        if (group.type == GroupType.Ordered)
        {
            group.orderedIDs = BuildVerticalOrdering(group.cubeIDs);
        }
        else
        {
            group.orderedIDs = group.cubeIDs.ToList();
        }
    }

    GroupType DetectGroupType(CubeGroup group) {
        if (group.cubeIDs.Count < 2)
            return GroupType.Unordered;

        List<int> ids = group.cubeIDs.ToList();

        Vector3 first = _cubes[ids[0]].transform.position;
        Vector3 last = _cubes[ids[ids.Count - 1]].transform.position;

        Vector3 dir = (last - first).normalized;

        float vertical = Mathf.Abs(Vector3.Dot(dir, Vector3.up));

        if (vertical > 0.7f)
            return GroupType.Ordered;

        return GroupType.Unordered;
    }

    List<int> BuildVerticalOrdering(HashSet<int> ids) {
        List<int> ordered = ids.ToList();

        ordered.Sort((a,b) =>
            _cubes[b].transform.position.y.CompareTo(
            _cubes[a].transform.position.y));

        return ordered;
    }

    // =====================================================
    // ORDERED STACK TRAVERSAL
    // =====================================================

    void HandleOrderedTraversalUpdate() {
        foreach (int id in touchedCubeIDs)
        {
            if (id == lastTouchedCube)
                continue;

            HandleOrderedTraversal(id, lastTouchedCube);
            lastTouchedCube = id;
        }
    }

    void HandleOrderedTraversal(int currentID, int previousID) {
        if (!cubeToGroup.ContainsKey(currentID))
            return;

        CubeGroup group = cubeToGroup[currentID];

        if (group.type != GroupType.Ordered)
            return;

        int currentIndex = group.orderedIDs.IndexOf(currentID);
        int previousIndex = group.orderedIDs.IndexOf(previousID);

        // must move downward
        if (currentIndex == previousIndex + 1) {
            completedOrderedTasks.Add(previousID);

            Debug.Log($"Completed ordered task {previousID}");

            RefreshGroupUI(group);
        }
    }

    // =====================================================
    // ROUTINE GROUP UI
    // =====================================================

    void CreateGroupUI(CubeGroup group) {
        if (groupUIPrefab == null)
            return;

        Vector3 center = GetGroupCenter(group);
        GameObject ui = Instantiate(groupUIPrefab, center, Quaternion.identity);
        group.groupUI = ui;
        RefreshGroupUI(group);
    }

    void RefreshGroupUI(CubeGroup group) {
        if (group.groupUI == null)
            return;

        group.groupUI.transform.position = GetGroupCenter(group);
        TMP_Text label = group.groupUI.GetComponentInChildren<TMP_Text>();
        if (label == null)
            return;

        string typeString = group.type == GroupType.Ordered
            ? "Ordered Routine"
            : "Todo Group";

        int completed = 0;

        foreach (int id in group.cubeIDs) {
            if (completedOrderedTasks.Contains(id))
                completed++;
        }

        label.text =
            $"{typeString}\n" +
            $"Tasks: {group.cubeIDs.Count}\n" +
            $"Completed: {completed}";
    }

    Vector3 GetGroupCenter(CubeGroup group) {
        Vector3 sum = Vector3.zero;
        foreach (int id in group.cubeIDs) {
            sum += _cubes[id].transform.position;
        }
        return sum / group.cubeIDs.Count;
    }

}