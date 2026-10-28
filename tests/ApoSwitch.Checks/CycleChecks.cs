using ApoSwitch.Core;

internal static partial class Checks
{
    private static void CycleChecks()
    {
        var a = new Preset("A", "New Door Open", "A", true);
        var b = new Preset("B", "New Door Closed", "B", false);
        var c = new Preset("C", "GSR_BK_filters", "C", false);
        var settings = UserSettings.Initial([a, b, c]);
        Assert(settings.Contains("A") && !settings.Contains("B") && settings.Contains("C"), "初始循环为红圈两套");
        Assert(settings.Next([a, b, c]) == "C", "按文件顺序循环");
        Assert(settings.Next([a with { IsEnabled = false }, b with { IsEnabled = true }, c]) == "A", "当前不在循环时从首项开始");
        settings.CyclePresetIds.Clear();
        Assert(settings.Next([a, b, c]) is null, "允许空循环");
        settings.CyclePresetIds.Add("C");
        Assert(settings.Next([a, b, c]) == "C", "单成员循环");
        var store = new SettingsStore(Path.Combine(root, "settings", "settings.json"));
        store.Save(settings);
        Assert(store.LoadSaved()!.CyclePresetIds.SequenceEqual(["C"]), "循环成员重启记忆");
    }
}
