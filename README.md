# FunnyCursor

一款 Windows 平台的鼠标美化工具，基于 **WinUI 3 + Win2D** 构建。它在屏幕上叠加一个全屏透明、可点击穿透的图层，实时渲染鼠标点击特效、悬挂绳物理、光标拖尾与光晕，并常驻系统托盘。

## 功能特性

1. **点击特效**（粒子 / 彩纸 / 光环 / 水波）
   - 全局低级鼠标钩子（`WH_MOUSE_LL`）检测左右中键点击，在点击位置触发动画。
   - 四种预设：`sparkle` 闪烁粒子、`confetti` 彩色纸屑、`ring` 扩散光环、`ripple` 水波纹。
   - 可调节粒子数量、喷射速度、重力、颜色。

2. **悬挂绳子 + 图标**（Verlet 物理）
   - 绳子顶部锚定在光标处，下方悬挂一个图标。
   - 基于 Verlet 积分模拟重力、阻尼与刚度，随光标速度/加速度自然摆动。
   - **自动朝向**：图标随绳子摆动角度与鼠标移动速度自动调整倾斜（朝向运动方向），静止时保持正立。
   - **保底机制**：无论鼠标怎么移动 / 物理如何计算，悬挂物（bob）距光标永远 ≤ `RopeLength - IconSize`，悬挂物远端 ≤ `RopeLength`。算法见下方「绳子物理与稳定性」。
   - 图标支持内置矢量形状（五角星 / 圆形 / 方形 / 三角 / 菱形 / 心形 / 笑脸）、**内置图片（🐷 粉色小猪 / 👧 二次元女孩）**或**自定义图片（PNG / SVG / GIF，均保留透明度）**。
   - **GIF 动画**：选择 GIF 自定义图标后自动逐帧播放，循环无卡顿。
   - 渲染统一走 Win2D `DrawImage` / `DrawSvg`，PNG / GIF 的 Alpha 透明度被完整保留。
   - 可调节绳子长度、节数、重力、阻尼、刚度、粗细、颜色、图标大小与颜色。

3. **光标拖尾**
   - 记录光标轨迹，按存活时间淡出，可调节颜色、长度、宽度。

4. **星云环绕**
   - 星尘、云雾、彗星尾迹与星尘光晕围绕光标旋转（支持正反向）。
   - 每颗星尘为三层同心结构：中心点 → 描边（可设颜色/宽度/不透明度）→ 最外层柔和光晕。
   - 颜色统一使用 RGB；云雾、尾迹、星尘本体、描边、星尘光晕分别提供明确的不透明度，描边有独立宽度、光晕有独立大小控制。

5. **光标光晕**
   - 在光标处绘制径向渐变光晕，可调节颜色、半径、强度。

6. **系统托盘 + 开机自启**
   - 最小化到托盘而非退出；左键 / 双击托盘图标打开设置面板，右键弹出菜单（打开 / 退出）。
   - 设置内可开启开机自启（写入注册表 `Run` 键）。

7. **关于面板**
   - 设置面板底部新增「关于」分组，集中展示产品名、版本号、作者、版权与仓库地址。
   - 版本号旁可一键复制；仓库地址可一键在浏览器打开。

## 技术栈

| 项目 | 说明 |
| --- | --- |
| UI 框架 | WinUI 3（`Microsoft.WindowsAppSDK` 1.6.250602001） |
| 渲染 | Win2D（`Microsoft.Graphics.Win2D` 1.3.2，每显示器 `CanvasRenderTarget`） |
| 运行时 | .NET 10（`net10.0-windows10.0.19041.0`） |
| 分发 | 自包含非打包（`WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`） |
| 模拟 | 无 UI 依赖的 `FunnyCursor.Core`；120 Hz 固定步长，Verlet 绳子、粒子、拖尾与时间戳输入队列 |
| 窗口 | 每台物理显示器一个 Win32 **分层窗口**（`WS_EX_LAYERED`）；同一个模拟快照经坐标投影后由各窗口渲染，支持负虚拟屏幕坐标和混合 DPI |
| 托盘 | Win32 消息窗口 + `Shell_NotifyIcon` + `TrackPopupMenu` |

## 架构

- `MouseBeautifier` 是 WinUI 3 外壳：`App.xaml` 提供应用资源，`SettingsWindow.xaml` 定义设置窗口，代码隐藏负责设置绑定、文件选择和生命周期。
- `FunnyCursor.Core` 不依赖 WinUI / Win2D，集中管理设置模型、固定步长时钟、时间戳输入、粒子、拖尾、绳子和挂件几何，可在无桌面的测试进程中验证。
- `OverlayHost` 只创建一个 `EffectWorld`。每帧先消费鼠标输入并推进一次模拟，再把同一个只读快照投影到每台物理显示器，避免多屏分别推进导致速度翻倍或状态分叉。
- 星云的确定性几何位于无 UI 依赖的 `NebulaLayout`，Win2D 分层绘制位于 `NebulaRenderer`；设置先归一化为不可变 `NebulaRenderSettings`，同一帧的所有显示器共享同一份参数快照。
- 每台显示器拥有独立的 `CanvasRenderTarget`、DIB 和分层窗口。Win2D 输出读回 32 位预乘 BGRA，之后通过 `UpdateLayeredWindow` 提交；窗口保持置顶、无激活且点击穿透。
- 显示器增删、分辨率变化、`WM_DPICHANGED` 和 Win2D 设备丢失均会触发表面或设备资源重建。原生 DC、GDI 对象、鼠标钩子、菜单和图标由 `SafeHandle` 包装。
- 设置由 `ISettingsService` 隔离，当前实现以 schema v2 写入 `%LOCALAPPDATA%\FunnyCursor\settings.json`；旧版扁平环绕字段会在加载时自动迁移，滑块修改即时预览并在停止操作 250 ms 后原子持久化。

## 编译

### 前置条件
- Windows 10 19041+ / Windows 11
- .NET 10 SDK
- Visual Studio 2022（含“使用 C++ 的桌面开发”与 Windows SDK 10.0.19041+）或仅 .NET SDK 命令行

### Debug
```powershell
cd MouseBeautifier
dotnet build -c Debug -p:Platform=x64
```

### Release
```powershell
cd MouseBeautifier
dotnet clean FunnyCursor.sln -c Release -p:Platform=x64
dotnet restore FunnyCursor.sln -p:Platform=x64
dotnet build FunnyCursor.sln -c Release -p:Platform=x64 --no-restore
受限环境：
dotnet build FunnyCursor.sln -c Release -p:Platform=x64 --no-restore -p:EnableSourceControlManagerQueries=false
dotnet test FunnyCursor.sln -c Release -p:Platform=x64 --no-restore --no-build
```

> 首次构建会还原 WinAppSDK 自包含运行时，耗时较长，请耐心等待。

Release x64 成功构建后，主输出目录应至少包含：

- `FunnyCursor.exe`、`FunnyCursor.dll`、`FunnyCursor.Core.dll`
- `FunnyCursor.pri`（应用与 WinUI 资源索引）
- `App.xbf`、`SettingsWindow.xbf`（已编译 XAML）
- `Microsoft.WindowsAppRuntime.dll`、`Microsoft.ui.xaml.dll` 与其他自包含 Windows App SDK 文件
- `Assets/funnycursor.ico`、`Assets/pig.png`、`Assets/girl.png`

## 自动化测试

测试项目为 `MouseBeautifier.Core.Tests`，当前包含 39 个 xUnit 测试，覆盖：

- 固定步长在 60 / 144 Hz 呈现频率下的一致性与卡顿追帧上限。
- 时间戳输入的容量、顺序、消费门限及清空后的新时间纪元。
- 粒子生命周期和数量上限、拖尾等距重采样 / 生命周期 / 点数上限。
- 绳子静止、瞬移、剧烈抖动、运行中几何参数变化时的有限值与长度约束。
- 挂件连接点、方向、非法输入回退和 `R * T` 变换顺序。
- 负坐标、多显示器混合 DPI 往返映射及单一共享模拟快照。
- 星云参数范围、RGB/不透明度契约、旧 JSON 迁移、布局确定性、零速尾迹与反向旋转。
- 2,000 帧高复杂度无头模拟的宽松耗时与托管分配预算，用于捕获每帧闭包分配等明显性能回归；它不是硬件渲染基准。

## 运行

构建产物位于 `MouseBeautifier/bin/x64/<Debug|Release>/net10.0-windows10.0.19041.0/`。
直接运行 `FunnyCursor.exe` 即可。设置文件保存在：

```
%LOCALAPPDATA%\FunnyCursor\settings.json
```

## 使用说明

1. 启动后托盘区出现图标，屏幕上即开始显示美化效果。
2. 左键双击托盘图标（或右键 → 打开面板）打开设置窗口。
3. 在设置窗口中实时调整各项参数，修改即时生效。
4. 关闭设置窗口会最小化到托盘；从托盘菜单选择“退出”才真正结束程序。
5. **全局快捷键 `Ctrl+Shift+Q`** 可随时退出程序（即使设置面板未打开）。
   - 若该快捷键被其他程序占用，会自动回退到 `Ctrl+Alt+Q`。

## 设置项说明

| 分组 | 设置 | 说明 |
| --- | --- | --- |
| 点击特效 | 启用 / 预设 / 颜色 / 粒子数 / 速度 / 重力 | 控制点击动画 |
| 绳子 | 启用 / 长度 / 节数 / 重力 / 阻尼 / 刚度 / 图标类型（含内置图片选项） / 大小 / 颜色 / 绳子颜色 / 粗细 / 自定义路径 | 控制悬挂物理 |
| 拖尾 | 启用 / 颜色 / 长度 / 宽度 | 控制光标轨迹 |
| 星云环绕 | 启用 / 数量 / 范围 / 速度 / 星尘大小 / 星尘颜色 / 云雾颜色 / 描边颜色+宽度 / 光晕颜色+大小 / 各层不透明度 | 中心点→描边→光晕 三层结构；云雾拥有独立颜色，可与星尘不同 |
| 光晕 | 启用 / 颜色 / 半径 / 强度 | 控制光标光晕 |
| 常规 | 开机自启 | 写入注册表自启 |

点击"恢复默认设置"可一键还原；图标类型除矢量形状外，还提供 **粉色小猪** 与 **二次元女孩** 两款 AI 生成内置图片图标，无需额外文件；选择"自定义图片"需点击"浏览…"选择本地 **PNG / SVG / GIF**（支持透明背景，GIF 自动播放动画）。

## 目录结构

```
MouseBeautifier/
├── App.xaml / App.xaml.cs                  # WinUI 应用资源、单实例与生命周期
├── SettingsWindow.xaml / .xaml.cs          # Mica 设置窗口及代码隐藏
├── OverlayHost.cs                          # 每显示器分层窗口、共享模拟时钟与设备恢复
├── EffectRenderer.cs / NebulaRenderer.cs   # Win2D 视觉投影与星云分层绘制
├── IconResourceManager.cs / IconImage.cs   # PNG/SVG/GIF 等图标资源
├── MouseTracker.cs / TrayIcon.cs           # 全局输入、托盘与退出快捷键
├── JsonSettingsService.cs                  # JSON 设置与开机启动注册
├── NativeMethods.cs / NativeHandles.cs     # P/Invoke 与原生资源安全句柄
├── MouseBeautifier.Core/
│   ├── EffectWorld.cs                      # 单一模拟世界与共享帧快照
│   ├── NebulaSettings.cs / NebulaLayout.cs # 星云参数契约、不可变快照与纯几何
│   ├── AppSettingsJson.cs                  # schema v2 序列化与旧设置迁移
│   ├── FixedStepClock.cs                   # 120 Hz 固定步长
│   ├── TimestampedInputQueue.cs             # 有界线程安全输入
│   ├── RopeSimulator.cs                    # Verlet 绳子物理
│   └── DisplayGeometry.cs / PendantGeometry.cs
├── MouseBeautifier.Core.Tests/             # xUnit 无头回归与性能预算测试
├── Assets/                                 # 内置图标与挂件图片
├── app.manifest
├── MouseBeautifier.csproj
└── FunnyCursor.sln
```

## 已知限制

- 仅支持 x64 与 Windows 平台。
- 透明覆盖层依赖 DWM、Win2D 和显卡驱动；远程桌面、HDR、独占全屏、显卡驱动重置等环境下的视觉与置顶行为可能不同。
- 分层窗口每帧执行 GPU 渲染结果读回并复制整屏 BGRA 缓冲；高分辨率、多显示器会增加内存带宽和 CPU/GPU 同步成本，60 FPS 是目标而非实时保证。
- 混合 DPI 坐标数学已有无头测试，但显示器热插拔、主屏切换、旋转、不同缩放比例与负坐标布局仍需在真实硬件上验证。
- 自动化测试不创建 WinUI 窗口，也不验证托盘、全局鼠标钩子、点击穿透、Mica、颜色 / 文件选择器、GIF 实际播放或设备丢失后的视觉恢复。
- 自定义图标使用 Win2D `CanvasBitmap` / `CanvasSVGDocument` 解码，复杂 SVG 可能无法完美渲染。
- 星云光晕和云雾的“不透明度”是径向渐变中心的峰值，边缘仍会按发光效果自然衰减到透明。

## 绳子物理与稳定性

悬挂绳子是这个项目最反复出问题的子系统（"五角星乱飞"系列 bug），稳定性由以下两层共同保证：

### 1. 物理层保底 clamp（`RopeSimulator.ClampToAnchor`）

- 段长 = `(RopeLength - IconSize) / 段数`——绳子自然下垂长度即 clamp 目标，**零压缩、零折叠**，避免末端段方向翻转朝上。
- `Update` 的所有返回路径（正常积分 / NaN 重置 / 首帧初始化）**都执行** `ClampToAnchor`，确保悬挂物（bob）距光标永远 ≤ `RopeLength - IconSize`。
- 非破坏性：每个点独立径向 clamp，保留链条形状（旧代码重建整条链为直线，视觉上像五角星"飞走"）。

### 2. 渲染层矩阵顺序（`EffectRenderer.DrawRope`）

悬挂物局部坐标的原点 `(0,0)` 是其顶点（焊在绳子末端）。`System.Numerics` 用 row-vector（`v' = v*M`），变换矩阵**必须是 `R * T`**：

```
v' = v · (R · T) = R · v + Tip
```

这样 `local(0,0) → Tip`（顶点锚定绳末端，永不分离），`local(0, size) → Tip + Direction*size`（沿绳方向延伸）。

> 历史教训：旧代码写成 `T * R`，展开是 `(v + Tip) · R`——把已平移的点绕**屏幕原点**旋转。鼠标不动时 `angle≈0` 看似正常，一旦绳子摆动 `angle≠0`，五角星就被绕屏幕原点甩到 `rotate(Tip)` 处，正是"鼠标移动后五角星飞离指针"的根因。

### 3. 测试覆盖

- `RopeSimulatorTests` 验证静止收敛、瞬移、剧烈抖动和完整挂件不越界。
- `PendantGeometryTests` 直接验证连接原点、方向、非法输入回退和 `R*T` 语义。
- `SimulationRegressionTests` 在输入突发、运行中绳子参数变化及长时间高复杂度模拟下验证容量、有限值、长度和性能预算。
- 这些测试通过标准 `dotnet test` 运行，不再依赖应用内的 `--test-*` 调试入口。
