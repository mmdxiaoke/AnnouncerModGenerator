using System;
using System.Collections;
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
    [SettingRange(0, 100)] public int CornerBoostVolume { get; set; }
    [SettingRange(0, 100)] public int DemoDashVolume { get; set; }
    [SettingRange(0, 100)] public int FastBubbleVolume { get; set; }
    [SettingRange(0, 100)] public int HyperVolume { get; set; }
    [SettingRange(0, 100)] public int NeutralJumpVolume { get; set; }
    [SettingRange(0, 100)] public int SuperVolume { get; set; }
    [SettingRange(0, 100)] public int UltraVolume { get; set; }
    [SettingRange(0, 100)] public int WallBounceVolume { get; set; }
    [SettingRange(0, 100)] public int WavedashVolume { get; set; }
    [SettingRange(0, 100)] public int DeathVolume { get; set; }
    [SettingRange(0, 100)] public int GoldenDeathVolume { get; set; }
    [SettingRange(0, 100)] public int StrawberryVolume { get; set; }
    [SettingRange(0, 100)] public int GoldenStrawberryVolume { get; set; }
    public AnnouncerSettings() {
        Enabled = true; Volume = 10;
        CornerBoostVolume = 100;
        DemoDashVolume = 100;
        FastBubbleVolume = 100;
        HyperVolume = 100;
        NeutralJumpVolume = 100;
        SuperVolume = 100;
        UltraVolume = 100;
        WallBounceVolume = 100;
        WavedashVolume = 100;
        DeathVolume = 100;
        GoldenDeathVolume = 100;
        StrawberryVolume = 100;
        GoldenStrawberryVolume = 100;
        using (var stream = typeof(AnnouncerSettings).Assembly.GetManifestResourceStream("announcer.volumes")) {
            if (stream != null) using (var reader = new StreamReader(stream)) {
                foreach (string item in reader.ReadToEnd().Split(';')) {
                    string[] parts = item.Split('='); int value;
                    if (parts.Length == 2 && Int32.TryParse(parts[1], out value) && value >= 0 && value <= 100) SetEventVolume(parts[0], value);
                }
            }
        }
    }
    public int EventVolume(string key) {
        switch (key) {
            case "cornerboost": return CornerBoostVolume;
            case "demodash": return DemoDashVolume;
            case "fastbubble": return FastBubbleVolume;
            case "hyperdash": return HyperVolume;
            case "neutral": return NeutralJumpVolume;
            case "superdash": return SuperVolume;
            case "ultradash": return UltraVolume;
            case "wallbounce": return WallBounceVolume;
            case "wavedash": return WavedashVolume;
            case "death": return DeathVolume;
            case "goldendeath": return GoldenDeathVolume;
            case "strawberry": return StrawberryVolume;
            case "goldenstrawberry": return GoldenStrawberryVolume;
            default: return 100;
        }
    }
    void SetEventVolume(string key, int value) {
        switch (key) {
            case "cornerboost": CornerBoostVolume = value; break;
            case "demodash": DemoDashVolume = value; break;
            case "fastbubble": FastBubbleVolume = value; break;
            case "hyperdash": HyperVolume = value; break;
            case "neutral": NeutralJumpVolume = value; break;
            case "superdash": SuperVolume = value; break;
            case "ultradash": UltraVolume = value; break;
            case "wallbounce": WallBounceVolume = value; break;
            case "wavedash": WavedashVolume = value; break;
            case "death": DeathVolume = value; break;
            case "goldendeath": GoldenDeathVolume = value; break;
            case "strawberry": StrawberryVolume = value; break;
            case "goldenstrawberry": GoldenStrawberryVolume = value; break;
        }
    }
}

// All hooks observe the game. Each original method is invoked exactly once.
public sealed class AnnouncerModule : EverestModule {
    public override Type SettingsType { get { return typeof(AnnouncerSettings); } }
    AnnouncerSettings Settings { get { return (AnnouncerSettings)_Settings; } }
    readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    readonly Random random = new Random();
    readonly Dictionary<string, FMOD.Sound> sounds = new Dictionary<string, FMOD.Sound>();
    readonly List<Voice> channels = new List<Voice>();
    class Voice { public string Key; public FMOD.Channel Channel; }
    ConditionalWeakTable<Player, Dash> dashes = new ConditionalWeakTable<Player, Dash>();
    FMOD.System core;
    FMOD.Studio.Bus bus;
    FMOD.ChannelGroup group;
    bool loaded, busLocked, audioErrorLogged;
    class Dash {
        public Vector2 Direction;
        public int Serial, UltraDirection;
        public float UltraSpeed;
        public bool Airborne, Fresh, BubbleAnnounced, PendingUltra, LandedThisUpdate;
    }
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
        On.Celeste.Player.DashCoroutine += DashCoroutine;
        On.Celeste.Player.BoostBegin += BoostBegin;
        On.Celeste.Player.SuperJump += SuperJump;
        On.Celeste.Player.Jump += Jump;
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
        On.Celeste.Player.DashCoroutine -= DashCoroutine;
        On.Celeste.Player.BoostBegin -= BoostBegin;
        On.Celeste.Player.SuperJump -= SuperJump;
        On.Celeste.Player.Jump -= Jump;
        On.Celeste.Player.SuperWallJump -= WallBounce;
        On.Celeste.Player.WallJump -= WallJump;
        On.Celeste.Player.ClimbJump -= ClimbJump;
        On.Celeste.Player.OnCollideV -= CollideV;
        On.Celeste.Player.BoostUpdate -= BoostUpdate;
        On.Celeste.Player.Die -= Die;
        On.Celeste.Player.Update -= Update;
        On.Celeste.Strawberry.OnCollect -= Collect;
        foreach (var voice in channels) voice.Channel.stop(); channels.Clear();
        foreach (var sound in sounds.Values) sound.release(); sounds.Clear();
        if (busLocked && bus != null) bus.unlockChannelGroup();
        busLocked = false; bus = null; group = null; core = null;
        counts.Clear(); dashes = new ConditionalWeakTable<Player, Dash>(); loaded = false; audioErrorLogged = false;
    }
    void Check(FMOD.RESULT result) { if (result != FMOD.RESULT.OK) throw new InvalidOperationException("FMOD: " + result); }
    void Announce(string key) {
        int count;
        if (Settings == null || !Settings.Enabled || Gain(key) <= 0 || !counts.TryGetValue(key, out count) || count == 0) return;
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
            if (channels.Count >= 8) { channels[0].Channel.stop(); channels.RemoveAt(0); }
            FMOD.Channel channel;
            Check(core.playSound(sound, group, true, out channel)); channels.Add(new Voice { Key = key, Channel = channel });
            Check(channel.setVolume(Gain(key))); Check(channel.setPaused(false));
            audioErrorLogged = false;
        } catch (Exception error) {
            if (!audioErrorLogged) Logger.Log(GetType().Assembly.GetName().Name, "Announcer audio: " + error.Message);
            audioErrorLogged = true;
        }
    }
    float Gain(string key) {
        if (Settings == null || !Settings.Enabled) return 0;
        return Math.Max(0, Math.Min(10, Settings.Volume)) / 10f * Math.Max(0, Math.Min(100, Settings.EventVolume(key))) / 100f;
    }
    void Refresh() {
        for (int i = channels.Count - 1; i >= 0; i--) {
            Voice voice = channels[i]; bool playing; float gain = Gain(voice.Key);
            if (gain <= 0) voice.Channel.stop();
            if (voice.Channel.isPlaying(out playing) != FMOD.RESULT.OK || !playing) channels.RemoveAt(i);
            else voice.Channel.setVolume(gain);
        }
    }
    static bool KeepsUltraSpeed(Player self, Dash dash) {
        return Math.Sign(self.Speed.X) == dash.UltraDirection && Math.Abs(self.Speed.X) >= dash.UltraSpeed / 1.2f - .01f;
    }
    void CancelUltra(Player self) {
        Dash dash;
        if (dashes.TryGetValue(self, out dash)) { dash.PendingUltra = false; dash.Fresh = false; }
    }
    void Update(On.Celeste.Player.orig_Update orig, Player self) {
        orig(self);
        Dash dash;
        if (dashes.TryGetValue(self, out dash) && dash.PendingUltra) {
            // onGround is sampled before movement, so it can still be false in
            // the Update that ran the landing callback. Allow that frame only.
            bool landed = dash.LandedThisUpdate; dash.LandedThisUpdate = false;
            if (self.Dead || self.StateMachine.State != 0 || !KeepsUltraSpeed(self, dash) || self.Speed.Y < -.01f ||
                (!landed && !Field(self, "onGround", false))) dash.PendingUltra = false;
        }
        Refresh();
    }
    void CallDash(On.Celeste.Player.orig_CallDashEvents orig, Player self) {
        bool already = Field(self, "calledDashEvents", false);
        orig(self);
        if (already || !Field(self, "calledDashEvents", true)) return;
        Dash dash = dashes.GetOrCreateValue(self);
        dash.Direction = self.DashDir;
        Vector2 aim = Field(self, "lastAim", self.DashDir);
        // Ground contact can flatten a downward dash before this callback.
        if (self.Ducking && Math.Abs(aim.X) > .01f && aim.Y > .01f) dash.Direction = aim;
        dash.Airborne = !Field(self, "dashStartedOnGround", true); dash.PendingUltra = false; dash.LandedThisUpdate = false; dash.Fresh = true;
        dash.Serial++;
        if (Math.Abs(self.DashDir.X) > .01f && Math.Abs(self.DashDir.Y) < .01f && (self.Ducking || Field(self, "demoDashed", false))) Announce("demodash");
    }
    void SuperJump(On.Celeste.Player.orig_SuperJump orig, Player self) {
        bool duck = self.Ducking; Dash dash;
        bool wave = dashes.TryGetValue(self, out dash) && dash.Fresh && dash.Airborne && Math.Abs(dash.Direction.X) > .01f && dash.Direction.Y > .01f;
        bool ultra = TakeUltra(self, dash);
        orig(self);
        if (ultra && self.Speed.Y < -.01f) Announce("ultradash");
        else Announce(duck ? (wave ? "wavedash" : "hyperdash") : "superdash");
    }
    static bool TakeUltra(Player self, Dash dash) {
        if (dash == null) return false;
        bool ultra = dash.PendingUltra && !self.Dead && self.StateMachine.State == 0 &&
            Math.Abs(self.Speed.Y) < .01f && KeepsUltraSpeed(self, dash);
        // Consume before orig to prevent nested hooks or later jumps replaying it.
        dash.PendingUltra = false; dash.Fresh = false;
        return ultra;
    }
    void Jump(On.Celeste.Player.orig_Jump orig, Player self, bool particles, bool playSfx) {
        Dash dash; dashes.TryGetValue(self, out dash);
        bool ultra = TakeUltra(self, dash);
        orig(self, particles, playSfx);
        if (ultra && self.Speed.Y < -.01f) Announce("ultradash");
    }
    void WallBounce(On.Celeste.Player.orig_SuperWallJump orig, Player self, int dir) { CancelUltra(self); orig(self, dir); Announce("wallbounce"); }
    void WallJump(On.Celeste.Player.orig_WallJump orig, Player self, int dir) {
        bool neutral = Input.MoveX.Value == 0; CancelUltra(self); orig(self, dir); if (neutral) Announce("neutral");
    }
    void ClimbJump(On.Celeste.Player.orig_ClimbJump orig, Player self) {
        float before = self.Speed.X * (int)self.Facing;
        bool corner = before > 0 && !Field(self, "onGround", true) && !self.CollideCheck<Solid>(self.Position + new Vector2((int)self.Facing, -6));
        CancelUltra(self); orig(self);
        if (corner && self.Speed.X * (int)self.Facing > before && self.Speed.Y < 0) Announce("cornerboost");
    }
    void CollideV(On.Celeste.Player.orig_OnCollideV orig, Player self, CollisionData data) {
        Vector2 direction = self.DashDir; float speed = Math.Abs(self.Speed.X);
        Dash dash = dashes.GetOrCreateValue(self); int serial = dash.Serial;
        // A Wave can also get a collision multiplier during StDash. Only a
        // completed diagonal-down dash landing in StNormal can arm Ultra.
        bool eligible = dash.Fresh && self.StateMachine.State == 0 && self.Speed.Y > 0 && speed > .01f &&
            Math.Abs(direction.X) > .01f && direction.Y > .01f &&
            Math.Abs(dash.Direction.X) > .01f && dash.Direction.Y > .01f &&
            Math.Sign(self.Speed.X) == Math.Sign(direction.X);
        orig(self, data);
        float boosted = speed * 1.2f;
        if (eligible && dash.Serial == serial && self.StateMachine.State == 0 &&
            Math.Abs(self.DashDir.Y) < .01f && Math.Sign(self.DashDir.X) == Math.Sign(direction.X) &&
            Math.Sign(self.Speed.X) == Math.Sign(direction.X) && Math.Abs(self.Speed.Y) < .01f &&
            Math.Abs(Math.Abs(self.Speed.X) - boosted) <= Math.Max(.01f, boosted * .001f)) {
            dash.PendingUltra = true; dash.LandedThisUpdate = true;
            dash.UltraSpeed = Math.Abs(self.Speed.X); dash.UltraDirection = Math.Sign(self.Speed.X);
        }
    }
    IEnumerator DashCoroutine(On.Celeste.Player.orig_DashCoroutine orig, Player self) {
        // Starting another dash invalidates the landing even before freeze ends
        // and CallDashEvents runs. Never treat startup acceleration as Ultra.
        CancelUltra(self); return orig(self);
    }
    void BoostBegin(On.Celeste.Player.orig_BoostBegin orig, Player self) {
        CancelUltra(self); dashes.GetOrCreateValue(self).BubbleAnnounced = false; orig(self);
    }
    int BoostUpdate(On.Celeste.Player.orig_BoostUpdate orig, Player self) {
        bool inBubble = self.StateMachine.State == 4;
        int next = orig(self); Dash dash = dashes.GetOrCreateValue(self);
        if (inBubble && (next == 2 || next == 5) && !dash.BubbleAnnounced) {
            dash.BubbleAnnounced = true; Announce("fastbubble");
        }
        return next;
    }
    PlayerDeadBody Die(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        bool wasDead = self.Dead, golden = false;
        if (self.Leader != null) foreach (Follower follower in self.Leader.Followers) {
            Strawberry berry = follower.Entity as Strawberry;
            if (berry != null && berry.Golden) { golden = true; break; }
        }
        PlayerDeadBody body = orig(self, direction, evenIfInvincible, registerDeathInStats);
        if (body != null) CancelUltra(self);
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
