using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AnnouncerBuilder {
public static class Builder {
    public static readonly string[] Events = { "cornerboost", "demodash", "fastbubble", "hyperdash", "neutral", "superdash", "ultradash", "wallbounce", "wavedash", "death", "goldendeath", "strawberry", "goldenstrawberry" };
    public static readonly string[] Labels = { "抓角加速", "下蹲冲刺", "泡泡快启", "Hyper", "中性跳", "Super", "Ultra", "蹭墙跳", "凌波微步", "普通死亡", "带金草莓死亡", "吃掉草莓", "吃掉金草莓" };
    public static readonly string[] Extensions = { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".aiff", ".aif" };
    const int Rate = 48000, MaximumBytes = Rate * 2 * 30;
    const string TemplateHash = "0ef0f3fc74299b70b896410b5a1b58e0261e3d500322b88b1385f1248ad008b2";
    public const string Version = "1.2.0";
    static readonly string[] BankEvents = Events.Take(9).ToArray();
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    public static string DetectEvent(string path) {
        string s = Regex.Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), @"[\s_\-()]", "");
        s = Regex.Replace(s, @"[1-5]$", "");
        if (s == "normaldeath") return "death";
        if (s == "golddeath") return "goldendeath";
        if (s == "collectstrawberry") return "strawberry";
        if (s == "collectgoldenstrawberry") return "goldenstrawberry";
        if (s == "neutraljump" || s == "中性跳") return "neutral";
        for (int i = 0; i < Events.Length; i++) if (s == Events[i] || s == Labels[i].ToLowerInvariant()) return Events[i];
        return null;
    }
    public static Dictionary<string, List<string>> ReadFolder(string folder) {
        if (!Directory.Exists(folder)) throw new Exception("音频文件夹不存在。");
        var result = new Dictionary<string, List<string>>();
        foreach (string p in Directory.GetFiles(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
            if (!Extensions.Contains(Path.GetExtension(p).ToLowerInvariant())) continue;
            string e = DetectEvent(p);
            if (e == null) continue;
            if (!result.ContainsKey(e)) result[e] = new List<string>();
            if (result[e].Count == 5) throw new Exception(e + " 超过五条音频，请只保留最多五条。");
            result[e].Add(Path.GetFullPath(p));
        }
        return result;
    }
    public static string FindFFmpeg() {
        var paths = new List<string> { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe") };
        string env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!String.IsNullOrEmpty(env)) paths.Add(env);
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            try { paths.Add(Path.Combine(folder.Trim('"'), "ffmpeg.exe")); } catch (ArgumentException) { }
        }
        foreach (string root in new[] { "D:\\steam", "C:\\Program Files (x86)\\Steam", "C:\\Steam" }) {
            paths.Add(Path.Combine(root, "steamapps", "common", "Celeste", "Mods", "Cache", "TASRecorder", "ffmpeg.exe"));
            paths.Add(Path.Combine(root, "steamapps", "common", "A Dance of Fire and Ice", "ffmpeg", "ffmpeg.exe"));
        }
        return paths.FirstOrDefault(File.Exists) ?? "";
    }
    // Windows command-line quoting, including embedded quotes and trailing backslashes.
    static string Quote(string s) { return "\"" + Regex.Replace(Regex.Replace(s, @"(\\*)\""", "$1$1\\\""), @"(\\+)$", "$1$1") + "\""; }
    static byte[] Decode(string path, string ffmpeg) {
        var info = new ProcessStartInfo(ffmpeg, "-nostdin -hide_banner -v error -i " + Quote(Path.GetFullPath(path)) + " -t 30.01 -map 0:a:0 -vn -f s16le -acodec pcm_s16le -ar 48000 -ac 1 -");
        info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using (var process = Process.Start(info)) {
            Task<string> errors = process.StandardError.ReadToEndAsync();
            Task<byte[]> decoded = Task.Run(() => {
                using (var output = new MemoryStream()) {
                    byte[] buffer = new byte[16384]; int n;
                    while ((n = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + n > MaximumBytes) throw new Exception("音频超过 30 秒，请先裁剪：" + Path.GetFileName(path));
                        output.Write(buffer, 0, n);
                    }
                    return output.ToArray();
                }
            });
            try {
                if (!decoded.Wait(60000)) throw new Exception("音频转换超时：" + Path.GetFileName(path));
                if (!process.WaitForExit(5000)) throw new Exception("音频转换未正常结束。");
                string stderr = errors.Result;
                if (process.ExitCode != 0) throw new Exception("无法读取音频 " + Path.GetFileName(path) + "：\n" + stderr.Substring(0, Math.Min(1000, stderr.Length)));
                byte[] pcm = decoded.Result;
                if (pcm.Length < 960) throw new Exception("音频为空或短于 0.01 秒：" + Path.GetFileName(path));
                bool sound = false;
                for (int i = 0; i + 1 < pcm.Length; i += 2) if (Math.Abs((int)BitConverter.ToInt16(pcm, i)) > 2) { sound = true; break; }
                if (!sound) throw new Exception("音频全是静音：" + Path.GetFileName(path));
                return pcm;
            } catch (Exception ex) {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                try { Task.WaitAll(new Task[] { decoded, errors }, 5000); } catch (AggregateException) { }
                if (ex is AggregateException) throw ((AggregateException)ex).Flatten().InnerExceptions[0];
                throw;
            }
        }
    }
    static byte[] Resource(string name) {
        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (input == null) {
                string template = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TechAnnouncer.zip");
                if (!File.Exists(template)) throw new Exception("请把原版 TechAnnouncer 1.0.1 的 TechAnnouncer.zip 放在生成器旁边，再重新生成。");
                string entry = name == "template.bank" ? "Audio/TechAnnouncer.bank" : name == "template.guids" ? "Audio/TechAnnouncer.guids.txt" : name == "template.detector" ? "bin/TechAnnouncer.dll" : null;
                if (entry == null) throw new Exception("未知的模板资源。");
                using (var archive = ZipFile.OpenRead(template)) {
                    var file = archive.GetEntry(entry);
                    if (file == null || file.Length > 8 * 1024 * 1024) throw new Exception("TechAnnouncer.zip 模板缺少文件或格式不正确：" + entry);
                    using (var stream = file.Open()) using (var output = new MemoryStream()) { stream.CopyTo(output); return output.ToArray(); }
                }
            }
            using (var ms = new MemoryStream()) { input.CopyTo(ms); return ms.ToArray(); }
        }
    }
    static string SHA(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static void Put(byte[] b, int pos, uint value) { Array.Copy(BitConverter.GetBytes(value), 0, b, pos, 4); }
    static byte[] Slice(byte[] b, int pos, int count) { var a = new byte[count]; Array.Copy(b, pos, a, 0, count); return a; }
    class Chunk { public string Tag; public int Pos, Size; public int Data { get { return Pos + 8; } } }
    static void Walk(byte[] bank, int start, int end, List<Chunk> chunks) {
        for (int pos = start; pos + 8 <= end; ) {
            string tag = Encoding.ASCII.GetString(bank, pos, 4); int size = checked((int)BitConverter.ToUInt32(bank, pos + 4));
            if (size < 0 || pos + 8L + size > end) throw new Exception("音频库模板结构损坏。");
            var chunk = new Chunk { Tag = tag, Pos = pos, Size = size }; chunks.Add(chunk);
            if (tag == "LIST") Walk(bank, pos + 12, pos + 8 + size, chunks);
            pos += 8 + size + size % 2;
        }
    }
    static Guid Unique(Guid ns, string label) {
        // RFC 4122 UUID v5: network byte order for hashing, .NET byte order for bank storage.
        byte[] prefix = ns.ToByteArray(); Swap(prefix); byte[] text = Encoding.UTF8.GetBytes(label); byte[] data = new byte[prefix.Length + text.Length];
        Array.Copy(prefix, data, prefix.Length); Array.Copy(text, 0, data, prefix.Length, text.Length);
        byte[] hash; using (var sha = SHA1.Create()) hash = sha.ComputeHash(data);
        hash = hash.Take(16).ToArray(); hash[6] = (byte)((hash[6] & 15) | 80); hash[8] = (byte)((hash[8] & 63) | 128); Swap(hash); return new Guid(hash);
    }
    static void Swap(byte[] b) { Array.Reverse(b, 0, 4); Array.Reverse(b, 4, 2); Array.Reverse(b, 6, 2); }
    static void ReplaceGuids(byte[] bank, int metadataEnd, Dictionary<Guid, Guid> replacements) {
        // One pass over the unmodified input avoids chained replacements.
        byte[] original = (byte[])bank.Clone();
        for (int pos = 0; pos + 16 <= metadataEnd; pos++) {
            Guid replacement;
            if (replacements.TryGetValue(new Guid(Slice(original, pos, 16)), out replacement)) {
                Array.Copy(replacement.ToByteArray(), 0, bank, pos, 16); pos += 15;
            }
        }
    }
    public static Dictionary<string, List<string>> ReadInputs(string manifest) {
        manifest = Path.GetFullPath(manifest);
        var raw = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
        if (raw == null) throw new Exception("音频映射应为 JSON 对象。");
        var files = new Dictionary<string, List<string>>();
        foreach (var pair in raw) {
            if (!Events.Contains(pair.Key)) throw new Exception("未知播报项目：" + pair.Key);
            var list = new List<string>();
            if (pair.Value is string) { if (!String.IsNullOrWhiteSpace((string)pair.Value)) list.Add((string)pair.Value); }
            else if (pair.Value != null) {
                var items = pair.Value as System.Collections.IEnumerable;
                if (items == null) throw new Exception(pair.Key + " 应为文件路径或路径数组。");
                foreach (object item in items) { if (!(item is string) || String.IsNullOrWhiteSpace((string)item)) throw new Exception(pair.Key + " 包含无效音频路径。"); list.Add((string)item); }
            }
            files[pair.Key] = list.Select(f => Path.GetFullPath(Path.IsPathRooted(f) ? f : Path.Combine(Path.GetDirectoryName(manifest), f))).ToList();
        }
        return files;
    }
    public static string Generate(string name, Dictionary<string, string> inputs, string outputPath, string ffmpeg, bool overwrite, Action<string> log) {
        return Generate(name, inputs.ToDictionary(p => p.Key, p => String.IsNullOrWhiteSpace(p.Value) ? new List<string>() : new List<string> { p.Value }), outputPath, ffmpeg, overwrite, log);
    }
    public static string Generate(string name, Dictionary<string, List<string>> inputs, string outputPath, string ffmpeg, bool overwrite, Action<string> log) {
        if (!Regex.IsMatch(name ?? "", @"^[A-Za-z][A-Za-z0-9_-]{2,63}$")) throw new Exception("语音包名字须为 3–64 位英文、数字、下划线或短横线，并以英文字母开头。");
        if (String.Equals(name, "TechAnnouncer", StringComparison.OrdinalIgnoreCase)) throw new Exception("请使用新的语音包名字，避免覆盖 TechAnnouncer 本体。");
        if (inputs.Keys.Any(e => !Events.Contains(e))) throw new Exception("音频映射包含未知播报项目。");
        var selected = Events.ToDictionary(e => e, e => inputs.ContainsKey(e) && inputs[e] != null ? new List<string>(inputs[e]) : new List<string>());
        foreach (var pair in selected) {
            if (pair.Value.Count > 5) throw new Exception(pair.Key + " 最多添加五条音频。");
            if (pair.Value.Any(f => String.IsNullOrWhiteSpace(f) || !File.Exists(f))) throw new Exception(pair.Key + " 的音频文件不存在。");
            if (pair.Value.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Count) throw new Exception(pair.Key + " 包含重复音频，请移除重复项。");
        }
        var allFiles = selected.Values.SelectMany(v => v).ToArray();
        if (allFiles.Length > 0 && !File.Exists(ffmpeg)) throw new Exception("未找到 FFmpeg，请选择 ffmpeg.exe，或把它放在生成器旁边。");
        outputPath = Path.GetFullPath(outputPath);
        if (!String.Equals(Path.GetExtension(outputPath), ".zip", StringComparison.OrdinalIgnoreCase)) throw new Exception("输出文件应以 .zip 结尾。");
        if (File.Exists(outputPath) && !overwrite) throw new Exception("输出 ZIP 已存在，请另选文件名，或确认覆盖后重新生成。");
        if (allFiles.Any(f => String.Equals(Path.GetFullPath(f), outputPath, StringComparison.OrdinalIgnoreCase))) throw new Exception("输出路径不能覆盖输入音频。");
        var banks = new List<GeneratedBank>(); var sourceInfo = new Dictionary<string, object>(); int done = 0;
        foreach (string e in Events) {
            var sources = new List<object>();
            for (int variant = 0; variant < selected[e].Count; variant++) {
                string file = selected[e][variant]; log("转换 " + (++done) + "/" + allFiles.Length + "：" + e + " #" + (variant + 1));
                byte[] audio = Decode(file, ffmpeg);
                sources.Add(new { file = Path.GetFileName(file), seconds = audio.Length / 96000.0, pcm_sha256 = SHA(audio) });
                banks.Add(BuildBank(name + "_" + e + "_" + (variant + 1), name.ToLowerInvariant(), e + "_v" + (variant + 1), audio));
            }
            if (sources.Count > 0) sourceInfo[e] = sources;
        }
        byte[] runtime;
        using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource("runtime.support")))) {
            assembly.Name.Name = name + "Runtime"; assembly.MainModule.Name = name + "Runtime.dll";
            using (var bytes = new MemoryStream()) { assembly.Write(bytes); runtime = bytes.ToArray(); }
        }
        byte[] detector = CreateDetector(name, selected.ToDictionary(p => p.Key, p => p.Value.Count), runtime);
        string yaml = "- Name: " + name + "\n  Version: " + Version + "\n  DLL: bin/" + name + ".dll\n  Dependencies:\n    - Name: Everest\n      Version: 1.2781.0\n";
        string readme = name + " " + Version + "\r\n\r\n将 ZIP 放进 Everest 版蔚蓝的 Mods 文件夹。开启本 Mod 的 Enabled，关闭其他播报 Mod。\r\n未添加音频的项目不播报；多条音频每次等概率随机选择。带金死亡和金草莓收集分别仅触发各自的播报。\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); string temp = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                Add(zip, "everest.yaml", Encoding.UTF8.GetBytes(yaml)); Add(zip, name + "Config.yaml", Encoding.UTF8.GetBytes("VoicePacks:\n  - " + name + "\n"));
                Add(zip, "bin/" + name + ".dll", detector); Add(zip, "bin/" + name + "Runtime.dll", runtime);
                foreach (var bank in banks) { Add(zip, "Audio/" + bank.Name + ".bank", bank.Data); Add(zip, "Audio/" + bank.Name + ".guids.txt", Encoding.UTF8.GetBytes(bank.Guids)); }
                Add(zip, "README.txt", Encoding.UTF8.GetBytes(readme));
                Add(zip, "build-info.json", Encoding.UTF8.GetBytes(Json.Serialize(new { name = name, version = Version, sample_rate = Rate, channels = 1, template_sha256 = TemplateHash, sources = sourceInfo, counts = selected.ToDictionary(p => p.Key, p => p.Value.Count) })));
            }
            if (File.Exists(outputPath)) { if (!overwrite) throw new Exception("输出文件在制作期间已被创建，请换一个输出路径。"); File.Replace(temp, outputPath, null); }
            else File.Move(temp, outputPath);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
        log("完成：" + outputPath); return outputPath;
    }
    class GeneratedBank { public string Name; public byte[] Data; public string Guids; }
    static GeneratedBank BuildBank(string name, string publicSlug, string eventKey, byte[] clip) {
        string slug = name.ToLowerInvariant();
        var pcm = BankEvents.ToDictionary(e => e, e => e == "cornerboost" ? clip : new byte[960]);
        pcm["farewell"] = new byte[960];
        byte[] bank = Resource("template.bank");
        if (SHA(bank) != TemplateHash) throw new Exception("模板校验失败，请使用原版 TechAnnouncer 1.0.1。");
        var chunks = new List<Chunk>(); Walk(bank, 12, bank.Length, chunks);
        var snd = chunks.Single(c => c.Tag == "SND "); int fsbPos = snd.Data + 20;
        if (Encoding.ASCII.GetString(bank, fsbPos, 4) != "FSB5") throw new Exception("模板音频格式不受支持。");
        int count = (int)BitConverter.ToUInt32(bank, fsbPos + 8), oldHeadersSize = (int)BitConverter.ToUInt32(bank, fsbPos + 12), namesSize = (int)BitConverter.ToUInt32(bank, fsbPos + 16);
        byte[] names = Slice(bank, fsbPos + 60 + oldHeadersSize, namesSize), fsbHeader = Slice(bank, fsbPos, 60);
        byte[] fsb;
        using (var sampleHeaders = new MemoryStream()) using (var sampleData = new MemoryStream()) {
            for (int i = 0; i < count; i++) {
                int offset = (int)BitConverter.ToUInt32(names, i * 4), end = offset; while (names[end] != 0) end++;
                string e = Encoding.UTF8.GetString(names, offset, end - offset); byte[] audio = pcm[e];
                while (sampleData.Length % 32 != 0) sampleData.WriteByte(0);
                ulong header = ((ulong)(audio.Length / 2) << 34) | ((ulong)(sampleData.Length / 32) << 7) | (9UL << 1);
                byte[] hb = BitConverter.GetBytes(header); sampleHeaders.Write(hb, 0, hb.Length); sampleData.Write(audio, 0, audio.Length);
            }
            while (sampleData.Length % 32 != 0) sampleData.WriteByte(0);
            Put(fsbHeader, 12, (uint)sampleHeaders.Length); Put(fsbHeader, 20, (uint)sampleData.Length); Put(fsbHeader, 24, 2);
            using (var md5 = MD5.Create()) Array.Copy(md5.ComputeHash(sampleData.ToArray()), 0, fsbHeader, 36, 16);
            using (var data = new MemoryStream()) { data.Write(fsbHeader, 0, fsbHeader.Length); sampleHeaders.WriteTo(data); data.Write(names, 0, names.Length); sampleData.WriteTo(data); fsb = data.ToArray(); }
        }
        var guidPaths = new Dictionary<Guid, string>();
        string guidText = Encoding.UTF8.GetString(Resource("template.guids"));
        foreach (string line in guidText.Split('\n')) {
            var m = Regex.Match(line.Trim(), @"^\{([\w-]+)\} (.+)$"); if (m.Success) guidPaths[new Guid(m.Groups[1].Value)] = m.Groups[2].Value;
        }
        // Flatten split phrases into one full clip; old authoring source offsets
        // would otherwise skip beginnings or overlap longer replacement audio.
        var secondaryWais = new HashSet<Guid>();
        var eventTimelines = new HashSet<Guid>();
        var silentWave = chunks.First(c => c.Tag == "WAV " && BitConverter.ToUInt32(bank, c.Data + 22) == 0);
        byte[] silenceId = Slice(bank, silentWave.Data, 16);
        foreach (var c in chunks.Where(c => c.Tag == "TLNB")) {
            string path; if (!guidPaths.TryGetValue(new Guid(Slice(bank, c.Data + 16, 16)), out path)) continue;
            string e = path.Substring(path.LastIndexOf('/') + 1);
            if (!pcm.ContainsKey(e)) continue;
            eventTimelines.Add(new Guid(Slice(bank, c.Data, 16)));
            var positions = new List<int>();
            for (int pos = c.Data + 54; pos + 14 <= c.Data + c.Size; pos += 24) positions.Add(pos);
            int primary = positions.OrderBy(p => BitConverter.ToUInt32(bank, p)).First();
            foreach (int pos in positions) {
                Put(bank, pos, 0); Put(bank, pos + 4, pos == primary ? (uint)(pcm[e].Length / 2 + 4800) : 0);
                if (pos != primary) secondaryWais.Add(new Guid(Slice(bank, pos - 16, 16)));
            }
        }
        foreach (var c in chunks.Where(c => c.Tag == "WAIB"))
            if (secondaryWais.Contains(new Guid(Slice(bank, c.Data, 16)))) Array.Copy(silenceId, 0, bank, c.Data + 16, 16);
        foreach (var c in chunks.Where(c => c.Tag == "INST"))
            if (c.Size == 123 && eventTimelines.Contains(new Guid(Slice(bank, c.Data, 16)))) Put(bank, c.Data + 111, 0);
        Guid ns = Unique(new Guid("9e70a2d8-94e2-4e94-8aa4-9f2db8d97805"), "pack:" + slug);
        var map = new Dictionary<Guid, Guid>(); var export = new List<string>();
        foreach (var item in guidPaths) {
            string path = item.Value, newPath;
            if (path.StartsWith("event:/brokemia/tech_announcer/")) {
                string[] parts = path.Split('/'); string originalPack = parts[parts.Length - 2], e = parts.Last();
                newPath = "event:/brokemia/tech_announcer/" + (originalPack == "jeffsteitzer" && e == "cornerboost" ? publicSlug + "/" + eventKey : slug + "_internal/" + originalPack + "/" + e);
                Guid id = Unique(ns, newPath); map[item.Key] = id; export.Add("{" + id + "} " + newPath);
            } else if (path == "bank:/TechAnnouncer") {
                newPath = "bank:/" + name; Guid id = Unique(ns, newPath); map[item.Key] = id; export.Add("{" + id + "} " + newPath);
            }
        }
        string[] owned = { "BNKI", "IBSB", "GBSB", "MBSB", "EVTB", "TLNB", "WAIB", "INST", "WAV " };
        foreach (var c in chunks.Where(c => owned.Contains(c.Tag))) {
            Guid old = new Guid(Slice(bank, c.Data, 16)); if (!map.ContainsKey(old)) map[old] = Unique(ns, "object:" + old);
        }
        ReplaceGuids(bank, snd.Pos, map);
        byte[] rebuilt = new byte[snd.Pos + 28 + fsb.Length]; Array.Copy(bank, rebuilt, snd.Pos); Encoding.ASCII.GetBytes("SND ").CopyTo(rebuilt, snd.Pos);
        Put(rebuilt, snd.Pos + 4, (uint)(20 + fsb.Length)); fsb.CopyTo(rebuilt, snd.Pos + 28); Put(rebuilt, 4, (uint)(rebuilt.Length - 8));
        var sndh = chunks.Single(c => c.Tag == "SNDH"); Put(rebuilt, sndh.Data + 8, (uint)fsb.Length);
        return new GeneratedBank { Name = name, Data = rebuilt, Guids = String.Join("\n", export) };
    }
    static void Add(ZipArchive zip, string name, byte[] bytes) { using (var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) s.Write(bytes, 0, bytes.Length); }
    static void LongBranches(MethodDefinition m) {
        foreach (var i in m.Body.Instructions) {
            if (i.OpCode == OpCodes.Br_S) i.OpCode = OpCodes.Br;
            else if (i.OpCode == OpCodes.Brtrue_S) i.OpCode = OpCodes.Brtrue;
            else if (i.OpCode == OpCodes.Brfalse_S) i.OpCode = OpCodes.Brfalse;
            else if (i.OpCode == OpCodes.Beq_S) i.OpCode = OpCodes.Beq;
            else if (i.OpCode == OpCodes.Bne_Un_S) i.OpCode = OpCodes.Bne_Un;
        }
    }
    static void ConfigureRuntime(ModuleDefinition module, TypeDefinition type, string name, Dictionary<string, int> counts, byte[] runtime) {
        using (var support = AssemblyDefinition.ReadAssembly(new MemoryStream(runtime))) {
            var helper = support.MainModule.GetType("AnnouncerRuntime.Support");
            Func<string, MethodReference> import = n => module.ImportReference(helper.Methods.Single(m => m.Name == n));
            var audioPath = type.Methods.Single(m => m.Name == "AudioPath");
            audioPath.Body = new Mono.Cecil.Cil.MethodBody(audioPath);
            var ip = audioPath.Body.GetILProcessor(); ip.Emit(OpCodes.Ldarg_0); ip.Emit(OpCodes.Call, import("AudioPath")); ip.Emit(OpCodes.Ret);
            var oldPlay = (MethodReference)type.Methods.Single(m => m.Name == "Player_WallJump").Body.Instructions.First(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == "Play").Operand;
            var safe = new MethodDefinition("PlayOptional", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, oldPlay.ReturnType);
            safe.Parameters.Add(new ParameterDefinition("path", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String)); type.Methods.Add(safe);
            var skip = Instruction.Create(OpCodes.Ldnull); var sp = safe.Body.GetILProcessor();
            sp.Emit(OpCodes.Call, type.Methods.Single(m => m.Name == "get_Settings"));
            sp.Emit(OpCodes.Callvirt, module.Types.Single(t => t.Name == "TechAnnouncerModuleSettings").Methods.Single(m => m.Name == "get_Enabled")); sp.Emit(OpCodes.Brfalse, skip);
            sp.Emit(OpCodes.Ldarg_0); sp.Emit(OpCodes.Brfalse, skip); sp.Emit(OpCodes.Ldarg_0); sp.Emit(OpCodes.Call, oldPlay); sp.Emit(OpCodes.Ret); sp.Append(skip); sp.Emit(OpCodes.Ret);
            foreach (var t in module.GetTypes()) foreach (var method in t.Methods.Where(m => m.HasBody && m != safe))
                foreach (var instruction in method.Body.Instructions)
                    if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference && ((MethodReference)instruction.Operand).FullName == oldPlay.FullName) instruction.Operand = safe;
            var callback = new MethodDefinition("AnnounceEvent", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            callback.Parameters.Add(new ParameterDefinition("key", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String)); type.Methods.Add(callback);
            var cp = callback.Body.GetILProcessor(); cp.Emit(OpCodes.Ldarg_0); cp.Emit(OpCodes.Call, audioPath); cp.Emit(OpCodes.Call, safe); cp.Emit(OpCodes.Pop); cp.Emit(OpCodes.Ret);
            var load = type.Methods.Single(m => m.Name == "Load"); var lp = load.Body.GetILProcessor(); var anchor = load.Body.Instructions[0];
            var action = module.ImportReference(typeof(Action<string>).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
            var setup = new[] { Instruction.Create(OpCodes.Ldstr, name.ToLowerInvariant()), Instruction.Create(OpCodes.Ldstr, String.Join(";", counts.Select(p => p.Key + "=" + p.Value))), Instruction.Create(OpCodes.Ldnull), Instruction.Create(OpCodes.Ldftn, callback), Instruction.Create(OpCodes.Newobj, action), Instruction.Create(OpCodes.Call, import("Configure")), Instruction.Create(OpCodes.Call, import("Load")) };
            foreach (var instruction in setup) lp.InsertBefore(anchor, instruction);
            var unload = type.Methods.Single(m => m.Name == "Unload"); unload.Body.GetILProcessor().InsertBefore(unload.Body.Instructions[0], Instruction.Create(OpCodes.Call, import("Unload")));
        }
    }
    public static byte[] CreateDetector(string name, Dictionary<string, int> counts, byte[] runtime) {
        using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource("template.detector")))) {
            var module = assembly.MainModule; var type = module.Types.Single(t => t.Name == "TechAnnouncerModule");
            Func<string, MethodDefinition> method = n => type.Methods.Single(m => m.Name == n);
            var wall = method("Player_WallJump"); var precision = method("Player_CorrectDashPrecision"); var dash = method("Player_CallDashEvents");
            var player = (TypeReference)wall.Parameters[1].ParameterType;
            var originalInvoke = (MethodReference)precision.Body.Instructions.First(i => i.OpCode == OpCodes.Callvirt && ((MethodReference)i.Operand).Name == "Invoke").Operand;
            precision.Body = new Mono.Cecil.Cil.MethodBody(precision);
            var pp = precision.Body.GetILProcessor(); pp.Append(Instruction.Create(OpCodes.Ldarg_0)); pp.Append(Instruction.Create(OpCodes.Ldarg_1)); pp.Append(Instruction.Create(OpCodes.Ldarg_2)); pp.Append(Instruction.Create(OpCodes.Callvirt, originalInvoke)); pp.Append(Instruction.Create(OpCodes.Ret));
            var settingsType = module.Types.Single(t => t.Name == "TechAnnouncerModuleSettings");
            var settings = method("get_Settings"); var enabled = settingsType.Methods.Single(m => m.Name == "get_Enabled"); var announceDemo = settingsType.Methods.Single(m => m.Name == "get_AnnounceDemodash");
            var duck = new MethodReference("get_Ducking", module.TypeSystem.Boolean, player) { HasThis = true };
            var demoFlag = new FieldReference("demoDashed", module.TypeSystem.Boolean, player);
            var vector = module.GetTypeReferences().First(t => t.FullName == "Microsoft.Xna.Framework.Vector2");
            var dashDir = new FieldReference("DashDir", vector, player); var x = new FieldReference("X", module.TypeSystem.Single, vector); var y = new FieldReference("Y", module.TypeSystem.Single, vector);
            var play = (MethodReference)wall.Body.Instructions.First(i => i.OpCode == OpCodes.Call && ((MethodReference)i.Operand).Name == "Play").Operand;
            // Existing calledDashEvents guard ensures one announcement for each dash.
            var anchor = dash.Body.Instructions[3]; var ducked = Instruction.Create(OpCodes.Ldarg_1);
            var added = new[] {
                Instruction.Create(OpCodes.Call, settings), Instruction.Create(OpCodes.Callvirt, enabled), Instruction.Create(OpCodes.Brfalse, anchor),
                Instruction.Create(OpCodes.Call, settings), Instruction.Create(OpCodes.Callvirt, announceDemo), Instruction.Create(OpCodes.Brfalse, anchor),
                Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldfld, demoFlag), Instruction.Create(OpCodes.Brtrue, ducked),
                Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Callvirt, duck), Instruction.Create(OpCodes.Brfalse, anchor),
                ducked, Instruction.Create(OpCodes.Ldflda, dashDir), Instruction.Create(OpCodes.Ldfld, x), Instruction.Create(OpCodes.Ldc_R4, 0F), Instruction.Create(OpCodes.Beq, anchor),
                Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldflda, dashDir), Instruction.Create(OpCodes.Ldfld, y), Instruction.Create(OpCodes.Ldc_R4, 0F), Instruction.Create(OpCodes.Bne_Un, anchor),
                Instruction.Create(OpCodes.Ldstr, "demodash"), Instruction.Create(OpCodes.Call, method("AudioPath")), Instruction.Create(OpCodes.Call, play), Instruction.Create(OpCodes.Pop)
            };
            foreach (var i in added) dash.Body.GetILProcessor().InsertBefore(anchor, i);
            LongBranches(dash);
            foreach (var t in module.GetTypes()) {
                foreach (var m in t.Methods.Where(m => m.HasBody)) foreach (var i in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)) {
                    string s = (string)i.Operand; if (s == "JeffSteitzer" || s == "TechAnnouncer") i.Operand = name; else if (s == "TechAnnouncerConfig") i.Operand = name + "Config";
                }
            }
            ConfigureRuntime(module, type, name, counts, runtime);
            string ns = "Celeste.Mod.GeneratedAnnouncer." + name.Replace('-', '_');
            foreach (var t in module.GetTypeReferences()) if (t.Namespace == "Celeste.Mod.TechAnnouncer") t.Namespace = ns;
            foreach (var t in module.GetTypes()) if (t.Namespace == "Celeste.Mod.TechAnnouncer") t.Namespace = ns;
            assembly.Name.Name = name; assembly.Name.Version = new Version(1, 2, 0, 0); module.Name = name + ".dll"; module.Mvid = Unique(new Guid("6ecb33b7-2c51-466a-bf8e-42d0938f2347"), name.ToLowerInvariant());
            using (var output = new MemoryStream()) { assembly.Write(output); return output.ToArray(); }
        }
    }
}

public class MainWindow : Form {
    TextBox packName = new TextBox(), ffmpeg = new TextBox();
    TextBox[] inputs = new TextBox[Builder.Events.Length];
    List<string>[] clips = Builder.Events.Select(e => new List<string>()).ToArray(); Button build = new Button(), folder = new Button(), ffmpegBrowse = new Button(), openOutput = new Button();
    List<Button> inputButtons = new List<Button>(); RichTextBox log = new RichTextBox(); string result;
    public MainWindow() {
        Text = "Announcer Mod 生成器"; Font = new Font("Microsoft YaHei UI", 10F); BackColor = Color.FromArgb(247, 249, 252);
        ClientSize = new Size(900, 770); MinimumSize = new Size(830, 740); StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 18), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 342)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(layout);
        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "自选语音，生成你的播报 Mod", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = Color.FromArgb(25, 42, 72), AutoSize = true, Location = new Point(0, 0) });
        heading.Controls.Add(new Label { Text = "所有项目可留空；每项最多 5 条，触发时随机播放。支持 MP3、WAV 等格式。", AutoSize = true, ForeColor = Color.FromArgb(86, 102, 125), Location = new Point(0, 43) }); layout.Controls.Add(heading, 0, 0);
        var naming = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 }; naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); naming.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
        naming.Controls.Add(new Label { Text = "语音包名字", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        packName.Text = "RosmontisAnnouncer"; packName.Dock = DockStyle.Fill; packName.Margin = new Padding(0, 9, 15, 8); naming.Controls.Add(packName, 1, 0);
        naming.Controls.Add(new Label { Text = "3–64 位英文 / 数字 / _ / -", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(86, 102, 125) }, 2, 0); layout.Controls.Add(naming, 0, 1);
        var imports = new FlowLayoutPanel { Dock = DockStyle.Fill };
        folder.Text = "选择音频文件夹"; folder.Size = new Size(160, 34); folder.Click += (s, e) => ImportFolder(); imports.Controls.Add(folder);
        imports.Controls.Add(new Label { Text = "共 13 项，可向下滚动；支持 death_1 等编号文件名。", AutoSize = true, Padding = new Padding(10, 8, 0, 0), ForeColor = Color.FromArgb(86, 102, 125) }); layout.Controls.Add(imports, 0, 2);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = Builder.Events.Length, BackColor = Color.White, Padding = new Padding(8, 2, 8, 2) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        for (int i = 0; i < Builder.Events.Length; i++) {
            int index = i; grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.Controls.Add(new Label { Text = Builder.Labels[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9F) }, 0, i);
            inputs[i] = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 3), AllowDrop = true, ReadOnly = true };
            inputs[i].DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            inputs[i].DragDrop += (s, e) => { string[] p = (string[])e.Data.GetData(DataFormats.FileDrop); SetClips(index, p); };
            grid.Controls.Add(inputs[i], 1, i); var button = new Button { Text = "管理…", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) }; button.Click += (s, e) => SelectAudio(index); inputButtons.Add(button); grid.Controls.Add(button, 2, i);
        }
        grid.Dock = DockStyle.Top; grid.Height = Builder.Events.Length * 42 + 8;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(grid); layout.Controls.Add(scroll, 0, 3);
        var conversion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 }; conversion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); conversion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); conversion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        conversion.Controls.Add(new Label { Text = "音频转换工具（自动查找）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9F) }, 0, 0);
        ffmpeg.Dock = DockStyle.Fill; ffmpeg.Text = Builder.FindFFmpeg(); ffmpeg.Margin = new Padding(0, 6, 10, 3); conversion.Controls.Add(ffmpeg, 1, 0);
        ffmpegBrowse.Text = "选择…"; ffmpegBrowse.Dock = DockStyle.Fill; ffmpegBrowse.Margin = new Padding(0, 3, 0, 3); ffmpegBrowse.Click += (s, e) => { using (var d = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe", Title = "选择 ffmpeg.exe" }) if (d.ShowDialog() == DialogResult.OK) ffmpeg.Text = d.FileName; }; conversion.Controls.Add(ffmpegBrowse, 2, 0); layout.Controls.Add(conversion, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0) };
        build.Text = "生成 Mod ZIP"; build.Size = new Size(180, 40); build.BackColor = Color.FromArgb(38, 97, 196); build.ForeColor = Color.White; build.FlatStyle = FlatStyle.Flat; build.FlatAppearance.BorderSize = 0; build.Click += async (s, e) => await Generate(); actions.Controls.Add(build);
        openOutput.Text = "打开输出文件夹"; openOutput.Size = new Size(150, 40); openOutput.Enabled = false; openOutput.Click += (s, e) => { if (File.Exists(result)) Process.Start("explorer.exe", "/select,\"" + result + "\""); }; actions.Controls.Add(openOutput);
        actions.Controls.Add(new Label { Text = "成品直接放进 Mods，关闭其他技巧播报。", AutoSize = true, Padding = new Padding(12, 10, 0, 0), ForeColor = Color.FromArgb(86, 102, 125) }); layout.Controls.Add(actions, 0, 5);
        log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BorderStyle = BorderStyle.FixedSingle; log.BackColor = Color.White; log.Font = new Font(Font.FontFamily, 9F); log.Text = "准备就绪。未添加的项目不播报；每项最多五条，每条支持 0.01–30 秒。"; layout.Controls.Add(log, 0, 6);
        try { string candidate = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..")); Fill(Builder.ReadFolder(candidate)); } catch (Exception) { }
        FormClosing += (s, e) => { if (!build.Enabled) { e.Cancel = true; MessageBox.Show("正在生成，请等待完成后关闭。", Text); } };
    }
    void SetClips(int i, IEnumerable<string> files) {
        var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count > 5) { MessageBox.Show("每项最多五条音频。", Text); return; }
        clips[i] = list; inputs[i].Text = list.Count == 0 ? "未添加 · 不播报" : list.Count + " 条 · " + String.Join("；", list.Select(Path.GetFileName));
    }
    void Fill(Dictionary<string, List<string>> files) { for (int i = 0; i < Builder.Events.Length; i++) SetClips(i, files.ContainsKey(Builder.Events[i]) ? files[Builder.Events[i]] : new List<string>()); }
    void ImportFolder() {
        using (var d = new FolderBrowserDialog { Description = "选择音频文件夹；未提供的项目保持静音" }) if (d.ShowDialog() == DialogResult.OK) {
            try { var files = Builder.ReadFolder(d.SelectedPath); Fill(files); MessageBox.Show("已匹配 " + files.Count + "/" + Builder.Events.Length + " 项，共 " + files.Values.Sum(v => v.Count) + " 条音频。", Text); } catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    void SelectAudio(int i) {
        using (var dialog = new Form { Text = Builder.Labels[i] + " · 最多五条音频", ClientSize = new Size(640, 280), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog }) {
            var list = new ListBox { Location = new Point(12, 12), Size = new Size(616, 210), HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended }; list.Items.AddRange(clips[i].ToArray()); dialog.Controls.Add(list);
            var add = new Button { Text = "添加音频…", Location = new Point(12, 235), Size = new Size(120, 32) };
            add.Click += (sender, e) => { using (var file = new OpenFileDialog { Multiselect = true, Filter = "音频文件|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.aac;*.wma;*.opus;*.aiff;*.aif", Title = "一次可选择多条音频" }) if (file.ShowDialog(dialog) == DialogResult.OK) { var items = list.Items.Cast<string>().Concat(file.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); if (items.Length > 5) MessageBox.Show("每项最多五条，请先移除不需要的音频。", dialog.Text); else { list.Items.Clear(); list.Items.AddRange(items); } } }; dialog.Controls.Add(add);
            var remove = new Button { Text = "移除选中", Location = new Point(140, 235), Size = new Size(100, 32) }; remove.Click += (sender,e) => { foreach (var item in list.SelectedItems.Cast<object>().ToArray()) list.Items.Remove(item); }; dialog.Controls.Add(remove);
            var clear = new Button { Text = "清空", Location = new Point(248, 235), Size = new Size(80, 32) }; clear.Click += (sender,e) => list.Items.Clear(); dialog.Controls.Add(clear);
            var ok = new Button { Text = "确定", Location = new Point(528, 235), Size = new Size(100, 32), DialogResult = DialogResult.OK }; dialog.Controls.Add(ok); dialog.AcceptButton = ok;
            if (dialog.ShowDialog(this) == DialogResult.OK) SetClips(i, list.Items.Cast<string>());
        }
    }
    void Busy(bool busy) { build.Enabled = !busy; folder.Enabled = !busy; packName.Enabled = !busy; ffmpeg.Enabled = !busy; ffmpegBrowse.Enabled = !busy; foreach (var t in inputs) t.Enabled = !busy; foreach (var b in inputButtons) b.Enabled = !busy; openOutput.Enabled = !busy && File.Exists(result); }
    async Task Generate() {
        var files = new Dictionary<string, List<string>>(); for (int i = 0; i < Builder.Events.Length; i++) files[Builder.Events[i]] = new List<string>(clips[i]);
        string name = packName.Text.Trim(), converter = ffmpeg.Text.Trim().Trim('"');
        using (var d = new SaveFileDialog { Filter = "Mod ZIP|*.zip", FileName = name + ".zip", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory, OverwritePrompt = true, AddExtension = true }) {
            if (d.ShowDialog() != DialogResult.OK) return;
            bool overwrite = File.Exists(d.FileName); Busy(true); log.Clear();
            try {
                string target = d.FileName;
                result = await Task.Run(() => Builder.Generate(name, files, target, converter, overwrite, line => BeginInvoke(new Action(() => { log.AppendText(line + Environment.NewLine); log.ScrollToCaret(); }))));
                MessageBox.Show("已生成：\n" + result + "\n\n把 ZIP 放进 Mods，在 " + name + " 的选项中开启 Enabled，并关闭其他技巧播报 Mod 的 Enabled。", "制作完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) { log.AppendText("失败：" + ex.Message); MessageBox.Show(ex.Message, "未能生成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { Busy(false); }
        }
    }
}

static class Program {
    [STAThread] static int Main(string[] args) {
        if (args.Length > 0 && args[0] != "--screenshot") {
            try {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                var a = new Dictionary<string, string>(); bool overwrite = false;
                for (int i = 0; i < args.Length; i++) { if (args[i] == "--overwrite") overwrite = true; else { if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new Exception("参数格式错误。"); a[args[i]] = args[++i]; } }
                if (!a.ContainsKey("--name") || !a.ContainsKey("--output") || (!a.ContainsKey("--input-dir") && !a.ContainsKey("--inputs"))) throw new Exception("用法：AnnouncerMod生成器.exe --name MyAnnouncer --input-dir 音频文件夹 --output MyAnnouncer.zip [--ffmpeg 路径] [--overwrite]\n也可用 --inputs 映射.json 指定可选文件或每项最多五条的路径数组。");
                Dictionary<string, List<string>> files;
                if (a.ContainsKey("--inputs")) files = Builder.ReadInputs(a["--inputs"]);
                else files = Builder.ReadFolder(a["--input-dir"]);
                Builder.Generate(a["--name"], files, a["--output"], a.ContainsKey("--ffmpeg") ? a["--ffmpeg"] : Builder.FindFFmpeg(), overwrite, Console.WriteLine); return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new MainWindow()) {
            if (args.Length == 2 && args[0] == "--screenshot") { window.Show(); Application.DoEvents(); window.PerformLayout(); using (var b = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height)); b.Save(args[1]); } return 0; }
            Application.Run(window); return 0;
        }
    }
}
}
