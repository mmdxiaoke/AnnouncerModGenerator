# Announcer Mod Generator

Windows 图形工具：自选音频，自动生成 Everest 版《蔚蓝》的独立播报 Mod。13 个项目全部可选，每项支持 0–5 条音频。

支持九种技巧，以及普通死亡、带金草莓死亡、吃掉草莓和吃掉金草莓。未添加音频的项目不播报；添加多条时，每次触发等概率随机播放其中一条，允许连续抽到同一条。

![生成器窗口](docs/screenshot.png)

## 使用

1. 从本仓库的 Releases 下载 **v1.3.0 或更新版本**的工具 ZIP 并解压。
2. 从 [FFmpeg 官方下载页](https://ffmpeg.org/download.html) 选择 Windows 构建，将解压后的 `ffmpeg.exe` 放在程序旁边，也可以在界面中指定路径。
3. 双击 `AnnouncerMod生成器.exe`，点击所需项目旁的“管理…”，添加、移除或清空音频。列表可向下滚动查看死亡和草莓项目。填写语音包名字，点击“生成 Mod ZIP”。
4. 将生成的 ZIP 放入蔚蓝 `Mods` 文件夹。在 Mod 选项中开启你填写的名字对应的 **Enabled**，可用 **Volume（0–10）** 调节播报音量；关闭 TechAnnouncer、NeuroAnnouncer 等其他技巧播报的 Enabled，避免重复播报。

**v1.3.0 起完全不需要模板。** 保留程序旁边的 `Mono.Cecil.dll`。FFmpeg 只用于生成时转换音频，由用户自行安装，下载包不包含其二进制。

名字以英文字母开头，长度 3–64 位，可使用英文、数字、下划线和短横线。同一项目内不能重复添加同一文件，不同项目可以共用音频。每条音频长 0.01–30 秒，不能是全静音。支持 MP3、WAV、OGG、FLAC、M4A、AAC、WMA、OPUS、AIFF。生成的 Mod 不需要 FFmpeg，也不依赖另装 TechAnnouncer。全部留空也可以生成静音模组，此时不需要 FFmpeg。

同名文件夹中可自动识别以下文件名，大小写不敏感，扩展名可以更换：

| 技巧事件 | 示例文件名 |
| --- | --- |
| cornerboost | CornerBoost.mp3 |
| demodash | Demodash.mp3 |
| fastbubble | fastbubble.mp3 |
| hyperdash | Hyperdash.mp3 |
| neutral | NeutralJump.mp3 |
| superdash | superdash.mp3 |
| ultradash | UltraDash.mp3 |
| wallbounce | WallBounce.mp3 |
| wavedash | wavedash.mp3 |
| death（普通死亡） | death_1.mp3 |
| goldendeath（带金草莓死亡） | goldendeath_1.mp3 |
| strawberry（吃掉草莓） | strawberry_1.mp3 |
| goldenstrawberry（吃掉金草莓） | goldenstrawberry_1.mp3 |

每项多条音频可命名为 `death_1.mp3` 至 `death_5.mp3`；也支持 `death1.mp3`、`death-1.mp3`、中文事件名称。文件夹匹配超过五条时会提示处理，不会静默丢弃。

带金草莓死亡仅触发 `goldendeath`，不会额外触发普通死亡；金草莓收集仅触发 `goldenstrawberry`。对应项目留空时保持静音，不回退到普通事件。收集指草莓被正式吃掉，不是刚碰到并开始跟随；同一草莓不会重复播报。无敌状态下未实际死亡也不会播报。支持原版 `Strawberry` 及其子类，另有自定义收集机制的模组草莓不保证适用。

## v1.3.0：无需模板

生成器内置本项目自行编写的播报运行程序，将音频转换为 PCM WAV，嵌入一个模块 DLL，和 Everest 清单一起打包。运行时监听冲刺、跳跃、碰撞、死亡和草莓收集，使用游戏已有的 FMOD Core 直接播放 WAV。生成过程不读取 TechAnnouncer，不修改第三方 DLL，不制作 `.bank` 或 `.guids.txt`。

各语音包使用独立的程序集身份、设置和音频缓存。播放沿用游戏音效总线，最多同时播放八条；禁用和卸载会停止并释放音频。检测规则与旧模板不保证完全一致，具体依据、特殊模组兼容范围见 [独立运行原理](docs/independent-runtime.md)。

**旧版已生成的 Mod 不会自动升级。** 请重新生成并替换同名 ZIP，然后重启游戏。

## 从源码编译

Windows 自带的 .NET Framework 编译器即可编译，不需要安装 Python、FMOD Studio 或 Visual Studio：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

脚本从 NuGet 官方源下载固定版本的 Mono.Cecil，并检查包的 SHA-256，输出到 `dist`。离线编译可通过 `-CecilPath` 指定已有的 Mono.Cecil 0.10.4 DLL。独立运行程序使用本项目编写的最小 API 声明编译，无需游戏文件；这些声明只是构建/测试辅助，不会放入下载包或生成的 Mod。

`scripts/package.ps1` 将程序、Mono.Cecil、使用说明及许可打包为 v1.3.0 下载包。脚本按文件清单打包，即使本地 `dist` 存在模板、FFmpeg、游戏 DLL 或用户音频，也不会包含它们。

## 命令行

```powershell
& '.\dist\AnnouncerMod生成器.exe' --name MyAnnouncer --input-dir 'D:\音频' --output 'D:\输出\MyAnnouncer.zip'
```

默认拒绝覆盖已有输出；主动加 `--overwrite` 才会覆盖。可用 `--ffmpeg 路径` 指定 FFmpeg。任意文件名可以通过 `--inputs 映射.json` 指定，相对路径以 JSON 所在目录为基准，示例见 [examples/inputs.json](examples/inputs.json)。

映射支持单个路径或最多五条的数组；省略、`null`、`[]` 和空字符串均表示不播报。旧版单路径映射仍可使用。例如：

```json
{
  "demodash": "audio/demo.mp3",
  "death": ["audio/death_a.mp3", "audio/death_b.mp3"],
  "goldendeath": [],
  "strawberry": "audio/berry.mp3"
}
```

## 验证

v1.3.0 已检查独立运行程序的全部 13 类事件、Demo Dash 重复回调、定向蹬墙跳、金草莓分类、收集去重、五条随机语音、禁用/音量设置、八条并发上限、缓存和卸载。检查生成程序引用的游戏接口，并用蔚蓝自带的 FMOD 1.10.20 将全部 65 个音频位置逐一播放到游戏音效总线，包括六秒长音频，比较完整波形。报告见 [v1.3.0 验证记录](docs/independent-validation.json)。

这些检查没有进行游戏内实际操作。技巧判定仍需要在游戏中复测，尤其抓角、Ultra、变体和其他修改玩家行为的模组。

集成测试需要 Python、NumPy、本地蔚蓝游戏和 FFmpeg；无需模板。测试自动制作测试音频，不下载或分发游戏资源：

```powershell
$env:CELESTE_DIR = 'D:\Steam\steamapps\common\Celeste'
$env:FFMPEG_PATH = 'D:\工具\ffmpeg.exe'
python .\tests\test_generator.py
```

下载包验证：先运行 `scripts/package.ps1`，然后指定含有九项匹配文件名的测试音频文件夹。测试在干净解压目录中生成模组，不放入任何模板；也验证全部留空时不需要 FFmpeg。

```powershell
$env:ANNOUNCER_AUDIO_DIR = 'D:\九条测试音频'
python .\tests\test_distribution.py
```

测试产生的临时文件自动清理。历史版本记录 [v1.1 验证](docs/validation-report.json) 和 [v1.2 验证](docs/optional-validation.json) 仅对应旧版实现。

## 来源与许可

本项目自行编写的生成器和运行程序按 MIT 许可发布。v1.3.0 不依赖或分发 TechAnnouncer 模板、检测程序及音频库。游戏、Everest、FNA 和 FMOD 由用户已有的游戏安装提供，本项目不分发其程序。用户必须有权使用和再分发所选音频，本工具不授予素材使用权。

- [最初参考的制作教程](https://www.bilibili.com/opus/1033106442681319432)。新版使用独立检测和直接 WAV 播放，生成方式与教程不同。
- 程序集资源打包：[Mono.Cecil](https://github.com/jbevain/cecil)，MIT 许可。完整声明见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
- [FFmpeg 官方下载来源](https://ffmpeg.org/download.html)。转换器由用户自行安装，通过独立进程调用；本项目仅提供官方来源链接，不捆绑其二进制。
