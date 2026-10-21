using ApoSwitch;
using ApoSwitch.Core;

// Test-only native menu host; not part of either shipped executable.
internal sealed class MenuPreview : Form
{
    private readonly ComboBox states = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ConfigDocument config = ConfigDocument.Load(WindowsSettings.ConfigPath());
    private readonly UserSettings settings;
    public MenuPreview()
    {
        settings = UserSettings.Initial(config.Presets);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9);
        Text = "APO Switch 原生菜单验收";
        ClientSize = new Size(460, 360);
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        Controls.Add(panel);
        panel.Controls.Add(new Label { Text = "与托盘共用菜单模型和 Windows HMENU。", AutoSize = true });
        states.Items.AddRange(["已安装 APO", "未安装 APO", "没有播放设备", "检测失败"]);
        states.SelectedIndex = 0;
        panel.Controls.Add(states);
        foreach (var (label, dark) in new[] { ("查看深色原生菜单", true), ("查看浅色原生菜单", false) })
        {
            var button = new Button { Text = label, Width = 230, Height = 34 };
            button.Click += (_, _) =>
            {
                var state = new DeviceState((DeviceAvailability)states.SelectedIndex);
                var entries = TrayMenuBuilder.Build(state, config, null, settings, false, false, _ => { },
                    id => { if (settings.Contains(id)) settings.CyclePresetIds.Remove(id); else settings.CyclePresetIds.Add(id); return settings.Contains(id); }, () => { }, () => { }, () => { }, () => { });
                using var menu = new NativeMenu(entries);
                menu.Show(Handle, PointToScreen(new Point(35, 150)), dark);
            };
            panel.Controls.Add(button);
        }
    }
}
