// Minimal API declarations/test doubles, authored for this project.
using System.Collections.Generic;
using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace Monocle {
    public class Entity { }
    public class Component { public Entity Entity { get; set; } }
}
namespace Celeste {
    public class Leader : Monocle.Component { public List<Follower> Followers = new List<Follower>(); }
    public class Follower : Monocle.Component { public Leader Leader; }
    public class Player : Monocle.Entity { public Leader Leader; public bool Dead { get; set; } }
    public class PlayerDeadBody { }
    public class Strawberry : Monocle.Entity {
        private bool collected;
        public bool Golden { get; set; }
        public void TestCollect() { collected = true; }
        public bool TestCollected { get { return collected; } }
    }
}
