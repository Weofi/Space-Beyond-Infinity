using UnityEngine;

namespace Voidward.Networking
{
    /// <summary>
    /// Tints the pawn's glowing crew strips (helmet crest, pauldron rings, backpack bar) in the owner's crew
    /// colour (#60). The strips are white glow geometry; VW_ToonFlat multiplies glow by _BaseColor, so a
    /// property block is enough. The crew suit (#95) has no strips: its helmet's visor rim takes the colour instead.
    /// Purely visual, every peer reads the replicated slot.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class CrewLights : MonoBehaviour
    {
        [SerializeField] Renderer strips;
        [SerializeField] string colorProperty = "_BaseColor";   // the crew suit (#95) tints its visor rim (_RimColor)

        int colorId;
        NetworkPlayer player;
        MaterialPropertyBlock block;
        int applied = -1;

        void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            colorId = Shader.PropertyToID(colorProperty);
        }

        void Update()
        {
            if (strips == null || !player.IsSpawned) return;
            int slot = player.CrewSlot.Value;
            if (slot == applied) return;
            applied = slot;
            block ??= new MaterialPropertyBlock();
            strips.GetPropertyBlock(block);
            block.SetColor(colorId, player.CrewColor);
            strips.SetPropertyBlock(block);
        }
    }
}
