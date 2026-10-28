internal static partial class Checks
{
    private static int passed;
    private static string root = "";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is 2 or 3 && args[0] == "--verify-release")
        {
            ReleaseChecks(args[1], args.Length == 3 ? args[2] : AppContext.BaseDirectory);
            Console.WriteLine($"PASS: {passed} release isolation checks");
            return 0;
        }
        if (args.Contains("--startup-check"))
        {
            StartupChecks();
            return 0;
        }
        if (args.Contains("--menu-preview"))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.Run(new MenuPreview());
            return 0;
        }
        if (args.Contains("--simulator-check"))
        {
            try { SimulatorLifecycleChecks(); Console.WriteLine($"PASS: {passed} simulator lifecycle checks"); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        string[] knownArguments = ["--core-checks", "--menu-checks", "--debug-checks", "--device-check", "--tray-check", "--live-config"];
        if (args.Any(argument => !knownArguments.Contains(argument)))
        {
            Console.Error.WriteLine("未知检查参数。可用：" + string.Join(", ", knownArguments));
            return 1;
        }
        root = Path.Combine(Path.GetTempPath(), "ApoSwitch-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool all = args.Length == 0 || args.Contains("--live-config");
            if (all || args.Contains("--core-checks"))
            {
                ConfigChecks();
                CycleChecks();
                PathChecks();
                CoreReliabilityChecks();
            }
            if (all || args.Contains("--menu-checks"))
            {
                MenuChecks();
                WindowsReliabilityChecks();
            }
            if (all || args.Contains("--debug-checks"))
            {
                DebugChecks().GetAwaiter().GetResult();
                DebugReliabilityChecks().GetAwaiter().GetResult();
            }
            if (args.Contains("--device-check")) WindowsDeviceSmokeChecks();
            if (args.Contains("--tray-check")) TrayIntegrationChecks();
            if (args.Contains("--live-config")) LiveConfigChecks();
            Console.WriteLine($"PASS: {passed} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(root);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("ApoSwitch-Checks-"))
                Directory.Delete(resolved, true);
        }
    }
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("OK: " + name); passed++;
    }
}
