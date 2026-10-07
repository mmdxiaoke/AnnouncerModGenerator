using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Celeste;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.IndependentAnnouncer {
public sealed class AnnouncerSettings : EverestModuleSettings {
    public bool Enabled { get; set; }
    [SettingRange(0, 10)] public int Volume { get; set; }
    public AnnouncerSettings() { Enabled = true; Volume = 10; }
}

// All hooks observe the game. Each original method is invoked exactly once.
public sealed class AnnouncerModule : EverestModule {
    public override Type SettingsType { get { return typeof(AnnouncerSettings); } }
    AnnouncerSettings Settings { get { return (AnnouncerSettings)_Settings; } }
    readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    readonly Random random = new Random();
    readonly Dictionary<string, FMOD.Sound> sounds = new Dictionary<string, FMOD.Sound>();
    readonly List<FMOD.Channel> channels = new List<FMOD.Channel>();
    ConditionalWeakTable<Player, Dash> dashes = new ConditionalWeakTable<Player, Dash>();
    FMOD.System core;
    FMOD.Studio.Bus bus;
    FMOD.ChannelGroup group;
    bool loaded, busLocked, audioErrorLogged;
    class Dash { public Vector2 Direction; public bool Airborne, Ultra, Fresh; }
    static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
    static readonly FieldInfo Collected = typeof(Strawberry).GetField("collected", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    static T Field<T>(Player player, string name, T fallback) {
        FieldInfo info;
        if (!Fields.TryGetValue(name, out info)) {
            info = typeof(Player).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Fields[name] = info;
        }
        return info == null ? fallback : (T)info.GetValue(player);
    }
    public override void Load() {
        if (loaded) return;
        using (var stream = GetType().Assembly.GetManifestResourceStream("announcer.config")) {
            if (stream != null) using (var reader = new StreamReader(stream)) {
                foreach (string item in reader.ReadToEnd().Split(';')) {
                    string[] parts = item.Split('='); int count;
                    if (parts.Length == 2 && Int32.TryParse(parts[1], out count) && count >= 0 && count <= 5) counts[parts[0]] = count;
                }
            }
        }
        On.Celeste.Player.CallDashEvents += CallDash;
        On.Celeste.Player.SuperJump += SuperJump;
        On.Celeste.Player.SuperWallJump += WallBounce;
        On.Celeste.Player.WallJump += WallJump;
        On.Celeste.Player.ClimbJump += ClimbJump;
        On.Celeste.Player.OnCollideV += CollideV;
        On.Celeste.Player.BoostUpdate += BoostUpdate;
        On.Celeste.Player.Die += Die;
        On.Celeste.Player.Update += Update;
        On.Celeste.Strawberry.OnCollect += Collect;
        loaded = true;
    }
    public override void Unload() {
        if (!loaded) return;
        On.Celeste.Player.CallDashEvents -= CallDash;
        On.Celeste.Player.SuperJump -= SuperJump;
        On.Celeste.Player.SuperWallJump -= WallBounce;
        On.Celeste.Player.WallJump -= WallJump;
        On.Celeste.Player.ClimbJump -= ClimbJump;
        On.Celeste.Player.OnCollideV -= CollideV;
        On.Celeste.Player.BoostUpdate -= BoostUpdate;
        On.Celeste.Player.Die -= Die;
        On.Celeste.Player.Update -= Update;
        On.Celeste.Strawberry.OnCollect -= Collect;
        foreach (var channel in channels) channel.stop(); channels.Clear();
        foreach (var sound in sounds.Values) sound.release(); sounds.Clear();
        if (busLocked && bus != null) bus.unlockChannelGroup();
        busLocked = false; bus = null; group = null; core = null;
        counts.Clear(); dashes = new ConditionalWeakTable<Player, Dash>(); loaded = false; audioErrorLogged = false;
    }
    void Check(FMOD.RESULT result) { if (result != FMOD.RESULT.OK) throw new InvalidOperationException("FMOD: " + result); }
    void Announce(string key) {
        int count;
        if (Settings == null || !Settings.Enabled || Settings.Volume <= 0 || !counts.TryGetValue(key, out count) || count == 0) return;
        try {
            if (Audio.System == null) return;
            if (core == null) Check(Audio.System.getLowLevelSystem(out core));
            if (bus == null) Check(Audio.System.getBus("bus:/gameplay_sfx", out bus));
            if (!busLocked) { Check(bus.lockChannelGroup()); busLocked = true; Check(Audio.System.flushCommands()); }
            if (group == null) Check(bus.getChannelGroup(out group));
            int variant; lock (random) variant = random.Next(count) + 1;
            string resource = "announcer." + key + ".v" + variant + ".wav";
            FMOD.Sound sound;
            if (!sounds.TryGetValue(resource, out sound)) {
                byte[] data;
                using (var stream = GetType().Assembly.GetManifestResourceStream(resource)) {
                    if (stream == null) throw new InvalidOperationException("Missing audio: " + resource);
                    using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); data = buffer.ToArray(); }
                }
                var info = new FMOD.CREATESOUNDEXINFO { cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)), length = (uint)data.Length };
                Check(core.createSound(data, FMOD.MODE.OPENMEMORY | FMOD.MODE.LOOP_OFF | FMOD.MODE._2D, ref info, out sound));
                sounds.Add(resource, sound);
            }
            Refresh();
            if (channels.Count >= 8) { channels[0].stop(); channels.RemoveAt(0); }
            FMOD.Channel channel;
            Check(core.playSound(sound, group, true, out channel)); channels.Add(channel);
            Check(channel.setVolume(Math.Min(10, Settings.Volume) / 10f)); Check(channel.setPaused(false));
            audioErrorLogged = false;
        } catch (Exception error) {
            if (!audioErrorLogged) Logger.Log(GetType().Assembly.GetName().Name, "Announcer audio: " + error.Message);
            audioErrorLogged = true;
        }
    }
    void Refresh() {
        for (int i = channels.Count - 1; i >= 0; i--) {
            bool playing;
            if (Settings == null || !Settings.Enabled || Settings.Volume <= 0) channels[i].stop();
            if (channels[i].isPlaying(out playing) != FMOD.RESULT.OK || !playing) channels.RemoveAt(i);
            else channels[i].setVolume(Math.Min(10, Settings.Volume) / 10f);
        }
    }
    void Update(On.Celeste.Player.orig_Update orig, Player self) { orig(self); Refresh(); }
    void CallDash(On.Celeste.Player.orig_CallDashEvents orig, Player self) {
        bool already = Field(self, "calledDashEvents", false);
        orig(self);
        if (already || !Field(self, "calledDashEvents", true)) return;
        Dash dash = dashes.GetOrCreateValue(self);
        dash.Direction = self.DashDir;
        Vector2 aim = Field(self, "lastAim", self.DashDir);
        // Ground contact can flatten a downward dash before this callback.
        if (self.Ducking && Math.Abs(aim.X) > .01f && aim.Y > .01f) dash.Direction = aim;
        dash.Airborne = !Field(self, "dashStartedOnGround", true); dash.Ultra = false; dash.Fresh = true;
        if (Math.Abs(self.DashDir.X) > .01f && Math.Abs(self.DashDir.Y) < .01f && (self.Ducking || Field(self, "demoDashed", false))) Announce("demodash");
    }
    void SuperJump(On.Celeste.Player.orig_SuperJump orig, Player self) {
        bool duck = self.Ducking; Dash dash;
        bool wave = dashes.TryGetValue(self, out dash) && dash.Fresh && dash.Airborne && Math.Abs(dash.Direction.X) > .01f && dash.Direction.Y > .01f;
        orig(self);
        if (dash != null) dash.Fresh = false;
        Announce(duck ? (wave ? "wavedash" : "hyperdash") : "superdash");
    }
    void WallBounce(On.Celeste.Player.orig_SuperWallJump orig, Player self, int dir) { orig(self, dir); Announce("wallbounce"); }
    void WallJump(On.Celeste.Player.orig_WallJump orig, Player self, int dir) {
        bool neutral = Input.MoveX.Value == 0; orig(self, dir); if (neutral) Announce("neutral");
    }
    void ClimbJump(On.Celeste.Player.orig_ClimbJump orig, Player self) {
        float before = self.Speed.X * (int)self.Facing;
        bool corner = before > 0 && !Field(self, "onGround", true) && !self.CollideCheck<Solid>(self.Position + new Vector2((int)self.Facing, -6));
        orig(self);
        if (corner && self.Speed.X * (int)self.Facing > before && self.Speed.Y < 0) Announce("cornerboost");
    }
    void CollideV(On.Celeste.Player.orig_OnCollideV orig, Player self, CollisionData data) {
        Vector2 dir = self.DashDir; float speed = Math.Abs(self.Speed.X); Dash dash;
        bool ultra = dashes.TryGetValue(self, out dash) && !dash.Ultra && Math.Abs(dir.X) > .01f && dir.Y > .01f && self.Speed.Y > 0 && self.StateMachine.State == 2 && speed > 240f * Math.Abs(dir.X) + 1f;
        orig(self, data);
        if (ultra && Math.Abs(self.Speed.Y) < .01f && Math.Abs(self.Speed.X) > speed * 1.1f) { dash.Ultra = true; Announce("ultradash"); }
    }
    int BoostUpdate(On.Celeste.Player.orig_BoostUpdate orig, Player self) {
        bool green = self.StateMachine.State == 4 && !Field(self, "boostRed", true);
        int next = orig(self); if (green && next == 2) Announce("fastbubble"); return next;
    }
    PlayerDeadBody Die(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        bool wasDead = self.Dead, golden = false;
        if (self.Leader != null) foreach (Follower follower in self.Leader.Followers) {
            Strawberry berry = follower.Entity as Strawberry;
            if (berry != null && berry.Golden) { golden = true; break; }
        }
        PlayerDeadBody body = orig(self, direction, evenIfInvincible, registerDeathInStats);
        if (!wasDead && body != null) Announce(golden ? "goldendeath" : "death");
        return body;
    }
    void Collect(On.Celeste.Strawberry.orig_OnCollect orig, Strawberry self) {
        bool collected = Collected != null && (bool)Collected.GetValue(self), golden = self.Golden;
        orig(self);
        if (!collected && Collected != null && (bool)Collected.GetValue(self)) Announce(golden ? "goldenstrawberry" : "strawberry");
    }
}
}
