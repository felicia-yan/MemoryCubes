// GestureDetection - detect user hand gestures with cubes (e.g. shake, spin, stack) to trigger interactions

using System;
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

    [SerializeField] private ReminderManager reminderManager;

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

            // Initialize cube state once
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
                // Update transform in case object changed
                _cubes[id].transform = cubeObj.transform;
            }
        }
    }

    // Get IDs of cubes currently detected on camera
    void UpdateTouchedCubes() {
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

        // Update visuals
        foreach (var kvp in ArUcoTrackingAppCoordinator.m_markerGameObjectDictionary) {
            int id = kvp.Key;
            if (_cubes.ContainsKey(id))
                SetOutline(kvp.Value, _cubes[id].isTouched);
        }

        bool anyTouched = touchedCubeIDs.Count > 0;
        SetDim(anyTouched);
        // debugText.text = $"Touched: {string.Join(", ", touchedCubeIDs)}"; 
    }

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
        float targetBrightness = dim ? -0.9f : 0f; // -1f is fully black

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
        // Use raw velocity so the pose smoothing filter doesn't suppress direction reversals
        Vector3 velocity = cube.rawVelocity;

        float speed = velocity.magnitude;

        if (speed < 0.5f) return;

        float dot = Vector3.Dot(velocity.normalized, cube.lastRawVelocity.normalized);
        if (dot < -0.6f)
            cube.shakeDirectionChanges++;
        cube.lastRawVelocity = velocity;

        cube.shakeTimer += Time.deltaTime;

        if (cube.shakeTimer > 0.5f) {
            if (cube.shakeDirectionChanges >= 3) {
                Debug.Log($"Cube {id} SHAKE");
                debugText.text = $"Cube {id} SHAKE DETECTED!";
                reminderManager.CreateReminder(id);
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

}