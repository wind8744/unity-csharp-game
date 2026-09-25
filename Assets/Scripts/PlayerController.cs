using UnityEngine;

/// <summary>
/// 기본 플레이어 이동 컨트롤러. WASD/방향키로 이동하고 Space로 점프합니다.
/// Rigidbody가 붙은 오브젝트에 부착하세요.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _jumpForce = 5f;

    [Header("Ground Check")]
    [SerializeField] private Transform _groundCheck;
    [SerializeField] private float _groundRadius = 0.2f;
    [SerializeField] private LayerMask _groundLayer;

    private Rigidbody _rigidbody;
    private Vector3 _moveInput;
    private bool _jumpRequested;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        _moveInput = new Vector3(horizontal, 0f, vertical).normalized;

        if (Input.GetButtonDown("Jump") && IsGrounded())
        {
            _jumpRequested = true;
        }
    }

    private void FixedUpdate()
    {
        Vector3 velocity = _moveInput * _moveSpeed;
        velocity.y = _rigidbody.linearVelocity.y;
        _rigidbody.linearVelocity = velocity;

        if (_jumpRequested)
        {
            _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
            _jumpRequested = false;
        }
    }

    private bool IsGrounded()
    {
        if (_groundCheck == null)
        {
            return true;
        }

        return Physics.CheckSphere(_groundCheck.position, _groundRadius, _groundLayer);
    }
}
