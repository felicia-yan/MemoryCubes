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

        public Vector3 smoothedPosition;
        public Vector3 smoothedVelocity;
        public Vector3 lastSmoothedPosition;
        public Vector3 lastSmoothedVelocity;

        public bool smoothingInitialized;

        public Quaternion smoothedRotation;
        public bool rotationInitialized;

        public float lastSeenTime;
        public float lastTouchedTime;
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

        // Position smoothing
        public Vector3 smoothedCenter;
        public bool centerInitialized;

    }
    [SerializeField] private float groupUIPositionSmoothing = 0.05f;

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

    // Gesture thresholds
    [SerializeField] private float spinThreshold = 1.5f;
    [SerializeField] private float shakeSpeedThreshold = 0.1f;
    [SerializeField] [Range(0f, 1f)] private float positionSmoothing = 0.3f;
    [SerializeField] [Range(0f, 1f)] private float rotationSmoothing = 0.1f;
    
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
    
    // Overlay for instructions about allowed gestures
    [SerializeField] private GameObject gestureHelpUI;
    [SerializeField] private float gestureHelpDelay = 4.0f;

    private HashSet<int> _gestureHelpShown = new();



    // =====================================================

    void Start()
    {
        SetDim(false); 
        Camera.main.transparencySortMode = TransparencySortMode.CustomAxis;
        Camera.main.transparencySortAxis = new Vector3(0, 0, 1); // sort along Z (depth)
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

        foreach (int id in _detectedIDs) {
            CubeState cube = _cubes[id];
            bool recentlyTouched = cube.isTouched ||
                (Time.time - cube.lastTouchedTime < 0.35f &&
                Time.time - cube.lastSeenTime < 0.35f);

            if (!recentlyTouched) continue;
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
                    lastRawVelocity = Vector3.zero, 
                    lastSeenTime = Time.time 
                };

                if (!_initializedCubeIDs.Contains(id))
                {
                    _initializedCubeIDs.Add(id);
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
                _cubes[id].lastSeenTime = Time.time; 
            }
        }
    }

    void UpdateTouchedCubes()
    {
        List<int> previouslyTouched = new List<int>(touchedCubeIDs);

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
                _cubes[id].lastTouchedTime = Time.time;
            }
        }

        // Increment touchHeldTime for all currently touched cubes
        foreach (int id in touchedCubeIDs)
            _cubes[id].touchHeldTime += Time.deltaTime;

        // Handle touch-start
        foreach (int id in _detectedIDs)
        {
            if (_cubes[id].isTouched && !previouslyTouched.Contains(id))
            {
                _anchoredCubeIDs.Remove(id);
                _cubeAnchors.Remove(id);
                _gestureHelpShown.Remove(id);
                _cubes[id].touchHeldTime = 0f;
                _cubes[id].lastTouchedTime = Time.time;

                bool hasReminder = reminderManager.HasReminder(id);
                if (_activeVoiceCube == -1)
                {
                    _activeVoiceCube = id;
                    if (hasReminder)
                        voiceManager.BeginDeleteListening(id);
                    else
                        voiceManager.BeginReminderCreation(id);
                }
            }
        }

        // Handle touch-end
        foreach (int id in previouslyTouched)
        {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id))
            {
                if (id == _activeVoiceCube)
                {
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

        // Outlines
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            if (!_cubes.ContainsKey(id)) continue;

            if (_cubes[id].isTouched)
                SetOutline(kvp.Value, true, Color.white);
            else
                SetOutline(kvp.Value, false);
        }

        // Cube child mesh visibility
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
        if (!anyTouched)
        {
            voiceManager.CancelListening();
            _activeVoiceCube = -1;
        }

        UpdateGestureHelp();
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

        // cube.touchHeldTime += Time.deltaTime;
        if (cube.touchHeldTime < 0.2f) return;

        Vector3 pos = cube.transform.position;
        float horizontalDelta = new Vector2(pos.x - cube.lastSmoothedPosition.x, pos.z - cube.lastSmoothedPosition.z).magnitude;

        if (horizontalDelta > 0.01f) // moved more than 1cm horizontally this frame
        {
            Vector2 currentDir = new Vector2(pos.x - cube.lastSmoothedPosition.x, pos.z - cube.lastSmoothedPosition.z).normalized;
            Vector2 lastDir = new Vector2(cube.lastSmoothedVelocity.x, cube.lastSmoothedVelocity.z).normalized;

            if (lastDir.magnitude > 0.001f && Vector2.Dot(currentDir, lastDir) < -0.3f)
                cube.shakeDirectionChanges++;

            cube.lastSmoothedVelocity = new Vector3(currentDir.x, 0, currentDir.y); // reuse field to store last direction
        }

        cube.shakeTimer += Time.deltaTime;
        // systemText.text = $"[Shake] id={id} held={cube.touchHeldTime:F2} changes={cube.shakeDirectionChanges} timer={cube.shakeTimer:F2}";

        if (cube.shakeTimer > 0.6f)
        {
            if (cube.shakeDirectionChanges >= 3)
                OnCubeShaken(id);

            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void OnCubeShaken(int id) {
        systemText.text = $"[Shaken] id={id} hasReminder={reminderManager.HasReminder(id)} dismissed={_dismissedCubeIDs.Contains(id)}";

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
                reminderManager.SetCubeDismissed(id);
                _justCompletedID = id;
                _justCompletedTimer = JUST_COMPLETED_DISPLAY_TIME;
                ForceRebuildGroupUI(group);
                return;
            }

            // Unordered group
            _dismissedCubeIDs.Add(id);
            reminderManager.SetCubeDismissed(id);
            ForceRebuildGroupUI(group); // ← was already here, should work
            return;
        }

        // Solo cube — no group UI to refresh, just mark it
        _dismissedCubeIDs.Add(id);
        reminderManager.SetCubeDismissed(id);
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
                cube.lastRawPosition = rawPos;

                // SMOOTH POSITION + VELOCITY
                if (!cube.smoothingInitialized)
                {
                    cube.smoothedPosition = t.position;
                    cube.lastSmoothedPosition = t.position;
                    cube.smoothingInitialized = true;
                }
                else
                {
                    cube.smoothedPosition = Vector3.Lerp(cube.smoothedPosition, t.position, positionSmoothing);
                }

                cube.smoothedVelocity = (cube.smoothedPosition - cube.lastSmoothedPosition) / Time.deltaTime;
                cube.lastSmoothedPosition = cube.smoothedPosition;
            }
            else
            {
                cube.rawVelocity = cube.velocity;
                cube.lastRawPosition = t.position;
            }

            if (!cube.rotationInitialized) {
                cube.smoothedRotation = t.rotation;
                cube.rotationInitialized = true;
            }
            else {
                cube.smoothedRotation = Quaternion.Slerp(cube.smoothedRotation, t.rotation, rotationSmoothing);
            }
            
            t.rotation = cube.smoothedRotation;
        }
    }

    // =====================================================
    // GROUPING INTO ROUTINES
    // =====================================================

    void HandleCubeGrouping() {
        // COMBINE: touched cube near any other detected cube (not just other touched cubes)
        foreach (int idA in touchedCubeIDs)
        {
            foreach (int idB in _detectedIDs)
            {
                if (idA == idB) continue;

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
        // Debugging ugh...
        // if (cubeToGroup.TryGetValue(idA, out CubeGroup dbgGroup))
        // {
        //     systemText.text =
        //         $"idA={idA} groupSize={dbgGroup.cubeIDs.Count} ids={string.Join(",", dbgGroup.cubeIDs)}";
        // }
        // else
        // {
        //     systemText.text = $"idA={idA} not in a group yet";
        // }

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

            // Handle the largest component
            if (largest.Count == 1)
            {
                int soloId = largest.First();
                cubeToGroup.Remove(soloId);
                reminderManager.SetCubeGrouped(soloId, false);
                if (group.groupUI != null) Destroy(group.groupUI);
                groups.Remove(group.groupID);
                _pendingGroupTypes.Remove(group.groupID);
                _pendingGroupTypeTimers.Remove(group.groupID);
            }
            else
            {
                group.cubeIDs = largest;
                foreach (int id in largest)
                    cubeToGroup[id] = group;

                UpdateGroupSemantics(group);
                RefreshGroupUI(group);
                SyncGroupVisibility(group);
            }

            // Handle remaining components
            foreach (HashSet<int> component in components)
            {
                if (component == largest) continue;

                if (component.Count == 1)
                {
                    int soloId = component.First();
                    cubeToGroup.Remove(soloId);
                    reminderManager.SetCubeGrouped(soloId, false);
                    continue;
                }

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
        
        const float TOUCH_GRACE = 0.4f;
        float timeSinceLastTouch = Time.time - _cubes[id].lastTouchedTime;
        bool gapWasLong = timeSinceLastTouch > TOUCH_GRACE;
        if (gapWasLong)
            _cubes[id].touchHeldTime = 0f;

        cube.lastRawVelocity = Vector3.zero;
        cube.smoothedVelocity = Vector3.zero;
        cube.lastSmoothedPosition = cube.smoothedPosition; // prevents velocity spike on resume
        cube.smoothingInitialized = false;
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

                systemText.text = $"Group {group.groupID} type changed to {group.type}";
                RefreshGroupUI(group);
                SyncGroupVisibility(group);
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

    void EnsureCorrectUIPrefab(CubeGroup group) {
        if (group.groupUI != null && group.spawnedUIType == group.type)
            return;

        if (group.groupUI != null)
            Destroy(group.groupUI);

        Vector3 center = GetGroupCenter(group);
        Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;

        float highestY = float.MinValue;
        foreach (int id in group.cubeIDs)
            highestY = Mathf.Max(highestY, _cubes[id].transform.position.y);

        Vector3 uiPos = center;
        uiPos.y = highestY + 0.15f;

        GameObject prefab = group.type == GroupType.Ordered ? stepsUIPrefab : toDoUIPrefab;
        group.groupUI = Instantiate(prefab, uiPos + awayFromCamera * 0.1f, Quaternion.identity);
        group.spawnedUIType = group.type;
    }

    void RefreshGroupUI(CubeGroup group)
    {
        Debug.Log($"RefreshGroupUI group={group.groupID} cubes={string.Join(",", group.cubeIDs)}");

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
        systemText.text = $"Passing {data.items.Count} items to UI";
        if (ui != null) ui.Refresh(data);
    }


    Vector3 GetGroupCenter(CubeGroup group)
    {
        Vector3 sum = Vector3.zero;
        foreach (int id in group.cubeIDs)
            sum += _cubes[id].smoothedPosition;
        return sum / group.cubeIDs.Count;
    }

    void UpdateRoutines() {
        foreach (var group in groups.Values)
        {
            if (group.groupUI == null) {
                RefreshGroupUI(group); // respawn if missing
                continue;
            }

            Vector3 rawCenter = GetGroupCenter(group);

            if (!group.centerInitialized)
            {
                group.smoothedCenter = rawCenter;
                group.centerInitialized = true;
            }
            else
            {
                group.smoothedCenter = Vector3.Lerp(group.smoothedCenter, rawCenter, groupUIPositionSmoothing);
            }

            Vector3 awayFromCamera = (group.smoothedCenter - Camera.main.transform.position).normalized;
            group.groupUI.transform.position = group.smoothedCenter + awayFromCamera * 0.1f + Vector3.up * 0.2f;

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

        GameObject marker = Instantiate(homeMarkerPrefab, anchorPos + Vector3.down * 0.025f, Quaternion.identity);
        _homeMarkers[id] = marker;
        
        Color cubeColor = reminderManager.GetCubeColor(id);
        cubeColor.a = 0.5f;
        Renderer markerRenderer = marker.GetComponentInChildren<Renderer>();
        if (markerRenderer != null)
            markerRenderer.material.color = cubeColor;
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

        Color cubeColor = reminderManager.GetCubeColor(id);
        cubeColor.a = 0.5f;
        Renderer markerRenderer = marker.GetComponentInChildren<Renderer>();
        if (markerRenderer != null)
            markerRenderer.material.color = cubeColor;
            
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
            systemText.text = $"Group {targetGroup.groupID} UNlocked";

        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            systemText.text = $"Group {targetGroup.groupID} locked";
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
            systemText.text = $"Group {targetGroup.groupID} UNlocked";

        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            systemText.text = $"Group {targetGroup.groupID} locked";
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

    void UpdateLockedGroupVisibility()
    {
        // Track which cubes are currently managed by a group
        HashSet<int> managedByGroup = new HashSet<int>();

        foreach (var group in groups.Values)
        {
            Vector3 center = GetGroupCenter(group);
            bool isLocked = _lockedGroups.Contains(group.groupID);

            foreach (int id in group.cubeIDs)
            {
                if (!_cubes.ContainsKey(id)) continue;
                managedByGroup.Add(id);

                if (isLocked)
                {
                    float dist = Vector3.Distance(_cubes[id].transform.position, center);
                    bool awayFromGroup = dist > showReminderWhenAwayDistance;
                    reminderManager.SetCubeGrouped(id, !awayFromGroup);
                }
                else
                {
                    reminderManager.SetCubeGrouped(id, true);
                }
            }
        }

        // Any cube not in a group should always show its individual reminder
        foreach (int id in _detectedIDs)
        {
            if (!managedByGroup.Contains(id))
                reminderManager.SetCubeGrouped(id, false);
        }
    }

    private void HandleReminderCreated(int cubeId) {
        if (_activeVoiceCube == cubeId)
            _activeVoiceCube = -1;
    }
    
    void ForceRebuildGroupUI(CubeGroup group) {   
        if (group.groupUI != null)
            Destroy(group.groupUI);
        group.groupUI = null;

        // Force EnsureCorrectUIPrefab to respawn
        group.spawnedUIType = (GroupType)(-1);
        RefreshGroupUI(group);
    }

    // Gesture instructions shown when holding onto reminder 
    void UpdateGestureHelp() {
        if (gestureHelpUI == null) return;

        if (touchedCubeIDs.Count == 0)
        {
            gestureHelpUI.SetActive(false);
            return;
        }

        int id = touchedCubeIDs[0];

        if (_gestureHelpShown.Contains(id))
        {
            UpdateGestureHelpUIPosition(id);
            return;
        }

        float heldTime = _cubes[id].touchHeldTime;

        if (heldTime >= gestureHelpDelay)
        {
            ShowGestureHelp(id);
            _gestureHelpShown.Add(id);
        }

        UpdateGestureHelpUIPosition(id);
    }   

    void ShowGestureHelp(int id) {
        bool hasReminder = reminderManager.HasReminder(id);
        bool inGroup = cubeToGroup.ContainsKey(id) && cubeToGroup[id].cubeIDs.Count > 1;
        if (hasReminder && !inGroup) {
            gestureHelpUI.SetActive(true);
        }
    }

    void UpdateGestureHelpUIPosition(int id) {
        if (!gestureHelpUI.activeSelf) return;

        Vector3 pos = _cubes[id].smoothedPosition + new Vector3(0, 0.2f, 0);

        gestureHelpUI.transform.position = pos;

        Vector3 camDir = Camera.main.transform.position - pos;
        gestureHelpUI.transform.rotation = Quaternion.LookRotation(-camDir, Vector3.up);
    }




}
