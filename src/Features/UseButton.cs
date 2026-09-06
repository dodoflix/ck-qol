using UnityEngine;

namespace CkQol.Features
{
    /// Arbitrates the player's use button between features.
    ///
    /// Every automatic feature drives the same SecondInteract bit, and two writing it
    /// in one frame is undefined. A claim is first come, and held only while it keeps
    /// being re-asserted: a holder that stops asking - because its feature was
    /// switched off, its world went away, or it simply finished - loses it on the next
    /// frame without having to remember to release it.
    ///
    /// Holds are bounded by how long a press lasts, so a feature waiting for the
    /// button waits a fraction of a second at worst.
    internal static class UseButton
    {
        private static object _holder;
        private static int _frame;

        /// Takes the button, or keeps it. False means another feature has it and the
        /// caller should do nothing this frame.
        internal static bool Claim(object who)
        {
            int now = Time.frameCount;

            bool free = _holder == null ||
                        ReferenceEquals(_holder, who) ||
                        _frame < now - 1;

            if (!free) return false;

            _holder = who;
            _frame = now;
            return true;
        }

        /// Gives it up early. Not required - a claim lapses on its own - but it lets
        /// another feature act on the same frame rather than the next.
        internal static void Release(object who)
        {
            if (ReferenceEquals(_holder, who)) _holder = null;
        }
    }
}
