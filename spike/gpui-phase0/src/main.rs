use gpui::{
    App, Application, Bounds, Context, SharedString, TitlebarOptions, Window, WindowBounds,
    WindowOptions, div, prelude::*, px, rgb, size,
};

struct LauncherWindow {
    status: SharedString,
}

impl Render for LauncherWindow {
    fn render(&mut self, _window: &mut Window, _cx: &mut Context<Self>) -> impl IntoElement {
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
                    .child("Rust + GPUI Phase 0"),
            )
            .child(
                div()
                    .text_sm()
                    .text_color(rgb(0xaab2bd))
                    .child("中文渲染测试：启动、停止、队列、节点、设置"),
            )
            .child(
                div()
                    .mt_4()
                    .p_4()
                    .rounded_lg()
                    .bg(rgb(0x20262f))
                    .child(self.status.clone()),
            )
    }
}

fn main() {
    Application::new().run(|cx: &mut App| {
        let bounds = Bounds::centered(None, size(px(900.), px(560.)), cx);
        cx.open_window(
            WindowOptions {
                titlebar: Some(TitlebarOptions {
                    title: Some("XantaComfyLauncher GPUI Phase 0".into()),
                    appears_transparent: false,
                    ..Default::default()
                }),
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                ..Default::default()
            },
            |_, cx| {
                cx.new(|_| LauncherWindow {
                    status: "GPUI Windows 窗口已启动。下一步接入 C# CoreHost。".into(),
                })
            },
        )
        .unwrap();
        cx.activate(true);
    });
}
