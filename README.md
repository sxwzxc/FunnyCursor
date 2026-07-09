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
   - 图标支持内置矢量形状（五角星 / 圆形 / 方形 / 三角 / 菱形 / 心形 / 笑脸）、**内置图片（🐷 粉色小猪 / 👧 二次元女孩）**或**自定义图片（PNG / SVG / GIF，均保留透明度）**。
   - **GIF 动画**：选择 GIF 自定义图标后自动逐帧播放，循环无卡顿。
   - 渲染统一走 Win2D `DrawImage` / `DrawSvg`，PNG / GIF 的 Alpha 透明度被完整保留。
   - 可调节绳子长度、节数、重力、阻尼、刚度、粗细、颜色、图标大小与颜色。

3. **光标拖尾**
   - 记录光标轨迹，按存活时间淡出，可调节颜色、长度、宽度。

4. **环绕粒子**
   - 在光标周围生成一圈粒子，以可调速度持续旋转（顺时针 / 逆时针）。
   - 粒子带透明度与大小渐变，配合淡环描边，呈现彗星拖尾般的扫光效果。
   - 可调节启用、粒子数量、环绕半径、旋转速度（度/秒，支持负向反向）、粒子大小、颜色。

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
| 渲染 | Win2D（`Microsoft.Graphics.Win2D` 1.3.2，`CanvasControl`） |
| 运行时 | .NET 8（`net8.0-windows10.0.19041.0`） |
| 分发 | 自包含非打包（`WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`） |
| 物理 | 自实现 Verlet 积分绳子模拟 |
| 窗口 | 透明全屏覆盖层：DWM 透明合成 + `WS_EX_TRANSPARENT` 点击穿透 + `WS_EX_TOPMOST` 置顶，覆盖多显示器虚拟屏幕 |
| 托盘 | Win32 消息窗口 + `Shell_NotifyIcon` + `TrackPopupMenu` |

> 本项目采用**代码优先（Code-Behind / 无 XAML）**方式构建 UI，所有窗口与控件均在 C# 中创建。

## 编译

### 前置条件
- Windows 10 19041+ / Windows 11
- .NET 8 SDK
- Visual Studio 2022（含“使用 C++ 的桌面开发”与 Windows SDK 10.0.19041+）或仅 .NET SDK 命令行

### Debug
```powershell
cd MouseBeautifier
dotnet build -c Debug -p:Platform=x64
```

### Release
```powershell
cd MouseBeautifier
dotnet build -c Release -p:Platform=x64
```

> 首次构建会还原 WinAppSDK 自包含运行时，耗时较长，请耐心等待。

## 运行

构建产物位于 `MouseBeautifier/bin/x64/<Debug|Release>/net8.0-windows10.0.19041.0/`。
直接运行 `FunnyCursor.exe` 即可。设置文件保存在：

```
%LOCALAPPDATA%\FunnyCursor\settings.json
```

## 使用说明

1. 启动后托盘区出现图标，屏幕上即开始显示美化效果。
2. 左键双击托盘图标（或右键 → 打开面板）打开设置窗口。
3. 在设置窗口中实时调整各项参数，修改即时生效。
4. 关闭设置窗口会最小化到托盘；从托盘菜单选择“退出”才真正结束程序。
5. **全局快捷键 `Ctrl+Shift+F10`** 可随时退出程序（即使设置面板未打开）。
   - 若该快捷键被其他程序占用，会自动回退到 `Ctrl+Alt+Q`。

## 设置项说明

| 分组 | 设置 | 说明 |
| --- | --- | --- |
| 点击特效 | 启用 / 预设 / 颜色 / 粒子数 / 速度 / 重力 | 控制点击动画 |
| 绳子 | 启用 / 长度 / 节数 / 重力 / 阻尼 / 刚度 / 图标类型（含内置图片选项） / 大小 / 颜色 / 绳子颜色 / 粗细 / 自定义路径 | 控制悬挂物理 |
| 拖尾 | 启用 / 颜色 / 长度 / 宽度 | 控制光标轨迹 |
| 环绕粒子 | 启用 / 数量 / 半径 / 速度 / 大小 / 颜色 | 控制光标周围旋转粒子环 |
| 光晕 | 启用 / 颜色 / 半径 / 强度 | 控制光标光晕 |
| 常规 | 开机自启 | 写入注册表自启 |

点击"恢复默认设置"可一键还原；图标类型除矢量形状外，还提供 **粉色小猪** 与 **二次元女孩** 两款 AI 生成内置图片图标，无需额外文件；选择"自定义图片"需点击"浏览…"选择本地 **PNG / SVG / GIF**（支持透明背景，GIF 自动播放动画）。

## 目录结构

```
MouseBeautifier/
├── App.xaml.cs            # 应用入口与生命周期（无 XAML）
├── AppInfo.cs             # 版本号 / 作者 / 版权 / 仓库地址（集中管理）
├── OverlayWindow.xaml.cs  # 透明覆盖层 + 120fps 渲染循环
├── SettingsDialog.cs      # 设置面板（纯 Win32 对话框，避开 WinUI 主题资源依赖）
├── DialogNative.cs        # 设置对话框所需的 Win32 P/Invoke 声明
├── EffectRenderer.cs      # 所有视觉的绘制编排
├── IconImage.cs           # 图标加载（PNG/SVG/GIF，含帧动画与透明度）
├── ParticleSystem.cs      # 点击粒子与涟漪
├── RopeSimulator.cs       # Verlet 绳子物理
├── Trail.cs               # 光标拖尾
├── MouseTracker.cs        # 全局鼠标钩子 + 光标轮询
├── TrayIcon.cs            # 系统托盘（消息窗口）
├── Settings.cs            # 设置模型 / 持久化 / 注册表自启
├── NativeMethods.cs       # 全部 P/Invoke 声明
├── app.manifest          # DPI / 兼容 / 权限清单
├── MouseBeautifier.csproj # 项目配置
└── Assets/               # 内置资源
    ├── pig.png            # 🐷 可爱粉色小猪（挂坠）
    ├── girl.png           # 👧 可爱二次元女孩（挂坠）
    └── funnycursor.ico   # 应用图标（紫粉蓝渐变 + 光标 + 彩虹拖尾）
```

## 已知限制

- 仅支持 x64 与 Windows 平台。
- 透明覆盖层依赖 DWM，在远程桌面 / 某些显卡驱动下效果可能受限。
- 自定义图标使用 Win2D `CanvasBitmap` / `CanvasSVGDocument` 解码，复杂 SVG 可能无法完美渲染。
