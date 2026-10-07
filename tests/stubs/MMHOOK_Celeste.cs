using Celeste;
using Microsoft.Xna.Framework;
namespace On.Celeste {
    public static class Player {
        public delegate PlayerDeadBody orig_Die(global::Celeste.Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats);
        public delegate PlayerDeadBody hook_Die(orig_Die orig, global::Celeste.Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats);
        public static event hook_Die Die;
        public static PlayerDeadBody TestDie(orig_Die orig, global::Celeste.Player self) { return Die(orig, self, new Vector2(), false, true); }
        public static int TestSubscribers { get { return Die == null ? 0 : Die.GetInvocationList().Length; } }
    }
    public static class Strawberry {
        public delegate void orig_OnCollect(global::Celeste.Strawberry self);
        public delegate void hook_OnCollect(orig_OnCollect orig, global::Celeste.Strawberry self);
        public static event hook_OnCollect OnCollect;
        public static void TestCollect(orig_OnCollect orig, global::Celeste.Strawberry self) { OnCollect(orig, self); }
        public static int TestSubscribers { get { return OnCollect == null ? 0 : OnCollect.GetInvocationList().Length; } }
    }
}
