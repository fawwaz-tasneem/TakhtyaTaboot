namespace TakhtyaTaboot.Util
{
    // The arithmetic of the qasid (messenger), PURE and unit-tested. Where the akhbaar scout
    // brings word BACK, the qasid carries YOUR word OUT: when he reaches the lord, you speak
    // through him as if you stood there yourself (the Diplomacy-mod messenger, native to this
    // mod under the no-Diplomacy mandate). A rider with a single letter is faster and cheaper
    // than a scout who must lurk, count, and return. MessengerBehavior owns the engine side.
    public static class MessengerMath
    {
        public const int BaseCost = 120;          // dinars, own realm
        public const float ForeignFactor = 1.5f;  // foreign courts must be bribed through

        public static int DispatchCost(bool foreignRealm)
            => (int)(BaseCost * (foreignRealm ? ForeignFactor : 1f));

        // One rider, one road: half a day at the gate, at most four on the far marches.
        // Distance is raw map units (the akhbaar scale: ~100 units per travel day for a
        // laden runner; the qasid rides lighter and faster).
        public static float DaysToReach(float distance)
            => Clamp(0.5f + distance / 150f, 0.5f, 4f);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
