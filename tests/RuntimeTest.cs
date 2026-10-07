using System;
using System.Collections.Generic;
using AnnouncerRuntime;
using Celeste;
using Microsoft.Xna.Framework;
class RuntimeTest {
 static void Check(bool truth,string message) {if(!truth)throw new Exception(message);}
 static void Main() {
  var sounds=new List<string>(); Support.Configure("test", "death=5;goldendeath=1;strawberry=2;goldenstrawberry=1;demodash=0", sounds.Add); Support.Load();Support.Load();
  Check(On.Celeste.Player.TestSubscribers==1 && On.Celeste.Strawberry.TestSubscribers==1,"duplicate subscriptions");
  Check(Support.AudioPath("demodash")==null && Support.AudioPath("absent")==null,"optional not silent");
  var picks=new HashSet<string>();for(int i=0;i<2000;i++)picks.Add(Support.AudioPath("death"));Check(picks.Count==5,"five random variants unreachable");
  var p=new Player {Leader=new Leader()};int calls=0;
  On.Celeste.Player.orig_Die die=(self,dir,inv,stats)=>{calls++;self.Dead=true;self.Leader.Followers.Clear();return new PlayerDeadBody();};
  On.Celeste.Player.TestDie(die,p);Check(sounds.Count==1&&sounds[0]=="death"&&calls==1,"ordinary death");
  p.Dead=false;p.Leader.Followers.Add(new Follower {Entity=new Strawberry {Golden=true}});sounds.Clear();
  On.Celeste.Player.TestDie(die,p);Check(sounds.Count==1&&sounds[0]=="goldendeath","gold removed before classification");
  sounds.Clear();On.Celeste.Player.TestDie(die,p);Check(sounds.Count==0,"repeated death");
  p.Dead=false;On.Celeste.Player.TestDie((self,dir,inv,stats)=>null,p);Check(sounds.Count==0,"invincible death");
  foreach(bool gold in new[]{false,true}) {sounds.Clear();var b=new Strawberry {Golden=gold};int collected=0;On.Celeste.Strawberry.orig_OnCollect collect=self=>{collected++;self.TestCollect();};On.Celeste.Strawberry.TestCollect(collect,b);On.Celeste.Strawberry.TestCollect(collect,b);Check(sounds.Count==1&&sounds[0]==(gold?"goldenstrawberry":"strawberry")&&collected==2,"collection once");}
  sounds.Clear();On.Celeste.Strawberry.TestCollect(self=>{},new Strawberry());Check(sounds.Count==0,"unsuccessful collection");
  Support.Unload();Support.Unload();Check(On.Celeste.Player.TestSubscribers==0&&On.Celeste.Strawberry.TestSubscribers==0,"hooks not removed");
  Console.WriteLine("PASS: optional silence, five random variants, death/golden distinction, original calls, duplicate suppression, collections and unload.");
 }
}