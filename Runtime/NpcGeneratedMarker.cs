using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    public sealed class NpcGeneratedMarker : MonoBehaviour
    {
        [SerializeField] private string npcId;
        public string NpcId => npcId;
        public void SetId(string value) => npcId = value;
    }
}
