using UnityEngine;

namespace Xiyue.AINpcGenerator
{
    public sealed class NpcInteractionBubble : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TextMesh label;
        private float hideAt;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        private void Update()
        {
            if (root != null && root.activeSelf && Time.time >= hideAt) root.SetActive(false);
        }

        public void Show(string message, string emotion, float seconds)
        {
            if (label != null) label.text = $"[{emotion}] {message}";
            if (root != null) root.SetActive(true);
            hideAt = Time.time + Mathf.Max(0.1f, seconds);
        }

        public void Configure(GameObject rootObject, TextMesh textMesh)
        {
            root = rootObject;
            label = textMesh;
            if (root != null) root.SetActive(false);
        }
    }
}
