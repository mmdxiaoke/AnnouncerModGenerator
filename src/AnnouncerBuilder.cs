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

namespace AnnouncerBuilder {
public static class Builder {
    public static readonly string[] Events = { "cornerboost", "demodash", "fastbubble", "hyperdash", "neutral", "superdash", "ultradash", "wallbounce", "wavedash", "death", "goldendeath", "strawberry", "goldenstrawberry" };
    public static readonly string[] Labels = { "抓角加速", "下蹲冲刺", "泡泡快启", "Hyper", "中性跳", "Super", "Ultra", "蹭墙跳", "凌波微步", "普通死亡", "带金草莓死亡", "吃掉草莓", "吃掉金草莓" };
    public static readonly string[] Extensions = { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".aiff", ".aif" };
    const int Rate = 48000, MaximumBytes = Rate * 2 * 30;
    public const string Version = "1.4.0";
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
            if (input == null) throw new Exception("生成器内置运行程序缺失，请重新下载。");
            using (var ms = new MemoryStream()) { input.CopyTo(ms); return ms.ToArray(); }
        }
    }
    static string SHA(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static byte[] Wave(byte[] pcm) {
        using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms)) {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(pcm.Length + 36); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm); return ms.ToArray();
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
    public static Dictionary<string, int> ReadVolumes(string manifest) {
        var raw = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest, Encoding.UTF8));
        if (raw == null) throw new Exception("音量映射应为 JSON 对象。");
        var values = new Dictionary<string, int>();
        foreach (var pair in raw) {
            if (!Events.Contains(pair.Key)) throw new Exception("未知播报项目：" + pair.Key);
            if (!(pair.Value is int) || (int)pair.Value < 0 || (int)pair.Value > 100) throw new Exception(pair.Key + " 的音量须为 0–100 的整数。");
            values.Add(pair.Key, (int)pair.Value);
        }
        return values;
    }
    public static string Generate(string name, Dictionary<string, string> inputs, string outputPath, string ffmpeg, bool overwrite, Action<string> log) {
        return Generate(name, inputs.ToDictionary(p => p.Key, p => String.IsNullOrWhiteSpace(p.Value) ? new List<string>() : new List<string> { p.Value }), outputPath, ffmpeg, overwrite, log);
    }
    public static string Generate(string name, Dictionary<string, List<string>> inputs, string outputPath, string ffmpeg, bool overwrite, Action<string> log) {
        return Generate(name, inputs, outputPath, ffmpeg, overwrite, log, new Dictionary<string, int>());
    }
    public static string Generate(string name, Dictionary<string, List<string>> inputs, string outputPath, string ffmpeg, bool overwrite, Action<string> log, Dictionary<string, int> volumes) {
        if (volumes == null || volumes.Any(p => !Events.Contains(p.Key) || p.Value < 0 || p.Value > 100)) throw new Exception("每项播报音量须为 0–100 的整数。");
        var eventVolumes = Events.ToDictionary(e => e, e => volumes.ContainsKey(e) ? volumes[e] : 100);
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
        var clips = new Dictionary<string, byte[]>(); var sourceInfo = new Dictionary<string, object>(); int done = 0;
        foreach (string e in Events) {
            var sources = new List<object>();
            for (int variant = 0; variant < selected[e].Count; variant++) {
                string file = selected[e][variant]; log("转换 " + (++done) + "/" + allFiles.Length + "：" + e + " #" + (variant + 1));
                byte[] audio = Decode(file, ffmpeg);
                sources.Add(new { file = Path.GetFileName(file), seconds = audio.Length / 96000.0, pcm_sha256 = SHA(audio) });
                clips.Add("announcer." + e + ".v" + (variant + 1) + ".wav", Wave(audio));
            }
            if (sources.Count > 0) sourceInfo[e] = sources;
        }
        byte[] runtime;
        using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource("runtime.support")))) {
            assembly.Name.Name = name; assembly.Name.Version = new Version(1, 4, 0, 0);
            assembly.MainModule.Name = name + ".dll"; assembly.MainModule.Mvid = Guid.NewGuid();
            foreach (var clip in clips) assembly.MainModule.Resources.Add(new EmbeddedResource(clip.Key, Mono.Cecil.ManifestResourceAttributes.Private, clip.Value));
            string config = String.Join(";", selected.Select(p => p.Key + "=" + p.Value.Count));
            assembly.MainModule.Resources.Add(new EmbeddedResource("announcer.config", Mono.Cecil.ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes(config)));
            assembly.MainModule.Resources.Add(new EmbeddedResource("announcer.volumes", Mono.Cecil.ManifestResourceAttributes.Private, Encoding.UTF8.GetBytes(String.Join(";", eventVolumes.Select(p => p.Key + "=" + p.Value)))));
            using (var bytes = new MemoryStream()) { assembly.Write(bytes); runtime = bytes.ToArray(); }
        }
        string yaml = "- Name: " + name + "\n  Version: " + Version + "\n  DLL: bin/" + name + ".dll\n  Dependencies:\n    - Name: Everest\n      Version: 1.2781.0\n";
        string readme = name + " " + Version + "\r\n\r\n将 ZIP 放进 Everest 版蔚蓝的 Mods 文件夹。开启本 Mod 的 Enabled，关闭其他播报 Mod。\r\n未添加音频的项目不播报；多条音频每次等概率随机选择。带金死亡和金草莓收集分别仅触发各自的播报。\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); string temp = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                Add(zip, "everest.yaml", Encoding.UTF8.GetBytes(yaml));
                Add(zip, "bin/" + name + ".dll", runtime);
                Add(zip, "README.txt", Encoding.UTF8.GetBytes(readme));
                Add(zip, "build-info.json", Encoding.UTF8.GetBytes(Json.Serialize(new { name = name, version = Version, sample_rate = Rate, channels = 1, runtime = "IndependentAnnouncer", audio_format = "embedded_pcm_wav", sources = sourceInfo, volumes = eventVolumes, counts = selected.ToDictionary(p => p.Key, p => p.Value.Count) })));
            }
            if (File.Exists(outputPath)) { if (!overwrite) throw new Exception("输出文件在制作期间已被创建，请换一个输出路径。"); File.Replace(temp, outputPath, null); }
            else File.Move(temp, outputPath);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
        log("完成：" + outputPath); return outputPath;
    }
    static void Add(ZipArchive zip, string name, byte[] bytes) { using (var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) s.Write(bytes, 0, bytes.Length); }

}

public class MainWindow : Form {
    TextBox packName = new TextBox(), ffmpeg = new TextBox();
    TextBox[] inputs = new TextBox[Builder.Events.Length];
    NumericUpDown[] volumes = new NumericUpDown[Builder.Events.Length];
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
        imports.Controls.Add(new Label { Text = "共 13 项，可向下滚动；每项音量 0–100%，游戏中也可调整。", AutoSize = true, Padding = new Padding(10, 8, 0, 0), ForeColor = Color.FromArgb(86, 102, 125) }); layout.Controls.Add(imports, 0, 2);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = Builder.Events.Length, BackColor = Color.White, Padding = new Padding(8, 2, 8, 2) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        for (int i = 0; i < Builder.Events.Length; i++) {
            int index = i; grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.Controls.Add(new Label { Text = Builder.Labels[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9F) }, 0, i);
            inputs[i] = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 3), AllowDrop = true, ReadOnly = true };
            inputs[i].DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            inputs[i].DragDrop += (s, e) => { string[] p = (string[])e.Data.GetData(DataFormats.FileDrop); SetClips(index, p); };
            volumes[i] = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100, Increment = 5, Width = 65, Margin = new Padding(0, 6, 0, 0) };
            var volumePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) }; volumePanel.Controls.Add(volumes[i]);
            volumePanel.Controls.Add(new Label { Text = "%", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }); grid.Controls.Add(volumePanel, 2, i);
            grid.Controls.Add(inputs[i], 1, i); var button = new Button { Text = "管理…", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) }; button.Click += (s, e) => SelectAudio(index); inputButtons.Add(button); grid.Controls.Add(button, 3, i);
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
    void Busy(bool busy) { build.Enabled = !busy; folder.Enabled = !busy; packName.Enabled = !busy; ffmpeg.Enabled = !busy; ffmpegBrowse.Enabled = !busy; foreach (var t in inputs) t.Enabled = !busy; foreach (var volume in volumes) volume.Enabled = !busy; foreach (var b in inputButtons) b.Enabled = !busy; openOutput.Enabled = !busy && File.Exists(result); }
    async Task Generate() {
        var files = new Dictionary<string, List<string>>(); for (int i = 0; i < Builder.Events.Length; i++) files[Builder.Events[i]] = new List<string>(clips[i]);
        var gains = Builder.Events.Select((e, i) => new { Key = e, Value = (int)volumes[i].Value }).ToDictionary(p => p.Key, p => p.Value);
        string name = packName.Text.Trim(), converter = ffmpeg.Text.Trim().Trim('"');
        using (var d = new SaveFileDialog { Filter = "Mod ZIP|*.zip", FileName = name + ".zip", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory, OverwritePrompt = true, AddExtension = true }) {
            if (d.ShowDialog() != DialogResult.OK) return;
            bool overwrite = File.Exists(d.FileName); Busy(true); log.Clear();
            try {
                string target = d.FileName;
                result = await Task.Run(() => Builder.Generate(name, files, target, converter, overwrite, line => BeginInvoke(new Action(() => { log.AppendText(line + Environment.NewLine); log.ScrollToCaret(); })), gains));
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
                if (!a.ContainsKey("--name") || !a.ContainsKey("--output") || (!a.ContainsKey("--input-dir") && !a.ContainsKey("--inputs"))) throw new Exception("用法：AnnouncerMod生成器.exe --name MyAnnouncer --input-dir 音频文件夹 --output MyAnnouncer.zip [--ffmpeg 路径] [--volumes 音量.json] [--overwrite]\n也可用 --inputs 映射.json 指定可选文件或每项最多五条的路径数组。");
                Dictionary<string, List<string>> files;
                if (a.ContainsKey("--inputs")) files = Builder.ReadInputs(a["--inputs"]);
                else files = Builder.ReadFolder(a["--input-dir"]);
                Builder.Generate(a["--name"], files, a["--output"], a.ContainsKey("--ffmpeg") ? a["--ffmpeg"] : Builder.FindFFmpeg(), overwrite, Console.WriteLine, a.ContainsKey("--volumes") ? Builder.ReadVolumes(a["--volumes"]) : new Dictionary<string, int>()); return 0;
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
