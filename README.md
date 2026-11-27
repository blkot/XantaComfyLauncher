# XantaComfyLauncher

轻量级 Windows 启动器，用于管理指定的 ComfyUI 实例。

启动器自身与 ComfyUI 实例是两个独立的更新域：

- **启动器**：由 Velopack 安装和更新，只包含 `XantaComfyLauncher` 程序文件。
- **ComfyUI 实例**：仍由启动器内的版本切换、依赖同步和自定义节点管理功能维护。

安装包不会包含 ComfyUI、Python venv、模型或用户工作流。

## 安装与更新架构

```text
Setup.exe / 更新源
        ↓
%LOCALAPPDATA%\Programs\XantaComfyLauncher\   启动器程序目录
        ↓
%LOCALAPPDATA%\XantaComfyLauncher\           启动器配置、日志、更新状态
        ↓
用户指定的 ComfyUI 实例路径                   ComfyUI / .venv / models / nodes
```

关键规则：

- 启动器配置不写入安装目录，避免升级覆盖和 UAC 问题。
- 更新启动器前必须停止 ComfyUI。
- 启动器更新失败不能影响已有安装和 ComfyUI 实例。
- 卸载启动器默认不删除 ComfyUI 实例。
- 启动器的稳定版/Beta 通道与 ComfyUI 的预发布版本选项相互独立。

## 配置位置

默认位置：

```text
%LOCALAPPDATA%\XantaComfyLauncher\config.json
```

旧版 `launcher-config.json` 如果位于程序目录或常见开发目录，会在首次启动时尝试迁移。

测试和便携场景可以使用环境变量覆盖：

```text
XANTA_LAUNCHER_CONFIG
XANTA_LAUNCHER_DATA_DIR
XANTA_LAUNCHER_UPDATE_FEED
XANTA_LAUNCHER_GITHUB_TOKEN
```

为兼容旧版本，仍接受 `COMFY_LAUNCHER_CONFIG`、`COMFY_LAUNCHER_DATA_DIR` 和
`COMFY_LAUNCHER_UPDATE_FEED`。

配置示例：

```json
{
  "InstanceRoot": "H:\\Work\\Personal\\AIProjects\\ComfyUI_instances\\ComfyUI_261114",
  "Port": 8188,
  "ListenMode": "Lan",
  "UseSageAttention": false,
  "UseFp16Accumulation": false,
  "AutoOpenBrowser": true,
  "UpdateDependenciesOnSwitch": true,
  "IncludePrerelease": false,
  "ExtraArguments": "",
  "LauncherUpdateChannel": "stable",
  "CheckLauncherUpdatesOnStartup": true,
  "UpdateFeedUrl": "",
  "SkippedLauncherVersion": ""
}
```

界面中修改实例路径后，点击 **应用路径** 才会重新绑定实例并写回配置。

## 功能

- 显式指定 / 浏览 / 切换 ComfyUI 实例路径
- 启动 / 停止 ComfyUI，并管理完整进程树
- 仅本机 `127.0.0.1` / 局域网 `0.0.0.0` 监听切换
- 普通模式 / SageAttention 模式
- FP16 累加加速选项，对应 `--fast fp16_accumulation`
- 端口设置、额外启动参数
- 实时日志，限制内存中的日志长度
- 环境信息：ComfyUI、提交、Python、torch、SageAttention、Triton、GPU
- ComfyUI 版本只从远程 **Git tags** 获取，不再请求 GitHub Releases
- ComfyUI 有新 tag 时，版本管理界面用绿色文字提示可更新
- 按 tag 切换 ComfyUI 版本
- 切换前检查已跟踪文件修改
- 可选同步 `requirements.txt` 依赖
- 失败自动回滚
- 手动回滚到上一版本
- 启动器自身通过 Velopack 检查更新、下载并重启安装
- 不导入 PyTorch，不创建 CUDA 上下文，不占用显存

## 开发运行

```bat
dotnet run --project XantaComfyLauncher.csproj
```

## 构建

安装包使用的发布产物（self-contained、多文件，不要求目标机器安装 .NET）：

```bat
build.bat
```

输出：

```text
artifacts\publish\XantaComfyLauncher.exe
```

生成安装包和更新源文件：

```bat
dotnet tool install --global vpk
pack-release.bat 0.1.26 stable
```

会同时生成：

- `XantaComfyLauncher-Installer.exe`：**推荐**。启动后先选择安装目录，
  再调用 Velopack 安装到该目录，后续自动更新仍然可用。
- `Setup.exe`：Velopack 原生的一键安装，默认安装到
  `%LOCALAPPDATA%\XantaComfyLauncher`，不提供路径选择。

如果你不想让启动器自动装到 C 盘，请使用 `XantaComfyLauncher-Installer.exe`。

输出：

```text
artifacts\releases\
├─ XantaComfyLauncher-stable-Setup.exe
├─ XantaComfyLauncher-Installer.exe
├─ XantaComfyLauncher-stable-Setup.exe
├─ XantaComfyLauncher-0.1.26-stable-full.nupkg
├─ XantaComfyLauncher-0.1.26-stable-delta.nupkg   （有上一版本产物时）
├─ XantaComfyLauncher-stable-Portable.zip
├─ RELEASES-stable
├─ releases.stable.json
└─ assets.stable.json
```

把 `artifacts\releases\` 的完整内容上传到更新源。首次安装推荐：

- `XantaComfyLauncher-Installer.exe`：可选择安装目录。
- `XantaComfyLauncher-stable-Setup.exe`：一键安装到 `%LOCALAPPDATA%`，不推荐给
  不希望安装到 C 盘的用户。

已安装用户无论使用哪种安装包，都由启动器通过
`releases.stable.json` / `RELEASES-stable` 自动更新。

Delta 包需要上一版本的发布产物。CI 应保留历史 release 文件，否则只能生成 full 包。

便携单文件版本：

```bat
build-portable.bat
```

框架依赖版本（目标机器需要 .NET Desktop Runtime 9）：

```bat
build-framework-dependent.bat
```

## 启动器更新源

默认使用 GitHub 仓库：

```text
https://github.com/blkot/XantaComfyLauncher
```

私有仓库不要把访问令牌打包进客户端。若 `--check-launcher-update` 返回 404，
说明匿名客户端无法读取私有仓库的 Releases，需要：

- 把 `artifacts\releases\` 上传到公开只读静态源并设置 `XANTA_LAUNCHER_UPDATE_FEED`；或
- 仅在本机开发测试时设置只读的 `XANTA_LAUNCHER_GITHUB_TOKEN`。

也可以在配置或环境变量中指定静态更新源：

```text
XANTA_LAUNCHER_UPDATE_FEED=https://your-static-host.example.com/xanta/stable
```

公开仓库可以直接用 GitHub Releases；私有仓库不要把 PAT 放进客户端，建议把
Velopack 更新清单和安装包上传到公开只读的静态存储（R2/S3/Azure Blob/自己的 HTTPS 目录）。

## 无界面测试

环境、Git、tag、端口自检：

```bat
XantaComfyLauncher.exe --self-test
```

也可以临时指定实例：

```bat
XantaComfyLauncher.exe --self-test --root "H:\path\to\instance"
```

结果：

```text
<instance>\.launcher\self-test.json
```

检查启动器自身是否有更新（需要安装版）：

```bat
XantaComfyLauncher.exe --check-launcher-update
```

结果写入：

```text
%LOCALAPPDATA%\XantaComfyLauncher\update-check.json
```

也可以用 `XANTA_LAUNCHER_UPDATE_FEED` 临时指向测试更新源：

```bat
set XANTA_LAUNCHER_UPDATE_FEED=H:\path\to\artifacts\releases
XantaComfyLauncher.exe --check-launcher-update
```

测试完整下载、应用和重启流程：

```bat
XantaComfyLauncher.exe --apply-launcher-update
```

`--apply-launcher-update` 会下载更新并重启启动器，重启后自动执行一次
`--check-launcher-update`。该命令主要用于安装测试，不建议在 ComfyUI 运行期间使用。

实际启动 ComfyUI、等待健康检查并停止：

```bat
XantaComfyLauncher.exe --smoke-test
```

结果：

```text
<instance>\.launcher\smoke-test.json
```

## 自定义节点管理

主界面 **节点管理** 按钮打开独立窗口。窗口内的 **从 GitHub 安装** 按钮
可以按 GitHub 仓库直接安装节点。

支持输入：

- 完整 URL：`https://github.com/kijai/ComfyUI-KJNodes`
- SSH URL：`git@github.com:kijai/ComfyUI-KJNodes.git`
- `author/repo`：`kijai/ComfyUI-KJNodes`
- 关键词：`KJNodes`

URL 和 `author/repo` 会直接定位仓库；关键词会调用 GitHub Search API，
显示匹配仓库列表，选择后点击 **安装选中节点**。安装复用现有 manifest、
git clone、requirements 自动检测和 pip 依赖安装流程。

GitHub 匿名 API 有速率限制。需要提高限额或访问私有仓库时，可设置：

```text
XANTA_LAUNCHER_GITHUB_TOKEN
```

图标资源位于：

```text
Assets\XantaComfyLauncher.ico
```

主程序、安装器和 Setup 快捷方式都使用该图标。

## WPF Desktop

仓库现在拆分为共享 Core、WinForms 回退版和 WPF 正式桌面版：

```text
XantaComfyLauncher.Core      业务服务、模型、进程/队列/GPU/节点/更新
XantaComfyLauncher           WinForms 默认 UI
XantaComfyLauncher.Wpf       WPF 实验 UI（随包放在 wpf\ 子目录）
```

WinForms 继续作为默认入口，WPF 保留为可选实验界面：

```bat
dotnet run --project XantaComfyLauncher.Wpf\XantaComfyLauncher.Wpf.csproj
```

当前 WPF 实验界面已迁移：

- WPF-UI 深色 Fluent 主题
- 左侧导航
- 实例启动/停止/打开界面
- GPU 实时仪表盘
- WPF 队列页面：队列列表、中断/清空/删除、WebSocket 实时进度
- WPF 节点管理：远程检查、更新、安装缺失、GitHub 安装、冲突检测
- WPF 工作流 Profile：分析、持久化、启动配置、始终启用包、节点索引刷新
- WPF 模型路径：路径校验、YAML 编辑、Junction、目录迁移
- WPF 设置页：实例、启动参数、更新、托盘和显存预警
- WPF 内部托盘实现
- GitHub 节点搜索安装和冲突检测 WPF 页面
- WinForms 主界面可通过“WPF 界面”按钮启动可选 WPF 实验界面
- WPF Settings 可切回根目录的 WinForms 默认界面

Velopack 发布入口为 WinForms 主程序，WPF 作为可选子程序分发。

## 按工作流加载节点

主界面的 **工作流加载** 按钮打开 Profile 管理：

- 选择 `ComfyUI/user/default/workflows` 或实例 `workflows` 下的 JSON
- **分析并保存**：解析 UI/API 工作流，生成持久化 Profile
- **刷新节点索引**：通过运行中的 ComfyUI `/object_info` 建立节点类型到插件包的映射
- **设为启动 Profile**：启动 ComfyUI 时使用：
  `--disable-all-custom-nodes --whitelist-custom-nodes ...`
- **启动时全部加载**：恢复原有行为
- 可勾选 **始终启用的插件包**（UI、Manager、纯前端扩展等）

持久化文件：

```text
<instance>/.launcher/workflow-profiles.json
<instance>/.launcher/node-package-map.json
```

解析会同时考虑：

- 节点的 `class_type` / UI `type`
- `properties.cnr_id` / `aux_id`
- 后端 `/object_info.python_module = custom_nodes.<包名>`
- `WEB_DIRECTORY` / 前端扩展的 `cnr_id` 包映射

刷新节点索引时请先用 **启动时全部加载** 模式启动 ComfyUI，否则索引只会包含当前白名单里的包。

## 运维增强

- 系统托盘：最小化到托盘、托盘启动/停止/打开界面/队列、开机启动、意外退出自动重启
- 通知：ComfyUI 就绪、启动失败、意外退出、启动器更新可用/已下载
- 队列窗口 WebSocket 实时进度：当前节点、class_type、进度、节点耗时、ETA、执行错误
- GPU/VRAM 监控：独立可视化仪表盘（使用率/显存进度条、温度色块、ComfyUI 进程显存）；启动前低显存预警
- 托盘悬浮提示：队列运行/等待数量、显存占用、GPU 温度
- 节点冲突检测：重复插件目录、重复 `NODE_CLASS_MAPPINGS` 节点类型、`pip check` 依赖问题
- 模型路径管理：路径校验、YAML 原文编辑与自动备份、Junction 创建、同卷/跨卷安全迁移
- 启动器更新：stable/beta 通道、自动下载但不自动安装、已下载更新延迟安装、release notes、更新前配置备份与损坏恢复

## 日志

启动器和 ComfyUI 运行时日志分开保存：

```text
%LOCALAPPDATA%\XantaComfyLauncher\logs\launcher.log
<instance>\logs\comfyui-YYYYMMDD-HHmmss-fff.log
```

- `launcher.log`：启动器启动、配置、节点安装、GitHub 操作、更新检查等。
- `comfyui-*.log`：每次启动 ComfyUI 的 stdout/stderr 完整日志，带时间戳和流标签。
- 主界面日志工具栏可以直接打开两个日志目录。
- Git 进度和 pip 警告不再被误标成 `err`；只有进程失败时才抛出错误。

节点清单：

```text
<instance>\.launcher\custom-nodes.json
```

支持：

- **安装缺失**：安装清单中目录不存在的节点
- **检查更新**：比较本地提交与远程分支 HEAD，并显示远端最新 tag
- **更新到最新提交**：沿当前跟踪分支更新
- **更新到最新 Tag**：仓库存在 tag 时检出最新 tag，适合只跟进稳定版本的节点
- **更新选中 / 全部更新**：工具栏的“更新到”下拉框决定使用最新提交还是最新 Tag
- 有已跟踪文件修改时阻止更新
- 更新后 requirements 变化时自动执行 pip install
- 不执行 `git clean`
- 更新完成后提示重启 ComfyUI

节点窗口打开时会优先显示本次启动器运行期间缓存的节点状态，不会每次自动远程检查。
点击 **检查更新** 后在后台读取本地状态和远程提交/tag，列表和窗口仍可操作。

新增 **队列管理**：ComfyUI 运行时可以查看 running / pending 队列，支持中断当前任务、
清空等待队列和删除选中等待项，队列每 2 秒自动刷新。

主界面还提供工作流、插件、模型、输入、输出目录快捷按钮：

```text
ComfyUI/user/default/workflows
ComfyUI/custom_nodes
ComfyUI/models
ComfyUI/input
ComfyUI/output
```

“模型”按钮会弹出目录菜单，包含：

- 默认模型目录 `ComfyUI/models`
- `extra_model_paths.yaml` 中实际存在的模型目录（按类别显示）
- 上次打开的自定义模型目录
- **选择其他模型目录...**
- **编辑 extra_model_paths.yaml**

上次选择的模型目录会保存到启动器配置中。

无界面检查：

```bat
XantaComfyLauncher.exe --nodes-check
```

单节点更新：

```bat
XantaComfyLauncher.exe --nodes-update ComfyUI-KJNodes
```

## 版本切换安全性

- 只接受版本 tag，例如 `v0.45.3`
- 不执行任意 commit 或分支切换
- 切换前阻止存在已跟踪文件修改的仓库
- 不使用 `git clean`
- 不触碰 `models/`、`output/`、`input/`、`user/` 和 `extra_model_paths.yaml`
- 切换前保存当前 commit、requirements 哈希和 pip freeze 快照
- 依赖安装或启动自检失败时自动回滚
- 自定义节点与旧版本 ComfyUI 的兼容性仍需自行验证

## 实例内数据

```text
<instance>/.launcher/
├─ version-state.json
├─ custom-nodes.json
├─ cache/
└─ snapshots/*-pip-freeze.txt
```

## 发布检查清单

1. 确认当前版本号（`XantaComfyLauncher.csproj` 的 `Version` 是默认值）。
2. `build.bat` 通过。
3. `pack-release.bat <version> stable` 生成 Setup 和更新包；脚本会用该版本覆盖编译版本。
4. 在干净用户环境测试首次安装。
5. 从上一版本测试在线更新，确认配置和实例路径保留。
6. 测试 ComfyUI 运行中点击更新时的保护逻辑。
7. 保留上一版本发布产物以生成 delta 包。
8. 正式分发前对 exe/Setup 做 Authenticode 签名。

## GPUI 可选界面（实验）

`v0.1.21` 之后，发布包结构为：

```text
XantaComfyLauncher.exe              WinForms 默认
wpf\XantaComfyLauncher.Wpf.exe      WPF 可选
gpui\XantaComfyLauncher.Gpui.exe    Rust + GPUI 可选
gpui\corehost\                      C# CoreHost
```

WinForms 主界面通过“GPUI 界面”按钮启动实验性 Rust + GPUI 前端。

构建 GPUI 需要：

- Rust 1.100.0 或更新
- Visual Studio 2022 C++ Build Tools
- Windows 10 SDK

`build.bat` 会自动构建 CoreHost 和 GPUI。GPUI 源码和兼容性说明：

```text
spike/gpui-phase0/
```

GPUI 当前仍是实验界面，不替代 WinForms 默认入口。

## GitHub Token 配置

节点搜索使用 GitHub REST API。未配置 Token 时容易触发速率限制。

WinForms 设置页：

```text
设置 → GitHub Token
```

操作：

1. 点击“创建 GitHub Token”
2. 创建 classic token，公开仓库搜索不需要 scope
3. 也可以使用 fine-grained token，授予 `Public Repositories: Read-only`
4. 粘贴到输入框，点击“保存 Token”
5. 状态栏显示“当前来源：Windows 凭据管理器”即生效

安全说明：

- Token 保存在 Windows 凭据管理器，并同时供节点搜索和启动器更新检查使用
- 不写入 `config.json`
- 不写入发布包或日志
- 支持环境变量回退：
  - `XANTA_LAUNCHER_GITHUB_TOKEN`
  - `GITHUB_TOKEN`

速率限制参考：

```text
未认证 Search API：约 10 次/分钟
已认证 Search API：约 30 次/分钟
未认证 Core API：60 次/小时
已认证 Core API：5000 次/小时
```

## WinForms 界面主题与日志布局

设置页提供：

```text
设置 → 深色模式（重启后生效）
```

当前实现使用 WinForms 的深色模式基础支持，并对自定义面板、输入框、列表、按钮和日志区域应用深色配色。切换后需要重启启动器。

日志区域工具栏提供：

```text
扩展日志 / 恢复默认布局
```

- 扩展日志：隐藏启动器实例、启动设置、环境信息和系统状态区域，让日志占满主内容区。
- 恢复默认布局：恢复固定高度的上部区域和自适应日志区域。
