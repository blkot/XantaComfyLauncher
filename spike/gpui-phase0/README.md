# GPUI Phase 1 Spike

Rust + GPUI 可选前端的最小 CoreHost 控制验证。该 spike 不属于发布包，也不接入现有 WinForms/WPF。

## 已验证

- Windows 10/11 x64
- Rust 1.100.0
- GPUI 0.2.2
- `cargo build --release --locked` 成功
- `cargo check` 成功
- GPUI 窗口真实启动
- 中文文本正常渲染
- GPUI 自动启动 C# CoreHost
- GPUI 通过本地 TCP + token + JSON 读取状态和日志
- CoreHost 提供 `/health`、`/status`、`/config`、`/logs`
- CoreHost 提供 `/start`、`/stop`、`/queue`、`/gpu`
- CoreHost 提供只读 `/nodes`、`/workflows`
- CoreHost 单实例保护
- 启动/停止异步执行，不阻塞 GPUI
- GPUI 每 2 秒自动刷新状态、日志、队列和 GPU

## 组件

```text
spike/gpui-phase0/
  src/main.rs              GPUI 窗口和 CoreHost 客户端
  bin/corehost/            构建时生成的 C# CoreHost
  target/release/          GPUI release exe

XantaComfyLauncher.CoreHost/
  Program.cs               本地控制进程，引用 XantaComfyLauncher.Core
```

## 构建

```bat
build.bat
```

构建脚本会：

1. 加载 Visual Studio 2022 Build Tools 环境
2. `dotnet publish` C# CoreHost 到 `bin\corehost`
3. `cargo build --release --locked`

## 运行

```bat
run.bat
```

`run.bat` 会设置 `XANTA_COREHOST_EXE` 并启动 GPUI。GPUI 启动 CoreHost 后读取：

```text
COREHOST_READY <port> <token>
```

随后通过 `127.0.0.1` 的本地接口控制。

## 当前按钮

- 启动 ComfyUI
- 停止 ComfyUI
- 刷新状态
- 刷新节点
- 刷新工作流
- 返回经典界面

按钮调用本地接口；CoreHost 的启动/停止已在后台任务执行。

## 环境兼容性处理

1. `Cargo.lock` 固定 `cc 1.2.33`，避免 ring 在 MSVC 归档步骤失败。
2. `[profile.dev] debug = 0`，避免当前 VS Build Tools 下 `/Z7` 触发 cl D8050。
3. `build.bat` 自动加载 VS 开发环境。

## 下一步

- 启动/停止改为异步，避免 UI 阻塞
- 增加队列、GPU、节点页面
- 增加 CoreHost 单实例和进程生命周期保护
- 将 GPUI 作为 `gpui\` 子目录随包分发
- 从 WinForms 增加 GPUI 入口
