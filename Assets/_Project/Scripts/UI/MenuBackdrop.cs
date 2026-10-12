using UnityEngine;
using Voidward.Core;

namespace Voidward.UI
{
    /// <summary>Main-menu scenery: slowly spins and is hidden while a run is in progress.</summary>
    public class MenuBackdrop : MonoBehaviour
    {
        Transform[] children;

        void Awake()
        {
            children = new Transform[transform.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = transform.GetChild(i);
        }

        void Update()
        {
            bool show = GameState.Phase != GamePhase.Playing && GameState.Phase != GamePhase.GameOver && GameState.Phase != GamePhase.DemoComplete;
            foreach (var c in children)
                if (c.gameObject.activeSelf != show) c.gameObject.SetActive(show);
            if (!show) return;
            if (children.Length > 0) children[0].Rotate(0f, 3f * Time.deltaTime, 0f, Space.Self);
            if (children.Length > 2) children[2].Rotate(0f, -1.5f * Time.deltaTime, 0f, Space.Self);
        }
    }
}
