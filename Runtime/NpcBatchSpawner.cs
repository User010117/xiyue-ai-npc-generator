using System.Collections.Generic;
using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    public sealed class NpcBatchSpawner : MonoBehaviour
    {
        [SerializeField] private List<GameObject> npcPrefabs = new();
        [SerializeField, Min(0.2f)] private float spacing = 1.5f;
        [SerializeField, Min(1)] private int columns = 5;

        public void SpawnAll()
        {
            for (int index = 0; index < npcPrefabs.Count; index++)
            {
                GameObject prefab = npcPrefabs[index];
                if (prefab == null) continue;
                Vector3 offset = new((index % columns) * spacing, -(index / columns) * spacing, 0f);
                Instantiate(prefab, transform.position + offset, Quaternion.identity, transform);
            }
        }
    }
}
