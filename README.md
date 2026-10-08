# JellyWallpaper 果冻壁纸

Windows 10 桌面壁纸增强工具：**点击桌面时，壁纸会像果冻一样在按压点向内凹陷，松开后平滑回弹**。壁纸可定时自动轮换（丝滑淡入淡出）、支持多种显示模式与全局快捷键，完全不影响桌面图标的正常操作。

- 语言/框架：C# · WPF（.NET 8）
- 目标系统：Windows 10（x64）
- 交付形态：**单文件自包含 EXE，双击即可运行，无需安装、无需 .NET 运行时**
- CI：GitHub Actions 自动构建 + 发布

---

## ✨ 核心特性

| 特性 | 说明 |
|---|---|
| 果冻形变 | 鼠标左键在桌面按下 → 按压点向内凹陷（高斯型平滑衰减，**无圆环波纹**）；松开 → 阻尼弹簧平滑回弹（带轻微过冲，Q 弹手感） |
| 不干扰图标 | 渲染层挂在**桌面壁纸层（WorkerW）之下**，透明、不参与命中测试；图标选中/拖拽/新建/删除、右键菜单全部照常 |
| 壁纸轮换 | 读取指定文件夹内 jpg/png/bmp/gif/tiff/webp，按序或随机定时轮换，切换为淡入淡出，**无闪烁** |
| 全局快捷键 | 一键随机换壁纸，系统级热键，托盘后台运行时也生效，可在设置面板自定义 |
| 显示模式 | 填充 / 适应 / 拉伸 / 平铺 / 跨区（多显示器扩展）/ 居中，兼容 Windows 原生壁纸设置习惯 |
| 背景填充色 | 自定义适应/居中模式的空白区颜色 |
| 参数可调 | 形变强度、回弹速度、按压衰减、渲染分辨率、轮换间隔、文件夹、模式、填充色、快捷键，全部**实时预览** |
| 托盘运行 | 默认最小化到系统托盘；菜单含 打开设置 / 手动换壁纸 / 重启 / 退出 |
| 版本与日志 | 设置面板显示版本号；全局异常捕获，错误写日志 + 面板展示，日志存程序同目录 `logs\` |
| 开机自启 | 可选，写当前用户注册表 Run 项，无需管理员 |
| 低资源占用 | GPU 像素着色器渲染形变；空闲时停止渲染循环，仅动画期间耗帧 |

---

## 🚀 快速开始

### 1. 直接使用打包好的 EXE（推荐）

从 GitHub Releases 下载 `JellyWallpaper.exe`，**双击运行**即可。启动后自动进入系统托盘（右下角图标），壁纸层立即生效。

> 首次运行请到设置面板（托盘图标双击，或托盘右键 → 打开设置面板）里**选择壁纸文件夹**，否则桌面保持系统原生壁纸。

### 2. 从源码编译（Windows 10 / 11）

**环境要求：**
- Windows 10/11，64 位
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 SDK（提供 HLSL 编译器 `fxc.exe`，用于把 `Jelly.fx` 编译成 PS3.0 字节码内嵌进 EXE）

**命令：**

```bash
# 构建（Debug）
dotnet build JellyWallpaper.sln

# 发布单文件自包含 EXE（推荐，可直接分发）
dotnet publish src/JellyWallpaper/JellyWallpaper.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o publish
# 产物：publish/JellyWallpaper.exe（单文件，内置运行时与依赖，双击即用）
```

> **fxc.exe 未找到怎么办？** 构建会自动在常见 Windows SDK 路径搜索。若未命中，请显式指定：
> ```bash
> dotnet publish ... -p:FxcPath="C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\fxc.exe"
> ```

### 3. GitHub Actions 自动打包发布

把本仓库推到 GitHub，Actions 会自动在 Windows 环境构建单文件 EXE：

- 推送到 `main` → 构建并上传 `JellyWallpaper-win-x64` 构件（Actions 页面可下载）。
- 打 tag（如 `git tag v1.0.0 && git push --tags`）→ 自动创建 **GitHub Release** 并附带 `JellyWallpaper.exe`。
- 也可在 Actions 页面手动触发（`workflow_dispatch`）。

---

## 🖱️ 使用说明

### 果冻形变效果

1. 鼠标左键在**桌面区域**（包括图标上）按下 → 该处壁纸向内凹陷。
2. 保持按住可看到持续凹陷；松开 → 平滑回弹复原（带轻微 Q 弹过冲）。
3. 点击其他应用窗口**不会**触发，只对桌面区域生效。

### 托盘菜单

| 菜单项 | 作用 |
|---|---|
| 打开设置面板 | 显示/隐藏参数设置窗口（双击托盘图标同效） |
| 手动切换壁纸 | 立即随机切换一张壁纸 |
| 重启程序 | 退出后重新启动本程序 |
| 退出程序 | 退出，**立即恢复系统原始桌面壁纸**，不留渲染残留 |

### 全局快捷键

默认 **`Ctrl + Alt + W`**，任意程序在前台、托盘后台运行时都可用，立即随机换壁纸。可在设置面板改（组合键勾选 + 触发键点击后按下新按键）。若提示"注册失败"，说明该组合被其他程序占用，请换一个。

---

## ⚙️ 参数说明（设置面板）

| 参数 | 取值范围 | 说明 |
|---|---|---|
| 图片文件夹 | 路径 | 壁纸来源目录，支持 jpg/jpeg/png/bmp/gif/tiff/webp |
| 显示模式 | 填充/适应/拉伸/平铺/跨区/居中 | 见下方"显示模式" |
| 背景填充色 | 色值 `#RRGGBB` | 适应/居中模式下空白区域的颜色 |
| 轮换间隔(秒) | ≥0（0=关闭自动轮换） | 自动切换壁纸的时间间隔 |
| 轮换顺序 | 顺序/随机 | 定时轮换的取图方式（快捷键始终随机） |
| 形变强度 | 0~3 | 凹陷/回弹的位移幅度 |
| 回弹速度 | 20~300 | 弹簧刚度，越大回弹越快 |
| 按压衰减系数 | 2~40 | 阻尼，越小越 Q（过冲越明显） |
| 形变渲染分辨率 | 原图/高/中/低 | 壁纸解码长边上限，控制显存/内存占用 |
| 手动换壁纸快捷键 | Ctrl/Alt/Shift/Win + 按键 | 全局热键，可自定义 |
| 开机自动启动 | 开/关 | 写入当前用户注册表 Run 项 |

> 所有修改即时生效（实时预览），关闭窗口后自动保存到程序同目录 `settings.json`（目录不可写时存 `%AppData%\JellyWallpaper`）。

### 显示模式说明

| 模式 | 行为（单显示器） | 多显示器 |
|---|---|---|
| 填充 | 等比缩放填满，超出裁剪 | 在整体虚拟屏上按填充处理 |
| 适应 | 完整显示整张图，空白处填背景色 | 同上 |
| 拉伸 | 强行铺满，不保留比例 | 同上 |
| 平铺 | 图片按原始尺寸重复平铺 | 同上 |
| 跨区（扩展） | 等价于填充 | **单张壁纸横跨全部显示器**，形成一体画面 |
| 居中 | 原图居中，其余填背景色 | 同上 |

> 说明：除"跨区"外，其余模式以整体虚拟屏幕为基准进行布局（与多数多屏场景期望一致）。

---

## 📁 目录结构

```
JellyWallpaper/
├─ JellyWallpaper.sln
├─ .github/workflows/build.yml     # GitHub Actions 自动打包发布
├─ tools/make_icon.py              # 生成应用图标的脚本
└─ src/JellyWallpaper/
   ├─ JellyWallpaper.csproj
   ├─ app.manifest                 # 系统 DPI 感知、Win10 兼容清单
   ├─ App.xaml / App.xaml.cs       # 入口：单实例 + 全局异常捕获
   ├─ AppController.cs             # 编排：渲染层/壁纸/钩子/热键/托盘/设置
   ├─ WallpaperLayerWindow.cs      # 透明渲染层（桌面图标层之下 + 果冻动画）
   ├─ Effects/JellyEffect.cs       # 像素着色器封装（ShaderEffect）
   ├─ Shaders/Jelly.fx             # HLSL 果冻形变着色器
   ├─ Core/
   │  ├─ NativeMethods.cs          # Win32 P/Invoke
   │  ├─ DesktopLayerLocator.cs    # 定位"壁纸 WorkerW"（图标层之下）
   │  ├─ MouseHook.cs              # 全局低级鼠标钩子（只观察、不拦截）
   │  ├─ WallpaperManager.cs       # 壁纸扫描/解码/轮换
   │  ├─ JellyAnimator.cs          # 阻尼弹簧回弹动画器
   │  ├─ HotkeyManager.cs          # 全局热键
   │  ├─ AppSettings.cs / SettingsStore.cs  # 设置模型与持久化
   │  ├─ AutoStart.cs              # 开机自启
   │  └─ Logger.cs                 # 日志
   └─ UI/
      ├─ TrayController.cs         # 系统托盘
      └─ SettingsWindow.xaml(.cs)  # 参数设置面板
```

---

## 🔧 实现原理（关键设计）

**1. 渲染层如何"盖住壁纸却不挡图标"？**
利用 Windows 桌面的窗口层级：`Progman` 是桌面根窗口，向它发送 `0x052C` 消息可让系统生成一个"壁纸 WorkerW"（承载原生壁纸）。我们的渲染窗口通过 `SetParent` 挂到这个 WorkerW 之下，正好位于**图标层（SHELLDLL_DefView）之下、原生壁纸之上**。因此：
- 覆盖了原生壁纸 → 我们的壁纸+果冻效果可见；
- 在图标层之下 → 图标完全在上层，选中/拖拽/右键不受影响；
- 退出时销毁渲染窗口 → 系统原生壁纸自动还原。

**2. 不拦截鼠标事件的监听方式？**
用 `WH_MOUSE_LL` 全局低级钩子**被动观察**左键按下/松开，回调里始终 `CallNextHookEx` 放行，绝不修改或吞掉任何鼠标消息。再用 `WindowFromPoint` 判断点击是否落在桌面区域（Progman/WorkerW/SHELLDLL_DefView/SysListView32），只在桌面点击时触发形变。

**3. 果冻形变怎么做？**
`Jelly.fx`（PS 3.0 像素着色器）对壁纸纹理按按压点做**高斯型径向位移**：越靠近按压点采样偏移越大，向外平滑衰减（高斯分布无振荡 → 绝无"圆环波纹"），形成向内凹陷；配合轻微按压阴影增强立体感。形变深度由**阻尼弹簧动画器**驱动（`JellyAnimator`），按下 0→1、松开 1→0 且欠阻尼带过冲，即"Q 弹回弹"。

**4. 丝滑无闪烁切换？**
轮换时新旧两张图做**交叉淡化**（新图叠在上层从 0 淡入到 1，完成后移除旧图），整个过程在渲染层内一次性完成，无黑屏闪烁。

**5. 低资源占用？**
形变只在按压/回弹动画期间运行渲染循环（`CompositionTarget.Rendering` 动态注册/注销）；空闲时不重绘，GPU 由 WPF 按需渲染，CPU 占用极低。

---

## 📝 注意事项

- **webp 支持**：WPF 本身不解码 webp，依赖系统 WIC 编解码器（Windows 10 22H2 及以上内置，或安装微软"WebP Image Extensions"）。缺编解码器时 webp 会自动跳过并写入日志，不影响其他格式。
- **DPI**：应用采用"系统 DPI 感知"，与桌面 Shell 一致，避免壁纸层缩放错位。
- **多显示器**：渲染窗口覆盖整体虚拟屏幕；跨区模式下单张壁纸横跨所有屏幕。
- **端口/权限**：普通用户权限即可，无需管理员。
- **杀软**：全局鼠标钩子是常见技术，个别杀软可能误报，可加入白名单。
- 若设置面板关闭后想再次打开：双击托盘图标或托盘右键 → 打开设置面板。
- 修改壁纸文件夹后，设置保存即自动重新加载该文件夹。

---

## 🏷️ 版本

- v1.0.0：首个正式版。

## 📄 License

仅供学习交流使用。
