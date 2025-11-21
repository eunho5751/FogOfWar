using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField]
    private float _speed;

    private Rigidbody _rigidbody;
    private InputAction _moveAction;
    private Vector2 _moveDir;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _moveAction = InputSystem.actions.FindAction("Move");
    }

    private void FixedUpdate()
    {
        _rigidbody.linearVelocity = new Vector3(_moveDir.x, 0f, _moveDir.y) * _speed;
    }

    private void Update()
    {
        _moveDir = _moveAction.ReadValue<Vector2>();
    }
}
