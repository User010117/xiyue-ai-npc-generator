using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    [RequireComponent(typeof(NpcBrain2D))]
    public sealed class NpcClickableInteraction : MonoBehaviour
    {
        private void OnMouseDown() => GetComponent<NpcBrain2D>().Interact();
    }
}
