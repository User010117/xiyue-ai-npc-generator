using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [RequireComponent(typeof(SpriteRenderer), typeof(Animator), typeof(Rigidbody2D))]
    public sealed class NpcBrain2D : MonoBehaviour
    {
        private enum BrainState { Idle, Wander, Interact, Emote }

        [SerializeField] private NpcDefinition definition;
        [SerializeField] private Vector2 wanderHalfExtents = new(3f, 3f);
        [SerializeField] private float idleMinSeconds = 1f;
        [SerializeField] private float idleMaxSeconds = 3f;
        [SerializeField] private float arrivalDistance = 0.08f;
        [SerializeField] private NpcInteractionBubble interactionBubble;

        private Rigidbody2D body;
        private Animator animator;
        private BrainState state;
        private Vector2 origin;
        private Vector2 target;
        private float stateUntil;
        private int dialogueIndex;

        public NpcDefinition Definition => definition;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
            origin = body.position;
            EnterIdle();
        }

        private void FixedUpdate()
        {
            if (state == BrainState.Wander) TickWander();
            else
            {
                body.linearVelocity = Vector2.zero;
                SetAnimation(Vector2.zero);
            }
        }

        private void Update()
        {
            if ((state == BrainState.Idle || state == BrainState.Emote) && Time.time >= stateUntil) EnterWander();
        }

        public void Configure(NpcDefinition value, NpcInteractionBubble bubble = null)
        {
            definition = value;
            if (bubble != null) interactionBubble = bubble;
        }

        public void Interact()
        {
            state = BrainState.Interact;
            body.linearVelocity = Vector2.zero;
            string[] lines = definition?.Character?.dialogueLines;
            string line = lines != null && lines.Length > 0 ? lines[dialogueIndex++ % lines.Length] : "...";
            interactionBubble?.Show(line, definition?.Character?.defaultEmotion ?? "neutral", 2.5f);
            state = BrainState.Emote;
            stateUntil = Time.time + 2.5f;
        }

        private void EnterIdle()
        {
            state = BrainState.Idle;
            stateUntil = Time.time + Random.Range(idleMinSeconds, idleMaxSeconds);
        }

        private void EnterWander()
        {
            state = BrainState.Wander;
            target = origin + new Vector2(
                Random.Range(-wanderHalfExtents.x, wanderHalfExtents.x),
                Random.Range(-wanderHalfExtents.y, wanderHalfExtents.y));
        }

        private void TickWander()
        {
            Vector2 delta = target - body.position;
            if (delta.sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                EnterIdle();
                return;
            }

            float speed = definition != null ? definition.MoveSpeed : 1.5f;
            Vector2 velocity = delta.normalized * speed;
            body.linearVelocity = velocity;
            SetAnimation(velocity);
        }

        private void SetAnimation(Vector2 velocity)
        {
            if (animator == null) return;
            animator.SetFloat("Speed", velocity.magnitude);
            if (velocity.sqrMagnitude <= 0.001f) return;
            int direction = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y)
                ? (velocity.x < 0 ? 1 : 2)
                : (velocity.y < 0 ? 0 : 3);
            animator.SetInteger("Direction", direction);
        }
    }
}
