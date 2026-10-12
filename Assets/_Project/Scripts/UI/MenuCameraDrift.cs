using UnityEngine;

namespace Voidward.UI
{
    /// <summary>Gentle drift of the menu camera.</summary>
    public class MenuCameraDrift : MonoBehaviour
    {
        Vector3 basePos;
        Quaternion baseRot;

        void Awake()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
        }

        void Update()
        {
            float t = Time.time * 0.05f;
            transform.position = basePos + new Vector3(Mathf.Sin(t) * 25f, Mathf.Sin(t * 0.7f) * 10f, 0f);
            transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.8f) * 1.5f, Mathf.Sin(t) * 3f, 0f);
        }
    }
}
