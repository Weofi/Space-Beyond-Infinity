using System;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Player
{
    /// <summary>
    /// Cross-assembly integration point for the ping system. The Networking/UI layer hooks
    /// <see cref="Send"/> to broadcast pings over the network and place world-space markers.
    /// <para>
    /// <b>Integration point for Networking/UI developers:</b> assign
    /// <c>Voidward.Player.PingSender.Send = (pos, kind, label) => { ... }</c> during startup
    /// to wire pings into the network broadcast + HUD marker system.
    /// </para>
    /// </summary>
    public static class PingSender
    {
        /// <summary>
        /// Invoked by <see cref="PingSystem"/> with (worldPosition, kind, label).
        /// Wire this to network broadcast + UI marker placement.
        /// </summary>
        public static Action<Vector3, PingKind, string> Send;
    }
}
