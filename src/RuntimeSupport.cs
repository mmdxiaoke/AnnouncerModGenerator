using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;

namespace AnnouncerRuntime {
public static class Support {
    static readonly Random Random = new Random();
    static Dictionary<string, int> counts = new Dictionary<string, int>();
    static string prefix;
    static Action<string> announce;
    static readonly FieldInfo Collected = typeof(Strawberry).GetField("collected", BindingFlags.Instance | BindingFlags.NonPublic);
    static bool loaded;
    public static void Configure(string slug, string configuration, Action<string> callback) {
        counts.Clear(); prefix = "event:/brokemia/tech_announcer/" + slug + "/"; announce = callback;
        foreach (string item in configuration.Split(';')) {
            if (item.Length == 0) continue;
            string[] parts = item.Split('='); counts[parts[0]] = Int32.Parse(parts[1]);
        }
    }
    public static string AudioPath(string key) {
        int count;
        if (!counts.TryGetValue(key, out count) || count == 0) return null;
        int variant; lock (Random) variant = Random.Next(count) + 1;
        return prefix + key + "_v" + variant;
    }
    public static void Load() {
        if (loaded) return;
        On.Celeste.Player.Die += PlayerDie;
        On.Celeste.Strawberry.OnCollect += StrawberryCollect;
        loaded = true;
    }
    public static void Unload() {
        if (!loaded) return;
        On.Celeste.Player.Die -= PlayerDie;
        On.Celeste.Strawberry.OnCollect -= StrawberryCollect;
        announce = null; counts.Clear(); loaded = false;
    }
    static PlayerDeadBody PlayerDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        bool wasDead = self.Dead, golden = false;
        // Followers can be removed by Die; capture the golden before calling it.
        if (self.Leader != null) foreach (Follower follower in self.Leader.Followers) {
            Strawberry berry = follower.Entity as Strawberry;
            if (berry != null && berry.Golden) { golden = true; break; }
        }
        PlayerDeadBody body = orig(self, direction, evenIfInvincible, registerDeathInStats);
        if (!wasDead && body != null && announce != null) announce(golden ? "goldendeath" : "death");
        return body;
    }
    static void StrawberryCollect(On.Celeste.Strawberry.orig_OnCollect orig, Strawberry self) {
        bool wasCollected = Collected != null && (bool)Collected.GetValue(self);
        bool golden = self.Golden;
        orig(self);
        // OnCollect also receives repeated calls; announce only successful collection.
        if (!wasCollected && Collected != null && (bool)Collected.GetValue(self) && announce != null)
            announce(golden ? "goldenstrawberry" : "strawberry");
    }
}
}
