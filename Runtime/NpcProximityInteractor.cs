using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    public sealed class NpcProximityInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float interactionRadius = 1.8f;
        [SerializeField] private LayerMask interactionMask = ~0;
        [SerializeField] private KeyCode legacyInteractionKey = KeyCode.E;

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(legacyInteractionKey)) InteractClosest();
#endif
        }

        public bool InteractClosest()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactionRadius, interactionMask);
            NpcBrain2D best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Collider2D hit in hits)
            {
                NpcBrain2D candidate = hit.GetComponentInParent<NpcBrain2D>();
                if (candidate == null) continue;
                float distance = ((Vector2)candidate.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }

            best?.Interact();
            return best != null;
        }
    }
}
