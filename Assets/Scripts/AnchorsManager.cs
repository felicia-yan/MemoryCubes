// AnchorsManager - public methods to instantiate, edit, and delete spatial anchors
// Used to place spatial markers at detected cube locations to save 
// them to the headset, so they can be loaded in future sessions

using System;
using System.Collections.Generic;
using UnityEngine;

public class AnchorsManager : MonoBehaviour
{
    /// <summary>
    /// Anchor manager singleton instance
    /// </summary>
    public static AnchorsManager Instance;

    // Saves to headset
    [SerializeField] private GameObject _saveableAnchorPrefab;
    [SerializeField] private GameObject _saveablePreview;
    [SerializeField] private Transform _saveableTransform;
    
    // Not saved to headset
    // [SerializeField] private GameObject _nonSaveableAnchorPrefab;
    // [SerializeField] private GameObject _nonSaveablePreview;
    // [SerializeField] private Transform _nonSaveableTransform;

    // Active spatial anchor instances
    private List<OVRSpatialAnchor> _anchorInstances = new(); 

    private HashSet<Guid> _anchorUuids = new(); // Simulated external location, like PlayerPrefs

    private Action<bool, OVRSpatialAnchor.UnboundAnchor> _onLocalized;

    private void Awake() {
        if (Instance == null)
        {
            Instance = this;
            _onLocalized = OnLocalized;
        }
        else
        {
            Destroy(this);
        }
    }

    void Update() {
    }

    // Create a savable spatial anchor
    public void CreateAnchor() {
        var go = Instantiate(_saveableAnchorPrefab, _saveableTransform.position, _saveableTransform.rotation); // Anchor A
        SetupAnchorAsync(go.AddComponent<OVRSpatialAnchor>(), saveAnchor: true);
    }

    // Version of CreateAnchor that takes position and rotation arguments 
    public OVRSpatialAnchor CreateAnchorAt(Vector3 position, Quaternion rotation) {
        var go = Instantiate(_saveableAnchorPrefab, position, rotation);
        var anchor = go.AddComponent<OVRSpatialAnchor>();
        SetupAnchorAsync(anchor, saveAnchor: true);
        return anchor;
    }

    // Destroys all runtime anchors, but remains in saved storage
    public void DestroyRuntimeAnchors() {
        foreach (var anchor in _anchorInstances)
        {
            Destroy(anchor.gameObject);
        }
        _anchorInstances.Clear();
    }

    // You need to make sure the anchor is ready to use before you save it.
    // Also, only save if specified
    private async void SetupAnchorAsync(OVRSpatialAnchor anchor, bool saveAnchor) {
        // Keep checking for a valid and localized anchor state
        if (!await anchor.WhenLocalizedAsync())
        {
            Debug.LogError($"Unable to create anchor.");
            Destroy(anchor.gameObject);
            return;
        }

        // Add the anchor to the list of all instances
        _anchorInstances.Add(anchor);

        // Save the saveable (green) anchors only
        if (saveAnchor && (await anchor.SaveAnchorAsync()).Success)
        {
            // Remember UUID so you can load the anchor later
            _anchorUuids.Add(anchor.Uuid);
        }
    }

    /******************* Load Anchor Methods **********************/
    // Save and display all saved anchors
    public async void LoadAllAnchors() {
        // Load and localize
        var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
        var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundAnchors);

        if (result.Success)
        {
            foreach (var anchor in unboundAnchors)
            {
                anchor.LocalizeAsync().ContinueWith(_onLocalized, anchor);
            }
        }
        else
        {
            Debug.LogError($"Load anchors failed with {result.Status}.");
        }
    }

    private void OnLocalized(bool success, OVRSpatialAnchor.UnboundAnchor unboundAnchor) {
        var pose = unboundAnchor.Pose;
        var go = Instantiate(_saveableAnchorPrefab, pose.position, pose.rotation);
        var anchor = go.AddComponent<OVRSpatialAnchor>();

        unboundAnchor.BindTo(anchor);

        // Add the anchor to the running total
        _anchorInstances.Add(anchor);
    }

    /******************* Erase Anchor Methods *****************/
    // Erase all anchors saved in the headset, but don't destroy them; they should remain displayed
    public async void EraseAllAnchors() {
        var result = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: _anchorUuids);
        if (result.Success) {
            _anchorUuids.Clear();
            Debug.Log($"Anchors erased.");
        }
        else {
            Debug.LogError($"Anchors NOT erased {result.Status}");
        }
    }
}
