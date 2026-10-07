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
 static void Assert(bool ok, string why) { checks++; if(!ok) throw new Exception(why); }
 static string Signature(byte[] bytes) { return Convert.ToBase64String(bytes); }
 static string Last() { return labels[Signature(core.Channels[core.Channels.Count-1].Sound.Bytes)]; }
 static void Expect(string key, Action action) { int before=core.Channels.Count; action(); Assert(core.Channels.Count==before+1,key+" count"); Assert(Last().StartsWith(key+".v"),key+" classification: "+Last()); }
 static void Silent(Action action) { int before=core.Channels.Count; action(); Assert(core.Channels.Count==before,"unexpected audio"); }
 static Player New() { return new Player(); }
 static void Dash(Player p, Vector2 direction, bool ground) { p.DashDir=direction; p.lastAim=direction; p.dashStartedOnGround=ground; p.calledDashEvents=false; On.Celeste.Player.TestCallDashEvents(delegate(Player x) { x.calledDashEvents=true; },p); }
 static void Jump(Player p) { On.Celeste.Player.TestSuperJump(delegate(Player x) { x.Ducking=false; x.Speed.Y=-105; },p); }
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
  On.Celeste.Player.TestJump(delegate(Player x,bool particles,bool sfx) { calls++;Assert(!particles && sfx,"original jump flags");x.Speed.Y=-105;x.onGround=false; },p,false,true);
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
  Audio.System=new FMOD.Studio.System(); core=Audio.System.Core; module.Load(); module.Load(); Assert(On.Celeste.Player.TestSubscribers==1,"double load");
  Player p=New(); p.Ducking=true; p.demoDashed=true;
  Expect("demodash",delegate { Dash(p,new Vector2(1,0),true); });
  Silent(delegate { On.Celeste.Player.TestCallDashEvents(delegate(Player x) { },p); });
  p=New();p.Ducking=true; Silent(delegate { Dash(p,new Vector2(0,-1),false); });
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
  // A fast, repeated Wave landing during StDash must never arm Ultra.
  foreach(int direction in new[] { -1,1 }) {
   p=New();
   for(int repeat=0;repeat<12;repeat++) {
    p.StateMachine.State=2;p.Ducking=true;Dash(p,new Vector2(direction*.707f,.707f),false);p.Speed=new Vector2(direction*(300+repeat*20),170);
    Silent(delegate { Land(p,1.2f); });
    Expect("wavedash",delegate { Jump(p); });
   }
  }
  // Only a real 1.2x landing AFTER StDash ends, followed by a jump, announces.
  foreach(int direction in new[] { -1,1 }) foreach(bool super in new[] { false,true }) {
   p=New();p.StateMachine.State=2;Dash(p,new Vector2(direction*.707f,.707f),false);
   p.StateMachine.State=0;p.Speed=new Vector2(direction*300,170);
   Silent(delegate { Land(p,1.2f); });
   Expect("ultradash",delegate { if(super) Jump(p);else NormalJump(p); });
   Silent(delegate { NormalJump(p); });
  }
  // No minimum carried-speed heuristic: verify the actual multiplier.
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(120,80);
  Silent(delegate { Land(p,1.2f); });Expect("ultradash",delegate { NormalJump(p); });
  foreach(float multiplier in new[] { 1f,1.11f,1.19f,1.21f,1.3f }) {
   p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);
   Silent(delegate { Land(p,multiplier);NormalJump(p); });
  }
  // Ending StDash inside the collision is still too early; state BEFORE
  // contact is authoritative. Neither speed alone nor a non-down dash qualifies.
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=2;p.Speed=new Vector2(300,170);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.StateMachine.State=0;x.Speed=new Vector2(360,0);x.DashDir=new Vector2(1,0); },p,new CollisionData());NormalJump(p); });
  foreach(Vector2 aim in new[] { new Vector2(1,0),new Vector2(.707f,-.707f),new Vector2(0,1) }) {
   p=New();Dash(p,aim,false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);
   Silent(delegate { Land(p,1.2f);NormalJump(p); });
  }
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(360,0);
  Silent(delegate { NormalJump(p); });
  // Red dash and ground-start acceleration remain silent, even at high speed.
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=5;p.Speed=new Vector2(300,170);
  Silent(delegate { Land(p,1.2f);NormalJump(p); });
  p=New();p.StateMachine.State=2; int coroutineCalls=0;
  IEnumerator dashRoutine=On.Celeste.Player.TestDashCoroutine(delegate(Player x) { coroutineCalls++; return GroundDash(x); },p);
  Silent(delegate { Assert(dashRoutine.MoveNext() && (string)dashRoutine.Current=="freeze","original first yield"); });
  Silent(delegate { Assert(dashRoutine.MoveNext() && (string)dashRoutine.Current=="after-boost","original second yield"); });
  Expect("hyperdash",delegate { Jump(p); });
  Silent(delegate { Assert(!dashRoutine.MoveNext(),"original end"); });Assert(coroutineCalls==1,"original coroutine once");
  // A failed jump, new dash, wall jump, reversal, walk-off, death or lost boost
  // must consume/cancel the pending landing so an unrelated later jump is silent.
  for(int cancel=0;cancel<8;cancel++) {
   p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);Silent(delegate { Land(p,1.2f); });
   int reason=cancel;
   Silent(delegate {
    if(reason==0) On.Celeste.Player.TestJump(delegate(Player x,bool particles,bool sfx) { },p,false,true);
    if(reason==1) Dash(p,new Vector2(0,-1),false);
    if(reason==2) { Input.MoveX.Value=1;On.Celeste.Player.TestWallJump(delegate(Player x,int d) { },p,1); }
    if(reason==3) p.Speed.X=-360;
    if(reason==4) { p.onGround=false;p.Speed.Y=30; }
    if(reason==5) { p.Dead=true;On.Celeste.Player.TestUpdate(delegate(Player x) { },p);p.Dead=false; }
    if(reason==6) p.Speed.X=90;
    if(reason==7) { On.Celeste.Player.TestDashCoroutine(delegate(Player x) { return GroundDash(x); },p); }
    NormalJump(p);
   });
  }
  // onGround is sampled before movement in Player.Update. Keep a new landing
  // armed through that frame, then cancel when a subsequent frame leaves ground.
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);
  Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x) { Land(x,1.2f);x.onGround=false; },p); });
  p.onGround=true;Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x) { },p); });
  Expect("ultradash",delegate { NormalJump(p); });
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);
  Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x) { Land(x,1.2f);x.onGround=false; },p); });
  Silent(delegate { On.Celeste.Player.TestUpdate(delegate(Player x) { },p);p.onGround=true;NormalJump(p); });
  // Upward collision or rebound, and zero speed, cannot establish a landing.
  foreach(float vy in new[] { -170f,0f }) {
   p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,vy);
   Silent(delegate { Land(p,1.2f);NormalJump(p); });
  }
  p=New();Dash(p,new Vector2(.707f,.707f),false);p.StateMachine.State=0;p.Speed=new Vector2(300,170);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.Speed=new Vector2(360,-100);x.DashDir=new Vector2(1,0); },p,new CollisionData());NormalJump(p); });
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
  module.Unload();module.Unload();Assert(On.Celeste.Player.TestSubscribers==0,"unload hooks");Assert(Audio.System.Bus.Locks==0,"unlock bus");foreach(var s in core.Sounds) Assert(s.Released,"release sound");
  module.Load();Expect("death",delegate { Kill(New()); }); module.Unload();
  Console.WriteLine("Independent runtime checks: "+checks);
 }
}
