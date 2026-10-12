using UnityEngine;

namespace Voidward.World
{
    /// <summary>Cosmetic rock fragment flung out of a destroyed asteroid: drifts, tumbles, shrinks away.</summary>
    public class AsteroidDebris : MonoBehaviour
    {
        public Vector3 Velocity;
        public Vector3 Spin;
        public float Life = 4f;

        float age;
        Vector3 startScale;

        void Start() => startScale = transform.localScale;

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            transform.position += Velocity * dt;
            transform.Rotate(Spin * dt, Space.Self);
            Velocity *= 1f - 0.25f * dt;
            float k = Mathf.Clamp01((Life - age) / (Life * 0.4f));
            transform.localScale = startScale * k;
            if (age >= Life) Destroy(gameObject);
        }
    }
}
