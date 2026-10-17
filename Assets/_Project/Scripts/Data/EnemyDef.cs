using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>Enemy archetype stats. Prefabs are resolved via ContentCatalog by Kind.</summary>
    [CreateAssetMenu(menuName = "Voidward/Enemy", fileName = "Enemy_")]
    public class EnemyDef : ScriptableObject
    {
        public EnemyKind Kind = EnemyKind.Crawler;
        public string DisplayName = "Enemy";
        [Tooltip("Space enemies fly in zero-g; surface enemies walk aligned to planet gravity.")]
        public bool IsSpace;
        public float MaxHp = 40f;
        public float MoveSpeed = 10f;
        public float TurnSpeed = 120f;
        public float Damage = 8f;
        public float AttackRange = 20f;
        public float AttackCooldown = 1.5f;
        public float AggroRange = 60f;
        [Tooltip("Space enemies: a chased target farther than this is lost (0 = 1.5 x AggroRange).")]
        public float LeashRange;
        [Tooltip("Space enemies: max pursuit distance from their patrol home before they give up (0 = unlimited).")]
        public float PursuitRange;
        [Tooltip("Below this HP fraction the enemy may flee.")]
        public float FleeThreshold;
        [Tooltip("Salvage weight of a kill (x 6 x galaxy tier goes to the killer).")]
        public int KillReward = 1;
        public LootTable Loot;
        public float Scale = 1f;
        [Tooltip("Preferred spawn distance band around the corridor (world gen hint).")]
        public float SpawnRadiusMin = 50f;
        public float SpawnRadiusMax = 200f;
    }
}
