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
        public Vector3 filteredRawVelocity;

        public float spinTimer;
        public float pointHeldTime;
    }

    private Dictionary<int, CubeState> _cubes = new Dictionary<int, CubeState>();
    private List<int> _detectedIDs = new List<int>();

    private HashSet<int> _dismissedCubeIDs = new HashSet<int>();
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
        public GroupType type = GroupType.Unordered;
        public GroupType spawnedUIType = GroupType.Unordered;
        public GameObject groupUI;
    }

    [Header("Forming Routines")]
    private HashSet<int> _lockedGroups = new HashSet<int>();
    [SerializeField] private float combineDistance = 0.08f;
    [SerializeField] private float disconnectDistance = 0.1f;
    // [SerializeField] private GameObject groupUIPrefab;
    [SerializeField] private GameObject toDoUIPrefab;
    [SerializeField] private GameObject stepsUIPrefab;

    private Dictionary<int, CubeGroup> cubeToGroup = new();
    private Dictionary<int, CubeGroup> groups = new();

    private int nextGroupID = 1;
    private HashSet<int> completedOrderedTasks = new();

    private List<string> tasks = new List<string> { "Take a walk", "Brush my teeth", "Vacuum", "Take my medication" };

    private Dictionary<int, Vector3> _cubeHomePositions = new Dictionary<int, Vector3>();
    private Dictionary<int, bool> _cubeHomed = new Dictionary<int, bool>();

    [SerializeField] private GroundPathArrow groundPathArrow;
    [SerializeField] private float awayFromHomeThreshold = 0.1f;

    [SerializeField] private float spinThreshold = 1.5f;
    
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;
    [SerializeField] private float pointHoldDuration = 1.5f;

    [SerializeField] private GameObject homeMarkerPrefab;

    private Dictionary<int, GameObject> _homeMarkers = new Dictionary<int, GameObject>();

    // Group type hysteresis
    private Dictionary<int, GroupType> _pendingGroupTypes = new();
    private Dictionary<int, float> _pendingGroupTypeTimers = new();
    private const float GROUP_TYPE_HYSTERESIS = 0.5f;
    [SerializeField] private float orderedMinVerticalSpread = 0.03f;
    [SerializeField] private float orderedMaxHorizontalSpread = 0.02f;

    // "Place cube at bottom" cue
    private int _justCompletedID = -1;
    private float _justCompletedTimer = 0f;
    private const float JUST_COMPLETED_DISPLAY_TIME = 3f;

    [SerializeField] private float showReminderWhenAwayDistance = 0.3f;

    // Voice input
    [SerializeField] private VoiceManager voiceManager;
    private int _activeVoiceCube = -1;

    // Text for debugging
    [SerializeField] private GameObject systemCanvas; 
    [SerializeField] private TextMeshProUGUI systemText; 


    // =====================================================

    void Start()
    {
        SetDim(false); 
        if (voiceManager != null)
            voiceManager.OnReminderCreated += HandleReminderCreated;
    }

    void Update()
    {
        UpdateDetectedIDs(); 
        UpdateCubeMotion(); 
        UpdateTouchedCubes(); 

        HandleCubeGrouping();
        UpdateAllGroupSemantics();
        UpdateRoutines(); 
        UpdateReturnArrows();
        TickJustCompleted();
        UpdateLockedGroupVisibility();

        foreach (int id in _detectedIDs)
        {
            CubeState cube = _cubes[id];

            if (!cube.isTouched)
                continue;

            DetectShake(id, cube);
        }
    }

    void TickJustCompleted() {
        if (_justCompletedID < 0) return;
        _justCompletedTimer -= Time.deltaTime;
        if (_justCompletedTimer <= 0f)
        {
            _justCompletedID = -1;
            foreach (var group in groups.Values)
                RefreshGroupUI(group);
        }
    }

    void UpdateDetectedIDs()
    {
        _detectedIDs.Clear();

        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            GameObject cubeObj = kvp.Value;
            _detectedIDs.Add(id);

            if (!_cubes.ContainsKey(id))
            {
                _cubes[id] = new CubeState {
                    transform = cubeObj.transform,
                    lastPosition = cubeObj.transform.position,
                    lastRotation = cubeObj.transform.rotation,
                    lastRawPosition = cubeObj.transform.position,
                    lastRawVelocity = Vector3.zero
                };

                if (!_initializedCubeIDs.Contains(id))
                {
                    _initializedCubeIDs.Add(id);
                    // int taskIdx = UnityEngine.Random.Range(0, tasks.Count);
                    // string task = tasks[taskIdx];
                    // reminderManager.CreateReminder(
                    //     id,
                    //     task,
                    //     DateTime.Now.TimeOfDay.Add(TimeSpan.FromMinutes(10)),
                    //     "none");
                }
            }
            else
            {
                if (_cubeAnchors.TryGetValue(id, out OVRSpatialAnchor anchor) && anchor != null && anchor.Localized)
                {
                    cubeObj.transform.position = anchor.transform.position;
                    cubeObj.transform.rotation = anchor.transform.rotation;
                    _cubes[id].transform = cubeObj.transform;
                }
                else
                {
                    _cubes[id].transform = cubeObj.transform;
                }
            }
        }
    }

    void UpdateTouchedCubes()
    {
        List<int> previouslyTouched = new List<int>(touchedCubeIDs);
        foreach (int id in _detectedIDs) {   
            if (_cubes[id].isTouched && !previouslyTouched.Contains(id))
            {
                _cubes[id].touchHeldTime = 0f; // just picked up, start fresh
                _anchoredCubeIDs.Remove(id);
                _cubeAnchors.Remove(id);

                bool hasReminder = reminderManager.HasReminder(id);
                if (!hasReminder && _activeVoiceCube == -1) {
                    _activeVoiceCube = id;
                    voiceManager.BeginReminderCreation(id);               
                }
            }
        }

        foreach (int id in _detectedIDs)
            _cubes[id].isTouched = false;
        touchedCubeIDs.Clear();

        bool leftReady  = leftSkeleton.IsInitialized  && leftSkeleton.Bones  != null && leftSkeleton.Bones.Count  > 0;
        bool rightReady = rightSkeleton.IsInitialized && rightSkeleton.Bones != null && rightSkeleton.Bones.Count > 0;

        if (!leftReady && !rightReady)
        {
            SetDim(false);
            return;
        }

        foreach (int id in _detectedIDs)
        {
            Vector3 cubePos = _cubes[id].transform.position;
            bool touched = false;

            if (leftReady)
            {
                foreach (var bone in leftSkeleton.Bones)
                {
                    if (bone?.Transform == null) continue;
                    if (Vector3.Distance(bone.Transform.position, cubePos) < touchThreshold)
                    {
                        touched = true;
                        break;
                    }
                }
            }

            if (!touched && rightReady)
            {
                foreach (var bone in rightSkeleton.Bones)
                {
                    if (bone?.Transform == null) continue;
                    if (Vector3.Distance(bone.Transform.position, cubePos) < touchThreshold)
                    {
                        touched = true;
                        break;
                    }
                }
            }

            if (touched)
            {
                _cubes[id].isTouched = true;
                touchedCubeIDs.Add(id);
            }
        }

        foreach (int id in _detectedIDs)
        {
            if (_cubes[id].isTouched && !previouslyTouched.Contains(id))
            {
                _anchoredCubeIDs.Remove(id);
                _cubeAnchors.Remove(id);

                // If touching and no reminder, active voice input
                bool hasReminder = reminderManager.HasReminder(id);
                if (!hasReminder && _activeVoiceCube == -1) {
                    _activeVoiceCube = id;
                    voiceManager.BeginReminderCreation(id);               
                }
            }
        }

        foreach (int id in previouslyTouched)
        {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id))
            {
                if (id == _activeVoiceCube) {
                    voiceManager.CancelListening();
                    _activeVoiceCube = -1;
                    continue; 
                }

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
            if (!_cubes.ContainsKey(id)) continue;

            if (_cubes[id].isTouched)
                SetOutline(kvp.Value, true, Color.white);
            else
                SetOutline(kvp.Value, false);
        }

        foreach (int id in _detectedIDs)
        {
            bool touched = _cubes[id].isTouched;
            GameObject cubeObj = ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary[id];
            Transform cubeChild = cubeObj.transform.Find("Cube");
            if (cubeChild != null)
                cubeChild.GetComponent<MeshRenderer>().enabled = touched;
        }

        bool anyTouched = touchedCubeIDs.Count > 0;
        SetDim(anyTouched);
        if (!anyTouched) {
            voiceManager.CancelListening();
            _activeVoiceCube = -1;
        }
    }

    // =====================================================
    // GESTURES
    // =====================================================

    void DetectShake(int id, CubeState cube)
    {
        if (touchedCubeIDs.Count > 1)
        {
            ResetShakeState(id);
            return;
        }

        cube.touchHeldTime += Time.deltaTime; // always tick up while touched

        if (cube.touchHeldTime < 0.3f)
        {
            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
            return;
        }

        Vector3 velocity = cube.filteredRawVelocity;
        float speed = velocity.magnitude;

        if (speed < 0.5f) return;

        float dot = Vector3.Dot(velocity.normalized, cube.lastRawVelocity.normalized);
        if (dot < -0.6f)
            cube.shakeDirectionChanges++;
        cube.lastRawVelocity = velocity;

        cube.shakeTimer += Time.deltaTime;

        if (cube.shakeTimer > 0.5f) {
            if (cube.shakeDirectionChanges >= 3)
                OnCubeShaken(id);

            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void OnCubeShaken(int id) {
        bool hasReminder = reminderManager.HasReminder(id);
        bool alreadyDone = completedOrderedTasks.Contains(id) || _dismissedCubeIDs.Contains(id);

        if (!hasReminder || alreadyDone) return;

        if (cubeToGroup.TryGetValue(id, out CubeGroup group))
        {
            if (group.type == GroupType.Ordered)
            {
                int currentCube = GetCurrentOrderedCube(group);
                if (id != currentCube)
                {
                    if (debugText != null)
                        debugText.text = $"Complete cube {currentCube} first!";
                    return;
                }

                completedOrderedTasks.Add(id);
                _justCompletedID = id;
                _justCompletedTimer = JUST_COMPLETED_DISPLAY_TIME;
                RefreshGroupUI(group);
                return;
            }

            // Unordered group
            _dismissedCubeIDs.Add(id);
            RefreshGroupUI(group); // ← was already here, should work
            return;
        }

        // Solo cube — no group UI to refresh, just mark it
        _dismissedCubeIDs.Add(id);
        if (debugText != null)
            debugText.text = $"Cube {id}: task complete.";
    }

    void SetOutline(GameObject cube, bool show, Color? outlineColor = null)
    {
        Transform cubeChild = cube.transform.Find("Cube");
        if (cubeChild == null) return;

        MeshRenderer renderer = cubeChild.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        int id = -1;
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
            if (kvp.Value == cube) { id = kvp.Key; break; }

        Material[] mats = renderer.sharedMaterials;

        if (show)
        {
            if (!_outlineMaterialInstances.ContainsKey(id))
                _outlineMaterialInstances[id] = new Material(outlineMaterial);

            if (outlineColor.HasValue)
            {
                if (_outlineMaterialInstances[id].HasProperty("_Color"))
                    _outlineMaterialInstances[id].SetColor("_Color", outlineColor.Value);
                else if (_outlineMaterialInstances[id].HasProperty("_OutlineColor"))
                    _outlineMaterialInstances[id].SetColor("_OutlineColor", outlineColor.Value);
            }

            if (mats.Length < 2)
            {
                renderer.materials = new Material[]
                {
                    mats[0],
                    _outlineMaterialInstances[id]
                };
            }
        }
        else if (mats.Length >= 2) {
            renderer.materials = new Material[] { mats[0] };
        }
    }

    public void SetDim(bool dim)
    {
        float targetBrightness = dim ? -0.3f : 0f;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeTo(targetBrightness));
    }

    private IEnumerator FadeTo(float target)
    {
        float start = passthroughLayer.colorMapEditorBrightness;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * fadeSpeed;
            float brightness = Mathf.Lerp(start, target, t);
            passthroughLayer.SetBrightnessContrastSaturation(brightness, 0f, 0f);
            yield return null;
        }

        passthroughLayer.SetBrightnessContrastSaturation(target, 0f, 0f);
    }

    void UpdateCubeMotion()
    {
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
                cube.filteredRawVelocity = Vector3.Lerp(cube.filteredRawVelocity, cube.rawVelocity, 0.2f);
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

    void HandleCubeGrouping()
    {
        if (_activeVoiceCube != -1)
            return;
    
        // COMBINE
        for (int i = 0; i < touchedCubeIDs.Count; i++)
        {
            for (int j = i + 1; j < touchedCubeIDs.Count; j++)
            {
                int idA = touchedCubeIDs[i];
                int idB = touchedCubeIDs[j];

                float dist = Vector3.Distance(
                    _cubes[idA].transform.position,
                    _cubes[idB].transform.position);

                if (!reminderManager.HasReminder(idA) || !reminderManager.HasReminder(idB))
                    continue;

                if (dist < combineDistance)
                    CombineCubes(idA, idB);
            }
        }

        SplitDisconnectedGroups();
    }

    void SyncGroupVisibility(CubeGroup group)
    {
        bool inGroup = group.cubeIDs.Count > 1;
        foreach (int id in group.cubeIDs)
            reminderManager.SetCubeGrouped(id, inGroup);
    }

    void CombineCubes(int idA, int idB)
    {
        if (!reminderManager.HasReminder(idA) || !reminderManager.HasReminder(idB)) return;

        ResetShakeState(idA);
        ResetShakeState(idB);

        CubeGroup groupA = cubeToGroup.ContainsKey(idA) ? cubeToGroup[idA] : null;
        CubeGroup groupB = cubeToGroup.ContainsKey(idB) ? cubeToGroup[idB] : null;

        // Don't add to a locked group
        if (groupA != null && _lockedGroups.Contains(groupA.groupID)) return;
        if (groupB != null && _lockedGroups.Contains(groupB.groupID)) return;

        if (groupA != null && groupA == groupB) return;

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
            SyncGroupVisibility(newGroup);
            return;
        }

        if (groupA != null && groupB == null)
        {
            groupA.cubeIDs.Add(idB);
            cubeToGroup[idB] = groupA;
            UpdateGroupSemantics(groupA);
            RefreshGroupUI(groupA);
            SyncGroupVisibility(groupA);
            return;
        }

        if (groupB != null && groupA == null)
        {
            groupB.cubeIDs.Add(idA);
            cubeToGroup[idA] = groupB;
            UpdateGroupSemantics(groupB);
            RefreshGroupUI(groupB);
            SyncGroupVisibility(groupB);
            return;
        }

        // Merge B into A
        foreach (int id in groupB.cubeIDs)
        {
            groupA.cubeIDs.Add(id);
            cubeToGroup[id] = groupA;
        }
        groups.Remove(groupB.groupID);
        if (groupB.groupUI != null) Destroy(groupB.groupUI);
        _pendingGroupTypes.Remove(groupB.groupID);
        _pendingGroupTypeTimers.Remove(groupB.groupID);

        UpdateGroupSemantics(groupA);
        RefreshGroupUI(groupA);
        SyncGroupVisibility(groupA);
    }

    void SplitDisconnectedGroups()
    {
        List<CubeGroup> groupsToCheck = groups.Values.ToList();

        foreach (CubeGroup group in groupsToCheck)
        {
            if (!groups.ContainsKey(group.groupID)) continue;
            if (_lockedGroups.Contains(group.groupID)) continue;
            if (group.cubeIDs.Count <= 1) continue;
            if (!group.cubeIDs.Any(id => _cubes[id].isTouched)) continue;

            List<HashSet<int>> components = BuildConnectedComponents(group);
            if (components.Count <= 1) continue;

            HashSet<int> largest = components
                .OrderByDescending(component => component.Count)
                .First();

            group.cubeIDs = largest;
            foreach (int id in largest)
                cubeToGroup[id] = group;

            UpdateGroupSemantics(group);
            RefreshGroupUI(group);
            SyncGroupVisibility(group);

            foreach (HashSet<int> component in components)
            {
                if (component == largest) continue;

                CubeGroup newGroup = new CubeGroup();
                newGroup.groupID = nextGroupID++;
                newGroup.cubeIDs = component;
                groups[newGroup.groupID] = newGroup;

                foreach (int id in component)
                    cubeToGroup[id] = newGroup;

                UpdateGroupSemantics(newGroup);
                CreateGroupUI(newGroup);
                SyncGroupVisibility(newGroup);
            }
        }
    }

    List<HashSet<int>> BuildConnectedComponents(CubeGroup group)
    {
        List<int> ids = group.cubeIDs.ToList();
        Dictionary<int, List<int>> neighbors = new();

        foreach (int id in ids)
            neighbors[id] = new List<int>();

        for (int i = 0; i < ids.Count; i++)
        {
            for (int j = i + 1; j < ids.Count; j++)
            {
                int idA = ids[i];
                int idB = ids[j];
                float dist = Vector3.Distance(
                    _cubes[idA].transform.position,
                    _cubes[idB].transform.position);

                if (dist <= disconnectDistance)
                {
                    neighbors[idA].Add(idB);
                    neighbors[idB].Add(idA);
                }
            }
        }

        List<HashSet<int>> components = new();
        HashSet<int> visited = new();

        foreach (int start in ids)
        {
            if (visited.Contains(start)) continue;

            HashSet<int> component = new();
            Queue<int> queue = new();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                component.Add(current);

                foreach (int next in neighbors[current])
                {
                    if (visited.Contains(next)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    void DisconnectCubePair(int idA, int idB)
    {
        if (!cubeToGroup.ContainsKey(idA)) return;

        CubeGroup originalGroup = cubeToGroup[idA];
         
        if (_lockedGroups.Contains(originalGroup.groupID)) return;
        if (!originalGroup.cubeIDs.Contains(idB)) return;
        if (originalGroup.cubeIDs.Count <= 1) return;

        originalGroup.cubeIDs.Remove(idB);

        CubeGroup newGroup = new CubeGroup();
        newGroup.groupID = nextGroupID++;
        newGroup.cubeIDs.Add(idB);
        groups[newGroup.groupID] = newGroup;
        cubeToGroup[idB] = newGroup;

        UpdateGroupSemantics(originalGroup);
        UpdateGroupSemantics(newGroup);

        CreateGroupUI(newGroup);
        RefreshGroupUI(originalGroup);

        // Sync visibility for both groups after split
        SyncGroupVisibility(originalGroup);
        SyncGroupVisibility(newGroup);
    }

    // =====================================================
    // GROUP SEMANTICS
    // =====================================================

    void ResetShakeState(int id)
    {
        if (!_cubes.TryGetValue(id, out CubeState cube)) return;

        cube.shakeDirectionChanges = 0;
        cube.shakeTimer = 0f;
        cube.touchHeldTime = 0f;
        cube.lastRawVelocity = Vector3.zero;
        cube.filteredRawVelocity = Vector3.zero;
    }

    void UpdateAllGroupSemantics()
    {
        foreach (var group in groups.Values)
            UpdateGroupSemantics(group);
    }

    void UpdateGroupSemantics(CubeGroup group) {
        GroupType detected = DetectGroupType(group);

        if (!_pendingGroupTypes.ContainsKey(group.groupID))
        {
            _pendingGroupTypes[group.groupID]      = detected;
            _pendingGroupTypeTimers[group.groupID] = 0f;
        }

        if (_pendingGroupTypes[group.groupID] == detected)
        {
            _pendingGroupTypeTimers[group.groupID] += Time.deltaTime;

            // Asymmetric: slow to leave Ordered, fast to enter it
            float hysteresis = detected == GroupType.Unordered ? 0.8f : 0.2f;

            if (_pendingGroupTypeTimers[group.groupID] >= hysteresis
                && group.type != detected)
            {
                group.type = detected;
                if (group.type == GroupType.Ordered) {
                    group.orderedIDs = BuildVerticalOrdering(group.cubeIDs);
                }
                else {
                    group.orderedIDs = group.cubeIDs.ToList();
                }

                // systemText.text = $"Group {group.groupID} type changed to {group.type}";
                RefreshGroupUI(group);
            }
        }
        else
        {
            _pendingGroupTypes[group.groupID]      = detected;
            _pendingGroupTypeTimers[group.groupID] = 0f;
        }

        if (group.type == GroupType.Ordered)
            group.orderedIDs = BuildVerticalOrdering(group.cubeIDs);
        else
            group.orderedIDs = group.cubeIDs.ToList();
    }

    GroupType DetectGroupType(CubeGroup group)
    {
        if (group.cubeIDs.Count < 2)
            return GroupType.Unordered;

        int sideCount = 0;

        foreach (int id in group.cubeIDs)
        {
            Transform t = _cubes[id].transform;

            float dotUp = Mathf.Abs(
                Vector3.Dot(t.forward.normalized, Vector3.up));
            
            // systemText.text = $"Cube {id} dotUp = {dotUp:F2}";

            if (dotUp > 0.8f)
                sideCount++;
        }

        bool stacked = IsRoughlyVerticalStack(group.cubeIDs.ToList());
        
        if (sideCount < group.cubeIDs.Count * 0.5f && stacked) {
            // systemText.text = "detected ORDERED";
            return GroupType.Ordered;
        }
        
        // systemText.text = "detected UNORDERED";
        return GroupType.Unordered;
        
    }

    bool IsRoughlyVerticalStack(List<int> ids)
    {
        if (ids.Count < 2) return false;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (int id in ids)
        {
            Vector3 p = _cubes[id].transform.position;
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z);
            maxZ = Mathf.Max(maxZ, p.z);
        }

        float verticalSpread = maxY - minY;
        float horizontalSpread = Mathf.Max(maxX - minX, maxZ - minZ);

        // systemText.text = $"vertical={verticalSpread:F3}, " + $"horizontal={horizontalSpread:F3}";

        return verticalSpread > orderedMinVerticalSpread && horizontalSpread < orderedMaxHorizontalSpread;;
    }

    List<int> BuildVerticalOrdering(HashSet<int> ids) {
        List<int> ordered = ids.ToList();
        ordered.Sort((a, b) =>
        {
            float ay = _cubes[a].transform.position.y;
            float by = _cubes[b].transform.position.y;
            return by.CompareTo(ay); // highest Y first (top of stack = first task)
        });
        return ordered;
    }

    // =====================================================
    // ORDERED STACK TRAVERSAL
    // =====================================================

    public int GetCurrentOrderedCube(CubeGroup group)
    {
        if (group.type != GroupType.Ordered) return -1;

        foreach (int id in group.orderedIDs)
        {
            if (!completedOrderedTasks.Contains(id))
                return id;
        }
        return -1;
    }

    // =====================================================
    // ROUTINE GROUP UI
    // =====================================================
    void CreateGroupUI(CubeGroup group) {
        RefreshGroupUI(group);
    }
    // void CreateGroupUI(CubeGroup group)
    // {
    //     if (groupUIPrefab == null) return;

    //     Vector3 center = GetGroupCenter(group) + Vector3.up * 0.15f;
    //     GameObject ui = Instantiate(groupUIPrefab, center, Quaternion.identity);
    //     group.groupUI = ui;
    //     RefreshGroupUI(group);
    // }

    // void RefreshGroupUI(CubeGroup group)
    // {
    //     if (group.groupUI == null) return;

    //     bool isActive = group.cubeIDs.Count > 1;
    //     group.groupUI.SetActive(isActive);
    //     if (!isActive) return;

    //     // Position above group center
    //     Vector3 center = GetGroupCenter(group);
    //     Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
    //     group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.25f;

    //     Vector3 toCamera = Camera.main.transform.position - group.groupUI.transform.position;
    //     group.groupUI.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);

    //     // Build data
    //     // systemText.text = $"group type for refresh is {group.type}"; 
    //     GroupUIData data = new GroupUIData
    //     {
    //         groupType       = group.type,
    //         completedIDs    = new HashSet<int>(completedOrderedTasks.Union(_dismissedCubeIDs)),
    //         justCompletedID = _justCompletedID,
    //     };

    //     List<int> displayOrder = group.type == GroupType.Ordered ? group.orderedIDs : group.cubeIDs.ToList();

    //     int orderIndex = 0;
    //     foreach (int id in displayOrder)
    //     {
    //         if (!reminderManager.TryGetReminderData(id, out var reminder)) continue;

    //         data.items.Add(new GroupItemData
    //         {
    //             cubeId      = id,
    //             task        = reminder.task,
    //             icon        = reminder.icon,
    //             triggerTime = reminder.triggerTime,
    //             orderIndex  = orderIndex++,
    //         });
    //     }

    //     GroupUIManager uiManager = group.groupUI.GetComponent<GroupUIManager>();
    //     if (uiManager != null)
    //         uiManager.Refresh(data);
    // }
    void EnsureCorrectUIPrefab(CubeGroup group)
    {
        if (group.groupUI != null && group.spawnedUIType == group.type)
            return; // already correct prefab, no swap needed

        // Destroy old UI
        if (group.groupUI != null)
            Destroy(group.groupUI);

        // Spawn correct prefab
        Vector3 center = GetGroupCenter(group) + Vector3.up * 0.25f;
        GameObject prefab = group.type == GroupType.Ordered ? stepsUIPrefab : toDoUIPrefab;
        group.groupUI = Instantiate(prefab, center, Quaternion.identity);
        group.spawnedUIType = group.type;
    }

    void RefreshGroupUI(CubeGroup group)
    {
        if (group.cubeIDs.Count <= 1)
        {
            if (group.groupUI != null) group.groupUI.SetActive(false);
            return;
        }

        EnsureCorrectUIPrefab(group);
        group.groupUI.SetActive(true);

        // Position
        Vector3 center = GetGroupCenter(group);
        Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
        group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.25f;
        Vector3 toCamera = Camera.main.transform.position - group.groupUI.transform.position;
        group.groupUI.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);

        // Build data
        GroupUIData data = new GroupUIData
        {
            groupType    = group.type,
            completedIDs = new HashSet<int>(completedOrderedTasks.Union(_dismissedCubeIDs)),
            justCompletedID = _justCompletedID,
        };

        List<int> displayOrder = group.type == GroupType.Ordered
            ? group.orderedIDs
            : group.cubeIDs.ToList();

        int orderIndex = 0;
        foreach (int id in displayOrder)
        {
            if (!reminderManager.TryGetReminderData(id, out var reminder)) continue;
            data.items.Add(new GroupItemData
            {
                cubeId      = id,
                task        = reminder.task,
                icon        = reminder.icon,
                triggerTime = reminder.triggerTime,
                orderIndex  = orderIndex++,
            });
        }

        GroupUIBase ui = group.groupUI.GetComponent<GroupUIBase>();
        if (ui != null) ui.Refresh(data);
    }


    Vector3 GetGroupCenter(CubeGroup group)
    {
        Vector3 sum = Vector3.zero;
        foreach (int id in group.cubeIDs)
            sum += _cubes[id].transform.position;
        return sum / group.cubeIDs.Count;
    }

    void UpdateRoutines()
    {
        foreach (var group in groups.Values)
        {
            if (group.groupUI == null) continue;

            Vector3 center = GetGroupCenter(group);
            Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
            group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.2f;
            
            Vector3 toCamera = Camera.main.transform.position - group.groupUI.transform.position;
            group.groupUI.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
        }
    }

    void UpdateReturnArrows()
    {
        foreach (int id in touchedCubeIDs)
        {
            bool homed = _cubeHomed.ContainsKey(id) && _cubeHomed[id];
            if (!homed) continue;

            Vector3 homePos = _cubeHomePositions[id];
            Vector3 cubePos = _cubes[id].transform.position;
            float distFromHome = Vector3.Distance(cubePos, homePos);

            bool dismissed = _dismissedCubeIDs.Contains(id);
            bool inRoutine = cubeToGroup.ContainsKey(id) && cubeToGroup[id].cubeIDs.Count > 1;
            bool pickedUp  = _cubes[id].isTouched;

            bool shouldShowArrow = !dismissed && !inRoutine && (distFromHome > awayFromHomeThreshold) && pickedUp;

            if (shouldShowArrow)
            {
                ShowReturnArrow(id, homePos);
                return;
            }
        }
        HideReturnArrow();
    }

    void ShowReturnArrow(int id, Vector3 homePos)
    {
        if (groundPathArrow == null) return;
        groundPathArrow.GetComponent<LineRenderer>().enabled = true;
        groundPathArrow.startPosition  = _cubes[id].transform.position;
        groundPathArrow.targetPosition = homePos;
    }

    void HideReturnArrow()
    {
        if (groundPathArrow != null)
            groundPathArrow.GetComponent<LineRenderer>().enabled = false;
    }

    // =====================================================
    // POINT GESTURE FOR HOMING
    // =====================================================

    int GetPointedCubeID(OVRSkeleton skeleton)
    {
        Vector3? fingertip = GetIndexFingertipPosition(skeleton);
        if (fingertip == null) return -1;

        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            GameObject cubeObj = kvp.Value;
            Transform cubeChild = cubeObj.transform.Find("Cube");
            if (cubeChild == null) continue;

            Collider col = cubeChild.GetComponent<Collider>();
            if (col == null) continue;

            Vector3 closest = col.ClosestPoint(fingertip.Value);
            if (Vector3.Distance(fingertip.Value, closest) < 0.2f)
                return id;
        }
        return -1;
    }

    Vector3? GetIndexFingertipPosition(OVRSkeleton skeleton)
    {
        foreach (var bone in skeleton.Bones)
            if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip)
                return bone.Transform.position;
        return null;
    }

    public void OnLeftPointGesture()
    {
        int id = GetPointedCubeID(leftSkeleton);
        if (!reminderManager.HasReminder(id)) return;

        Vector3 anchorPos = _cubes[id].transform.position;
        _cubeHomePositions[id] = anchorPos;
        _cubeHomed[id] = true;

        if (debugText != null)
            debugText.text = $"Cube {id} home set at {anchorPos:F2}";

        if (_homeMarkers.TryGetValue(id, out GameObject oldMarker))
            Destroy(oldMarker);

        GameObject marker = Instantiate(homeMarkerPrefab, anchorPos, Quaternion.identity);
        _homeMarkers[id] = marker;
    }

    public void OnRightPointGesture()
    {
        int id = GetPointedCubeID(rightSkeleton);
        if (!reminderManager.HasReminder(id)) return;

        Vector3 anchorPos = _cubes[id].transform.position;
        _cubeHomePositions[id] = anchorPos;
        _cubeHomed[id] = true;

        if (debugText != null)
            debugText.text = $"Cube {id} home set at {anchorPos:F2}";

        if (_homeMarkers.TryGetValue(id, out GameObject oldMarker))
            Destroy(oldMarker);

        GameObject marker = Instantiate(homeMarkerPrefab, anchorPos, Quaternion.identity);
        _homeMarkers[id] = marker;
    }

    // =====================================================
    // HOVER HAND TO LOCK ROUTINE
    // =====================================================
    public void OnLeftHandOverGroup() {
        // Find which group the hand is over
        CubeGroup targetGroup = GetGroupUnderHand(leftSkeleton);
        if (targetGroup == null) return;

        if (_lockedGroups.Contains(targetGroup.groupID))
        {
            // Unlock
            _lockedGroups.Remove(targetGroup.groupID);
            Debug.Log($"Group {targetGroup.groupID} unlocked");
        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            Debug.Log($"Group {targetGroup.groupID} locked");
        }
    }

    public void OnRightHandOverGroup() {
        // Find which group the hand is over
        CubeGroup targetGroup = GetGroupUnderHand(rightSkeleton);
        if (targetGroup == null) return;

        if (_lockedGroups.Contains(targetGroup.groupID))
        {
            // Unlock
            _lockedGroups.Remove(targetGroup.groupID);
            Debug.Log($"Group {targetGroup.groupID} unlocked");
        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            Debug.Log($"Group {targetGroup.groupID} locked");
        }
    }


    CubeGroup GetGroupUnderHand(OVRSkeleton skeleton) {
        // Use whichever hand is making the gesture —
        // check both and return the first group found
        Vector3? palmPos = GetPalmPosition(skeleton); 
        if (palmPos == null) return null;

        CubeGroup closest = null;
        float closestDist = float.MaxValue;

        foreach (var group in groups.Values)
        {
            if (group.cubeIDs.Count < 2) continue;

            Vector3 center = GetGroupCenter(group);

            // Must be roughly above the group
            if (palmPos.Value.y < center.y) continue;

            float xzDist = Vector2.Distance(
                new Vector2(palmPos.Value.x, palmPos.Value.z),
                new Vector2(center.x, center.z));

            if (xzDist < 0.2f && xzDist < closestDist)
            {
                closestDist = xzDist;
                closest = group;
            }
        }

        return closest;
    }

    Vector3? GetPalmPosition(OVRSkeleton skeleton) {
        if (skeleton == null || !skeleton.IsInitialized 
            || skeleton.Bones == null) return null;

        foreach (var bone in skeleton.Bones)
        {
            if (bone.Id == OVRSkeleton.BoneId.Hand_WristRoot)
                return bone.Transform.position;
        }
        return null;
    }

    void UpdateLockedGroupVisibility() {
        foreach (var group in groups.Values)
        {
            if (!_lockedGroups.Contains(group.groupID)) continue;
            Vector3 center = GetGroupCenter(group);
            foreach (int id in group.cubeIDs) {
                if (!_cubes.ContainsKey(id)) continue;

                float dist = Vector3.Distance(
                    _cubes[id].transform.position, center);

                bool awayFromGroup = dist > showReminderWhenAwayDistance;

                // Show individual reminder when away, hide when back near group
                reminderManager.SetCubeGrouped(id, !awayFromGroup);
            }
        }
    }

    private void HandleReminderCreated(int cubeId) {
        if (_activeVoiceCube == cubeId)
            _activeVoiceCube = -1;
    }

}
