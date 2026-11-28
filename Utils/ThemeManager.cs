namespace XantaComfyLauncher.Utils;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    private static readonly Color DarkWindow = Color.FromArgb(24, 26, 30);
    private static readonly Color DarkSurface = Color.FromArgb(33, 36, 42);
    private static readonly Color DarkInput = Color.FromArgb(42, 46, 54);
    private static readonly Color DarkText = Color.FromArgb(232, 235, 240);
    private static readonly Color DarkBorder = Color.FromArgb(66, 72, 84);
    private static readonly Color DarkButton = Color.FromArgb(52, 57, 67);

    public static void Initialize(bool dark) => IsDark = dark;

    public static void Apply(Control root)
    {
        if (!IsDark || root.Disposing || root.IsDisposed) return;
        ApplyControl(root);
        foreach (Control child in root.Controls) Apply(child);
    }

    private static void ApplyControl(Control control)
    {
        switch (control)
        {
            case Form:
                control.BackColor = DarkWindow;
                control.ForeColor = DarkText;
                break;
            case GroupBox:
                control.BackColor = DarkSurface;
                control.ForeColor = DarkText;
                break;
            case TableLayoutPanel or FlowLayoutPanel or Panel:
                control.BackColor = DarkWindow;
                control.ForeColor = DarkText;
                break;
            case Label or CheckBox or RadioButton:
                control.BackColor = Color.Transparent;
                control.ForeColor = DarkText;
                break;
            case TextBox or ComboBox:
                control.BackColor = DarkInput;
                control.ForeColor = DarkText;
                break;
            case RichTextBox:
                control.BackColor = Color.FromArgb(15, 18, 22);
                control.ForeColor = DarkText;
                break;
            case ListView:
                control.BackColor = DarkInput;
                control.ForeColor = DarkText;
                break;
            case Button button:
                button.BackColor = DarkButton;
                button.ForeColor = DarkText;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = DarkBorder;
                button.UseVisualStyleBackColor = false;
                break;
            case MenuStrip or StatusStrip or ToolStrip:
                control.BackColor = DarkSurface;
                control.ForeColor = DarkText;
                if (control is ToolStrip strip)
                {
                    foreach (ToolStripItem item in strip.Items)
                    {
                        item.ForeColor = DarkText;
                        item.BackColor = DarkSurface;
                    }
                }
                break;
        }
    }
}
