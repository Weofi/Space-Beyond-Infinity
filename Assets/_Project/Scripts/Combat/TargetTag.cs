using UnityEngine;

namespace Voidward.Combat
{
    /// <summary>
    /// Marks a damageable for targeting rules (surface enemies only hunt pawns, space enemies
    /// may also attack ships).
    /// </summary>
    public class TargetTag : MonoBehaviour
    {
        public bool IsShip;
        public bool IsPawn;
    }
}
