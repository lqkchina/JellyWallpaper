# JellyWallpaper 果冻壁纸

Windows 10 桌面壁纸增强工具：**点击桌面时，壁纸会像果冻一样在按压点向内凹陷，松开后平滑回弹**。果冻作用在 **Windows 系统当前壁纸**上，支持多种显示模式与背景填充色，完全不影响桌面图标的正常操作。

- 语言/框架：C# · WPF（.NET 8）
- 目标系统：Windows 10（x64）
- 交付形态：**单文件自包含 EXE，双击即可运行，无需安装、无需 .NET 运行时**
- CI：GitHub Actions 自动构建 + 发布

---

## ✨ 核心特性

| 特性 | 说明 |
|---|---|
| 果冻形变 | 鼠标左键在桌面按下 → 按压点向内凹陷（高斯型平滑衰减，**无圆环波纹**）；松开 → 阻尼弹簧平滑回弹（带轻微过冲，Q 弹手感）。**纯 CPU 渲染，不依赖 GPU**，虚拟机/远程桌面也正常 |
| 不干扰图标 | 渲染层挂在**桌面壁纸层（WorkerW）之下**，透明、不参与命中测试；图标选中/拖拽/新建/删除、右键菜单全部照常 |
| 系统壁纸 | 直接读取 **Windows 系统当前壁纸** 作为果冻作用纹理（无需设置文件夹，Windows 换壁纸后重启应用即跟随） |
| 显示模式 | 填充 / 适应 / 拉伸 / 平铺 / 跨区（多显示器扩展）/ 居中，兼容 Windows 原生壁纸设置习惯 |
| 背景填充色 | 自定义适应/居中模式的空白区颜色 |
| 参数可调 | 形变强度、回弹速度、按压衰减、渲染分辨率、显示模式、填充色，全部**实时预览** |
| 托盘运行 | 默认最小化到系统托盘；菜单含 打开设置 / 重启 / 退出 |
| 版本与日志 | 设置面板显示版本号；全局异常捕获，错误写日志 + 面板展示，日志存程序同目录 `logs\` |
| 开机自启 | 可选，写当前用户注册表 Run 项，无需管理员 |
| 低资源占用 | GPU 像素着色器渲染形变；空闲时停止渲染循环，仅动画期间耗帧 |

---

## 🚀 快速开始

### 1. 直接使用打包好的 EXE（推荐）

从 GitHub Releases 下载 `JellyWallpaper.exe`，**双击运行**即可。启动后自动进入系统托盘（右下角图标），壁纸层立即生效——**无需任何配置**，它会自动读取 Windows 系统当前壁纸作为果冻作用画面。

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
| 重启程序 | 退出后重新启动本程序 |
| 退出程序 | 退出，**立即恢复系统原始桌面壁纸**，不留渲染残留 |

---

## ⚙️ 参数说明（设置面板）

| 参数 | 取值范围 | 说明 |
|---|---|---|
| 显示模式 | 填充/适应/拉伸/平铺/跨区/居中 | 见下方"显示模式" |
| 背景填充色 | 色值 `#RRGGBB` | 适应/居中模式下空白区域的颜色 |
| 形变强度 | 0~3 | 凹陷/回弹的位移幅度 |
| 回弹速度 | 20~300 | 弹簧刚度，越大回弹越快 |
| 按压衰减系数 | 2~40 | 阻尼，越小越 Q（过冲越明显） |
| 形变渲染分辨率 | 原图/高/中/低 | 壁纸解码长边上限，控制显存/内存占用 |
| 开机自动启动 | 开/关 | 写入当前用户注册表 Run 项 |

> 所有修改即时生效（实时预览），关闭窗口后自动保存到程序同目录 `settings.json`（目录不可写时存 `%AppData%\JellyWallpaper`）。
> 壁纸来源固定为 **Windows 系统当前壁纸**：应用启动时读取一次。若要更换果冻作用的画面，直接在 Windows 设置里换壁纸后重启应用即可。

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
   ├─ AppController.cs             # 编排：渲染层/壁纸/钩子/托盘/设置
   ├─ WallpaperLayerWindow.cs      # 渲染层（桌面图标层之下 + 果冻动画）
   ├─ Effects/JellyEffect.cs       # (v1.1.1 起弃用，未使用；原 GPU 着色器封装)
   ├─ Shaders/Jelly.fx             # (v1.1.1 起弃用，未使用；原 HLSL 着色器)
   ├─ Core/
   │  ├─ NativeMethods.cs          # Win32 P/Invoke
   │  ├─ DesktopLayerLocator.cs    # 定位"壁纸 WorkerW"（图标层之下）
   │  ├─ MouseHook.cs              # 全局低级鼠标钩子（只观察、不拦截）
   │  ├─ WallpaperManager.cs       # 系统壁纸读取/解码（v1.1.0 起不再轮换）
   │  ├─ CpuJellyRenderer.cs       # CPU 果冻形变渲染器（不依赖 GPU）
   │  ├─ JellyAnimator.cs          # 阻尼弹簧回弹动画器
   │  ├─ HotkeyManager.cs          # (v1.1.0 起弃用，未使用)
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
用**纯 CPU 渲染**（`CpuJellyRenderer`）：把壁纸按显示模式绘制成一张基图，再对输出每个像素计算到按压点的归一化距离 → **高斯型衰减**（无振荡 → 绝无"圆环波纹"）→ 向按压点方向偏移采样 → 双线性插值，形成向内凹陷；配合按压阴影增强立体感。形变深度由**阻尼弹簧动画器**驱动（`JellyAnimator`），按下 0→1、松开 1→0 且欠阻尼带过冲，即"Q 弹回弹"。
> 为什么不用 GPU 像素着色器（ShaderEffect）？因为 WPF 的 ShaderEffect 依赖硬件 GPU 加速，在虚拟机、远程桌面等无 GPU 环境会把整个壁纸层渲染成**全黑**。改用 CPU 逐像素重采样后任何环境都能工作，且**空闲时不重算、零 CPU 占用**，只在按压/回弹的短短几百毫秒内重绘。

**4. 系统壁纸从哪来？**
启动时按顺序查找系统壁纸文件：① `%AppData%\Microsoft\Windows\Themes\TranscodedWallpaper`（Windows 实际显示的壁纸，含多显示器拼接，最可靠）→ ② 注册表 `Control Panel\Desktop\WallPaper`。解码后作为果冻纹理显示在渲染层。若桌面是纯色/渐变（无图片文件），则渲染层仅显示背景填充色，此时无可见形变纹理。

**5. 低资源占用？**
形变只在按压/回弹动画期间运行渲染循环（`CompositionTarget.Rendering` 动态注册/注销）；空闲时不重绘，GPU 由 WPF 按需渲染，CPU 占用极低。

---

## 📝 注意事项

- **系统壁纸是纯色/渐变**：桌面没有图片文件时，渲染层仅显示背景填充色，此时点击桌面看不到形变（没有可形变的纹理）。换一张图片壁纸即可。
- **DPI**：应用采用"系统 DPI 感知"，与桌面 Shell 一致，避免壁纸层缩放错位。
- **多显示器**：渲染窗口覆盖整体虚拟屏幕；跨区模式下单张壁纸横跨所有屏幕。
- **端口/权限**：普通用户权限即可，无需管理员。
- **杀软**：全局鼠标钩子是常见技术，个别杀软可能误报，可加入白名单。
- 若设置面板关闭后想再次打开：双击托盘图标或托盘右键 → 打开设置面板。
- 应用启动时读取一次系统壁纸；Windows 里换了壁纸后，**重启应用**即可让果冻作用在新壁纸上。

---

## 🏷️ 版本

- v1.1.1：**修复壁纸层全黑** —— 果冻形变从 GPU 像素着色器（ShaderEffect）改为**纯 CPU 渲染**（WriteableBitmap 逐像素高斯凹陷）。原 ShaderEffect 在无 GPU 硬件加速的环境（虚拟机/远程桌面/部分驱动）会把整个壁纸层渲染成黑色，现已彻底解决，任何环境都能显示壁纸并正常果冻。
- v1.1.0：**去掉换壁纸功能**（文件夹轮换 / 手动切换 / 全局快捷键）——果冻改为作用在 **Windows 系统当前壁纸**上，启动即读一次，无需配置。
- v1.0.2：修复桌面壁纸层不显示 —— 渲染层改为不透明窗口（WPF 透明窗口挂到桌面子窗口在 Win10 上会渲染空白），并增强挂载日志。
- v1.0.1：修复 GitHub Actions 打包失败 —— 着色器编译输出目录不存在导致 CS1566；编译前自动创建目录。
- v1.0.0：首个正式版。

## 📄 License

仅供学习交流使用。
