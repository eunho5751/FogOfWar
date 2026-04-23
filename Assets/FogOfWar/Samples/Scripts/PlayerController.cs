using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField]
    private float _force = 10f;

    private Rigidbody _rigidbody;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        Vector2 input = ReadInput();
        Vector3 direction = new(input.x, 0f, input.y);
        _rigidbody.AddForce(direction * _force);
    }

    private static Vector2 ReadInput()
    {
        Vector2 input = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.wKey.isPressed) input.y += 1f;
        }
#else
        if (Input.GetKey(KeyCode.A)) input.x -= 1f;
        if (Input.GetKey(KeyCode.D)) input.x += 1f;
        if (Input.GetKey(KeyCode.S)) input.y -= 1f;
        if (Input.GetKey(KeyCode.W)) input.y += 1f;
#endif
        return input;
    }
}