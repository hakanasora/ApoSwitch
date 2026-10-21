# ApoSwitch 开发说明

[返回产品首页](README.md) · [English](DEVELOPMENT.en.md)

## 构建与交付

需要 Windows 和 .NET 10 SDK，无第三方 NuGet 依赖。普通构建使用仓库中已有的 ICO，不需要图形工具。在仓库根目录运行：

```powershell
.\build.ps1 -RunChecks
# 同时构建独立设备调试工具：
.\build.ps1 -IncludeDeviceDebug -OutputDirectory .\artifacts-debug -RunChecks
# 下载并核验可选官方运行时安装器，不执行安装：
.\build.ps1 -IncludeRuntime -OutputDirectory .\artifacts-with-runtime
```

输出目录必须为空或尚未创建，默认使用 `artifacts`。目录用途如下：

| 目录 | 内容 |
| --- | --- |
| `app` | 正式单文件 `ApoSwitch.exe` |
| `docs` | 中英文产品与开发说明、MIT 许可证、展示素材 |
| `device-debug` | 可选的正式 EXE、调试托盘和状态模拟器 |
| `runtime` | 可选的微软运行时安装器和 `verification.json` |

发布目标为 Windows x64，使用框架依赖单文件：`SelfContained=false`、`PublishSingleFile=true`，关闭 ReadyToRun、裁剪和调试符号。项目代码、核心库与图标合并到 EXE，WinForms 与框架资源由共享的 .NET 10 Windows Desktop Runtime x64 提供。SDK 可能需要还原微软构建包。

`tools/Get-Runtime.ps1` 当前固定获取官方 10.0.12 x64 桌面运行时安装器，通过微软发布元数据中的 SHA512 和 Authenticode 微软签名检查后，记录核验信息。脚本不安装运行时；安装器版本不代表必须锁定到同一个运行时补丁版本。

对外发布时，主 EXE、可选运行时、调试工具分别作为下载项。产品下载入口使用仓库的最新 Release 链接，不绑定版本号或本机打包目录。文档由构建脚本复制到 `docs`，保持中英文互链和图标引用可用；源码、工具与构建命令的使用以[仓库](https://github.com/hakanasora/ApoSwitch)为准。

## 配置发现与写入

自动定位读取 `HKLM\SOFTWARE\EqualizerAPO`：先检查 64 位、32 位视图中的 `ConfigPath`；都没有明确配置时，再考虑 `InstallPath\config`，最后尝试系统 Program Files 下的默认目录。明确路径缺失或不可读时显示错误，不静默改用其他配置。

设置中的非空 `ConfigFilePath` 优先于自动定位。手选文件先读取成功，再保存设置并切换监听；取消、读取失败或保存失败保留原选择。失效的手选路径持续报错，直到用户重新选择或恢复自动定位。选择路径不修改 APO 注册表、配置内容或启用状态。

自动模式在打开菜单、应用配置和定时刷新时重新定位。文件监听随主文件更换，外部保存使用 180 ms 防抖，并以 5 秒定时读取补充刷新。

候选只解析主文件直接包含的 `Include:` 和已注释的 Include，不读取嵌套内容、不扫描文件夹。相同目标路径合并为一个候选，顺序取首次出现位置。应用时启用所选目标的一条 Include，禁用其余候选行；其他文字、指令、编码及换行保留。

写入前检查目标文件存在；同目录临时文件写入并刷新后，核对主文件没有被其他程序修改，再以 `File.Replace` 替换并保留 `.aposwitch.bak` 备份。若内容不变则不写入；资源内容和实际音效不由该检查验证。

## 用户设置与托盘行为

设置保存到 `%LOCALAPPDATA%\ApoSwitch\settings.json`。`CyclePresetIds` 持久保存循环成员，`ConfigFilePath` 缺失或为空表示自动定位；旧设置继续兼容。修改循环成员保留路径设置。

循环顺序取主文件候选顺序，不取用户勾选顺序。当前不在循环中时从首项开始；空循环不操作；单成员已独占启用时不会重复写入。首次循环初始化规则由 `UserSettings.Initial` 定义，其中保留历史配置名称兼容逻辑；后续新增候选默认不加入循环。启动只读取当前配置。

左键单击等待系统双击间隔后打开菜单，右键立即打开，双击执行循环；循环子菜单支持连续勾选。原生菜单和动态图标响应系统主题与缩放变化。

自启动写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ApoSwitch`，默认关闭。正式和调试托盘都将启动项指向所在目录的 `ApoSwitch.exe`，因此需保留该文件名，移动后重新设置启动项。

## 真实设备与独立调试工具

真实设备源跟随 Core Audio 默认多媒体播放设备，通过设备通知、5 秒轮询、菜单打开和应用前查询刷新。检测 APO 设备效果注册信息，覆盖 LFX/GFX、SFX/MFX、SFX/EFX；不据此保证实际音频处理链生效，独占播放或关闭音效也不等于未安装 APO。

正式入口只引用核心库，无模拟模块依赖、模拟参数或命名管道服务端入口。独立调试托盘链接共用 UI 源码并注入 `IDeviceStateSource`，共用配置读写与循环逻辑。两种托盘共用单实例锁。

使用调试工具时：

1. 退出正式托盘，运行 `ApoSwitch.DeviceSimulator.exe`。
2. 点击 **启动调试托盘 → 连接**，启动并连接同目录的 `ApoSwitch.Debug.exe`。
3. 选择已安装 APO、未安装 APO、没有播放设备或检测失败；也可恢复真实检测。

模拟通道为仅限当前用户的本地命名管道。模拟状态只存在内存，断开或重启恢复真实检测。调试工具需要共享桌面运行时，自启动仍指向正式程序。

**只模拟设备状态；调试托盘中的配置切换仍会真实修改当前选定主文件。**

## 检查

`-RunChecks` 运行配置、循环、路径和设备状态检查，再检查正式发布隔离。普通检查修改临时配置，真实设备查询只读。

`-LiveConfigChecks` 会短暂切换真实 APO 配置并恢复原始字节，仅在明确需要时使用。日常文档检查不应运行它。

发布隔离检查需要单文件输出及打包前程序集目录，入口为 `ApoSwitch.Checks` 的 `--verify-release <app目录> <打包前程序集目录>`。检查正式目录只含一个不超过 2 MB 的 EXE、框架依赖要求，以及正式程序集没有模拟依赖或管道服务端入口。

维护文档时同步中英文内容。修改构建文档复制清单后检查输出文件、图片和相对链接；仓库源码与工具链接应指向 GitHub，以便随包开发说明也能使用。

## 图标与参考

`assets/ApoSwitch.svg` 是矢量母版，使用路径、平滑渐变和阴影，无嵌入位图、纹理噪声或字体依赖。ICO 的 16、20、24、32、40、48、64、96、128、256 px 帧分别从母版渲染并以无损 PNG 封装，嵌入应用；另生成 512 和 1024 px 展示图。

普通构建不重新生成图标。需要更新素材时，在仓库根目录安装 Node.js 和 sharp 后运行：

```powershell
npm install --no-save sharp
node tools/render-icon.cjs
```

动态图标使用系统 Segoe UI / Segoe Fluent Icons，不分发字体。未安装 APO 使用 Blocked（E733）与 EQ 角标，其他错误使用 WarningSolid（F736）。

- [Equalizer APO 设备检测源码](https://github.com/mirror/equalizerapo/blob/master/DeviceAPOInfo.cpp)
- [Equalizer APO 配置参考](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)
- [微软 .NET 10 下载](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [MIT 许可证](LICENSE)
