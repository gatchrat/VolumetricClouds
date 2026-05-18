using UnityEngine;
using UnityEngine.InputSystem;
//Taken under MIT License from https://gist.github.com/ashleydavis/f025c03a9221bc840a2b
//Converted to use new Input System

/// <summary>
/// A simple free camera to be added to a Unity game object.
/// 
/// Keys:
///	wasd / arrows	- movement
///	q/e 			- up/down (local space)
///	r/f 			- up/down (world space)
///	pageup/pagedown	- up/down (world space)
///	hold shift		- enable fast movement mode
///	right mouse  	- enable free look
///	mouse			- free look / rotation
///     
/// </summary>
public class FreeCamInputSystem : MonoBehaviour
{
    [Header("Movement")]
    public float movementSpeed = 10f;
    public float fastMovementSpeed = 100f;

    [Header("Look")]
    public float freeLookSensitivity = 3f;

    [Header("Zoom")]
    public float zoomSensitivity = 10f;
    public float fastZoomSensitivity = 50f;

    private bool looking = false;

    void Update()
    {
        if (Keyboard.current == null || Mouse.current == null)
            return;

        bool fastMode =
            Keyboard.current.leftShiftKey.isPressed;

        float currentMovementSpeed =
            fastMode ? fastMovementSpeed : movementSpeed;

        Vector3 movement = Vector3.zero;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            movement -= transform.right;

        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            movement += transform.right;

        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
            movement += transform.forward;

        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            movement -= transform.forward;


        transform.position += movement * currentMovementSpeed * Time.deltaTime;

        // Start / stop looking
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            StartLooking();
        }
        else if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            StopLooking();
        }

        // Mouse look
        if (looking)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            float newRotationX = transform.localEulerAngles.y + mouseDelta.x * freeLookSensitivity * Time.deltaTime;

            float newRotationY = transform.localEulerAngles.x - mouseDelta.y * freeLookSensitivity * Time.deltaTime;

            transform.localEulerAngles = new Vector3(newRotationY, newRotationX, 0f);
        }

        // Zoom
        float scrollValue = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scrollValue) > 0.01f)
        {
            float currentZoomSensitivity =
                fastMode ? fastZoomSensitivity : zoomSensitivity;

            transform.position +=
                transform.forward *
                scrollValue *
                currentZoomSensitivity *
                0.01f;
        }
    }

    void OnDisable()
    {
        StopLooking();
    }

    public void StartLooking()
    {
        looking = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void StopLooking()
    {
        looking = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}