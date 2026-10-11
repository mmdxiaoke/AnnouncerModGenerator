using System; using System.IO; using System.Reflection; using System.Runtime.CompilerServices;
class RealDetourCompatibilityTest {
 static int calls,observed;
 [MethodImpl(MethodImplOptions.NoInlining)] static int Target(int value) { calls++;return value+1; }
 static int Intercept(Func<int,int> orig,int value) { return orig(value)+10; }
 class Holder {
  bool value;
  public bool Ducking { get { return value; } [MethodImpl(MethodImplOptions.NoInlining)] set { this.value=value; } }
 }
 static void SetDuck(Action<Holder,bool> orig,Holder self,bool value) { if(value)observed++;orig(self,value); }
 static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
 public static void Main(string[] args) {
  string game=Path.GetFullPath(args[0]);
  AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender,ResolveEventArgs e) {
   string file=Path.Combine(game,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;
  };
  var assembly=Assembly.LoadFrom(Path.Combine(game,"MonoMod.RuntimeDetour.dll"));
  var type=assembly.GetType("MonoMod.RuntimeDetour.Hook",true);
  var ctor=type.GetConstructor(new[] { typeof(MethodBase),typeof(Delegate) });Check(ctor!=null,"Hook constructor");
  Check(Target(1)==2,"initial target");calls=0;
  var hook=(IDisposable)ctor.Invoke(new object[] { typeof(RealDetourCompatibilityTest).GetMethod("Target",BindingFlags.NonPublic|BindingFlags.Static),new Func<Func<int,int>,int,int>(Intercept) });
  Check(Target(1)==12 && calls==1,"static update original exactly once");hook.Dispose();calls=0;
  Check(Target(1)==2 && calls==1,"static update restored");
  var holder=new Holder();holder.Ducking=false;
  hook=(IDisposable)ctor.Invoke(new object[] { typeof(Holder).GetProperty("Ducking").GetSetMethod(),new Action<Action<Holder,bool>,Holder,bool>(SetDuck) });
  holder.Ducking=true;Check(holder.Ducking && observed==1,"Ducking setter intercepted and original preserved");
  holder.Ducking=false;Check(!holder.Ducking && observed==1,"false setter preserved");hook.Dispose();holder.Ducking=true;
  Check(holder.Ducking && observed==1,"Ducking setter restored");
  Console.WriteLine("PASS: real game RuntimeDetour static update and Ducking setter installation, originals and disposal.");
 }
}
