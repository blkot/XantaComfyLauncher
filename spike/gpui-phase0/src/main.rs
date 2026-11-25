use std::{
    io::{BufRead, BufReader, Read, Write},
    net::TcpStream,
    path::PathBuf,
    process::{Child, ChildStdin, Command, Stdio},
    sync::Arc,
    time::Duration,
};

use gpui::{
    App, Application, Bounds, Context, SharedString, Timer, TitlebarOptions, WeakEntity, Window,
    WindowBounds, WindowOptions, div, prelude::*, px, rgb, size,
};
use serde_json::Value;

#[derive(Clone)]
struct CoreHostClient {
    port: u16,
    token: String,
}

impl CoreHostClient {
    fn request(&self, method: &str, path: &str) -> Result<Value, String> {
        let mut stream = TcpStream::connect(("127.0.0.1", self.port))
            .map_err(|e| format!("连接 CoreHost 失败：{e}"))?;
        let request = format!(
            "{method} {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Xanta-Token: {}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n",
            self.token
        );
        stream
            .write_all(request.as_bytes())
            .map_err(|e| format!("发送请求失败：{e}"))?;
        let mut response = String::new();
        stream
            .read_to_string(&mut response)
            .map_err(|e| format!("读取响应失败：{e}"))?;
        let body = response
            .split_once("\r\n\r\n")
            .map(|(_, body)| body)
            .ok_or_else(|| "CoreHost 响应格式错误".to_string())?;
        let json: Value =
            serde_json::from_str(body).map_err(|e| format!("解析 CoreHost 响应失败：{e}"))?;
        if !json["ok"].as_bool().unwrap_or(false) {
            let message = json["error"]["message"]
                .as_str()
                .unwrap_or("CoreHost 操作失败");
            return Err(message.to_string());
        }
        Ok(json["data"].clone())
    }

    fn status_text(&self) -> Result<String, String> {
        let data = self.request("GET", "/status")?;
        let state = data["state"].as_str().unwrap_or("?");
        let running = data["running"].as_bool().unwrap_or(false);
        let detail = data["detail"].as_str().unwrap_or("");
        let url = data["localUrl"].as_str().unwrap_or("-");
        let pid = data["processId"]
            .as_i64()
            .map(|v| v.to_string())
            .unwrap_or_else(|| "-".into());
        Ok(format!(
            "state={state}  running={running}  pid={pid}\nurl={url}\n{detail}"
        ))
    }

    fn logs_text(&self) -> Result<String, String> {
        let data = self.request("GET", "/logs?tail=30")?;
        let lines = data["lines"]
            .as_array()
            .map(|items| {
                items
                    .iter()
                    .filter_map(|v| v.as_str())
                    .collect::<Vec<_>>()
                    .join("\n")
            })
            .unwrap_or_default();
        Ok(lines)
    }
}

struct HostConnection {
    _child: Child,
    _stdin: ChildStdin,
    client: CoreHostClient,
}

impl HostConnection {
    fn launch() -> Result<Self, String> {
        let exe = find_core_host()?;
        let mut child = Command::new(&exe)
            .arg("--exit-on-stdin-eof")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .map_err(|e| format!("无法启动 CoreHost：{e}\n{}", exe.display()))?;
        let stdout = child.stdout.take().ok_or("CoreHost 没有 stdout")?;
        let mut reader = BufReader::new(stdout);
        let mut line = String::new();
        reader
            .read_line(&mut line)
            .map_err(|e| format!("读取 CoreHost 启动信息失败：{e}"))?;
        let parts: Vec<_> = line.trim().split_whitespace().collect();
        if parts.len() < 3 || parts[0] != "COREHOST_READY" {
            let _ = child.kill();
            return Err(format!("CoreHost 启动失败：{}", line.trim()));
        }
        let port = parts[1]
            .parse::<u16>()
            .map_err(|e| format!("CoreHost 端口无效：{e}"))?;
        let token = parts[2].to_string();
        let stdin = child.stdin.take().ok_or("CoreHost 没有 stdin")?;
        Ok(Self {
            _child: child,
            _stdin: stdin,
            client: CoreHostClient { port, token },
        })
    }
}

impl Drop for HostConnection {
    fn drop(&mut self) {
        let _ = self._child.kill();
        let _ = self._child.wait();
    }
}

fn find_core_host() -> Result<PathBuf, String> {
    if let Ok(path) = std::env::var("XANTA_COREHOST_EXE") {
        let path = PathBuf::from(path);
        if path.is_file() {
            return Ok(path);
        }
    }
    let exe = std::env::current_exe().map_err(|e| format!("无法定位当前 exe：{e}"))?;
    let base = exe.parent().ok_or("无法定位当前 exe 目录")?;
    let candidates = [
        base.join("../../bin/corehost/XantaComfyLauncher.CoreHost.exe"),
        base.join("../../../../XantaComfyLauncher.CoreHost/bin/Release/net9.0-windows/XantaComfyLauncher.CoreHost.exe"),
    ];
    for candidate in candidates {
        if candidate.is_file() {
            return Ok(candidate);
        }
    }
    Err("找不到 CoreHost。请先运行 build.bat，或设置 XANTA_COREHOST_EXE。".into())
}

struct LauncherWindow {
    host: Option<Arc<HostConnection>>,
    status: SharedString,
    queue: SharedString,
    gpu: SharedString,
    logs: SharedString,
    last_error: SharedString,
}

impl LauncherWindow {
    fn refresh(&mut self) {
        let Some(host) = &self.host else {
            self.status = "CoreHost 未启动。".into();
            return;
        };
        match host.client.status_text() {
            Ok(status) => {
                self.status = status.into();
                self.last_error = "".into();
                if let Ok(logs) = host.client.logs_text() {
                    self.logs = if logs.is_empty() {
                        "（暂无日志）".into()
                    } else {
                        logs.into()
                    };
                }
                self.queue = match host.client.request("GET", "/queue") {
                    Ok(data) => format!(
                        "运行中 {} / 等待 {}",
                        data["runningCount"].as_u64().unwrap_or(0),
                        data["pendingCount"].as_u64().unwrap_or(0)
                    )
                    .into(),
                    Err(_) => "队列不可用（ComfyUI 未启动）".into(),
                };
                self.gpu = match host.client.request("GET", "/gpu") {
                    Ok(data) => {
                        let name = data["gpuName"].as_str().unwrap_or("GPU");
                        let util = data["utilizationPercent"]
                            .as_i64()
                            .map(|v| format!("{v}%"))
                            .unwrap_or_else(|| "-".into());
                        let used = data["memoryUsedMb"].as_i64().unwrap_or(0);
                        let total = data["memoryTotalMb"].as_i64().unwrap_or(0);
                        let temp = data["temperatureC"]
                            .as_i64()
                            .map(|v| format!("{v}°C"))
                            .unwrap_or_else(|| "-".into());
                        format!("{name}  util={util}  VRAM={used}/{total} MB  {temp}")
                    }
                    Err(error) => error,
                }
                .into();
            }
            Err(error) => self.last_error = error.into(),
        }
    }

    fn call(&mut self, method: &str, path: &str) {
        let Some(host) = &self.host else {
            self.last_error = "CoreHost 未启动。".into();
            return;
        };
        match host.client.request(method, path) {
            Ok(_) => {
                self.last_error = "".into();
                self.refresh();
            }
            Err(error) => self.last_error = error.into(),
        }
    }
}

impl Render for LauncherWindow {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let action =
            |id: &'static str, label: &'static str, method: &'static str, path: &'static str| {
                div()
                    .id(id)
                    .px_4()
                    .py_2()
                    .rounded_md()
                    .bg(rgb(0x2a3544))
                    .hover(|style| style.bg(rgb(0x34445a)))
                    .cursor_pointer()
                    .child(label)
                    .on_click(cx.listener(move |this, _, _, cx| {
                        this.call(method, path);
                        cx.notify();
                    }))
            };

        div()
            .flex()
            .flex_col()
            .size_full()
            .gap_4()
            .bg(rgb(0x15181e))
            .text_color(rgb(0xe8ebef))
            .p_8()
            .child(
                div()
                    .text_2xl()
                    .font_weight(gpui::FontWeight::BOLD)
                    .child("XantaComfyLauncher"),
            )
            .child(
                div()
                    .text_lg()
                    .text_color(rgb(0x7fc7ff))
                    .child("Rust + GPUI Phase 1 · CoreHost IPC"),
            )
            .child(
                div()
                    .flex()
                    .gap_3()
                    .child(action("start", "启动 ComfyUI", "POST", "/start"))
                    .child(action("stop", "停止 ComfyUI", "POST", "/stop"))
                    .child(action("refresh", "刷新状态", "GET", "/status")),
            )
            .child(
                div()
                    .p_4()
                    .rounded_lg()
                    .bg(rgb(0x20262f))
                    .text_sm()
                    .child(self.status.clone()),
            )
            .child(
                div()
                    .flex()
                    .gap_3()
                    .child(
                        div()
                            .flex_1()
                            .p_3()
                            .rounded_md()
                            .bg(rgb(0x1b222c))
                            .text_sm()
                            .child(format!("队列：{}", self.queue)),
                    )
                    .child(
                        div()
                            .flex_1()
                            .p_3()
                            .rounded_md()
                            .bg(rgb(0x1b222c))
                            .text_sm()
                            .child(format!("GPU：{}", self.gpu)),
                    ),
            )
            .when(!self.last_error.is_empty(), |this| {
                this.child(
                    div()
                        .p_3()
                        .rounded_md()
                        .bg(rgb(0x4a2525))
                        .text_color(rgb(0xffb4b4))
                        .child(self.last_error.clone()),
                )
            })
            .child(
                div()
                    .flex_1()
                    .min_h(px(160.))
                    .p_4()
                    .rounded_lg()
                    .bg(rgb(0x101319))
                    .text_sm()
                    .text_color(rgb(0xaab2bd))
                    .child(self.logs.clone()),
            )
    }
}

fn start_auto_refresh(entity: WeakEntity<LauncherWindow>, cx: &mut App) {
    cx.spawn(async move |cx| {
        loop {
            Timer::after(Duration::from_secs(2)).await;
            if entity
                .update(cx, |this, cx| {
                    this.refresh();
                    cx.notify();
                })
                .is_err()
            {
                break;
            }
        }
    })
    .detach();
}

fn main() {
    let (host, error) = match HostConnection::launch() {
        Ok(host) => (Some(Arc::new(host)), String::new()),
        Err(error) => (None, error),
    };

    Application::new().run(move |cx: &mut App| {
        let bounds = Bounds::centered(None, size(px(980.), px(680.)), cx);
        cx.open_window(
            WindowOptions {
                titlebar: Some(TitlebarOptions {
                    title: Some("XantaComfyLauncher GPUI Phase 1".into()),
                    appears_transparent: false,
                    ..Default::default()
                }),
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                ..Default::default()
            },
            move |_, cx| {
                let host = host.clone();
                let error = error.clone();
                let view = cx.new(move |_| {
                    let mut window = LauncherWindow {
                        host,
                        status: if error.is_empty() {
                            "正在读取 CoreHost 状态...".into()
                        } else {
                            "CoreHost 未启动。".into()
                        },
                        queue: "（等待刷新）".into(),
                        gpu: "（等待刷新）".into(),
                        logs: "（暂无日志）".into(),
                        last_error: error.into(),
                    };
                    window.refresh();
                    window
                });
                start_auto_refresh(view.downgrade(), cx);
                view
            },
        )
        .unwrap();
        cx.activate(true);
    });
}
