<p align="center"><img src="assets/ApoSwitch-512.png" width="160" alt="APO Switch icon"></p>

# APO 切换器 · ApoSwitch 1.0.12

一个轻量的 Windows 托盘工具，用单击菜单或双击循环切换 Equalizer APO 的一级校准配置。

[English](README.en.md) · [MIT License](LICENSE) · [GitHub](https://github.com/hakanasora/ApoSwitch)

## 下载与运行

适用于 Windows 11 x64 和 Equalizer APO。正式 `app` 目录只有一个 `ApoSwitch.exe`，项目代码、核心库和应用图标均已集成；旁边不需要 DLL、语言文件夹、JSON 清单或展示图片。

需要 **.NET 10 Windows Desktop Runtime x64**。已有兼容桌面运行时的用户直接运行。缺少环境时，双击可选 `runtime` 目录中的微软官方 `windowsdesktop-runtime-10.0.12-win-x64.exe`，按安装程序提示完成后，再运行 ApoSwitch。只装普通 .NET Runtime 不包含桌面界面支持。无需安装 SDK 或开发工具；系统级安装可能需要管理员授权。

补充包单独提供，不重复装进主 EXE，也不会由 ApoSwitch 自动下载安装。可从[微软官方下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)获取兼容的 x64 桌面运行时；随包安装器已按官方 SHA512 和微软数字签名核验，核验信息见 `runtime/verification.json`。

交付目录：`app` 为正式单 EXE，`runtime` 为可选环境补充包，`docs` 为说明、许可证和展示素材，`device-debug` 为独立调试工具，`source` 为源码。运行时补充包仍有自身的体积；下载主程序的用户不必重复下载它。

## 日常使用

1. 左键单击或右键单击托盘图标打开菜单；双击在“循环以下配置”中勾选的校准间轮换。单击会等待系统双击间隔后展开菜单。
2. 一级配置菜单直接应用一套配置，启用所选 Include，禁用其他一级候选；再次选择当前项不会将它关闭。
3. **循环以下配置** 独立决定循环成员，每次勾选立即保存，菜单保持展开，便于连续选择。循环成员不会同时被启用。
4. **配置文件位置 → 选择 config.txt…** 可以手动指定 Equalizer APO 真正使用的主配置文件；**恢复自动定位** 取消手选位置。
5. **开机自启动** 使用当前用户登录启动项，默认关闭；**退出** 保留当前已应用的校准。正式版与调试托盘共用单实例锁。

图标显示 `EQ1`、`EQ2` 等编号，按主文件中全部一级配置的顺序确定，包含禁用项，与循环勾选无关。全部未启用时显示 `EQ?`，外部编辑同时启用多套时显示 `EQ+`；应用任意配置后仍只启用所选的一套。未安装 APO 的设备显示大号单色 Blocked 符号与左下角 EQ 角标；没有设备、检测失败或配置读取失败时显示黄色警告。菜单和托盘图标跟随系统主题及缩放比例变化。

启动只读取现状，不自动套用上次配置。初次循环优先识别 `New Door Open` 与 `GSR_BK_filters`；无匹配时以当前启用项初始化。后续新增配置不自动参加循环。空循环不操作，单成员已启用时不重复写入。

## 配置位置与文件保护

自动定位读取 `HKLM\SOFTWARE\EqualizerAPO`，依次考虑 64 位和 32 位视图中的 `ConfigPath`。两处均未提供时尝试 `InstallPath\config`，最后尝试系统 Program Files 下的默认 Equalizer APO 配置目录；并未写死开发者电脑的盘符或安装位置。

已经得到明确的 ConfigPath，却找不到或无法读取主文件时会报错，不悄悄使用另一份旧默认配置。手选位置优先并会记住；该位置失效后保持报错，不自行换回自动路径。选择取消、文件不可读或设置保存失败时保留之前的选择。

选择位置只改变切换器读取和操作的主文件，不修改 APO 注册表、不创建配置、不自动启用任何 Include。请选 APO 实际加载的 `config.txt`。自动模式会在打开菜单和应用前重新定位，外部保存由文件监听和定时刷新跟进；切换位置时监听也随之更换。

**只识别主文件直接列出的 `Include:` 与 `# Include:`，不扫描目录、不递归读取嵌套文件。** 切换只调整主文件中候选 Include 的启用状态，保留其他指令、文字、编码和换行。上一版备份为同目录的 `config.txt.aposwitch.bak`。配置目录需要写权限；失败会明确提示，不显示为成功。滤波器资源和实际声音效果请用 Equalizer APO 和试听确认。

设置仍位于 `%LOCALAPPDATA%\ApoSwitch\settings.json`，其中 `ConfigFilePath` 为空表示自动定位。旧版本设置继续兼容，循环成员与手选路径一起保存。它们不会随 EXE 移动。自启动位于当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ApoSwitch`；更换程序存放目录后应重新开关一次自启动。

## 设备状态

跟随 Windows 默认多媒体播放设备。设备通知、每 5 秒轮询、打开菜单和应用前都会刷新。检测的是 Equalizer APO 的设备效果注册状态，覆盖 LFX/GFX、SFX/MFX、SFX/EFX。独占播放或系统关闭音效不等同于“未安装 APO”。

未安装 APO、没有设备或检测失败时，一级配置应用及双击切换不可用；循环设置、配置位置、自启动和退出仍可操作。错误区域独立分组并显示设备名称；配置读取失败时同时给出具体文件错误。

## 独立设备调试工具

正式 `ApoSwitch.exe` 只引用核心库，没有模拟模块依赖、模拟启动参数或命名管道服务端入口。

`device-debug` 中包括正式 `ApoSwitch.exe`、独立 `ApoSwitch.Debug.exe` 和 `ApoSwitch.DeviceSimulator.exe` 三个单文件程序，均使用共享的桌面运行时。先退出正式托盘，再打开模拟器，点击 **启动调试托盘 → 连接**，选择四种设备状态或恢复真实检测。关闭或断开模拟器会清除模拟状态；两种托盘的自启动始终指向正式 EXE。

**只模拟设备状态；在调试托盘选择校准仍会真实修改当前主配置。** 模拟状态仅存内存，重启不会保留。调试托盘链接共用的 UI 源码并注入调试设备源，配置逻辑始终共用，无需发布前手工删除模拟代码。

## 源码与构建

需要 .NET 10 SDK，无第三方 NuGet 依赖。正常构建直接使用随源码提供的 ICO，不需要图形工具。

```powershell
.\build.ps1 -RunChecks
# 同时构建独立调试工具，使用新的空输出目录：
.\build.ps1 -IncludeDeviceDebug -OutputDirectory .\artifacts-debug -RunChecks
# 显式下载、校验可选官方运行时安装器，但不安装：
.\build.ps1 -IncludeRuntime -OutputDirectory .\artifacts-with-runtime
```

默认输出 `artifacts`，下设 `app`、`docs` 等目录。要求输出目录为空，防止旧运行库混入。单文件采用框架依赖模式，关闭 ReadyToRun 与强制裁剪。SDK 可能需要还原微软的构建包。

普通检查只在临时目录写入配置，真实设备查询只读。`-LiveConfigChecks` 才会短暂切换真实 APO 配置并恢复原始字节，不应在日常验收中误用。发布隔离检查命令为 `ApoSwitch.Checks --verify-release <app目录> <打包前程序集目录>`。

图标母版 `assets/ApoSwitch.svg` 保留原始矢量路径、平滑渐变和阴影，无噪声、嵌入位图或字体依赖；多尺寸 ICO 无损封装并嵌入 EXE。动态托盘使用系统 Segoe UI / Segoe Fluent Icons，不分发字体。需要重新生成图标时安装 Node.js 和 sharp，运行 `node tools/render-icon.cjs`。

设备安装判断参考 [Equalizer APO 源码](https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp)，路径读取依据 [APO 配置文档](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)。