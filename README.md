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
pack-release.bat 0.1.14 stable
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
├─ XantaComfyLauncher-0.1.14-stable-full.nupkg
├─ XantaComfyLauncher-0.1.14-stable-delta.nupkg   （有上一版本产物时）
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
