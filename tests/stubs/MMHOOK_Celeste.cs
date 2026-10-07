using Celeste; using Microsoft.Xna.Framework;
namespace On.Celeste {
 public static class Player {
  public delegate PlayerDeadBody orig_Die(global::Celeste.Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats);
  public delegate PlayerDeadBody hook_Die(orig_Die orig,global::Celeste.Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats);
  public static event hook_Die Die;
  public static PlayerDeadBody TestDie(orig_Die orig,global::Celeste.Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) { return Die(orig,self, direction, evenIfInvincible, registerDeathInStats); }
  public delegate void orig_SuperJump(global::Celeste.Player self);
  public delegate void hook_SuperJump(orig_SuperJump orig,global::Celeste.Player self);
  public static event hook_SuperJump SuperJump;
  public static void TestSuperJump(orig_SuperJump orig,global::Celeste.Player self) { SuperJump(orig,self); }
  public delegate void orig_SuperWallJump(global::Celeste.Player self, int dir);
  public delegate void hook_SuperWallJump(orig_SuperWallJump orig,global::Celeste.Player self, int dir);
  public static event hook_SuperWallJump SuperWallJump;
  public static void TestSuperWallJump(orig_SuperWallJump orig,global::Celeste.Player self, int dir) { SuperWallJump(orig,self, dir); }
  public delegate void orig_WallJump(global::Celeste.Player self, int dir);
  public delegate void hook_WallJump(orig_WallJump orig,global::Celeste.Player self, int dir);
  public static event hook_WallJump WallJump;
  public static void TestWallJump(orig_WallJump orig,global::Celeste.Player self, int dir) { WallJump(orig,self, dir); }
  public delegate void orig_ClimbJump(global::Celeste.Player self);
  public delegate void hook_ClimbJump(orig_ClimbJump orig,global::Celeste.Player self);
  public static event hook_ClimbJump ClimbJump;
  public static void TestClimbJump(orig_ClimbJump orig,global::Celeste.Player self) { ClimbJump(orig,self); }
  public delegate void orig_CallDashEvents(global::Celeste.Player self);
  public delegate void hook_CallDashEvents(orig_CallDashEvents orig,global::Celeste.Player self);
  public static event hook_CallDashEvents CallDashEvents;
  public static void TestCallDashEvents(orig_CallDashEvents orig,global::Celeste.Player self) { CallDashEvents(orig,self); }
  public delegate int orig_BoostUpdate(global::Celeste.Player self);
  public delegate int hook_BoostUpdate(orig_BoostUpdate orig,global::Celeste.Player self);
  public static event hook_BoostUpdate BoostUpdate;
  public static int TestBoostUpdate(orig_BoostUpdate orig,global::Celeste.Player self) { return BoostUpdate(orig,self); }
  public delegate void orig_OnCollideV(global::Celeste.Player self, CollisionData data);
  public delegate void hook_OnCollideV(orig_OnCollideV orig,global::Celeste.Player self, CollisionData data);
  public static event hook_OnCollideV OnCollideV;
  public static void TestOnCollideV(orig_OnCollideV orig,global::Celeste.Player self, CollisionData data) { OnCollideV(orig,self, data); }
  public delegate void orig_Update(global::Celeste.Player self);
  public delegate void hook_Update(orig_Update orig,global::Celeste.Player self);
  public static event hook_Update Update;
  public static void TestUpdate(orig_Update orig,global::Celeste.Player self) { Update(orig,self); }
  public static int TestSubscribers { get { return Die==null ? 0 : Die.GetInvocationList().Length; } }
 }
 public static class Strawberry {
 public delegate void orig_OnCollect(global::Celeste.Strawberry self); public delegate void hook_OnCollect(orig_OnCollect orig,global::Celeste.Strawberry self); public static event hook_OnCollect OnCollect; public static void TestCollect(orig_OnCollect orig,global::Celeste.Strawberry self) { OnCollect(orig,self); }
 }
}
