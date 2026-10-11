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
        public bool Airborne, Fresh, BubbleAnnounced;
    }
    // TechAnnouncer 1.0.1 records the Ducking setter inside vertical collisions.
    bool ultraDucked;
    int collidingVDepth, dashCoroutineDepth;
    IDisposable duckHook, frostHook;
    EventInfo frostBeginEvent;
    Delegate frostBeginHandler;
    FieldInfo frostBoostState, frostRedBoostState;
    bool frostScanPending;
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
        duckHook = CreateHook(typeof(Player).GetProperty("Ducking").GetSetMethod(),
            new Action<Action<Player, bool>, Player, bool>(SetDucking));
        AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
        TryHookFrost();
        On.Celeste.Player.CallDashEvents += CallDash;
        On.Celeste.Player.DashCoroutine += DashCoroutine;
        On.Celeste.Player.CorrectDashPrecision += CorrectDashPrecision;
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
    public override void Initialize() {
        if (loaded && frostHook == null) TryHookFrost();
    }
    public override void Unload() {
        if (!loaded) return;
        AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
        UnhookFrost();
        if (duckHook != null) duckHook.Dispose(); duckHook = null;
        ultraDucked = false; collidingVDepth = 0; dashCoroutineDepth = 0; frostScanPending = false;
        On.Celeste.Player.CallDashEvents -= CallDash;
        On.Celeste.Player.DashCoroutine -= DashCoroutine;
        On.Celeste.Player.CorrectDashPrecision -= CorrectDashPrecision;
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
    static IDisposable CreateHook(MethodBase method, Delegate handler) {
        Type hookType = Type.GetType("MonoMod.RuntimeDetour.Hook, MonoMod.RuntimeDetour", true);
        ConstructorInfo constructor = hookType.GetConstructor(new[] { typeof(MethodBase), typeof(Delegate) });
        if (method == null || constructor == null) throw new InvalidOperationException("Required game hook API unavailable.");
        // RuntimeDetour is supplied by Everest, never bundled into the generator.
        return (IDisposable)constructor.Invoke(new object[] { method, handler });
    }
    void SetDucking(Action<Player, bool> orig, Player self, bool value) {
        if (collidingVDepth > 0 && value) ultraDucked = true;
        orig(self, value);
    }
    void ForgetDash(Player self) {
        Dash dash; if (dashes.TryGetValue(self, out dash)) dash.Fresh = false;
    }
    void Update(On.Celeste.Player.orig_Update orig, Player self) {
        if (frostScanPending) TryHookFrost();
        orig(self); Refresh();
    }
    void AssemblyLoaded(object sender, AssemblyLoadEventArgs args) {
        string name = args.LoadedAssembly.GetName().Name;
        if (name == "FrostTempleHelper" || name == "FrostHelper") frostScanPending = true;
    }
    void TryHookFrost() {
        frostScanPending = false;
        if (frostHook != null) return;
        Type booster = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            booster = assembly.GetType("FrostHelper.Entities.Boosters.GenericCustomBooster", false);
            if (booster != null) break;
        }
        if (booster == null) return;
        try {
            MethodInfo update = booster.GetMethod("BoostUpdate", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Monocle.Entity) }, null);
            frostBoostState = booster.GetField("CustomBoostState", BindingFlags.Public | BindingFlags.Static);
            frostRedBoostState = booster.GetField("CustomRedBoostState", BindingFlags.Public | BindingFlags.Static);
            frostBeginEvent = booster.GetEvent("OnBoostBegin", BindingFlags.Public | BindingFlags.Static);
            if (update == null || update.ReturnType != typeof(int) || frostBoostState == null ||
                frostRedBoostState == null || frostBoostState.FieldType != typeof(int) ||
                frostRedBoostState.FieldType != typeof(int) || frostBeginEvent == null)
                throw new InvalidOperationException("Unsupported FrostHelper booster API.");
            frostBeginHandler = Delegate.CreateDelegate(frostBeginEvent.EventHandlerType, this,
                GetType().GetMethod("FrostBegin", BindingFlags.Instance | BindingFlags.NonPublic));
            frostHook = CreateHook(update, new Func<Func<Monocle.Entity, int>, Monocle.Entity, int>(FrostUpdate));
            frostBeginEvent.AddEventHandler(null, frostBeginHandler);
        } catch (Exception error) {
            UnhookFrost();
            Logger.Log(GetType().Assembly.GetName().Name, "FrostHelper fast bubble compatibility: " + error.Message);
        }
    }
    void UnhookFrost() {
        if (frostBeginEvent != null && frostBeginHandler != null) frostBeginEvent.RemoveEventHandler(null, frostBeginHandler);
        if (frostHook != null) frostHook.Dispose();
        frostHook = null; frostBeginEvent = null; frostBeginHandler = null;
        frostBoostState = null; frostRedBoostState = null;
    }
    void FrostBegin(Player self, Monocle.Entity booster) {
        ForgetDash(self); dashes.GetOrCreateValue(self).BubbleAnnounced = false;
    }
    int FrostUpdate(Func<Monocle.Entity, int> orig, Monocle.Entity entity) {
        Player self = entity as Player;
        bool inBubble = self != null && self.StateMachine.State == (int)frostBoostState.GetValue(null);
        int next = orig(entity);
        // Only BoostUpdate's manual exit returns a dash state; timed exits use
        // the separate coroutine. Read custom state IDs at the time of the call.
        if (inBubble && (next == 2 || next == (int)frostRedBoostState.GetValue(null))) AnnounceBubble(self);
        return next;
    }
    void AnnounceBubble(Player self) {
        Dash dash = dashes.GetOrCreateValue(self);
        if (!dash.BubbleAnnounced) { dash.BubbleAnnounced = true; Announce("fastbubble"); }
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
        dash.Airborne = !Field(self, "dashStartedOnGround", true); dash.Fresh = true;
    }
    void SuperJump(On.Celeste.Player.orig_SuperJump orig, Player self) {
        bool duck = self.Ducking; Dash dash;
        bool wave = dashes.TryGetValue(self, out dash) && dash.Fresh && dash.Airborne && Math.Abs(dash.Direction.X) > .01f && dash.Direction.Y > .01f;
        ultraDucked = false; ForgetDash(self);
        orig(self);
        Announce(duck ? (wave ? "wavedash" : "hyperdash") : "superdash");
    }
    void Jump(On.Celeste.Player.orig_Jump orig, Player self, bool particles, bool playSfx) {
        // Match TechAnnouncer 1.0.1: test and consume before the original Jump.
        // It does not require a measured 1.2x gain, ground contact or jump grace.
        if (ultraDucked && self.StateMachine.State == 0) {
            if (self.Speed.X * self.Speed.X + self.Speed.Y * self.Speed.Y > 57600f) Announce("ultradash");
            ultraDucked = false;
        }
        ForgetDash(self); orig(self, particles, playSfx);
    }
    void WallBounce(On.Celeste.Player.orig_SuperWallJump orig, Player self, int dir) { ForgetDash(self); orig(self, dir); Announce("wallbounce"); }
    void WallJump(On.Celeste.Player.orig_WallJump orig, Player self, int dir) {
        bool neutral = Input.MoveX.Value == 0; ForgetDash(self); orig(self, dir); if (neutral) Announce("neutral");
    }
    void ClimbJump(On.Celeste.Player.orig_ClimbJump orig, Player self) {
        float before = self.Speed.X * (int)self.Facing;
        bool corner = before > 0 && !Field(self, "onGround", true) && !self.CollideCheck<Solid>(self.Position + new Vector2((int)self.Facing, -6));
        ForgetDash(self); orig(self);
        if (corner && self.Speed.X * (int)self.Facing > before && self.Speed.Y < 0) Announce("cornerboost");
    }
    void CollideV(On.Celeste.Player.orig_OnCollideV orig, Player self, CollisionData data) {
        collidingVDepth++;
        try { orig(self, data); }
        finally { collidingVDepth--; }
    }
    Vector2 CorrectDashPrecision(On.Celeste.Player.orig_CorrectDashPrecision orig, Player self, Vector2 direction) {
        Vector2 corrected = orig(self, direction);
        // TechAnnouncer 1.0.1 tests the corrected direction during MoveNext only.
        if (dashCoroutineDepth > 0 && self.Ducking && corrected.Y <= 0) Announce("demodash");
        return corrected;
    }
    IEnumerator DashCoroutine(On.Celeste.Player.orig_DashCoroutine orig, Player self) {
        ForgetDash(self);
        IEnumerator routine = orig(self);
        try {
            while (true) {
                bool next;
                dashCoroutineDepth++;
                try { next = routine.MoveNext(); }
                finally { dashCoroutineDepth--; }
                if (!next) yield break;
                yield return routine.Current;
            }
        } finally {
            IDisposable disposable = routine as IDisposable;
            if (disposable != null) disposable.Dispose();
        }
    }
    void BoostBegin(On.Celeste.Player.orig_BoostBegin orig, Player self) {
        ForgetDash(self); dashes.GetOrCreateValue(self).BubbleAnnounced = false; orig(self);
    }
    int BoostUpdate(On.Celeste.Player.orig_BoostUpdate orig, Player self) {
        bool inBubble = self.StateMachine.State == 4;
        int next = orig(self);
        if (inBubble && (next == 2 || next == 5)) AnnounceBubble(self);
        return next;
    }
    PlayerDeadBody Die(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
        bool wasDead = self.Dead, golden = false;
        if (self.Leader != null) foreach (Follower follower in self.Leader.Followers) {
            Strawberry berry = follower.Entity as Strawberry;
            if (berry != null && berry.Golden) { golden = true; break; }
        }
        PlayerDeadBody body = orig(self, direction, evenIfInvincible, registerDeathInStats);
        if (body != null) ForgetDash(self);
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
