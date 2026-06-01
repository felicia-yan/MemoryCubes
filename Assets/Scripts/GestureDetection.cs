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
        public GameObject groupUI;
    }

    [Header("Forming Routines")]
    private HashSet<int> _lockedGroups = new HashSet<int>();
    [SerializeField] private float combineDistance = 0.08f;
    [SerializeField] private float disconnectDistance = 0.1f;
    [SerializeField] private GameObject groupUIPrefab;

    private Dictionary<int, CubeGroup> cubeToGroup = new();
    private Dictionary<int, CubeGroup> groups = new();

    private int nextGroupID = 1;
    private int lastTouchedCube = -1;
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

    // "Place cube at bottom" cue
    private int _justCompletedID = -1;
    private float _justCompletedTimer = 0f;
    private const float JUST_COMPLETED_DISPLAY_TIME = 3f;

    [SerializeField] private float showReminderWhenAwayDistance = 0.3f;

    // =====================================================

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
                    int taskIdx = UnityEngine.Random.Range(0, tasks.Count);
                    string task = tasks[taskIdx];
                    reminderManager.CreateReminder(
                        id,
                        task,
                        DateTime.Now.TimeOfDay.Add(TimeSpan.FromMinutes(10)),
                        "none");
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
        foreach (int id in previouslyTouched)
        {
            if (!touchedCubeIDs.Contains(id))
                _cubes[id].touchHeldTime = 0f;
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
            }
        }

        foreach (int id in previouslyTouched)
        {
            if (!touchedCubeIDs.Contains(id) && !_anchoredCubeIDs.Contains(id))
            {
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
    }

    // =====================================================
    // GESTURES
    // =====================================================

    void DetectShake(int id, CubeState cube)
    {
        if (cube.touchHeldTime < 0.3f)
        {
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

        if (cube.shakeTimer > 0.5f)
        {
            if (cube.shakeDirectionChanges >= 3)
                OnCubeShaken(id);

            cube.shakeDirectionChanges = 0;
            cube.shakeTimer = 0f;
        }
    }

    void OnCubeShaken(int id)
    {
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

                if (debugText != null)
                    debugText.text = $"Cube {id} done — place it at the bottom of the stack.";

                RefreshGroupUI(group);
                return;
            }
        }

        // Unordered / solo
        _dismissedCubeIDs.Add(id);
        if (debugText != null)
            debugText.text = $"Cube {id}: task complete.";

        if (cubeToGroup.TryGetValue(id, out CubeGroup uGroup))
            RefreshGroupUI(uGroup);
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
        float targetBrightness = dim ? -0.2f : 0f;

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

        // DISCONNECT
        List<(int, int)> disconnects = new();

        foreach (var group in groups.Values)
        {
            List<int> ids = group.cubeIDs.ToList();

            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    int idA = ids[i];
                    int idB = ids[j];

                    bool touching = _cubes[idA].isTouched || _cubes[idB].isTouched;
                    if (!touching) continue;

                    float dist = Vector3.Distance(
                        _cubes[idA].transform.position,
                        _cubes[idB].transform.position);

                    if (dist > disconnectDistance)
                        disconnects.Add((idA, idB));
                }
            }
        }

        foreach (var pair in disconnects)
            DisconnectCubePair(pair.Item1, pair.Item2);
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

    void UpdateGroupSemantics(CubeGroup group)
    {
        GroupType detected = DetectGroupType(group);

        if (!_pendingGroupTypes.ContainsKey(group.groupID))
        {
            _pendingGroupTypes[group.groupID]      = detected;
            _pendingGroupTypeTimers[group.groupID] = 0f;
        }

        if (_pendingGroupTypes[group.groupID] == detected)
        {
            _pendingGroupTypeTimers[group.groupID] += Time.deltaTime;

            if (_pendingGroupTypeTimers[group.groupID] >= GROUP_TYPE_HYSTERESIS
                && group.type != detected)
            {
                group.type = detected;
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
        if (group.cubeIDs.Count < 2) return GroupType.Unordered;

        List<int> ids = group.cubeIDs.ToList();
        Camera cam = Camera.main;

        float minScreenY = float.MaxValue, maxScreenY = float.MinValue;
        float minScreenX = float.MaxValue, maxScreenX = float.MinValue;

        foreach (int id in ids)
        {
            Vector3 screenPos = cam.WorldToScreenPoint(_cubes[id].transform.position);
            minScreenX = Mathf.Min(minScreenX, screenPos.x);
            maxScreenX = Mathf.Max(maxScreenX, screenPos.x);
            minScreenY = Mathf.Min(minScreenY, screenPos.y);
            maxScreenY = Mathf.Max(maxScreenY, screenPos.y);
        }

        float screenSpreadY = maxScreenY - minScreenY;
        float screenSpreadX = maxScreenX - minScreenX;

        return (screenSpreadY > screenSpreadX * 1.5f)
            ? GroupType.Ordered
            : GroupType.Unordered;
    }

    List<int> BuildVerticalOrdering(HashSet<int> ids)
    {
        Camera cam = Camera.main;
        List<int> ordered = ids.ToList();
        ordered.Sort((a, b) =>
        {
            float ay = cam.WorldToScreenPoint(_cubes[a].transform.position).y;
            float by = cam.WorldToScreenPoint(_cubes[b].transform.position).y;
            return by.CompareTo(ay); // top to bottom
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

    void HandleOrderedTraversalUpdate()
    {
        foreach (int id in touchedCubeIDs)
        {
            if (id == lastTouchedCube) continue;
            HandleOrderedTraversal(id, lastTouchedCube);
            lastTouchedCube = id;
        }
    }

    void HandleOrderedTraversal(int currentID, int previousID)
    {
        if (!cubeToGroup.ContainsKey(currentID)) return;

        CubeGroup group = cubeToGroup[currentID];
        if (group.type != GroupType.Ordered) return;

        int currentIndex  = group.orderedIDs.IndexOf(currentID);
        int previousIndex = group.orderedIDs.IndexOf(previousID);

        if (currentIndex == previousIndex + 1)
        {
            completedOrderedTasks.Add(previousID);
            Debug.Log($"Completed ordered task {previousID}");
            RefreshGroupUI(group);
        }
    }

    // =====================================================
    // ROUTINE GROUP UI
    // =====================================================

    void CreateGroupUI(CubeGroup group)
    {
        if (groupUIPrefab == null) return;

        Vector3 center = GetGroupCenter(group) + Vector3.up * 0.15f;
        GameObject ui = Instantiate(groupUIPrefab, center, Quaternion.identity);
        group.groupUI = ui;
        RefreshGroupUI(group);
    }

    void RefreshGroupUI(CubeGroup group)
    {
        if (group.groupUI == null) return;

        bool isActive = group.cubeIDs.Count > 1;
        group.groupUI.SetActive(isActive);
        if (!isActive) return;

        // Position above group center
        Vector3 center = GetGroupCenter(group);
        Vector3 awayFromCamera = (center - Camera.main.transform.position).normalized;
        group.groupUI.transform.position = center + awayFromCamera * 0.1f + Vector3.up * 0.2f;

        // Build data
        GroupUIData data = new GroupUIData
        {
            groupType       = group.type,
            completedIDs    = new HashSet<int>(completedOrderedTasks.Union(_dismissedCubeIDs)),
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

        GroupUIManager uiManager = group.groupUI.GetComponent<GroupUIManager>();
        if (uiManager != null)
            uiManager.Refresh(data);
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

            var cam = Camera.main;
            if (cam != null)
                group.groupUI.transform.rotation = Quaternion.LookRotation(
                    group.groupUI.transform.position - cam.transform.position);
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


}