# ComfyUI Launcher

轻量级 Windows 启动器，用于管理指定的 ComfyUI 实例。

## 实例绑定

实例路径由界面显式指定并保存在启动器自己的配置文件中：

```text
LauncherApp\launcher-config.json
```

配置示例：

```json
{
  "InstanceRoot": "H:\\Work\\Personal\\AIProjects\\ComfyUI_instances\\ComfyUI_261114",
  "Port": 8188,
  "ListenMode": "Local",
  "UseSageAttention": false,
  "AutoOpenBrowser": true,
  "UpdateDependenciesOnSwitch": true,
  "IncludePrerelease": false,
  "ExtraArguments": ""
}
```

界面中修改路径后，点击 **应用路径** 才会重新绑定实例并写回配置。

如果配置文件不存在，启动器会尝试检测候选实例填入路径框，但仍需要显式点击 **应用路径**。

## 功能

- 显式指定 / 浏览 / 切换 ComfyUI 实例路径
- 启动 / 停止 ComfyUI，并管理完整进程树
- 仅本机 `127.0.0.1` / 局域网 `0.0.0.0` 监听切换
- 普通模式 / SageAttention 模式
- 端口设置、额外启动参数
- 实时日志，限制内存中的日志长度
- 环境信息：ComfyUI、提交、Python、torch、SageAttention、Triton、GPU
- 自动获取 ComfyUI GitHub Releases 和 Git tags
- 按 tag 切换 ComfyUI 版本
- 切换前检查已跟踪文件修改
- 可选同步 `requirements.txt` 依赖
- 失败自动回滚
- 手动回滚到上一版本
- 不导入 PyTorch，不创建 CUDA 上下文，不占用显存

## 开发运行

```bat
dotnet run --project ComfyLauncher.csproj
```

## 构建

自包含单文件版本（不依赖已安装的 .NET 运行时）：

```bat
build.bat
```

输出：

```text
..\LauncherApp\ComfyLauncher.exe
```

框架依赖版本（文件更小，但需要 .NET Desktop Runtime 9）：

```bat
build-framework-dependent.bat
```

## 无界面测试

环境、Git、Release/tag、端口自检：

```bat
ComfyLauncher.exe --self-test
```

也可以通过参数临时指定实例：

```bat
ComfyLauncher.exe --self-test --root "H:\path\to\instance"
```

结果：

```text
<instance>\.launcher\self-test.json
```

实际启动 ComfyUI、等待健康检查并停止：

```bat
ComfyLauncher.exe --smoke-test
```

结果：

```text
<instance>\.launcher\smoke-test.json
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

## 全局配置与实例数据

```text
LauncherApp/launcher-config.json     # 显式实例路径和启动设置

<instance>/.launcher/
├─ version-state.json
├─ cache/github-releases.json
└─ snapshots/*-pip-freeze.txt
```
