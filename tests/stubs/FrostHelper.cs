// Test-only API fixture for FrostHelper 1.81.0. Never distributed.
using System; using Celeste; using Monocle;
namespace FrostHelper.Entities.Boosters {
 public class GenericCustomBooster : Entity {
  public static int CustomBoostState=31,CustomRedBoostState=42,UpdateCalls;
  public static bool DashPressed,CrouchPressed,AllowFastbubble=true,Red;
  public static event Action<Player,Entity> OnBoostBegin;
  public static int TestSubscribers { get { return OnBoostBegin==null?0:OnBoostBegin.GetInvocationList().Length; } }
  public static void TestBegin(Player p,Entity booster) { p.StateMachine.State=CustomBoostState; if(OnBoostBegin!=null)OnBoostBegin(p,booster); }
  public static int BoostUpdate(Entity entity) {
   UpdateCalls++;
   if((DashPressed||CrouchPressed)&&AllowFastbubble) { DashPressed=false;CrouchPressed=false;return Red?CustomRedBoostState:2; }
   return CustomBoostState;
  }
  public static int TestUpdate(Entity entity,bool dash,bool crouch,bool allowed,bool red) {
   DashPressed=dash;CrouchPressed=crouch;AllowFastbubble=allowed;Red=red;
   var handler=MonoMod.RuntimeDetour.Hook.TestFind(typeof(GenericCustomBooster).FullName,"BoostUpdate") as Func<Func<Entity,int>,Entity,int>;
   return handler==null?BoostUpdate(entity):handler(BoostUpdate,entity);
  }
  public static void TestTimedExit(Player p) { p.StateMachine.State=2; }
 }
 public class BlueBooster : GenericCustomBooster { }
 public class YellowBooster : GenericCustomBooster { }
}
