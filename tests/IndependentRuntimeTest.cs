using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework;
class IndependentRuntimeTest {
 static EverestModule module; static FMOD.System core; static object settings; static int checks;
 static Dictionary<string, string> labels = new Dictionary<string, string>();
 static void Assert(bool ok, string why) { checks++; if(!ok) { Console.Error.WriteLine("FAIL: "+why);throw new Exception(why); } }
 static string Signature(byte[] bytes) { return Convert.ToBase64String(bytes); }
 static string Last() { return labels[Signature(core.Channels[core.Channels.Count-1].Sound.Bytes)]; }
 static void Expect(string key, Action action) { int before=core.Channels.Count; action(); Assert(core.Channels.Count==before+1,key+" count"); Assert(Last().StartsWith(key+".v"),key+" classification: "+Last()); }
 static void Silent(Action action) { int before=core.Channels.Count; action(); Assert(core.Channels.Count==before,"unexpected audio"); }
 static Player New() { return new Player(); }
 static Type frostType;
 static object Frost(string method,params object[] args) { return frostType.GetMethod(method).Invoke(null,args); }
 static int FrostValue(string name) { return (int)frostType.GetField(name).GetValue(null); }
 static void FrostField(string name,object value) { frostType.GetField(name).SetValue(null,value); }
 static void ClearUltra() { Silent(delegate { NormalJump(New()); }); }

 static void Dash(Player p, Vector2 direction, bool ground) { p.DashDir=direction; p.lastAim=direction; p.dashStartedOnGround=ground; p.calledDashEvents=false; On.Celeste.Player.TestCallDashEvents(delegate(Player x) { x.calledDashEvents=true; },p); }
 static void Precision(Player p,Vector2 input,Vector2 corrected) {
  int calls=0;
  Vector2 result=On.Celeste.Player.TestCorrectDashPrecision(delegate(Player x,Vector2 direction) { calls++;Assert(Object.ReferenceEquals(x,p) && direction.X==input.X && direction.Y==input.Y,"precision arguments unchanged");return corrected; },p,input);
  Assert(calls==1,"original precision once");Assert(result.X==corrected.X && result.Y==corrected.Y,"corrected direction unchanged");
 }
 static IEnumerator PrecisionStep(Player p,Vector2 input,Vector2 corrected) { Precision(p,input,corrected);yield return null; }
 static void PrecisionDash(Player p,Vector2 input,Vector2 corrected) {
  int calls=0;
  IEnumerator routine=On.Celeste.Player.TestDashCoroutine(delegate(Player x) { calls++;return PrecisionStep(x,input,corrected); },p);
  Assert(calls==0,"original coroutine factory is lazy");Assert(routine.MoveNext(),"precision dash yield");Assert(!routine.MoveNext(),"precision dash completion");Assert(calls==1,"original coroutine factory once");
 }
 class ProbeRoutine : IEnumerator,IDisposable {
  public Func<bool> Next; public Func<object> Value; public Action Cleanup;
  public bool MoveNext() { return Next(); }
  public object Current { get { return Value(); } }
  public void Reset() { throw new NotSupportedException(); }
  public void Dispose() { Cleanup(); }
 }
 static void Jump(Player p) { On.Celeste.Player.TestSuperJump(delegate(Player x) { x.Ducking=false; x.Speed.Y=-105;x.jumpGraceTimer=0; },p); }
 static IEnumerator GroundDash(Player p) {
  yield return "freeze";
  p.Speed=new Vector2(300,170); Dash(p,new Vector2(.707f,.707f),true);
  p.Speed=new Vector2(360,0); p.DashDir=new Vector2(1,0); p.Ducking=true;
  yield return "after-boost";
 }
 static void Land(Player p,float multiplier) {
  On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.Speed=new Vector2(x.Speed.X*multiplier,0);x.DashDir=new Vector2(Math.Sign(x.Speed.X),0);x.Ducking=true;x.onGround=true; },p,new CollisionData());
 }
 static void NormalJump(Player p) {
  int calls=0;
  On.Celeste.Player.TestJump(delegate(Player x,bool particles,bool sfx) { calls++;Assert(!particles && sfx,"original jump flags");x.Speed.Y=-105;x.onGround=false;x.jumpGraceTimer=0; },p,false,true);
  Assert(calls==1,"original normal jump once");
 }
 static PlayerDeadBody Kill(Player p) { return On.Celeste.Player.TestDie(delegate(Player x,Vector2 d,bool a,bool b) { x.Leader.Followers.Clear(); x.Dead=true; return new PlayerDeadBody(); },p,new Vector2(),false,true); }
 static void Setting(string key, object value) { settings.GetType().GetProperty(key).SetValue(settings,value,null); }
 public static void Main(string[] args) {
  Assembly assembly=Assembly.LoadFrom(args[0]);
  foreach(string name in assembly.GetManifestResourceNames()) if(name.EndsWith(".wav")) {
   using(var stream=assembly.GetManifestResourceStream(name)) using(var buffer=new MemoryStream()) { stream.CopyTo(buffer); labels[Signature(buffer.ToArray())]=name.Substring("announcer.".Length); }
  }
  module=(EverestModule)Activator.CreateInstance(assembly.GetType("Celeste.Mod.IndependentAnnouncer.AnnouncerModule"));
  settings=Activator.CreateInstance(module.SettingsType); module._Settings=(EverestModuleSettings)settings;
  Assert((int)settings.GetType().GetProperty("UltraVolume").GetValue(settings,null)==40,"embedded ultra volume");
  Assert((int)settings.GetType().GetProperty("FastBubbleVolume").GetValue(settings,null)==60,"embedded bubble volume");
  Assert((int)settings.GetType().GetProperty("DeathVolume").GetValue(settings,null)==70,"embedded death volume");
  foreach(var property in settings.GetType().GetProperties()) if(property.Name.EndsWith("Volume") && property.Name!="Volume") property.SetValue(settings,100,null);
  Audio.System=new FMOD.Studio.System(); core=Audio.System.Core; module.Load(); module.Load(); module.Initialize(); Assert(On.Celeste.Player.TestSubscribers==1,"double load");Assert(MonoMod.RuntimeDetour.Hook.TestCount==1,"no FrostHelper installed: only Ducking detour");
  Player p=New(); p.Ducking=true; p.demoDashed=true;
  Silent(delegate { Dash(p,new Vector2(1,0),true); });
  Expect("demodash",delegate { PrecisionDash(p,new Vector2(1,0),new Vector2(1,0)); });
  Silent(delegate { On.Celeste.Player.TestCallDashEvents(delegate(Player x) { },p); });
  p=New();p.Ducking=true; Expect("demodash",delegate { PrecisionDash(p,new Vector2(0,-1),new Vector2(0,-1)); });
  p=New();p.Ducking=true; Dash(p,new Vector2(.707f,.707f),false);
  Expect("wavedash",delegate { Jump(p); });
  p=New();p.Ducking=true; Dash(p,new Vector2(.707f,.707f),true); Expect("hyperdash",delegate { Jump(p); });
  p=New();p.Ducking=true;Dash(p,new Vector2(1,0),false); Expect("hyperdash",delegate { Jump(p); });
  p=New(); Expect("superdash",delegate { Jump(p); });
  int calls=0; Expect("wallbounce",delegate { On.Celeste.Player.TestSuperWallJump(delegate(Player x,int d) { calls++; Assert(d==1,"wall dir"); },p,1); }); Assert(calls==1,"original wall bounce once");
  Input.MoveX.Value=0; Expect("neutral",delegate { On.Celeste.Player.TestWallJump(delegate(Player x,int d) { calls++; },p,1); });
  Input.MoveX.Value=1; Silent(delegate { On.Celeste.Player.TestWallJump(delegate(Player x,int d) { calls++; },p,1); });
  Input.MoveX.Value=-1; Silent(delegate { On.Celeste.Player.TestWallJump(delegate(Player x,int d) { calls++; },p,-1); });
  p=New();p.Speed=new Vector2(100,0); Expect("cornerboost",delegate { On.Celeste.Player.TestClimbJump(delegate(Player x) { x.Speed=new Vector2(140,-105); },p); });
  p.TestWall=true; Silent(delegate { On.Celeste.Player.TestClimbJump(delegate(Player x) { x.Speed=new Vector2(180,-105); },p); });
  p=New();p.onGround=true;p.Speed=new Vector2(100,0); Silent(delegate { On.Celeste.Player.TestClimbJump(delegate(Player x) { x.Speed=new Vector2(140,-105); },p); });
  // TechAnnouncer checks Ducking and corrected Y<=0 inside DashCoroutine.MoveNext.
  foreach(Vector2 direction in new[] { new Vector2(-1,0),new Vector2(1,0),new Vector2(0,-1),new Vector2(0,1),new Vector2(-.707f,-.707f),new Vector2(.707f,-.707f),new Vector2(-.707f,.707f),new Vector2(.707f,.707f),new Vector2(0,0),new Vector2(1,-.0001f),new Vector2(1,.0001f) }) {
   p=New();p.Ducking=true;
   Silent(delegate { Dash(p,direction,false);Precision(p,direction,direction); });
   if(direction.Y<=0) Expect("demodash",delegate { PrecisionDash(p,direction,direction); });
   else Silent(delegate { PrecisionDash(p,direction,direction); });
   p=New();p.demoDashed=true;Silent(delegate { PrecisionDash(p,direction,direction); });
   p=New();Silent(delegate { PrecisionDash(p,direction,direction); });
  }
  p=New();p.Ducking=true;
  Expect("demodash",delegate { PrecisionDash(p,new Vector2(0,1),new Vector2(0,-1)); });
  Silent(delegate { PrecisionDash(p,new Vector2(0,-1),new Vector2(0,1)); });
  // The original precision callback can change Ducking; test after orig returns.
  var afterPrecision=new ProbeRoutine { Next=delegate { Vector2 result=On.Celeste.Player.TestCorrectDashPrecision(delegate(Player x,Vector2 d){x.Ducking=true;return d;},p,new Vector2(1,0));Assert(result.X==1 && result.Y==0,"precision mutation result preserved");return false; },Value=delegate { return null; },Cleanup=delegate {} };
  p.Ducking=false;IEnumerator precise=On.Celeste.Player.TestDashCoroutine(delegate(Player x){return afterPrecision;},p);
  Expect("demodash",delegate { Assert(!precise.MoveNext(),"completion MoveNext is in dash context"); });
  // No additional deduplication: each qualifying precision call matches the original.
  IEnumerator multiple=On.Celeste.Player.TestDashCoroutine(delegate(Player x) { return new ProbeRoutine { Next=delegate { Expect("demodash",delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });Expect("demodash",delegate { Precision(p,new Vector2(-1,0),new Vector2(-1,0)); });return false; },Value=delegate { return null; },Cleanup=delegate {} }; },p);
  Assert(!multiple.MoveNext(),"two qualifying precision callbacks in one step");
  // Factory, Current, suspended yields and disposal are outside the dash context.
  int factoryCalls=0,steps=0,disposed=0;object[] tokens={new object(),new object()};
  var probe=new ProbeRoutine { Next=delegate { steps++;Precision(p,new Vector2(1,0),new Vector2(1,0));return steps<=2; },Value=delegate { Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });return tokens[steps-1]; },Cleanup=delegate { disposed++;Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); }); } };
  IEnumerator scoped=On.Celeste.Player.TestDashCoroutine(delegate(Player x) { factoryCalls++;Silent(delegate { Precision(x,new Vector2(1,0),new Vector2(1,0)); });return probe; },p);
  Assert(factoryCalls==0,"factory deferred to first step");
  Expect("demodash",delegate { Assert(scoped.MoveNext(),"first scoped step"); });Assert(Object.ReferenceEquals(scoped.Current,tokens[0]),"first yield object preserved");
  Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });
  Expect("demodash",delegate { Assert(scoped.MoveNext(),"resumed scoped step"); });Assert(Object.ReferenceEquals(scoped.Current,tokens[1]),"second yield object preserved");
  Expect("demodash",delegate { Assert(!scoped.MoveNext(),"completed scoped step"); });Assert(factoryCalls==1 && disposed==1,"factory and disposal once");
  ((IDisposable)scoped).Dispose();Assert(disposed==1,"repeat wrapper disposal does not repeat original cleanup");
  // Cancelling a paused coroutine cleans up without leaving a Demo context.
  steps=0;disposed=0;scoped=On.Celeste.Player.TestDashCoroutine(delegate(Player x){return probe;},p);
  Expect("demodash",delegate { Assert(scoped.MoveNext(),"cancelled coroutine first step"); });((IDisposable)scoped).Dispose();Assert(disposed==1,"cancelled original disposed once");
  Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });
  // Exceptions from MoveNext or Current propagate and always restore context.
  foreach(bool failCurrent in new[] { false,true }) {
   disposed=0;var failed=new ProbeRoutine { Next=delegate { if(!failCurrent)throw new InvalidOperationException("expected step failure");return true; },Value=delegate { throw new InvalidOperationException("expected Current failure"); },Cleanup=delegate { disposed++; } };
   scoped=On.Celeste.Player.TestDashCoroutine(delegate(Player x){return failed;},p);bool failure=false;
   try { scoped.MoveNext(); } catch(InvalidOperationException) { failure=true; }
   Assert(failure && disposed==1,"coroutine failure propagates and disposes original");Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });
  }
  Setting("Enabled",false);Silent(delegate { PrecisionDash(p,new Vector2(1,0),new Vector2(1,0)); });Setting("Enabled",true);
  Setting("DemoDashVolume",0);Silent(delegate { PrecisionDash(p,new Vector2(1,0),new Vector2(1,0)); });Setting("DemoDashVolume",100);
  var demoCounts=(Dictionary<string,int>)module.GetType().GetField("counts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(module);
  demoCounts["demodash"]=0;Silent(delegate { PrecisionDash(p,new Vector2(1,0),new Vector2(1,0)); });demoCounts["demodash"]=5;
  // Ultra boundaries match TechAnnouncer: marked collision + normal-state Jump
  // + pre-jump vector magnitude strictly above 240. No 1.2x/grace requirement.
  foreach(int direction in new[] { -1,1 }) foreach(float speed in new[] { 239.99f,240f,240.01f,360f }) {
   p=New();p.Speed=new Vector2(direction*speed,0);
   Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
   if(speed>240f) Expect("ultradash",delegate { NormalJump(p); });else Silent(delegate { NormalJump(p); });
   Silent(delegate { NormalJump(p); });
  }
  foreach(float vertical in new[] { 60f,80f }) {
   p=New();p.onGround=false;p.jumpGraceTimer=0;p.Speed=new Vector2(230,vertical);
   Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
   if(vertical==80f) Expect("ultradash",delegate { NormalJump(p); });else Silent(delegate { NormalJump(p); });
  }
  p=New();p.Speed=new Vector2(230,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData());NormalJump(p); });
  Assert(p.Speed.X*p.Speed.X+p.Speed.Y*p.Speed.Y>57600f,"post-jump acceleration is excluded");
  // Setting Ducking outside a vertical collision does not arm Ultra. A collision
  // without a true setter call also does not arm it, even if already crouched.
  p=New();p.Speed=new Vector2(360,0);p.Ducking=true;
  Silent(delegate { NormalJump(p); });
  p=New();p.Ducking=true;p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){},p,new CollisionData());NormalJump(p); });
  p=New();p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=false;},p,new CollisionData());NormalJump(p); });
  // No original jump success check: TechAnnouncer announces before orig, even
  // if another mod's original Jump leaves vertical velocity unchanged.
  p=New();p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
  int originalCalls=0;
  Expect("ultradash",delegate { On.Celeste.Player.TestJump(delegate(Player x,bool particles,bool sfx){originalCalls++;Assert(core.Channels[core.Channels.Count-1].Sound!=null && Last().StartsWith("ultradash.v"),"Ultra plays before original jump");},p,false,true); });
  Assert(originalCalls==1 && p.Speed.Y==0,"original jump runs once without requiring velocity change");
  ClearUltra();
  // A Jump in another state retains the mark; a later normal-state Jump consumes it.
  p=New();p.StateMachine.State=2;p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData());NormalJump(p); });
  p.StateMachine.State=0;Expect("ultradash",delegate { NormalJump(p); });ClearUltra();
  // SuperJump clears the mark before a nested Jump, retaining Wave/Hyper/Super.
  foreach(bool ground in new[] { false,true }) {
   p=New();Dash(p,new Vector2(.707f,.707f),ground);p.Speed=new Vector2(360,0);
   Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
   Expect(ground?"hyperdash":"wavedash",delegate { On.Celeste.Player.TestSuperJump(delegate(Player x){NormalJump(x);x.Ducking=false;},p); });
   Silent(delegate { NormalJump(p); });
  }
  // Changes outside the two TechAnnouncer consumption points do not clear Ultra.
  p=New();p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
  p.Ducking=false;Dash(p,new Vector2(0,-1),false);p.onGround=false;p.jumpGraceTimer=0;p.Speed=new Vector2(-360,20);
  Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x){},p); });
  Expect("ultradash",delegate { NormalJump(p); });ClearUltra();
  // Shared mark scope follows the original module's static player-independent record.
  p=New();Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){x.Ducking=true;},p,new CollisionData()); });
  p=New();p.Speed=new Vector2(360,0);Expect("ultradash",delegate { NormalJump(p); });ClearUltra();
  // Restore collision context on failure, and preserve outer context when nested.
  p=New();p.Speed=new Vector2(360,0);
  bool threw=false;try { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){throw new InvalidOperationException("expected");},p,new CollisionData()); } catch(InvalidOperationException){threw=true;}
  Assert(threw,"original collision exception propagates");p.Ducking=true;Silent(delegate { NormalJump(p); });
  p=New();p.Speed=new Vector2(360,0);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d){On.Celeste.Player.TestOnCollideV(delegate(Player y,CollisionData c){},x,d);x.Ducking=true;},p,new CollisionData()); });
  Expect("ultradash",delegate { NormalJump(p); });ClearUltra();
  // Consecutive Waves still classify through SuperJump, which clears Ultra each time.
  foreach(int direction in new[] { -1,1 }) for(int repeat=0;repeat<12;repeat++) {
   p=New();p.StateMachine.State=2;Dash(p,new Vector2(direction*.707f,.707f),false);p.Speed=new Vector2(direction*300,170);
   Silent(delegate { Land(p,1.2f); });Expect("wavedash",delegate { Jump(p); });ClearUltra();
  }
  // Dash coroutine yields remain unchanged; original factory runs once, lazily.
  p=New();p.StateMachine.State=2;int coroutineCalls=0;IEnumerator originalRoutine=GroundDash(p);
  IEnumerator dashRoutine=On.Celeste.Player.TestDashCoroutine(delegate(Player x){coroutineCalls++;return originalRoutine;},p);
  Assert(coroutineCalls==0,"ground dash original factory is lazy");
  Assert(dashRoutine.MoveNext() && (string)dashRoutine.Current=="freeze","original first yield");
  Assert(dashRoutine.MoveNext() && (string)dashRoutine.Current=="after-boost","original second yield");
  Expect("hyperdash",delegate { Jump(p); });Assert(!dashRoutine.MoveNext(),"original coroutine end");Assert(coroutineCalls==1,"original coroutine once");ClearUltra();
  // Manual exits for green and red, with one announcement per bubble entry.
  foreach(int next in new[] { 2,5 }) {
   p=New();p.StateMachine.State=4;p.boostRed=next==5;
   On.Celeste.Player.TestBoostBegin(delegate(Player x) { calls++; },p);
   Expect("fastbubble",delegate { Assert(On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return next; },p)==next,"bubble result preserved"); });
   Silent(delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return next; },p); });
   On.Celeste.Player.TestBoostBegin(delegate(Player x) { },p);
   Expect("fastbubble",delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return next; },p); });
  }
  p=New();p.StateMachine.State=4;On.Celeste.Player.TestBoostBegin(delegate(Player x) { },p);
  Silent(delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return 4; },p); });
  p.StateMachine.State=0; Silent(delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return 5; },p); });
  Assembly frost=Assembly.LoadFrom(args[1]);
  frostType=frost.GetType("FrostHelper.Entities.Boosters.GenericCustomBooster");
  Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x){},New()); });
  Assert(MonoMod.RuntimeDetour.Hook.TestCount==2,"late FrostHelper assembly installs one optional detour");
  module.Initialize();Assert(MonoMod.RuntimeDetour.Hook.TestCount==2,"Frost detour is not duplicated");
  foreach(int state in new[] { 31,79 }) foreach(string color in new[] { "BlueBooster","YellowBooster" }) foreach(bool red in new[] { false,true }) foreach(bool crouch in new[] { false,true }) {
   FrostField("CustomBoostState",state);FrostField("CustomRedBoostState",state+17);
   p=New();var booster=(Monocle.Entity)Activator.CreateInstance(frost.GetType("FrostHelper.Entities.Boosters."+color));Frost("TestBegin",p,booster);
   int updates=FrostValue("UpdateCalls");
   Silent(delegate { Assert((int)Frost("TestUpdate",p,false,false,true,red)==state,"Frost waiting stays in custom state"); });
   Expect("fastbubble",delegate { Assert((int)Frost("TestUpdate",p,!crouch,crouch,true,red)==(red?state+17:2),"Frost exit state preserved"); });
   Assert(FrostValue("UpdateCalls")==updates+2,"original Frost update runs once per call");
   Assert(!(bool)frostType.GetField("DashPressed").GetValue(null) && !(bool)frostType.GetField("CrouchPressed").GetValue(null),"Frost input consumption preserved");
   Silent(delegate { Frost("TestUpdate",p,true,false,true,red); });
   Frost("TestBegin",p,booster);Expect("fastbubble",delegate { Frost("TestUpdate",p,true,false,true,red); });
   Frost("TestBegin",p,booster);Silent(delegate { Frost("TestUpdate",p,true,false,false,red); });
   Frost("TestTimedExit",p);Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x){},p); });
   p.StateMachine.State=0;Silent(delegate { Frost("TestUpdate",p,true,false,true,red); });
  }
  // Switching between vanilla and custom bubbles resets the same one-entry guard.
  p=New();Frost("TestBegin",p,new Monocle.Entity());Expect("fastbubble",delegate { Frost("TestUpdate",p,true,false,true,false); });
  p.StateMachine.State=4;On.Celeste.Player.TestBoostBegin(delegate(Player x){},p);Expect("fastbubble",delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x){return 2;},p); });
  Frost("TestBegin",p,new Monocle.Entity());Expect("fastbubble",delegate { Frost("TestUpdate",p,true,false,true,false); });
  p=New();Expect("death",delegate { Assert(Kill(p)!=null,"body returned"); }); Silent(delegate { Kill(p); });
  p=New();Silent(delegate { On.Celeste.Player.TestDie(delegate(Player x,Vector2 d,bool a,bool b) { return null; },p,new Vector2(),false,true); });
  p=New();p.Leader.Followers.Add(new Follower { Entity=new Strawberry { Golden=true } }); Expect("goldendeath",delegate { Kill(p); });
  Strawberry berry=new Strawberry();Expect("strawberry",delegate { On.Celeste.Strawberry.TestCollect(delegate(Strawberry x) { x.TestCollect(); },berry); });
  Silent(delegate { On.Celeste.Strawberry.TestCollect(delegate(Strawberry x) { x.TestCollect(); },berry); });
  berry=new Strawberry { Golden=true };Expect("goldenstrawberry",delegate { On.Celeste.Strawberry.TestCollect(delegate(Strawberry x) { x.TestCollect(); },berry); });
  berry=new Strawberry();Silent(delegate { On.Celeste.Strawberry.TestCollect(delegate(Strawberry x) { },berry); });
  var seen=new Dictionary<string,int>(); for(int i=0;i<2000;i++) { Kill(New()); string name=Last(); if(!seen.ContainsKey(name)) seen[name]=0; seen[name]++; }
  Assert(seen.Count==5,"all five variants"); foreach(int n in seen.Values) Assert(n>270 && n<530,"random distribution");
  Assert(core.Sounds.Count<=65,"sound cache bounded"); int active=0;foreach(var c in core.Channels) if(c.Playing) active++;Assert(active<=8,"overlap cap");
  Setting("Volume",3);On.Celeste.Player.TestUpdate(delegate(Player x) { calls++; },New());foreach(var c in core.Channels) if(c.Playing) Assert(Math.Abs(c.Volume-.3f)<.001f,"live volume");
  // All thirteen independent volume controls, live changes and master multiplication.
  string[] keys={"cornerboost","demodash","fastbubble","hyperdash","neutral","superdash","ultradash","wallbounce","wavedash","death","goldendeath","strawberry","goldenstrawberry"};
  string[] properties={"CornerBoostVolume","DemoDashVolume","FastBubbleVolume","HyperVolume","NeutralJumpVolume","SuperVolume","UltraVolume","WallBounceVolume","WavedashVolume","DeathVolume","GoldenDeathVolume","StrawberryVolume","GoldenStrawberryVolume"};
  MethodInfo announce=module.GetType().GetMethod("Announce",BindingFlags.NonPublic|BindingFlags.Instance);
  Setting("Volume",10);
  for(int i=0;i<keys.Length;i++) {
   string key=keys[i];Setting(properties[i],25);Expect(key,delegate { announce.Invoke(module,new object[] { key }); });
   Assert(Math.Abs(core.Channels[core.Channels.Count-1].Volume-.25f)<.001f,"per-event initial gain "+key);
   Setting(properties[i],50);On.Celeste.Player.TestUpdate(delegate(Player x) { },New());
   Assert(Math.Abs(core.Channels[core.Channels.Count-1].Volume-.5f)<.001f,"per-event live gain "+key);Setting(properties[i],100);
  }
  Setting("Volume",5);Setting("DeathVolume",40);Expect("death",delegate { Kill(New()); });
  Assert(Math.Abs(core.Channels[core.Channels.Count-1].Volume-.2f)<.001f,"master times event");
  Setting("DeathVolume",0);Silent(delegate { Kill(New()); });On.Celeste.Player.TestUpdate(delegate(Player x) { },New());
  foreach(var c in core.Channels) if(labels[Signature(c.Sound.Bytes)].StartsWith("death.v")) Assert(!c.Playing,"mute only death");
  Expect("superdash",delegate { Jump(New()); });Assert(core.Channels[core.Channels.Count-1].Playing,"other event unaffected");
  Setting("DeathVolume",100);Setting("Volume",10);
  Assert(core.Channels[0].Group==Audio.System.Bus.Group,"gameplay bus"); Assert(Audio.System.Bus.Locks==1,"bus lock once");
  Setting("Enabled",false);Silent(delegate { Kill(New()); });On.Celeste.Player.TestUpdate(delegate(Player x) { },New());foreach(var c in core.Channels) Assert(!c.Playing,"disabled stops audio");
  Setting("Enabled",true); Setting("Volume",0);Silent(delegate { Kill(New()); });Setting("Volume",10);
  var counts=(Dictionary<string,int>)module.GetType().GetField("counts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(module);
  counts["goldendeath"]=0;p=New();p.Leader.Followers.Add(new Follower { Entity=new Strawberry { Golden=true } });Silent(delegate { Kill(p); });counts["goldendeath"]=5;
  counts["goldenstrawberry"]=0;berry=new Strawberry { Golden=true };Silent(delegate { On.Celeste.Strawberry.TestCollect(delegate(Strawberry x) { x.TestCollect(); },berry); });counts["goldenstrawberry"]=5;
  module.Unload();module.Unload();Assert(MonoMod.RuntimeDetour.Hook.TestCount==0,"unload all detours");Assert((int)frostType.GetProperty("TestSubscribers").GetValue(null,null)==0,"unload Frost event");Assert(On.Celeste.Player.TestSubscribers==0,"unload hooks");Assert(On.Celeste.Player.TestPrecisionSubscribers==0,"unload precision hook");Silent(delegate { Precision(p,new Vector2(1,0),new Vector2(1,0)); });Assert(Audio.System.Bus.Locks==0,"unlock bus");foreach(var s in core.Sounds) Assert(s.Released,"release sound");
  module.Load();Assert(MonoMod.RuntimeDetour.Hook.TestCount==2,"reload discovers already-loaded FrostHelper");Expect("death",delegate { Kill(New()); }); module.Unload();Assert(MonoMod.RuntimeDetour.Hook.TestCount==0,"reload cleanup");
  Console.WriteLine("Independent runtime checks: "+checks);
 }
}
