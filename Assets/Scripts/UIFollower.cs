using UnityEngine;

public class UIFollower : MonoBehaviour
{
    public Transform head;

    [Header("Position")]
    public float followDistance = 0.5f;
    public float verticalOffset = 0.5f;   // tweak to raise/lower relative to gaze
    public float moveThreshold = 0.8f;
    public float moveSpeed = 2f;

    [Header("Rotation")]
    public float rotationThreshold = 45f;
    public float rotationSpeed = 3f;

    void Update()
    {
        // Flatten head forward to XZ so UI stays at eye level
        Vector3 flatForward = head.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        Vector3 desiredPosition = head.position + flatForward * followDistance + Vector3.up * verticalOffset;

        float distance = Vector3.Distance(transform.position, desiredPosition);

        if (distance > moveThreshold)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                moveSpeed * Time.deltaTime
            );
        }

        // Billboard: always face user horizontally, never tilt
        Vector3 lookDir = transform.position - head.position;
        lookDir.y = 0f;

        if (lookDir != Vector3.zero)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDir, Vector3.up);

            float angle = Quaternion.Angle(transform.rotation, desiredRotation);

            if (angle > rotationThreshold)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    desiredRotation,
                    rotationSpeed * Time.deltaTime
                );
            }
        }
    }
}