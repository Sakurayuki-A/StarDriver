# Star Driver

> StarDriver 一款现代化的 PSO2：NGS 日服启动器专门为 PSO2：NGS 游戏本体下载和启动而制作的项目，支持智能多线程下载（28并发）、MD5完整性验证、增量更新和文件缓存。采用 .NET 8.0 + WPF 开发，提供流畅高级的深色主题UI。

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![Made with Love](https://img.shields.io/badge/Made%20with-❤-ff69b4.svg)](https://github.com/Sakurayuki-A/StarDriver)
[![Developer](https://img.shields.io/badge/Developer-Sakurayuki--A-blue.svg)](https://github.com/Sakurayuki-A)

> ⚡ 本项目由 [Sakurayuki-A](https://github.com/Sakurayuki-A) 开发，为爱发电，完全免费开源

## ✨ 功能特性

### 核心功能
- 🎮 **游戏启动** - 支持 XignCode 反作弊系统，一键启动 PSO2:NGS
- 📥 **完整下载** - NGS 完整游戏资源下载（约 90GB）
- 💾 **智能缓存** - JSON 文件缓存，加速二次扫描

### 性能优化
- 🚀 **智能线程池** - 28 线程并发（大文件 16 + 中等 6 + 小文件 6）
- ⚡ **动态负载均衡** - 空闲线程自动调配，性能提升 40%
- 📊 **实时监控** - 下载速度、活跃任务数、进度百分比
- 🔧 **连接健康监控** - 自动检测网络错误，智能重试

### 用户体验
- 🌙 **现代化 UI** - 深色主题，流畅动画过渡
- 🌍 **多语言支持** - 中文/英文界面
- 🎯 **首次运行向导** - 语言选择、安装路径配置
- 📝 **实时日志** - 详细的下载日志和错误提示

## 🛠️ 技术栈

- **.NET 8.0** - 最新的 .NET 平台
- **WPF** - Windows Presentation Foundation UI 框架
- **C# 12.0** - 现代 C# 特性（nullable 引用类型、record、pattern matching）
- **异步编程** - async/await 模式，高性能 I/O
- **并发编程** - ConcurrentQueue、SemaphoreSlim、Task 并行

## 📁 项目结构

```
StarDriver/
├── StarDriver.Core/              # 核心数据模型层
│   ├── Models/
│   │   ├── PatchListItem.cs     # 补丁文件项
│   │   ├── DownloadItem.cs      # 下载任务项
│   │   ├── PSO2Version.cs       # 版本号结构
│   │   └── GameClientSelection.cs
│   └── GameLauncher.cs          # 游戏启动器
│
├── StarDriver.Downloader/        # 下载引擎层
│   ├── GameDownloadEngine.cs    # 主下载引擎（28线程）
│   ├── AdaptiveDownloadEngine.cs # 智能线程池调配
│   ├── ConnectionHealthMonitor.cs # 连接健康监控
│   ├── FileHashCache.cs         # JSON 文件缓存
│   └── DownloadNode.cs          # 下载节点
│
├── StarDriver.UI/                # WPF 用户界面层
│   ├── Views/
│   │   ├── WelcomePage.xaml     # 首次运行向导
│   │   ├── HomePage.xaml        # 主页
│   │   ├── DownloadsPage.xaml   # 下载页面
│   │   └── SettingsPage.xaml    # 设置页面
│   ├── ViewModels/              # MVVM 视图模型
│   ├── Controls/                # 自定义控件
│   ├── Resources/               # 资源文件（语言包）
│   ├── MainWindow.xaml          # 主窗口
│   └── AppSettings.cs           # 应用设置
│
├── docs/                         # 文档
│   ├── ReferenceImplementation.md
│   └── SmartThreadPooling.md
│
└── StarDriver.Test/              # 测试项目
```

## 核心优化

### 性能优化（v2.0 - 2024）
1. **HTTP 连接池优化**：
   - `MaxConnectionsPerServer = 16`（支持更多并发连接）
   - `PooledConnectionLifetime = 5分钟`（连接复用）
   - `PooledConnectionIdleTimeout = 2分钟`（及时释放空闲连接）

2. **缓冲区优化**：
   - 使用 `ArrayPool<byte>.Shared` 租用 64KB 缓冲区
   - 读取块大小增加到 128KB，提升吞吐量
   - 避免频繁 GC，减少内存分配

3. **进度报告节流**：
   - 下载进度：每 256KB 或每秒报告一次
   - 扫描进度：每 100 个文件报告一次
   - 降低 CPU 占用 20-30%

4. **并发控制优化**：
   - 默认并发数从 8 增加到 28
   - 最大支持 32 并发（可配置）
   - 扫描并发限制为 `ProcessorCount * 2`，避免磁盘 I/O 竞争

5. **增量哈希计算**：使用 `IncrementalHash` 边下载边计算 MD5，无需二次读取

6. **文件预分配**：使用 `File.OpenHandle` + `FileOptions.Asynchronous` 预分配磁盘空间

7. **并行 I/O**：写入和哈希计算并行执行，最大化吞吐量

### 性能提升
- **下载速度**: 提升 50-100%（取决于网络条件）
- **CPU 占用**: 降低 20-30%（进度报告节流）
- **稳定性**: 提升（扫描并发控制）

详细优化说明请参考 [PERFORMANCE_OPTIMIZATION.md](PERFORMANCE_OPTIMIZATION.md)

### 智能重试策略
- **4xx 错误**：立即停止，不重试（客户端错误）
- **5xx 错误**：延迟 1 秒后重试（服务器错误）
- **Socket 10054**：延迟 500ms 后重试（连接关闭）
- **MD5 错误**：延迟 500ms 后重试
- **无状态码网络错误**：立即停止（致命错误）

### 文件安全
- 临时文件使用 `.dtmp` 扩展名
- 下载完成后原子替换（`File.Move` with overwrite）
- 自动处理只读属性
- 失败时自动清理临时文件

### StarDriver.Core/Models
- `PatchListItem`: 补丁文件项，解析补丁列表，生成下载 URL
- `PatchRootInfo`: 补丁服务器配置（从 management_beta.txt 解析）
- `PSO2Version`: 版本号结构体
- `GameClientSelection`: 枚举（NGS_Full / Launcher_Only）
- `FileScanFlags`: 扫描标志（MissingOnly / MD5 / FileSize 等）
- `DownloadItem`: 下载任务项，含状态、进度、重试次数

### StarDriver.Downloader
- `PSO2HttpClient`: HTTP 客户端
  - 关键请求头：`User-Agent: AQUA_HTTP`, `Host`, `Cache-Control: no-cache`
  - `GetPatchRootInfoAsync()`: 获取服务器配置
  - `GetRemoteVersionAsync()`: 获取版本号
  - `GetPatchListNGSAsync()`: 获取 NGS 补丁列表
  - `OpenForDownloadAsync()`: 打开下载流

- `GameDownloadEngine`: 主引擎
  - 并发下载（默认 8 线程，使用 ConcurrentQueue）
  - 扫描 → 下载 → MD5 验证 → 保存缓存
  - 事件：ScanProgress / DownloadProgress / FileVerified / DownloadCompleted
  - 使用临时文件 .tmp，验证通过后原子替换

- `FileHashCache`: JSON 缓存
  - 存储文件 MD5 / 大小 / 修改时间
  - 加速扫描，避免重复计算哈希

## 🚀 快速开始

### 系统要求
- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- 至少 70GB 可用磁盘空间

### 下载运行
1. 从 [Releases](../../releases) 下载最新版本
2. 解压到任意目录
3. 运行 `StarDriver.exe`
4. 首次运行会引导你选择语言和安装路径

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/yourusername/StarDriver.git
cd StarDriver

# 构建项目
dotnet build

# 运行 UI
dotnet run --project StarDriver.UI

# 发布单文件版本
dotnet publish StarDriver.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## ⚙️ 配置说明

### 默认配置
- **安装路径**: 用户自定义（首次运行时选择）
- **游戏目录结构**: `{安装路径}\PHANTASYSTARONLINE2_JP\pso2_bin\`
- **服务器地址**: `http://patch01.pso2gs.net/patch_prod/patches/management_beta.txt`
- **缓存文件**: `{游戏目录}\StarDriver.cache.json`
- **并发下载数**: 28 线程（默认）

### 设置文件位置
- **应用设置**: `%LocalAppData%\StarDriver\settings.json`
- **首次运行标记**: `%LocalAppData%\StarDriver\StarDriver.firstrun`

### 性能调优建议

根据网络环境调整并发数：
- **高速网络** (>100Mbps): 32 并发
- **中速网络** (50-100Mbps): 28 并发（默认）
- **低速网络** (<50Mbps): 8 并发

```csharp
var engine = new GameDownloadEngine(baseDir);
engine.ConcurrentDownloads = 28; // 调整并发数
```

## 📂 游戏目录结构

```
用户选择的安装目录/
└── PHANTASYSTARONLINE2_JP/
    └── pso2_bin/
        ├── data/                    # 游戏数据文件（约 90GB）
        │   ├── win32/
        │   ├── win32reboot/         # NGS 数据
        │   └── ...
        ├── sub/                     # XignCode 反作弊
        │   ├── pso2.exe            # 游戏主程序
        │   └── ucldr_PSO2_JP_loader_x64.exe
        ├── pso2launcher.exe         # 官方启动器
        └── StarDriver.cache.json    # StarDriver 文件缓存
```

## 📸 截图

> TODO: 添加应用截图

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！本项目为爱发电，期待你的参与。

### 开发指南
1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

### 支持项目
如果这个项目对你有帮助，欢迎：
- ⭐ 给项目点个 Star
- 🐛 提交 Bug 报告和功能建议
- 📖 完善文档和翻译
- 💻 贡献代码

## 📝 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件

## �‍💻 开发者

本项目由 [Sakurayuki-A](https://github.com/Sakurayuki-A) 独立开发并维护。

## 🙏 致谢

- 灵感来源于 [PSO2-Launcher-CSharp](https://github.com/Leayal/PSO2-Launcher-CSharp)
- 感谢 SEGA 开发的 PSO2:NGS 游戏
- 感谢所有为本项目贡献的开发者和用户

## 💖 为爱发电

本项目完全免费开源，由开发者利用业余时间维护。如果你觉得这个项目对你有帮助，可以通过以下方式支持：

- ⭐ 给项目点个 Star，让更多人看到
- 📢 分享给需要的朋友
- 🐛 反馈 Bug 和提出改进建议
- 💻 贡献代码和文档

你的支持是我们持续更新的动力！

## ⚠️ 免责声明

- 本项目为爱发电，完全免费开源，仅供学习交流使用
- 不用于任何商业目的，不接受任何形式的付费服务
- PSO2 和 PSO2:NGS 是 SEGA 的注册商标，本项目与 SEGA 官方无关
- 使用本软件产生的任何问题，开发者不承担责任
- 请支持正版游戏，遵守游戏服务条款

## 📧 联系方式

- **开发者**: [Sakurayuki-A](https://github.com/Sakurayuki-A)
- **问题反馈**: [Issues](../../issues)
- **项目主页**: [GitHub](https://github.com/Sakurayuki-A/StarDriver)

