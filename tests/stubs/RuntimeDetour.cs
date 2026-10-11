using System; using System.Reflection; using System.Collections.Generic;
namespace MonoMod.RuntimeDetour {
 public sealed class Hook : IDisposable {
  static readonly Dictionary<string,Hook> hooks=new Dictionary<string,Hook>();
  string key; public Delegate Handler; public bool Disposed;
  public Hook(MethodBase method,Delegate handler) { key=method.DeclaringType.FullName+"::"+method.Name; if(hooks.ContainsKey(key))throw new Exception("duplicate test hook");Handler=handler;hooks.Add(key,this); }
  public static Delegate TestFind(string type,string name) { Hook h;return hooks.TryGetValue(type+"::"+name,out h)?h.Handler:null; }
  public static int TestCount { get { return hooks.Count; } }
  public void Dispose() { if(!Disposed){hooks.Remove(key);Disposed=true;} }
 }
}
