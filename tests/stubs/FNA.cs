using System.Reflection;
[assembly: AssemblyVersion("23.3.0.0")]
namespace Microsoft.Xna.Framework { public struct Vector2 { public float X,Y; public Vector2(float x,float y) { X=x; Y=y; } public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.X+b.X,a.Y+b.Y); } } }
