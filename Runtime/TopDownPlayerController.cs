using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
    public sealed class TopDownPlayerController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 3f;
        private Rigidbody2D body;
        private Animator animator;
        private Vector2 requestedMove;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            requestedMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private void FixedUpdate()
        {
            Vector2 velocity = Vector2.ClampMagnitude(requestedMove, 1f) * moveSpeed;
            body.linearVelocity = velocity;
            animator.SetFloat("Speed", velocity.magnitude);
            if (velocity.sqrMagnitude <= 0.001f) return;
            int direction = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y)
                ? (velocity.x < 0 ? 1 : 2)
                : (velocity.y < 0 ? 0 : 3);
            animator.SetInteger("Direction", direction);
        }

        public void SetMoveInput(Vector2 value) => requestedMove = Vector2.ClampMagnitude(value, 1f);
    }
}
