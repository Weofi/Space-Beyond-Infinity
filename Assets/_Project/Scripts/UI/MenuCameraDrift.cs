using UnityEngine;

namespace Voidward.UI
{
    /// <summary>Gentle drift of the menu camera.</summary>
    public class MenuCameraDrift : MonoBehaviour
    {
        public Vector2 PositionAmplitude = new Vector2(25f, 10f);
        public Vector2 RotationAmplitude = new Vector2(1.5f, 3f);

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
            transform.position = basePos + baseRot * Vector3.right * (Mathf.Sin(t) * PositionAmplitude.x) + Vector3.up * (Mathf.Sin(t * 0.7f) * PositionAmplitude.y);
            transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.8f) * RotationAmplitude.x, Mathf.Sin(t) * RotationAmplitude.y, 0f);
        }
    }
}
