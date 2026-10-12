using Voidward.Core;

namespace Voidward.Player
{
    /// <summary>
    /// Cross-assembly integration point for ship seating. The Ship assembly's seat network
    /// logic sets <see cref="PendingSeat"/> on the owning client when occupancy changes
    /// (non-null = occupy that seat, null = vacate). <see cref="PlayerPawn"/> polls this
    /// every frame (when <c>IsOwner</c>) and calls <see cref="PlayerPawn.AttachToSeat"/> /
    /// <see cref="PlayerPawn.DetachFromSeat"/> accordingly.
    /// <para>
    /// <b>Integration point for Ship developers:</b> set
    /// <c>Voidward.Player.SeatLink.PendingSeat = seat</c> on the owning client when a seat
    /// is assigned, and <c>null</c> when vacated.
    /// </para>
    /// </summary>
    public static class SeatLink
    {
        /// <summary>The seat the local player should occupy, or null to vacate.</summary>
        public static ISeat PendingSeat;
    }
}
