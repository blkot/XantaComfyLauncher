# GPUI Phase 0 Spike

这是 Rust + GPUI 的最小可行性工程，不属于发布包，也不接入现有 WinForms/WPF。

## 已验证

- Windows 10/11 x64
- Rust 1.100.0
- MSVC x86_64-pc-windows-msvc
- GPUI 0.2.2
- `cargo build --release --locked` 成功
- `cargo check` 成功
- GPUI 窗口真实启动
- 中文文本正常渲染

## 环境要求

- Rust 1.100.0 或更新
- Visual Studio 2022 Build Tools
- Windows 10 SDK
- `cc` 固定为 `1.2.33`（见 `Cargo.lock`）

当前环境里有两点兼容性处理：

1. `Cargo.lock` 固定 `cc 1.2.33`。  
   较新的 `cc` 在 ring 的 MSVC 归档步骤可能失败，表现为 `lib.exe` 返回 1104。
2. `[profile.dev] debug = 0`。  
   当前 VS Build Tools 下开启 `/Z7` 时 `cl` 可能报 D8050；关闭调试信息后 debug check 正常。

## 构建

```bat
build.bat
```

构建脚本会先加载 Visual Studio 2022 Build Tools 环境，再执行：

```text
cargo build --release --locked
```

## 运行

```bat
run.bat
```

或直接运行：

```text
target\release\gpui_spike.exe
```

## 下一步

- 增加 C# CoreHost
- 用本地 JSON-RPC / Named Pipe 接入实例状态、启动/停止和日志
- 验证 GPUI 崩溃不影响 WinForms
- 验证 `gpui\` 子目录打包和 WinForms 入口
