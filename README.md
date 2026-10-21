<p align="center"><img src="assets/ApoSwitch-512.png" width="160" alt="APO Switch icon"></p>

# APO 切换器 · ApoSwitch 1.0.11

一个轻量的 Windows 托盘工具，用单击菜单或双击循环切换 Equalizer APO 的一级校准配置。

[English](README.en.md) · [MIT License](LICENSE)

适用于 Windows 11 x64、Equalizer APO 1.4.2。发布目录包含 .NET 运行依赖，放在固定位置即可使用。

## 应用图标

主程序、模拟器的可执行文件及模拟器窗口使用同一套手动挡图标。母版为 `assets/ApoSwitch.svg`，保留原始矢量路径、平滑渐变和阴影，不含噪声纹理、嵌入位图或字体依赖。ICO 包含 16、20、24、32、40、48、64、96、128、256 像素的 32 位透明图层，各尺寸直接从 SVG 渲染，以无损 PNG 存储。另附 512、1024 像素 PNG。Windows 根据显示尺寸选择或缩放；有限的屏幕像素仍会影响小尺寸细节。

应用图标与用于显示配置编号、设备状态的托盘图标各有用途，托盘仍显示 EQ 编号和状态。

## 日常使用

托盘采用紧凑横排图标，显示当前配置的 `EQ1`、`EQ2`、`EQ3` 等编号。编号按主 `config.txt` 的全部一级配置顺序确定，包含禁用项，与循环勾选无关。例如前三项依次编号为 EQ1、EQ2、EQ3。图标跟随任务栏深浅色和缩放比例变化。没有启用一级配置时显示 `EQ?`，同时启用多套时显示 `EQ+`，悬停提示显示启用数量和配置名称。设备未安装 APO 时显示大号 Blocked（E733）符号与左下角 `EQ` 角标；没有播放设备、设备检测失败或配置读取失败时显示黄色 Fluent 警告图标。

图标保留 Segoe UI 粗体：同位数编号共用缩放比例、文字基线和固定字符占位，数字及状态符号按各自笔画宽度在占位内居中，切换 EQ1、EQ2、EQ3、EQ+、EQ? 时 EQ 的大小和位置保持一致。按最宽数字或符号预留紧凑空间；Q 保留字体原有的下伸尾笔。未安装 APO 的设备以大号 Blocked 符号为主体，左下角放置 EQ 角标。

一级应用菜单始终单选：选择任意配置后，只启用该配置并禁用其他一级候选 Include；再次点击当前配置也不会将它关闭。外部编辑产生的多套启用或全部未启用状态只做如实显示，不自动修改文件。只有“循环以下配置”子菜单允许多选，它决定轮换成员，不会同时启用这些配置。

左键单击或右键单击托盘图标均可打开菜单；双击仍用于循环切换。为区分单击与双击，左键单击会等待系统设置的双击间隔后展开菜单。

1. 运行 `ApoSwitch.exe`，右下角托盘出现 EQ 图标；若被 Windows 收进隐藏图标区，展开箭头即可找到。
2. 双击图标，在“循环以下配置”勾选的校准间轮换。右键菜单上半部分可直接应用任意一级校准。
3. 右键 → **循环以下配置**，决定哪些校准参加循环。每次勾选立即保存，主菜单和子菜单保持展开，可以连续选择；点击菜单外或按 Esc 收起。它的勾选与上半部分“当前启用”的勾选各自独立。
4. 需要随登录启动时，勾选 **开机自启动**。默认关闭。程序只保留一个实例，退出不会撤销已应用的校准。

配置目录从 Equalizer APO 注册表设置读取。**只识别主 `config.txt` 直接列出的 `Include:` 与 `# Include:`，不扫描目录、不递归读取任何嵌套文件。** 一级配置内部可以继续嵌套，切换器完全保留这些文件。

启动只读取现状，不自动套用上次配置。初始循环选中 `New Door Open` 与 `GSR_BK_filters`；后续新增一级配置默认不参加循环。循环顺序与主配置顺序一致。空循环不操作文件，单成员已启用时不重复写入。

修改只发生在主配置的 Include 行；其他指令、文字、编码、换行保留。上一版主配置备份为同目录的 `config.txt.aposwitch.bak`。程序会跟随外部保存重新读取；不要让编辑器中较早的未保存内容覆盖你刚做的切换。

个人循环设置：`%LOCALAPPDATA%\ApoSwitch\settings.json`。自启动使用当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ApoSwitch`，不需要管理员权限。配置切换需要对 Equalizer APO 配置目录具有写入权限；无权限时会提示失败，不会显示为成功。

## 设备提示

错误提示独立为一个菜单分组，第二行以灰色显示具体设备名称。读取不到设备名称时显示“未知设备”；没有播放设备时显示“无播放设备”。

跟随 Windows 默认的多媒体播放设备。设备通知触发刷新，另每 5 秒重新检查一次；打开菜单和应用前也会刷新。

| 设备状态 | 悬停提示 / 菜单 | 实际切换 |
| --- | --- | --- |
| 已安装 APO | 当前校准及设备名 | 允许 |
| 未安装 APO | 当前设备不适用 / 此设备未安装 APO 服务 | 禁用 |
| 没有播放设备 | 未检测到播放设备 | 禁用 |
| 检测失败 | 无法读取设备状态 | 禁用 |

未安装 APO 属于日常设备状态，显示大号禁止符＋左下角 EQ 角标，随任务栏深浅色显示单色。主体使用 Windows 11 自带 Segoe Fluent Icons 的 Blocked（U+E733）字形，角标使用 Segoe UI 粗体；角标周围留透明间隙，避免与圆圈和斜线粘连。运行时将两种字形的矢量轮廓绘成同一张透明位图，不使用离线图标素材，也不绘制括号。悬停仍提示“当前设备不适用”。其他异常使用 Windows 11 自带 Segoe Fluent Icons 中的 WarningSolid（U+F736），绘成黄色三角警告图标；配置读取错误优先于未安装状态显示警告。图标字体直接使用本机系统字体，不随程序分发字体文件。参见[微软图标目录](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font)。

不能应用时配置应用项变灰；循环勾选、自启动和退出照常可用。切回适用设备后恢复操作，不自动修改配置。菜单颜色随 Windows“默认应用模式”变化，托盘图标颜色随任务栏深浅色变化。

检测的是 Equalizer APO 的设备效果注册状态，覆盖 LFX/GFX、SFX/MFX、SFX/EFX 安装方式。播放器独占模式、音效被系统禁用等情况不等同于“未安装 APO”；是否实际处理声音请结合试听确认。

## 设备状态模拟器

1. 若正常托盘已运行，先从托盘右键菜单退出。
2. 运行单独调试目录中的 `ApoSwitch.DeviceSimulator.exe`；它会启动同目录的 `ApoSwitch.Debug.exe`。
3. 点击 **启动调试托盘**，然后点击 **连接**。
4. 点击“已安装 APO”“未安装 APO”“没有播放设备”或“检测失败”，观察托盘图标、悬停文字、菜单启用状态。
5. 点击 **恢复真实设备检测**，或关闭/断开模拟器，托盘恢复读取真实设备。

**模拟器只模拟设备状态，托盘的校准切换会真实修改 Equalizer APO 主配置。** 调试模式会在菜单标题和悬停文字中标明。退出模拟器不会还原你主动选择的校准。

正式程序 `ApoSwitch.exe` 仅引用核心库，不引用 `ApoSwitch.DeviceDebug`，没有模拟启动参数或命名管道监听入口。调试程序独立为 `ApoSwitch.Debug.exe`，由模拟器启动，才会创建当前用户专用的本地命名管道。普通版与调试版共用单实例锁，避免同时切换配置。模拟状态仅存内存，关闭连接或重启不会保留。两种程序的开机自启动均指向正式 `ApoSwitch.exe`。

## 源码和构建

源码分为核心配置逻辑、正式托盘、独立调试托盘、设备模拟模块、模拟器。正式托盘的项目引用仅包含核心库。调试托盘链接共用的界面源码，并注入实现 `IDeviceStateSource` 的调试设备源；配置读写始终共用。无需发布前手工删代码。

需要 .NET 10 SDK；没有第三方 NuGet 库。发布时 SDK 会使用或下载微软的 win-x64 运行包。

```powershell
.\build.ps1 -RunChecks
# SDK 未加入 PATH 时：
.\build.ps1 -DotnetPath 'D:\tools\dotnet\dotnet.exe' -RunChecks
```

图标素材已包含在源码中，正常构建不需要额外图形工具。若要从 SVG 重新生成素材，可在源码根目录安装 Node.js 与 `sharp`（`npm install --no-save sharp`），再运行 `node tools/render-icon.cjs`。

默认只生成正式版，输出 `artifacts\app`。需要调试工具时加 `-IncludeDeviceDebug`，另生成 `artifacts\app-device-debug`，其中包含独立调试托盘、模拟器及用于正常启动的正式程序。输出目录必须为空，避免旧版本调试文件混入正式版；再次构建可指定新的 `-OutputDirectory`。`-LiveConfigChecks` 会短暂切换实际 APO 主配置，并在 finally 中恢复到测试前的原始字节；普通 `-RunChecks` 在临时目录验证配置写入，只读查询真实设备。

Windows 原生菜单通过 HMENU / TrackPopupMenuEx 实现，系统负责绘制和子菜单。原生菜单深色适配集中在 `NativeTheme` 内（UXTheme 兼容接口）；遇到系统版本差异时只需维护这一处。

设备安装判断参考 [Equalizer APO DeviceAPOInfo](https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp)，默认设备通知使用 [IMMNotificationClient](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)。
