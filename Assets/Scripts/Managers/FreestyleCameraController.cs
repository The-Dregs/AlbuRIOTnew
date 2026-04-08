using UnityEngine;

public class FreestyleCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float lookSpeed = 2f;
    public float shiftMultiplier = 2f;

    private float yaw = 0f;
    private float pitch = 0f;
    private bool cursorLocked = true;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
        LockCursor(true);
    }

    void Update()
    {
        // Toggle cursor lock with right mouse button
        if (Input.GetMouseButtonDown(1))
        {
            cursorLocked = !cursorLocked;
            LockCursor(cursorLocked);
        }

        if (cursorLocked)
        {
            // Mouse look
            yaw += Input.GetAxis("Mouse X") * lookSpeed;
            pitch -= Input.GetAxis("Mouse Y") * lookSpeed;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.eulerAngles = new Vector3(pitch, yaw, 0f);
        }

        // WASD movement
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? shiftMultiplier : 1f);
        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        move = transform.TransformDirection(move);
        transform.position += move * speed * Time.deltaTime;
    }

    private void LockCursor(bool lockIt)
    {
        Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockIt;
    }
}
