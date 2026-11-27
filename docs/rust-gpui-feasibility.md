# Rust + GPUI 作为可选 GUI 的可行性研究

> 调研日期：2026-11-24  
> 分支：`research/rust-gpui-feasibility`  
> 状态：仅调研，不包含实现代码  
> 前提：现有 WinForms GUI 必须保持可用，Rust + GPUI 只能作为新增的可选前端

## 1. 结论摘要

**结论：有条件可行，但不应现在替换现有 GUI。**

推荐路线：

```text
WinForms 继续作为默认主界面
WPF 继续作为可选实验界面
新增 gpui\XantaComfyLauncher.Gpui.exe 作为第三个可选前端
Rust UI 不直接重写业务层，而是通过本地控制协议调用现有 C# Core
```

可行性判断：

| 项目 | 判断 |
|---|---|
| GPUI 能否在 Windows 上运行 | 可以，Zed 已有 Windows 后端 |
| GPUI 是否适合作为稳定主 UI | 暂不适合，仍是 pre-1.0，Windows 平台问题仍在修 |
| 能否复用现有 C# Core | 不能直接引用，需要 IPC / 控制进程 |
| 是否值得做技术验证 | 值得，建议先做 3–5 天的隔离 POC |
| 是否现在全量重写 | 不建议 |
| 对现有 GUI 的风险 | 可控，前提是 WinForms 保持默认、GPUI 独立进程、失败可回退 |

推荐先做 **隔离 POC**，通过以下门槛后再考虑 MVP：

1. Windows 上能稳定编译和启动 GPUI 窗口。
2. 中文显示和中文输入法可用。
3. 能通过本地 IPC 控制 ComfyUI 启停并接收日志。
4. 能作为 `gpui\` 子目录随包分发。
5. GPUI 崩溃不会影响 WinForms 默认启动。

## 2. 范围与约束

### 2.1 必须保持

- `XantaComfyLauncher.exe` 仍是 WinForms 默认主程序。
- WinForms 的现有功能不因新增 GPUI 而改变。
- WPF 仍保留为 `wpf\` 下的可选界面。
- GPUI 只能通过 WinForms 的显式入口启动。
- GPUI 启动失败时必须能回到 WinForms。
- Velopack 主程序仍是 WinForms。
- 不修改当前 ComfyUI 实例和用户配置格式。

### 2.2 本期不做

- 不把 C# Core 重写成 Rust。
- 不把 GPUI 设为默认启动界面。
- 不在 WinForms 中引入 Rust 原生库。
- 不改变现有安装目录结构和更新通道。
- 不实现完整 GPUI 功能页面。

## 3. 当前架构基线

当前仓库结构：

```text
XantaComfyLauncher.Core
  Models/
  Services/
  Utils/

XantaComfyLauncher
  WinForms 默认 UI
  直接引用 Core

XantaComfyLauncher.Wpf
  WPF 可选 UI
  只引用 Core
```

Core 已经包含启动器的主要业务能力：

```text
LauncherConfigService
ComfyProcessService
EnvironmentProbeService
ComfyApiService
ComfyWebSocketService
GpuMonitorService
WorkflowProfileService
ModelPathManagementService
CustomNodeManager
CustomNodeStateCache
GitHubNodeSearchService
NodeConflictService
ComfyVersionService
ComfyVersionSwitcher
LauncherUpdateService
WindowsStartupService
```

这意味着：

- 业务逻辑不应该在 Rust 中复制一份。
- Rust 前端如果直接调用 Git、Python、ComfyUI API、文件系统，会形成第二套行为，长期必然分叉。
- 正确方向是让 C# Core 继续做唯一业务层，Rust 只做视图和交互。

当前 UI 规模：

```text
MainForm.cs                 约 2050 行
WPF ViewModels              约 1800 行
Core                        约 5000 行
```

结论：Rust 前端不是“写几个窗口”就能完成，必须先把控制边界设计清楚。

## 4. GPUI 技术现状

### 4.1 发布状态

调研时 GPUI 在 crates.io 的版本为：

```text
gpui 0.2.2
```

特点：

- 版本仍为 `0.x`，API 没有稳定承诺。
- Zed 主仓库更新非常活跃。
- Zed 主仓库当前使用 Rust 1.100.0 工具链。
- GPUI crate 声明许可证为 Apache-2.0。
- Zed 仓库整体还包含 GPL 代码，若复用 Zed 内部 UI 组件需要单独做法务确认。

### 4.2 Windows 支持

Zed 主仓库已经有 Windows 平台层，实际存在：

```text
crates/gpui_windows/
  directx_devices.rs
  directx_renderer.rs
  directx_atlas.rs
  direct_write.rs
  direct_manipulation.rs
  dialog.rs
  system_notifications.rs
  window.rs
  keyboard.rs
```

说明 Windows 后端不是纸面支持，已经包含：

- DirectX 渲染
- DirectWrite 文本
- DirectManipulation
- 文件对话框
- 系统通知
- 键盘和窗口管理

但调研时仍能看到若干 Windows 专项未关闭问题，例如：

- 窗口关闭时的 teardown race
- IME 输入法取消组合问题
- 系统通知期间的 `RefCell already borrowed` 风险
- 最小化窗口持续重绘
- 高 DPI / DirectComposition 相关问题

这些不是致命问题，但说明 GPUI 在 Windows 上还不是“零风险桌面框架”。

### 4.3 平台 crate 的发布风险

Zed 主仓库现在已经拆出：

```text
gpui_platform
gpui_windows
gpui_linux
gpui_macos
```

调研时这些平台 crate 没有在 crates.io 上查到独立发布条目。  
因此实际工程里可能出现两种情况：

1. 使用 crates.io 的 `gpui 0.2.2`，需要验证其 Windows 启动 API 是否完整。
2. 使用 Zed Git 依赖并固定 commit，API 更接近 Zed 当前状态，但升级和可复现性更差。

**建议 POC 必须固定 GPUI 来源版本/commit，不允许使用浮动的 `main`。**

### 4.4 GPUI 的 UI 生态

GPUI 是低层 UI 框架，不是 WinForms/WPF 那种成熟控件库。

需要自己组合或实现：

- 表格
- 列表虚拟化
- 表单布局
- 对话框
- 输入校验
- 右键菜单
- 托盘
- 多窗口状态同步
- 可访问性细节

GPUI 有一些基础示例和 `accesskit` 集成，但不能假定有 WinForms 同等级的现成控件。

## 5. 三种集成方案对比

### 方案 A：C# Core 控制进程 + Rust GPUI 客户端（推荐）

```text
WinForms 默认 UI
WPF 可选 UI
GPUI 可选 UI
        |
        v
本地 JSON-RPC / HTTP / Named Pipe
        |
        v
XantaComfyLauncher.CoreHost（C#，引用现有 Core）
```

优点：

- 业务逻辑只有一份
- WinForms/WPF/GPUI 行为一致
- GPUI 可以随时替换或删除
- 现有 Core 不需要 Rust 重写
- 未来可以把 WinForms/WPF 也接到同一个控制层

缺点：

- 需要新增一个 C# 控制进程
- 需要设计 IPC 协议和生命周期
- 需要处理单实例、鉴权、日志、错误映射

### 方案 B：Rust 直接启动 C# CLI 命令

例如 Rust 每次操作都执行：

```text
XantaComfyLauncher.exe --start
XantaComfyLauncher.exe --stop
XantaComfyLauncher.exe --queue
```

优点：

- 初期实现最快
- 不需要先做控制进程

缺点：

- 每次操作都有进程启动成本
- 状态、日志、WebSocket 难以持续推送
- 错误处理和取消操作困难
- 很容易变成临时脚本拼接
- 不适合正式产品

结论：只适合一次性验证，不作为正式架构。

### 方案 C：Rust 重写 Core

优点：

- 单一语言
- 理论上可以完全脱离 .NET

缺点：

- 需要重写进程管理、Git、Python venv、ComfyUI API、WebSocket、节点管理、工作流 Profile、模型路径、Velopack 更新等
- 两套实现会长期分叉
- 风险和工作量都远高于收益

结论：不建议。

## 6. 推荐架构

### 6.1 进程和职责

```text
XantaComfyLauncher.exe (WinForms, 默认)
  └─ 按钮：GPUI 界面
       └─ 启动 gpui\XantaComfyLauncher.Gpui.exe

XantaComfyLauncher.Gpui.exe (Rust + GPUI, 可选)
  └─ 显示界面
  └─ 通过本地 IPC 调用 CoreHost
  └─ 退出时释放自己的连接

XantaComfyLauncher.CoreHost.exe (C#)
  └─ 引用 XantaComfyLauncher.Core
  └─ 唯一持有启动器业务状态
  └─ 单实例
  └─ 负责 ComfyUI 进程生命周期
```

### 6.2 启动关系

推荐流程：

1. 用户从 WinForms 点击“GPUI 界面”。
2. WinForms 检查 `gpui\XantaComfyLauncher.Gpui.exe` 是否存在。
3. 不存在则提示，不影响 WinForms。
4. 存在则启动 GPUI。
5. GPUI 检查 CoreHost 是否运行。
6. 未运行则启动 CoreHost。
7. CoreHost 绑定随机 loopback 端口或命名管道，并写入短期 token。
8. GPUI 用 token 连接，加载状态。

### 6.3 IPC 选择

POC 推荐：

```text
127.0.0.1 + 随机端口 + 一次性 token + JSON
```

正式版推荐评估：

```text
Windows Named Pipe + JSON-RPC
```

原因：

- 不暴露网络端口
- 更容易做访问控制
- Windows-only 项目没有跨平台负担
- 延迟更低

不建议：

- 固定端口且无鉴权
- 把完整配置和控制接口暴露到局域网
- 通过命令行参数传递敏感信息

### 6.4 最小控制协议

POC 只需要以下接口：

```text
GET  /health
GET  /config
PUT  /config
GET  /instance/status
POST /instance/start
POST /instance/stop
GET  /instance/logs?tail=...
GET  /queue
POST /queue/interrupt
POST /queue/clear
GET  /gpu
```

第二阶段再增加：

```text
GET  /nodes
POST /nodes/update
GET  /workflows
POST /workflows/analyze
GET  /models/paths
PUT  /models/paths
```

所有响应应包含：

```json
{
  "ok": true,
  "data": {},
  "error": null
}
```

错误响应：

```json
{
  "ok": false,
  "data": null,
  "error": {
    "code": "comfy_start_failed",
    "message": "可读错误信息"
  }
}
```

### 6.5 状态和事件

GPUI 不能只靠轮询。推荐事件流：

```text
instance.state
instance.log
queue.snapshot
queue.progress
gpu.snapshot
```

POC 可以先用轮询；MVP 再引入 WebSocket 或命名管道事件流。

## 7. 分阶段计划

### Phase 0：环境验证（1–2 天）

- 安装 Rust 工具链
- 安装 MSVC C++ Build Tools
- 固定 GPUI 版本/commit
- 编译并运行最小窗口
- 验证中文显示
- 验证高 DPI
- 验证窗口关闭和最小化

通过条件：

- `cargo build --release` 成功
- 中文不出现方框/截断
- 窗口能正常关闭
- 不依赖管理员权限

### Phase 1：隔离 POC（3–5 天）

- GPUI 窗口显示仪表盘骨架
- 启动 C# CoreHost
- 通过 IPC 读取实例状态
- 启动 / 停止 ComfyUI
- 接收日志
- 显示队列摘要
- WinForms 保持默认，GPUI 只在 `gpui\` 子目录

通过条件：

- 启动/停止行为与 WinForms 一致
- CoreHost 崩溃不导致 WinForms 不可用
- GPUI 可以退出并回到 WinForms
- 不产生第二套配置

### Phase 2：MVP（2–3 周）

- 仪表盘
- 实例状态
- 队列管理和实时进度
- 日志查看
- 基础设置
- 节点只读列表
- 退出和回退入口

### Phase 3：功能对齐（6–10 周，估计）

- 节点安装/更新
- GitHub 节点搜索
- 冲突检测
- 工作流 Profile
- 模型路径管理
- 启动器更新
- 托盘和通知
- 完整可访问性

实际时间取决于 GPUI 控件自建量和 Windows 平台问题数量。

## 8. 打包和回退设计

建议发布目录：

```text
current/
  XantaComfyLauncher.exe          WinForms 默认
  wpf/
    XantaComfyLauncher.Wpf.exe    WPF 可选
  gpui/
    XantaComfyLauncher.Gpui.exe   Rust + GPUI 可选
```

Velopack 主程序保持：

```text
--mainExe XantaComfyLauncher.exe
```

入口：

```text
WinForms → GPUI 界面
GPUI → 返回经典界面
```

失败回退：

- `gpui\` 不存在：WinForms 提示，不崩溃
- GPUI 启动失败：WinForms 继续可用
- CoreHost 启动失败：GPUI 显示错误并允许返回 WinForms
- GPUI 崩溃：不影响 WinForms 主程序

## 9. 主要风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| GPUI API 变化 | 升级可能破坏代码 | 固定 commit，季度升级一次 |
| Windows 平台 bug | 窗口/IME/通知异常 | POC 先验证，保留 WinForms 回退 |
| 中文渲染/输入法 | 中文界面不可用 | Phase 0 必须验证 |
| 自建控件成本 | 工期超预期 | 先做只读和基础操作，不做完整表格 |
| 两套业务逻辑分叉 | 行为不一致 | 只允许通过 CoreHost 调用业务 |
| 本地 IPC 安全 | 本机其他程序可调用 | token + 随机端口/命名管道 + 单用户 |
| 包体积增加 | 更新包变大 | 灰度可选组件，后续再评估 |
| 托盘/通知能力 | 功能不完整 | 使用 Rust 系统托盘/通知 crate 或先不做 |
| GPL/Apache 许可证混用 | 法务风险 | 只用 gpui crate，不复制 Zed GPL UI 组件 |

## 10. Go / No-Go 门槛

### 继续投入的条件

- GPUI Windows 窗口在目标机器上稳定运行
- 中文显示和中文输入法没有阻断问题
- CoreHost + IPC 能稳定控制 ComfyUI 启停
- 启动/停止/队列行为与 WinForms 一致
- GPUI 崩溃不影响 WinForms
- 包内布局和回退路径验证通过

### 建议停止的条件

- Windows 上需要客户安装额外运行库或开发工具
- 中文输入法不可用且短期无解
- 必须复制 Core 业务逻辑才能完成基本功能
- 需要大幅修改现有 Core 才能接 IPC
- GPUI 升级导致持续破坏
- POC 工期超过 2 周仍无法稳定启动/停止

## 11. 最终建议

**建议做 POC，不建议直接进入正式开发。**

最稳妥的路线是：

1. 保持 WinForms 默认和现有发布结构。
2. 在独立分支做 `gpui\` 子目录的隔离实验。
3. 先实现“窗口 + CoreHost + 启动/停止 + 日志”四件事。
4. 通过 Go/No-Go 门槛后，再决定是否做完整页面。
5. 如果 GPUI 的 Windows 稳定性或中文支持不达标，保留 WPF 作为可选界面，删除 GPUI 实验即可。

这样可以在不破坏原版 GUI 的前提下验证 Rust + GPUI，而不是把启动器再次推倒重来。

## 12. 参考来源

- GPUI crate：`gpui 0.2.2`
- Zed 仓库：<https://github.com/zed-industries/zed>
- GPUI Windows 后端目录：`crates/gpui_windows/src`
- GPUI 示例：`crates/gpui/examples`
- Zed 主仓库工具链：`rust-toolchain.toml`
- GPUI crate 许可证：Apache-2.0
