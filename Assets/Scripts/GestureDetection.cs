using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using Meta.XR;
using TryAR.MarkerTracking;
using Oculus.Interaction.Input;
using UnityEngine;
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

    private readonly Dictionary<int, CubeState> _cubes = new();
    private readonly List<int> _detectedIDs = new();

    private readonly HashSet<int> _dismissedCubeIDs = new();
    private readonly HashSet<int> _initializedCubeIDs = new();

    [SerializeField] public Material outlineMaterial;
    private readonly Dictionary<int, Material> _outlineMaterialInstances = new();

    [SerializeField] float touchThreshold = 0.15f;
    [SerializeField] public OVRSkeleton leftSkeleton;
    [SerializeField] public OVRSkeleton rightSkeleton;
    private readonly List<int> touchedCubeIDs = new();

    [SerializeField] public OVRPassthroughLayer passthroughLayer;
    [SerializeField] private float fadeSpeed = 0.5f;
    private Coroutine fadeRoutine;

    [SerializeField] private TextMeshProUGUI debugText;

    [SerializeField] private ReminderManager reminderManager;
    private readonly HashSet<int> _anchoredCubeIDs = new();
    private readonly Dictionary<int, OVRSpatialAnchor> _cubeAnchors = new();

    public enum GroupType { Ordered, Unordered }

    [System.Serializable]
    public class CubeGroup
    {
        public int groupID;
        public HashSet<int> cubeIDs = new();
        public List<int> orderedIDs = new();
        public GroupType type = GroupType.Unordered;
        public GroupType spawnedUIType = GroupType.Unordered;
        public GameObject groupUI;

        public Vector3 smoothedCenter;
        public bool centerInitialized;
    }

    [SerializeField] private float groupUIPositionSmoothing = 0.05f;

    [Header("Forming Routines")]
    private readonly HashSet<int> _lockedGroups = new();
    [SerializeField] private float combineDistance = 0.08f;
    [SerializeField] private float disconnectDistance = 0.1f;
    [SerializeField] private GameObject toDoUIPrefab;
    [SerializeField] private GameObject stepsUIPrefab;

    // legacy maps used by the rest of the script
    private readonly Dictionary<int, CubeGroup> cubeToGroup = new();
    private readonly Dictionary<int, CubeGroup> groups = new();

    private readonly HashSet<int> completedOrderedTasks = new();

    private readonly Dictionary<int, Vector3> _cubeHomePositions = new();
    private readonly Dictionary<int, bool> _cubeHomed = new();

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
    private readonly Dictionary<int, GameObject> _homeMarkers = new();

    // Group type hysteresis
    private readonly Dictionary<int, GroupType> _pendingGroupTypes = new();
    private readonly Dictionary<int, float> _pendingGroupTypeTimers = new();
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

    private readonly HashSet<int> _gestureHelpShown = new();

    // ===================== STABLE GROUP SYSTEM =====================
    private readonly Dictionary<int, HashSet<int>> connections = new();

    // We use a dedicated edge state so build/break hysteresis can't fight each other.
    private class EdgeState
    {
        public float build; // [0..connectionBuildTime]
        public float breakT; // [0..connectionBreakTime]
    }

    private readonly Dictionary<(int, int), EdgeState> edgeStates = new();

    private readonly Dictionary<int, CubeGroup> stableGroupsById = new();
    private readonly Dictionary<int, HashSet<int>> lastGroupSnapshot = new();
    private int nextStableGroupId = 1;

    [SerializeField] private float connectionBreakTime = 0.15f;
    [SerializeField] private float connectionBuildTime = 0.2f;
    // =====================================================

    void Start()
    {
        SetDim(false);
        Camera.main.transparencySortMode = TransparencySortMode.CustomAxis;
        Camera.main.transparencySortAxis = new Vector3(0, 0, 1);

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
            bool recentlyTouched = cube.isTouched ||
                (Time.time - cube.lastTouchedTime < 0.35f &&
                 Time.time - cube.lastSeenTime < 0.35f);

            if (!recentlyTouched) continue;
            DetectShake(id, cube);
        }
    }

    void TickJustCompleted()
    {
        if (_justCompletedID < 0) return;
        _justCompletedTimer -= Time.deltaTime;
        if (_justCompletedTimer <= 0f)
        {
            _justCompletedID = -1;
            foreach (var group in groups.Values)
                HardRebuildGroupUI(group);
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
                _cubes[id] = new CubeState
                {
                    transform = cubeObj.transform,
                    lastPosition = cubeObj.transform.position,
                    lastRotation = cubeObj.transform.rotation,
                    lastRawPosition = cubeObj.transform.position,
                    lastRawVelocity = Vector3.zero,
                    lastSeenTime = Time.time
                };

                _initializedCubeIDs.Add(id);
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

            EnsureCube(id);
        }
    }

    // =====================================================
    // TOUCH DETECTION
    // =====================================================
    void UpdateTouchedCubes()
    {
        List<int> previouslyTouched = new(touchedCubeIDs);

        foreach (int id in _detectedIDs)
            _cubes[id].isTouched = false;
        touchedCubeIDs.Clear();

        bool leftReady = leftSkeleton != null && leftSkeleton.IsInitialized && leftSkeleton.Bones != null && leftSkeleton.Bones.Count > 0;
        bool rightReady = rightSkeleton != null && rightSkeleton.IsInitialized && rightSkeleton.Bones != null && rightSkeleton.Bones.Count > 0;

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

        foreach (int id in touchedCubeIDs)
            _cubes[id].touchHeldTime += Time.deltaTime;

        // touch-start
        foreach (int id in _detectedIDs)
        {
            if (_cubes[id].isTouched && !previouslyTouched.Contains(id))
            {
                _anchoredCubeIDs.Remove(id);
                _cubeAnchors.Remove(id);
                _gestureHelpShown.Remove(id);
                _cubes[id].touchHeldTime = 0f;
                _cubes[id].lastTouchedTime = Time.time;

                bool hasReminder = reminderManager != null && reminderManager.HasReminder(id);
                if (_activeVoiceCube == -1 && voiceManager != null)
                {
                    _activeVoiceCube = id;
                    if (hasReminder)
                        voiceManager.BeginDeleteListening(id);
                    else
                        voiceManager.BeginReminderCreation(id);
                }
            }
        }

        // touch-end
        foreach (int id in previouslyTouched)
        {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id))
            {
                if (id == _activeVoiceCube && voiceManager != null)
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

        // outlines
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary)
        {
            int id = kvp.Key;
            if (!_cubes.ContainsKey(id)) continue;

            if (_cubes[id].isTouched)
                SetOutline(kvp.Value, true, Color.white);
            else
                SetOutline(kvp.Value, false);
        }

        // cube child mesh visibility
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
        if (!anyTouched && voiceManager != null)
        {
            voiceManager.CancelListening();
            _activeVoiceCube = -1;
        }

        UpdateGestureHelp();
    }

    // =====================================================
    // GESTURES (unchanged bodies referenced by your code)
    // =====================================================
    void DetectShake(int id, CubeState cube)
    {
        if (touchedCubeIDs.Count > 1)
        {
            ResetShakeState(id);
            return;
        }

        if (cube.touchHeldTime < 0.2f) return;

        Vector3 pos = cube.transform.position;
        float horizontalDelta = new Vector2(pos.x - cube.lastSmoothedPosition.x, pos.z - cube.lastSmoothedPosition.z).magnitude;

        if (horizontalDelta > 0.01f)
        {
            Vector2 currentDir = new Vector2(pos.x - cube.lastSmoothedPosition.x, pos.z - cube.lastSmoothedPosition.z).normalized;
            Vector2 lastDir = new Vector2(cube.lastSmoothedVelocity.x, cube.lastSmoothedVelocity.z).normalized;

            if (lastDir.magnitude > 0.001f && Vector2.Dot(currentDir, lastDir) < -0.3f)
                cube.shakeDirectionChanges++;

            cube.lastSmoothedVelocity = new Vector3(currentDir.x, 0, currentDir.y);
        }

        cube.shakeTimer += Time.deltaTime;

        if (cube.shakeTimer > 0.6f)
        {
            if (cube.shakeDirectionChanges >= 3)
                OnCubeShaken(id);

            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void OnCubeShaken(int id)
    {
        bool hasReminder = reminderManager != null && reminderManager.HasReminder(id);
        bool alreadyDone = completedOrderedTasks.Contains(id) || _dismissedCubeIDs.Contains(id);

        if (!hasReminder || alreadyDone) return;

        if (cubeToGroup.TryGetValue(id, out CubeGroup group) && group != null && group.cubeIDs.Count > 1)
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

            _dismissedCubeIDs.Add(id);
            reminderManager.SetCubeDismissed(id);
            ForceRebuildGroupUI(group);
            return;
        }

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
        else if (mats.Length >= 2)
        {
            renderer.materials = new Material[] { mats[0] };
        }
    }

    public void SetDim(bool dim)
    {
        if (passthroughLayer == null) return;

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

            if (!cube.rotationInitialized)
            {
                cube.smoothedRotation = t.rotation;
                cube.rotationInitialized = true;
            }
            else
            {
                cube.smoothedRotation = Quaternion.Slerp(cube.smoothedRotation, t.rotation, rotationSmoothing);
            }

            t.rotation = cube.smoothedRotation;
        }
    }

    // =====================================================
    // GROUP SEMANTICS
    // =====================================================
    void UpdateAllGroupSemantics()
    {
        foreach (var group in groups.Values) {
            if (_lockedGroups.Contains(group.groupID)) {
                continue; 
            }
            UpdateGroupSemantics(group);
        }
    }

    void UpdateGroupSemantics(CubeGroup group)
    {
        if (group == null) return;
        if (_lockedGroups.Contains(group.groupID)) return;

        GroupType detected = DetectGroupType(group);

        if (!_pendingGroupTypes.ContainsKey(group.groupID))
        {
            _pendingGroupTypes[group.groupID] = detected;
            _pendingGroupTypeTimers[group.groupID] = 0f;
        }

        if (_pendingGroupTypes[group.groupID] == detected)
        {
            _pendingGroupTypeTimers[group.groupID] += Time.deltaTime;

            float hysteresis = detected == GroupType.Unordered ? 0.8f : 0.2f;

            if (_pendingGroupTypeTimers[group.groupID] >= hysteresis && group.type != detected)
            {
                group.type = detected;
                group.orderedIDs = (group.type == GroupType.Ordered)
                    ? BuildVerticalOrdering(group.cubeIDs)
                    : group.cubeIDs.ToList();

                // Refresh on type changes
                HardRebuildGroupUI(group);
                SyncGroupVisibility(group);
            }
        }
        else
        {
            _pendingGroupTypes[group.groupID] = detected;
            _pendingGroupTypeTimers[group.groupID] = 0f;
        }

        if (!_lockedGroups.Contains(group.groupID))
        {
            group.orderedIDs = (group.type == GroupType.Ordered)
                ? BuildVerticalOrdering(group.cubeIDs)
                : group.cubeIDs.ToList();
        }
    }

    GroupType DetectGroupType(CubeGroup group)
    {
        if (group.cubeIDs.Count < 2) return GroupType.Unordered;

        int sideCount = 0;

        foreach (int id in group.cubeIDs)
        {
            Transform t = _cubes[id].transform;
            float dotUp = Mathf.Abs(Vector3.Dot(t.forward.normalized, Vector3.up));
            if (dotUp > 0.8f) sideCount++;
        }

        bool stacked = IsRoughlyVerticalStack(group.cubeIDs.ToList());

        if (sideCount < group.cubeIDs.Count * 0.5f && stacked)
            return GroupType.Ordered;

        return GroupType.Unordered;
    }

    bool IsRoughlyVerticalStack(List<int> ids)
    {
        if (ids.Count < 2) return false;

        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

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

        return verticalSpread > orderedMinVerticalSpread && horizontalSpread < orderedMaxHorizontalSpread;
    }

    List<int> BuildVerticalOrdering(HashSet<int> ids)
    {
        List<int> ordered = ids.ToList();
        ordered.Sort((a, b) =>
        {
            float ay = _cubes[a].transform.position.y;
            float by = _cubes[b].transform.position.y;
            return by.CompareTo(ay);
        });
        return ordered;
    }

    void SyncGroupVisibility(CubeGroup group)
    {
        if (reminderManager == null) return;
        bool inGroup = group.cubeIDs.Count > 1;
        foreach (int id in group.cubeIDs)
            reminderManager.SetCubeGrouped(id, inGroup);
    }

    // =====================================================
    // ORDERED STACK TRAVERSAL
    // =====================================================
    public int GetCurrentOrderedCube(CubeGroup group)
    {
        if (group.type != GroupType.Ordered) return -1;

        foreach (int id in group.orderedIDs)
            if (!completedOrderedTasks.Contains(id))
                return id;

        return -1;
    }

    // =====================================================
    // ROUTINE GROUP UI
    // =====================================================
    void EnsureCorrectUIPrefab(CubeGroup group)
    {
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
        if (group == null) return;

        // No UI for solo
        if (group.cubeIDs == null || group.cubeIDs.Count < 2)
        {
            if (group.groupUI != null)
            {
                Destroy(group.groupUI);
                group.groupUI = null;
                group.spawnedUIType = (GroupType)(-1);
            }
            return;
        }

        EnsureCorrectUIPrefab(group);
        if (group.groupUI == null) return;

        group.groupUI.SetActive(true);

        Vector3 center = GetGroupCenter(group);
        Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
        group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.25f;

        Vector3 toCamera = Camera.main.transform.position - group.groupUI.transform.position;
        group.groupUI.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);

        GroupUIData data = new GroupUIData
        {
            groupType = group.type,
            completedIDs = new HashSet<int>(completedOrderedTasks.Union(_dismissedCubeIDs)),
            justCompletedID = _justCompletedID,
        };

        // IMPORTANT: always rebuild display order from current cubeIDs
        List<int> displayOrder = (group.type == GroupType.Ordered)
            ? BuildVerticalOrdering(group.cubeIDs)
            : group.cubeIDs.OrderBy(x => x).ToList();

        int orderIndex = 0;
        foreach (int id in displayOrder)
        {
            // If this fails, the cube won't appear. Keeping this behavior, but now grouping bugs are fixed.
            if (reminderManager == null || !reminderManager.TryGetReminderData(id, out var reminder)) continue;

            data.items.Add(new GroupItemData
            {
                cubeId = id,
                task = reminder.task,
                icon = reminder.icon,
                triggerTime = reminder.triggerTime,
                orderIndex = orderIndex++,
            });
        }

        GroupUIBase ui = group.groupUI.GetComponent<GroupUIBase>();
        if (ui != null) ui.Refresh(data);
    }

    Vector3 GetGroupCenter(CubeGroup group)
    {
        Vector3 sum = Vector3.zero;
        foreach (int id in group.cubeIDs)
            sum += _cubes[id].smoothedPosition;
        return sum / group.cubeIDs.Count;
    }

    void UpdateRoutines()
    {
        foreach (var group in groups.Values)
        {
            if (group.cubeIDs.Count < 2) continue;

            if (group.groupUI == null)
            {
                HardRebuildGroupUI(group);
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

    // =====================================================
    // HOMING ARROWS (unchanged)
    // =====================================================
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
            bool pickedUp = _cubes[id].isTouched;

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
        groundPathArrow.startPosition = _cubes[id].transform.position;
        groundPathArrow.targetPosition = homePos;
    }

    void HideReturnArrow()
    {
        if (groundPathArrow != null)
            groundPathArrow.GetComponent<LineRenderer>().enabled = false;
    }

    // =====================================================
    // LOCKED GROUP VISIBILITY (uses legacy groups map, now synced)
    // =====================================================
    void UpdateLockedGroupVisibility()
    {
        if (reminderManager == null) return;

        HashSet<int> managedByGroup = new();

        foreach (var group in groups.Values)
        {
            if (group.cubeIDs.Count < 2) continue;

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

        foreach (int id in _detectedIDs)
            if (!managedByGroup.Contains(id))
                reminderManager.SetCubeGrouped(id, false);
    }

    // =====================================================
    // RESET SHAKE STATE
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
        cube.lastSmoothedPosition = cube.smoothedPosition;
        cube.smoothingInitialized = false;
    }

    // =====================================================
    // GESTURE HELP (unchanged)
    // =====================================================
    void UpdateGestureHelp()
    {
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

    void ShowGestureHelp(int id)
    {
        bool hasReminder = reminderManager != null && reminderManager.HasReminder(id);
        bool inGroup = cubeToGroup.ContainsKey(id) && cubeToGroup[id].cubeIDs.Count > 1;
        if (hasReminder && !inGroup)
            gestureHelpUI.SetActive(true);
    }

    void UpdateGestureHelpUIPosition(int id)
    {
        if (!gestureHelpUI.activeSelf) return;

        Vector3 pos = _cubes[id].smoothedPosition + new Vector3(0, 0.2f, 0);
        gestureHelpUI.transform.position = pos;

        Vector3 camDir = Camera.main.transform.position - pos;
        gestureHelpUI.transform.rotation = Quaternion.LookRotation(-camDir, Vector3.up);
    }

    private void HandleReminderCreated(int cubeId)
    {
        if (_activeVoiceCube == cubeId)
            _activeVoiceCube = -1;
    }

    void ForceRebuildGroupUI(CubeGroup group)
    {
        if (group.groupUI != null)
            Destroy(group.groupUI);
        group.groupUI = null;
        group.spawnedUIType = (GroupType)(-1);
        RefreshGroupUI(group);
    }

    // =====================================================
    // STABLE GROUPING (fixed)
    // =====================================================
    void HandleCubeGrouping()
    {
        float dt = Time.deltaTime;

        // Ensure edge states for detected cubes exist
        foreach (int id in _detectedIDs) EnsureCube(id);

        // 1) Build edges only while touching (your UX)
        foreach (int idA in touchedCubeIDs)
        {
            foreach (int idB in _detectedIDs)
            {
                if (idA == idB) continue;

                if (reminderManager == null) continue;
                if (!reminderManager.HasReminder(idA) || !reminderManager.HasReminder(idB))
                    continue;

                float dist = Vector3.Distance(_cubes[idA].transform.position, _cubes[idB].transform.position);
                var key = EdgeKey(idA, idB);

                if (!edgeStates.TryGetValue(key, out var st))
                {
                    st = new EdgeState();
                    edgeStates[key] = st;
                }

                if (dist < combineDistance)
                {
                    st.build = Mathf.Min(connectionBuildTime, st.build + dt);
                    st.breakT = Mathf.Max(0f, st.breakT - dt); // cancel breaking

                    if (st.build >= connectionBuildTime)
                        AddConnection(key.Item1, key.Item2);
                }
                else
                {
                    // if we are not close, slowly decay build so it doesn't "stick"
                    st.build = Mathf.Max(0f, st.build - dt);
                }
            }
        }

        // 2) Break edges every frame (even not touched)
        EvaluateConnectionBreaks(dt);

        // 3) Build groups from graph and bridge to legacy
        BuildStableGroupsAndSync();
    }

    void EvaluateConnectionBreaks(float dt)
    {
        if (connections.Count == 0) return;

        var detectedSet = new HashSet<int>(_detectedIDs);

        // collect unique undirected edges
        List<(int a, int b)> edges = new();
        foreach (var kvp in connections)
        {
            int a = kvp.Key;
            foreach (int b in kvp.Value)
                if (a < b) edges.Add((a, b));
        }

        foreach (var (a, b) in edges)
        {
            bool aLocked = _lockedGroups.Contains(a);
            bool bLocked = _lockedGroups.Contains(b);
            
            if (!detectedSet.Contains(a) || !detectedSet.Contains(b)) continue;
            if (!_cubes.ContainsKey(a) || !_cubes.ContainsKey(b)) continue;

            float dist = Vector3.Distance(_cubes[a].transform.position, _cubes[b].transform.position);
            var key = EdgeKey(a, b);

            if (!edgeStates.TryGetValue(key, out var st))
            {
                st = new EdgeState();
                edgeStates[key] = st;
            }

           if (dist > disconnectDistance) {
                // do NOT break edges inside locked groups
                if (aLocked && bLocked)
                    continue;

                st.breakT = Mathf.Min(connectionBreakTime, st.breakT + dt);
                st.build = Mathf.Max(0f, st.build - dt);

                if (st.breakT >= connectionBreakTime)
                {
                    RemoveConnection(key.Item1, key.Item2);
                    st.breakT = 0f;
                    st.build = 0f;
                }
            }
        }
    }

    void BuildStableGroupsAndSync()
    {
        var detectedSet = new HashSet<int>(_detectedIDs);

        HashSet<int> visited = new();
        List<HashSet<int>> components = new();

        // -----------------------------
        // 1. Build connected components
        // -----------------------------
        foreach (int id in _detectedIDs)
        {
            if (visited.Contains(id)) continue;

            HashSet<int> component = new();
            Queue<int> queue = new();

            queue.Enqueue(id);
            visited.Add(id);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                component.Add(current);

                if (!connections.TryGetValue(current, out var neigh))
                    continue;

                foreach (int n in neigh)
                {
                    if (!detectedSet.Contains(n)) continue;
                    if (visited.Contains(n)) continue;

                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }

            components.Add(component);
        }

        // -----------------------------
        // 2. LOCK PRESERVATION PATCH
        // (this prevents locked groups from collapsing)
        // -----------------------------
        List<HashSet<int>> lockedComponents = new();

        foreach (int groupId in _lockedGroups)
        {
            if (!stableGroupsById.TryGetValue(groupId, out var lockedGroup))
                continue;

            if (lockedGroup.cubeIDs == null || lockedGroup.cubeIDs.Count < 2)
                continue;

            lockedComponents.Add(new HashSet<int>(lockedGroup.cubeIDs));
        }

        // Merge unlocked + locked components
        var allComponents = components.Concat(lockedComponents).ToList();

        HashSet<int> usedGroupIds = new();

        // -----------------------------
        // 3. Assign groups
        // -----------------------------
        foreach (var comp in allComponents)
        {
            if (comp.Count < 2) continue;

            int existingGroupId = FindBestMatchingGroup(comp);

            CubeGroup group;

            if (existingGroupId == -1)
            {
                group = new CubeGroup { groupID = nextStableGroupId++ };
                stableGroupsById[group.groupID] = group;
            }
            else
            {
                group = stableGroupsById[existingGroupId];
            }

            // -----------------------------
            // LOCK RULE (CRITICAL FIX)
            // -----------------------------
            HashSet<int> finalMembers;

            if (_lockedGroups.Contains(group.groupID))
            {
                // locked group is authoritative
                finalMembers = new HashSet<int>(group.cubeIDs);
            }
            else
            {
                finalMembers = new HashSet<int>(comp);
                group.cubeIDs = new HashSet<int>(finalMembers);
            }

            usedGroupIds.Add(group.groupID);

            // -----------------------------
            // Change detection
            // -----------------------------
            bool changed = HasGroupChanged(group.groupID, finalMembers);

            // semantics update — skip for locked groups
            if (!_lockedGroups.Contains(group.groupID))
                UpdateGroupSemantics(group);

            // UI update only if changed AND not locked
            if (changed && !_lockedGroups.Contains(group.groupID))
                HardRebuildGroupUI(group);
        }

        // -----------------------------
        // 4. Remove stale groups
        // -----------------------------
        var toRemove = stableGroupsById.Keys
            .Where(id => !usedGroupIds.Contains(id))
            .ToList();

        foreach (int id in toRemove)
        {
            if (stableGroupsById[id].groupUI != null)
                Destroy(stableGroupsById[id].groupUI);

            stableGroupsById.Remove(id);
            lastGroupSnapshot.Remove(id);
            _pendingGroupTypes.Remove(id);
            _pendingGroupTypeTimers.Remove(id);
            _lockedGroups.Remove(id);
        }

        // -----------------------------
        // 5. Sync to legacy system
        // -----------------------------
        SyncStableIntoLegacy();

        // -----------------------------
        // 6. Reminder sync
        // -----------------------------
        if (reminderManager != null)
        {
            foreach (int id in _detectedIDs)
            {
                bool in2Plus =
                    cubeToGroup.ContainsKey(id) &&
                    cubeToGroup[id].cubeIDs.Count > 1;

                reminderManager.SetCubeGrouped(id, in2Plus);
            }
        }
    }

    void SyncStableIntoLegacy()
    {
        groups.Clear();
        cubeToGroup.Clear();

        foreach (var kvp in stableGroupsById)
        {
            CubeGroup g = kvp.Value;
            if (g == null || g.cubeIDs == null || g.cubeIDs.Count < 2) continue;

            groups[g.groupID] = g;
            foreach (int id in g.cubeIDs)
                cubeToGroup[id] = g;
        }
    }

    int FindBestMatchingGroup(HashSet<int> comp)
    {
        if (comp.Count > 0)
        {
            foreach (var kvp in stableGroupsById)
            {
                if (_lockedGroups.Contains(kvp.Key) &&
                    kvp.Value.cubeIDs.Overlaps(comp))
                {
                    return kvp.Key; // force identity preservation
                }
            }
        }

        // More forgiving so group IDs stay stable across 2->3->2 changes
        int bestId = -1;
        int bestOverlap = 0;

        foreach (var kvp in stableGroupsById)
        {
            int overlap = kvp.Value.cubeIDs.Intersect(comp).Count();
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestId = kvp.Key;
            }
        }

        // accept any overlap >= 1, otherwise IDs churn and UIs get weird
        if (bestId != -1 && bestOverlap >= 1)
            return bestId;

        return -1;
    }

    bool HasGroupChanged(int groupId, HashSet<int> current)
    {
        if (!lastGroupSnapshot.TryGetValue(groupId, out var prev))
        {
            lastGroupSnapshot[groupId] = new HashSet<int>(current);
            return true;
        }

        if (prev.SetEquals(current))
            return false;

        lastGroupSnapshot[groupId] = new HashSet<int>(current);
        return true;
    }

    static (int, int) EdgeKey(int a, int b) => (Mathf.Min(a, b), Mathf.Max(a, b));

    void EnsureCube(int id)
    {
        if (!connections.ContainsKey(id))
            connections[id] = new HashSet<int>();
    }

    void AddConnection(int a, int b)
    {
        EnsureCube(a);
        EnsureCube(b);

        bool added1 = connections[a].Add(b);
        bool added2 = connections[b].Add(a);

        // if ((added1 || added2) && systemText != null)
        //     systemText.text = $"[EDGE +] {a}<->{b}";
    }

    void RemoveConnection(int a, int b)
    {
        bool removed = false;
        if (connections.ContainsKey(a)) removed |= connections[a].Remove(b);
        if (connections.ContainsKey(b)) removed |= connections[b].Remove(a);

        // if (removed && systemText != null)
        //     systemText.text = $"[EDGE -] {a}<->{b}";
    }

    void HardRebuildGroupUI(CubeGroup group)
    {
        if (group == null) return;

        // destroy old instance
        if (group.groupUI != null)
            Destroy(group.groupUI);

        group.groupUI = null;
        group.spawnedUIType = (GroupType)(-1); // force prefab re-pick
        group.centerInitialized = false;       // optional: reset smoothing

        // re-create + populate
        RefreshGroupUI(group);
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
            // systemText.text = $"Group {targetGroup.groupID} UNlocked";

        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            // systemText.text = $"Group {targetGroup.groupID} locked";
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
            // systemText.text = $"Group {targetGroup.groupID} UNlocked";

        }
        else
        {
            // Lock
            _lockedGroups.Add(targetGroup.groupID);
            // systemText.text = $"Group {targetGroup.groupID} locked";
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


}