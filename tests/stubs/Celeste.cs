// Original API declarations/test doubles. Never included in distributions.
using System; using System.Collections.Generic; using System.Reflection; using Microsoft.Xna.Framework;
[assembly: AssemblyVersion("1.0.0.0")]
namespace Monocle {
 public class Entity { public Vector2 Position; public bool TestWall; public bool CollideCheck<T>(Vector2 at) where T:Entity { return TestWall; } }
 public class Component { public Entity Entity { get; set; } }
 public class StateMachine { public int State { get; set; } }
 public class VirtualIntegerAxis { public int Value; }
}
namespace Celeste {
 public enum Facings { Left=-1, Right=1 }
 public class Solid:Monocle.Entity { }
 public class Leader:Monocle.Component { public List<Follower> Followers=new List<Follower>(); }
 public class Follower:Monocle.Component { public Leader Leader; }
 public class Player:Monocle.Entity {
  public Leader Leader=new Leader(); public bool Dead { get; set; } public bool Ducking { get; set; }
  public float jumpGraceTimer;
  public Vector2 Speed,DashDir,lastAim; public Facings Facing=Facings.Right;
  public Monocle.StateMachine StateMachine=new Monocle.StateMachine();
  public bool onGround,dashStartedOnGround,calledDashEvents,demoDashed,boostRed;
  public bool DashAttacking { get; set; }
 }
 public static class Input { public static Monocle.VirtualIntegerAxis MoveX=new Monocle.VirtualIntegerAxis(); }
 public struct CollisionData { }
 public class PlayerDeadBody { }
 public class Strawberry:Monocle.Entity { private bool collected; public bool Golden { get; set; } public void TestCollect() { collected=true; } public bool TestCollected { get { return collected; } } }
 public static class Audio { public static FMOD.Studio.System System { get; set; } }
}
namespace Celeste.Mod {
 public class EverestModuleSettings { }
 public class SettingRangeAttribute:Attribute { public SettingRangeAttribute(int min,int max) { } }
 public abstract class EverestModule { public EverestModuleSettings _Settings { get; set; } public virtual Type SettingsType { get { return null; } } public virtual void Load() { } public virtual void Unload() { } }
 public static class Logger { public static void Log(string tag,string message) { Console.WriteLine(tag+": "+message); } }
}
namespace FMOD {
 public enum RESULT { OK=0, ERR_INTERNAL=1 }
 [Flags] public enum MODE:uint { LOOP_OFF=1, _2D=8, OPENMEMORY=2048 }
 public struct CREATESOUNDEXINFO { public int cbsize; public uint length; }
 public class Sound { public byte[] Bytes; public bool Released; public RESULT release() { Released=true; return RESULT.OK; } }
 public class ChannelControl {
  public bool Playing=true,Paused; public float Volume;
  public RESULT stop() { Playing=false; return RESULT.OK; } public RESULT setPaused(bool value) { Paused=value; return RESULT.OK; }
  public RESULT isPlaying(out bool value) { value=Playing; return RESULT.OK; } public RESULT setVolume(float value) { Volume=value; return RESULT.OK; }
 }
 public class Channel:ChannelControl { public Sound Sound; public ChannelGroup Group; }
 public class ChannelGroup:ChannelControl { }
 public class System {
  public List<Sound> Sounds=new List<Sound>(); public List<Channel> Channels=new List<Channel>(); public bool FailCreate;
  public RESULT createSound(byte[] data,MODE mode,ref CREATESOUNDEXINFO info,out Sound sound) {
   sound=null; if(FailCreate) return RESULT.ERR_INTERNAL;
   if((mode & MODE.OPENMEMORY)==0 || info.length!=data.Length || info.cbsize<=0) throw new Exception("Invalid memory sound");
   sound=new Sound { Bytes=data }; Sounds.Add(sound); return RESULT.OK;
  }
  public RESULT playSound(Sound sound,ChannelGroup group,bool paused,out Channel channel) { channel=new Channel { Sound=sound, Group=group, Paused=paused }; Channels.Add(channel); return RESULT.OK; }
 }
}
namespace FMOD.Studio {
 public class Bus {
  public int Locks; public FMOD.ChannelGroup Group=new FMOD.ChannelGroup();
  public FMOD.RESULT lockChannelGroup() { Locks++; return FMOD.RESULT.OK; } public FMOD.RESULT unlockChannelGroup() { Locks--; return FMOD.RESULT.OK; }
  public FMOD.RESULT getChannelGroup(out FMOD.ChannelGroup group) { group=Group; return FMOD.RESULT.OK; }
 }
 public class System {
  public FMOD.System Core=new FMOD.System(); public Bus Bus=new Bus();
  public FMOD.RESULT flushCommands() { return FMOD.RESULT.OK; }
  public FMOD.RESULT getLowLevelSystem(out FMOD.System core) { core=Core; return FMOD.RESULT.OK; }
  public FMOD.RESULT getBus(string path,out Bus bus) { if(path!="bus:/gameplay_sfx") throw new Exception("Unexpected bus"); bus=Bus; return FMOD.RESULT.OK; }
 }
}
