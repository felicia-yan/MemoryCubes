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
    public class CubeState
    {
        public Transform transform;

        public Vector3 lastPosition;
        public Quaternion lastRotation;

        public Vector3 velocity;
        public Vector3 angularVelocity;

        public bool isTouched;

        public Vector3 lastRawPosition;
        public Vector3 rawVelocity;
        public int shakeDirectionChanges;
        public float shakeTimer;
        public Vector3 lastRawVelocity;
        public float touchHeldTime;

        public float spinTimer;
        public float pointHeldTime;
    }

    private Dictionary<int, CubeState> _cubes = new Dictionary<int, CubeState>();
    private List<int> _detectedIDs = new List<int>();

    // Track which cube IDs have been dismissed for the day
    private HashSet<int> _dismissedCubeIDs = new HashSet<int>();
    // Track which cube IDs already have a placeholder reminder created
    private HashSet<int> _initializedCubeIDs = new HashSet<int>();

    [SerializeField] public Material outlineMaterial; 
    private Dictionary<int, Material> _outlineMaterialInstances = new Dictionary<int, Material>();

    [SerializeField] float touchThreshold = 0.15f;
    [SerializeField] public OVRSkeleton leftSkeleton;
    [SerializeField] public OVRSkeleton rightSkeleton; 
    private Vector3 leftHandPos; 
    private Vector3 rightHandPos;
    private List<int> touchedCubeIDs = new List<int>(); 

    [SerializeField] public OVRPassthroughLayer passthroughLayer; 
    [SerializeField] private float fadeSpeed = 0.5f;
    private Coroutine fadeRoutine;

    // Debugging 
    [SerializeField] private TextMeshProUGUI debugText;

    [SerializeField] private ReminderManager reminderManager;
    private HashSet<int> _anchoredCubeIDs = new HashSet<int>();
    private Dictionary<int, OVRSpatialAnchor> _cubeAnchors = new Dictionary<int, OVRSpatialAnchor>();

    public enum GroupType
    {
        Ordered,
        Unordered
    }

    [System.Serializable]
    public class CubeGroup
    {
        public int groupID;
        public HashSet<int> cubeIDs = new HashSet<int>();
        public List<int> orderedIDs = new List<int>();
        public GroupType type;
        public GameObject groupUI;
    }

    [Header("Forming Routines")]
    [SerializeField] private float combineDistance = 0.08f;
    [SerializeField] private float disconnectDistance = 0.1f;
    [SerializeField] private GameObject groupUIPrefab;

    private Dictionary<int, CubeGroup> cubeToGroup = new();
    private Dictionary<int, CubeGroup> groups = new();

    private int nextGroupID = 1;
    private int lastTouchedCube = -1;
    private HashSet<int> completedOrderedTasks = new();

    private List<string> tasks = new List<string> { "Take a walk", "Brush my teeth", "Vacuum", "Take my medication" };

    // Home position set by gesture
    private Dictionary<int, Vector3> _cubeHomePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, bool> _cubeHomed = new Dictionary<int, bool>(); // true once spin-anchored

    [SerializeField] private GroundPathArrow groundPathArrow;
    [SerializeField] private float awayFromHomeThreshold = 0.1f;

    [SerializeField] private float spinThreshold = 1.5f; // was hardcoded 4f
    
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;
    [SerializeField] private float pointHoldDuration = 1.5f;

    [SerializeField] private GameObject homeMarkerPrefab;

    private Dictionary<int, GameObject> _homeMarkers = new Dictionary<int, GameObject>();
        


    void Start()
    {
        SetDim(false); 
    }

    void Update()
    {
        UpdateDetectedIDs(); 
        UpdateCubeMotion(); 
        UpdateTouchedCubes(); 

        HandleCubeGrouping();
        HandleOrderedTraversalUpdate();
        UpdateRoutines(); 
        UpdateReturnArrows();

        foreach (int id in _detectedIDs) {
            CubeState cube = _cubes[id];
            // DetectPoint(id, cube);

            if (!cube.isTouched)
                continue;

            DetectShake(id, cube);
        }
    }

    void UpdateDetectedIDs() {
        _detectedIDs.Clear();

        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary) {
            int id = kvp.Key;
            GameObject cubeObj = kvp.Value;
            _detectedIDs.Add(id);

            if (!_cubes.ContainsKey(id)) {
                _cubes[id] = new CubeState {
                    transform = cubeObj.transform,
                    lastPosition = cubeObj.transform.position,
                    lastRotation = cubeObj.transform.rotation,
                    lastRawPosition = cubeObj.transform.position,
                    lastRawVelocity = Vector3.zero
                };

                // Auto-create a placeholder reminder the first time this cube is detected
                if (!_initializedCubeIDs.Contains(id)) {
                    _initializedCubeIDs.Add(id);
                    int taskIdx = UnityEngine.Random.Range(0, tasks.Count);
                    string task = tasks[taskIdx];
                    reminderManager.CreateReminder(
                        id,
                        task,
                        DateTime.Now.AddMinutes(1),
                        "none");
                }
            }
            else {
               if (_cubeAnchors.TryGetValue(id, out OVRSpatialAnchor anchor) && anchor != null && anchor.Localized) {
                    cubeObj.transform.position = anchor.transform.position;
                    cubeObj.transform.rotation = anchor.transform.rotation;
                    _cubes[id].transform = cubeObj.transform;
               } else {
                    _cubes[id].transform = cubeObj.transform;
               }
            }
        }
    }

    void UpdateTouchedCubes() {
        List<int> previouslyTouched = new List<int>(touchedCubeIDs);
        foreach (int id in previouslyTouched) {
            if (!touchedCubeIDs.Contains(id)) {
                _cubes[id].touchHeldTime = 0f;
            }
        }

        foreach (int id in _detectedIDs)
            _cubes[id].isTouched = false;
        touchedCubeIDs.Clear();

        bool leftReady = leftSkeleton.IsInitialized && leftSkeleton.Bones != null && leftSkeleton.Bones.Count > 0;
        bool rightReady = rightSkeleton.IsInitialized && rightSkeleton.Bones != null && rightSkeleton.Bones.Count > 0;

        if (!leftReady && !rightReady) {
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

        foreach (int id in previouslyTouched) {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id)) {
                if (_cubeAnchors.TryGetValue(id, out OVRSpatialAnchor old) && old != null)
                    Destroy(old.gameObject);
                
                var anchor = AnchorsManager.Instance.CreateAnchorAt(
                    _cubes[id].transform.position, 
                    _cubes[id].transform.rotation);
                _cubeAnchors[id] = anchor;
                _anchoredCubeIDs.Add(id);
            }
        }


        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            if (!_cubes.ContainsKey(id))
                continue;

            if (_cubes[id].isTouched) {
                SetOutline(kvp.Value, true, Color.white);
            }
        }

        foreach (int id in _detectedIDs) {
            bool touched = _cubes[id].isTouched;
            // bool pointed = (id == pointedID);
    
            GameObject cubeObj = ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary[id];
            Transform cubeChild = cubeObj.transform.Find("Cube");
            if (cubeChild != null)
                cubeChild.GetComponent<MeshRenderer>().enabled = touched;
        }

        bool anyTouched = touchedCubeIDs.Count > 0;
        SetDim(anyTouched);
    }

    // =========================================================
    // GESTURES
    // =========================================================

    void DetectShake(int id, CubeState cube) {
        if (cube.touchHeldTime < 0.3f) {
            cube.touchHeldTime += Time.deltaTime;
            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
            return;
        }

        Vector3 velocity = cube.rawVelocity;
        float speed = velocity.magnitude;

        if (speed < 1.0f) return;

        float dot = Vector3.Dot(velocity.normalized, cube.lastRawVelocity.normalized);
        if (dot < -0.6f)
            cube.shakeDirectionChanges++;
        cube.lastRawVelocity = velocity;

        cube.shakeTimer += Time.deltaTime;

        if (cube.shakeTimer > 0.5f) {
            if (cube.shakeDirectionChanges >= 3) {
                bool hasReminder = reminderManager.HasReminder(id);
                bool alreadyDismissed = _dismissedCubeIDs.Contains(id);

                if (hasReminder && !alreadyDismissed)
                {
                    // Dismiss the reminder for the day (keep it in reminderManager but mark dismissed)
                    _dismissedCubeIDs.Add(id);
                    debugText.text = $"Cube {id}: reminder dismissed for today.";

                }
                else
                {
                    debugText.text = $"Cube {id}: no active reminder to dismiss.";
                }
            }
            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void SetOutline(GameObject cube, bool show, Color? outlineColor = null) {
        Transform cubeChild = cube.transform.Find("Cube");
        if (cubeChild == null) {
            Debug.Log($"No child named 'Cube' found on {cube.name}");
            return;
        }

        MeshRenderer renderer = cubeChild.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        int id = -1;
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
            if (kvp.Value == cube) { id = kvp.Key; break; }

        Material[] mats = renderer.sharedMaterials;

        if (show){
            if (!_outlineMaterialInstances.ContainsKey(id))
                _outlineMaterialInstances[id] = new Material(outlineMaterial);

            // Always update color
            if (outlineColor.HasValue)
            {
                if (_outlineMaterialInstances[id].HasProperty("_Color"))
                    _outlineMaterialInstances[id].SetColor("_Color", outlineColor.Value);
                else if (_outlineMaterialInstances[id].HasProperty("_OutlineColor"))
                    _outlineMaterialInstances[id].SetColor("_OutlineColor", outlineColor.Value);
            }

            // Only add the material if it isn't already present
            if (mats.Length < 2)
            {
                renderer.materials = new Material[]
                {
                    mats[0],
                    _outlineMaterialInstances[id]
                };
            }
        }
        else if (mats.Length >= 2)
        {
            renderer.materials = new Material[] { mats[0] };
        }
    }

    public void SetDim(bool dim) {
        float targetBrightness = dim ? -0.3f : 0f;

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

        passthroughLayer.SetBrightnessContrastSaturation(target, 0f, 0f);
    }

    void UpdateCubeMotion() {
        foreach (int id in _detectedIDs)
        {
            CubeState cube = _cubes[id];
            Transform t = cube.transform;

            cube.velocity = (t.position - cube.lastPosition) / Time.deltaTime;

            Quaternion delta = t.rotation * Quaternion.Inverse(cube.lastRotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            cube.angularVelocity = axis * angle * Mathf.Deg2Rad / Time.deltaTime;

            cube.lastPosition = t.position;
            cube.lastRotation = t.rotation;

            if (ArUcoTrackingAppCoordinator.m_markerRawPositionDictionary != null &&
                ArUcoTrackingAppCoordinator.m_markerRawPositionDictionary.TryGetValue(id, out Vector3 rawPos))
            {
                cube.rawVelocity = (rawPos - cube.lastRawPosition) / Time.deltaTime;
                cube.lastRawPosition = rawPos;
            }
            else
            {
                cube.rawVelocity = cube.velocity;
                cube.lastRawPosition = t.position;
            }
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
        CubeGroup groupA = cubeToGroup.ContainsKey(idA) ? cubeToGroup[idA] : null;
        CubeGroup groupB = cubeToGroup.ContainsKey(idB) ? cubeToGroup[idB] : null;

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

        if (groupB != null && groupA == null) {
            groupB.cubeIDs.Add(idA);
            cubeToGroup[idA] = groupB;
            UpdateGroupSemantics(groupB);
            RefreshGroupUI(groupB);
            Debug.Log($"Added cube {idA} to group {groupB.groupID}");
            return;
        }

        foreach (int id in groupB.cubeIDs) {
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

        reminderManager.SetCubeGrouped(idB, false);

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
        return GroupType.Unordered; 
        // TO-DO: Implementing ordered lists
        // if (group.cubeIDs.Count < 2)
        //     return GroupType.Unordered;

        // List<int> ids = group.cubeIDs.ToList();
        // Camera cam = Camera.main;

        // float minScreenY = float.MaxValue, maxScreenY = float.MinValue;
        // float minScreenX = float.MaxValue, maxScreenX = float.MinValue;

        // foreach (int id in ids) {
        //     Vector3 screenPos = cam.WorldToScreenPoint(_cubes[id].transform.position);
        //     minScreenX = Mathf.Min(minScreenX, screenPos.x);
        //     maxScreenX = Mathf.Max(maxScreenX, screenPos.x);
        //     minScreenY = Mathf.Min(minScreenY, screenPos.y);
        //     maxScreenY = Mathf.Max(maxScreenY, screenPos.y);
        // }

        // float screenSpreadY = maxScreenY - minScreenY;
        // float screenSpreadX = maxScreenX - minScreenX;

        // // Ordered if taller than wide in screen space
        // if (screenSpreadY > screenSpreadX * 1.5f)
        //     return GroupType.Ordered;

        // return GroupType.Unordered;
    }

    List<int> BuildVerticalOrdering(HashSet<int> ids) {
        Camera cam = Camera.main;
        List<int> ordered = ids.ToList();
        ordered.Sort((a, b) => {
            float ay = cam.WorldToScreenPoint(_cubes[a].transform.position).y;
            float by = cam.WorldToScreenPoint(_cubes[b].transform.position).y;
            return by.CompareTo(ay); // top to bottom
        });
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

        Vector3 center = GetGroupCenter(group) + Vector3.up * 0.15f;
        GameObject ui = Instantiate(groupUIPrefab, center, Quaternion.identity);
        group.groupUI = ui;
        RefreshGroupUI(group);
    }

    void RefreshGroupUI(CubeGroup group) {
         if (group.groupUI == null) return;

        bool isActive = group.cubeIDs.Count > 1;
        group.groupUI.SetActive(isActive);

        if (!isActive) return;

        Vector3 center = GetGroupCenter(group);
        Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
        group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.2f;        
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

   void UpdateRoutines() {
        foreach (var group in groups.Values) {
            if (group.groupUI == null) continue;
            Vector3 center = GetGroupCenter(group);
            Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
            group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.2f;

            // Face the camera
            var cam = Camera.main;
            if (cam != null)
                group.groupUI.transform.rotation = Quaternion.LookRotation(
                    group.groupUI.transform.position - cam.transform.position);
        }
    }

    void UpdateReturnArrows() {
        foreach (int id in touchedCubeIDs) {
            bool homed = _cubeHomed.ContainsKey(id) && _cubeHomed[id];
            if (!homed) {
                // No anchor set yet — hide arrow if it exists
                continue;
            } 

            Vector3 homePos = _cubeHomePositions[id];
            Vector3 cubePos = _cubes[id].transform.position;
            float distFromHome = Vector3.Distance(cubePos, homePos);

            bool dismissed = _dismissedCubeIDs.Contains(id);
            bool inRoutine = cubeToGroup.ContainsKey(id) && cubeToGroup[id].cubeIDs.Count > 1;

            bool shouldShowArrow = !dismissed && distFromHome > awayFromHomeThreshold;
            if (shouldShowArrow) {
                ShowReturnArrow(id, homePos);
                return; 
            }
        }
        HideReturnArrow(); 
    }

    void ShowReturnArrow(int id, Vector3 homePos) {
        if (groundPathArrow == null) return;
        groundPathArrow.GetComponent<LineRenderer>().enabled = true; 
        groundPathArrow.startPosition = _cubes[id].transform.position;
        groundPathArrow.targetPosition = homePos;
    }

    void HideReturnArrow()
    {
        if (groundPathArrow != null)
            groundPathArrow.GetComponent<LineRenderer>().enabled = false; 
    }

    // TODO: Fix gesture for setting homing point
    // -----------------------------------
    // POINT GESTURE FOR HOMING
    // -----------------------------------
    // int GetPointedCubeID() {
    //     Vector3? fingertip = GetIndexFingertipPosition();
    //     if (fingertip == null) return -1;

    //     foreach (int id in _detectedIDs)
    //     {
    //         Collider col = _cubes[id].transform.GetComponentInChildren<Collider>();
    //         if (col == null) continue;

    //         if (Vector3.Distance(fingertip.Value, _cubes[id].transform.position) < 0.1f) {
    //             return id;
    //         }
    //     }

    //     return -1;
    // }

    // Vector3? GetIndexFingertipPosition() {
    //     OVRHand hand = null;
    //     if (leftHand  != null && leftHand.IsTracked && IsPointingGesture(leftHand))
    //         hand = leftHand;
    //     else if (rightHand != null && rightHand.IsTracked && IsPointingGesture(rightHand))
    //         hand = rightHand;

    //     if (hand == null) return null;

    //     var skeleton = hand.GetComponent<OVRSkeleton>();
    //     if (skeleton == null) return null;

    //     foreach (var bone in skeleton.Bones)
    //         if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
    //             return bone.Transform.position;

    //     return null;
    // }

    // bool IsPointingGesture(OVRHand hand){
    //     // if (hand == null || !hand.IsTracked) return false;

    //     // var skeleton = hand.GetComponent<OVRSkeleton>();
    //     // if (skeleton == null || !skeleton.IsInitialized) return false;

    //     // // Index finger must be extended
    //     // bool indexExtended = hand.GetFingerIsPinching(OVRHand.HandFinger.Index) == false
    //     //     && IsFingerExtended(skeleton, OVRSkeleton.BoneId.Hand_Index3);

    //     // // Other fingers must be curled
    //     // bool middleCurled = !IsFingerExtended(skeleton, OVRSkeleton.BoneId.Hand_Middle3);
    //     // bool ringCurled   = !IsFingerExtended(skeleton, OVRSkeleton.BoneId.Hand_Ring3);
    //     // bool pinkyCurled  = !IsFingerExtended(skeleton, OVRSkeleton.BoneId.Hand_Pinky3);

    //     // return indexExtended && middleCurled && ringCurled && pinkyCurled;
    //     return hand != null && hand.IsTracked && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
    // }

    // bool IsFingerExtended(OVRSkeleton skeleton, OVRSkeleton.BoneId tipBoneId) {
    //     var bones = skeleton.Bones;
    //     Transform wrist = null, tip = null;

    //     foreach (var bone in bones)
    //     {
    //         if (bone.Id == OVRSkeleton.BoneId.Hand_WristRoot) wrist = bone.Transform;
    //         if (bone.Id == tipBoneId) tip = bone.Transform;
    //     }

    //     if (wrist == null || tip == null) return false;

    //     // Finger is extended when its tip is far enough from the wrist
    //     return Vector3.Distance(wrist.position, tip.position) > 0.08f;
    // }

    // void DetectPoint(int id, CubeState cube)
    // {
    //     if (ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary.TryGetValue(id, out GameObject cubeObj)) {
    //         SetOutline(cubeObj, true, Color.yellow);
    //     }
    //     // int pointedID = GetPointedCubeID();
    //     // if (pointedID == id)
    //     // {
    //     //     cube.pointHeldTime += Time.deltaTime;
    //     //     if (cube.pointHeldTime > 1.5f)
    //     //     {
    //     //         cube.pointHeldTime = 0f;
    //     //         OnPointGesture(id);
    //     //     }
    //     // }
    //     // else
    //     // {
    //     //     cube.pointHeldTime = 0f;
    //     // }
    // }

    public void OnPointGesture(int id) {
        if (!reminderManager.HasReminder(id))
            return;

        Vector3 anchorPos = _cubes[id].transform.position;
        Quaternion anchorRot = _cubes[id].transform.rotation;
        _cubeHomePositions[id] = anchorPos;
        _cubeHomed[id] = true;

        debugText.text = $"Cube {id} home set at {anchorPos:F2}";

        if (_homeMarkers.TryGetValue(id, out GameObject oldMarker))
            Destroy(oldMarker);

        GameObject marker = Instantiate(homeMarkerPrefab, anchorPos, Quaternion.identity);
        _homeMarkers[id] = marker;
    }

}