using System;
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
 static void Jump(Player p) { On.Celeste.Player.TestSuperJump(delegate(Player x) { x.Ducking=false; },p); }
 static PlayerDeadBody Kill(Player p) { return On.Celeste.Player.TestDie(delegate(Player x,Vector2 d,bool a,bool b) { x.Leader.Followers.Clear(); x.Dead=true; return new PlayerDeadBody(); },p,new Vector2(),false,true); }
 static void Setting(string key, object value) { settings.GetType().GetProperty(key).SetValue(settings,value,null); }
 public static void Main(string[] args) {
  Assembly assembly=Assembly.LoadFrom(args[0]);
  foreach(string name in assembly.GetManifestResourceNames()) if(name.EndsWith(".wav")) {
   using(var stream=assembly.GetManifestResourceStream(name)) using(var buffer=new MemoryStream()) { stream.CopyTo(buffer); labels[Signature(buffer.ToArray())]=name.Substring("announcer.".Length); }
  }
  module=(EverestModule)Activator.CreateInstance(assembly.GetType("Celeste.Mod.IndependentAnnouncer.AnnouncerModule"));
  settings=Activator.CreateInstance(module.SettingsType); module._Settings=(EverestModuleSettings)settings;
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
  p=New();p.StateMachine.State=2;Dash(p,new Vector2(.707f,.707f),false);p.Speed=new Vector2(300,170);
  Expect("ultradash",delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.Speed=new Vector2(360,0); x.DashDir=new Vector2(1,0); },p,new CollisionData()); });
  p.DashDir=new Vector2(.707f,.707f);p.Speed=new Vector2(360,170); Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.Speed=new Vector2(432,0); },p,new CollisionData()); });
  p=New();p.StateMachine.State=2;Dash(p,new Vector2(.707f,.707f),false);p.Speed=new Vector2(169.7f,170);
  Silent(delegate { On.Celeste.Player.TestOnCollideV(delegate(Player x,CollisionData d) { x.Speed=new Vector2(203.64f,0); },p,new CollisionData()); });
  p=New();p.StateMachine.State=4; Expect("fastbubble",delegate { Assert(On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return 2; },p)==2,"return state"); });
  p.boostRed=true; Silent(delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return 2; },p); });
  p.boostRed=false; Silent(delegate { On.Celeste.Player.TestBoostUpdate(delegate(Player x) { return 4; },p); });
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
