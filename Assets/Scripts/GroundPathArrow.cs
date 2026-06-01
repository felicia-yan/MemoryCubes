using UnityEngine;

public class GroundPathArrow : MonoBehaviour
{
    [Header("Line Settings")]
    [SerializeField] private int segmentCount = 20;
    [SerializeField] private float lineWidth = 0.02f;
    [SerializeField] private float groundOffset = 0.02f; // float just above floor
    [SerializeField] private float arcHeight = 0.15f;     // how much the path bows upward in middle

    [Header("Animation")]
    [SerializeField] private float scrollSpeed = 1.2f;   // texture scroll speed — pulls user forward
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float pulseWidthMin = 0.02f;
    [SerializeField] private float pulseWidthMax = 0.05f;

    [Header("Arrow Head")]
    [SerializeField] private GameObject arrowHeadPrefab; // optional flat cone at destination end

    // Set these from GestureDetection
    [HideInInspector] public Vector3 startPosition;
    [HideInInspector] public Vector3 targetPosition;

    [SerializeField] private LineRenderer _line;
    private Material _mat;

    void Awake()
    {
        if (_line.sharedMaterial == null) {
            _mat = new Material(Shader.Find("Sprites/Default"));
            _mat.color = Color.cyan;
        }
        else {
            _mat = new Material(_line.sharedMaterial);
        }

        _line.positionCount = segmentCount;
        _line.useWorldSpace = true;
        _line.textureMode = LineTextureMode.Tile;
    }

    void Update()
    {
        DrawPath();
        PulseWidth();

        // Keep arrow head at target
        // if (arrowHeadPrefab != null)
        // {
        //     arrowHeadPrefab.transform.position = targetPosition + Vector3.up * (groundOffset + 0.01f);
        //     Vector3 dir = (targetPosition - startPosition).normalized;
        //     dir.y = 0;
        //     if (dir != Vector3.zero)
        //         arrowHeadPrefab.transform.rotation = Quaternion.LookRotation(dir);
        // }
    }

    void DrawPath() {
        Vector3 start = startPosition;
        Vector3 end = targetPosition;

        Vector3 mid = (start + end) * 0.5f + Vector3.up * arcHeight;

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (float)(segmentCount - 1);
            _line.SetPosition(i, QuadraticBezier(start, mid, end, t));
        }
    }

    void PulseWidth()
    {
        float pulse = Mathf.Lerp(pulseWidthMin, pulseWidthMax,
                        (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
        _line.startWidth = pulse;
        _line.endWidth   = pulse * 0.4f; // taper toward destination like a real arrow
    }

    Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

}