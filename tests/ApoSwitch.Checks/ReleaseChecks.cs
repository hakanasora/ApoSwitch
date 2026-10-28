using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

internal static partial class Checks
{
    private static void ReleaseChecks(string directory, string assemblyDirectory)
    {
        Assert(File.Exists(Path.Combine(directory, "ApoSwitch.exe")), "正式程序存在");
        Assert(Directory.GetFileSystemEntries(directory).Length == 1, "正式目录仅有一个 EXE，没有运行库、语言文件夹和说明图片");
        Assert(new FileInfo(Path.Combine(directory, "ApoSwitch.exe")).Length <= 2_000_000, "正式 EXE 不超过 2 MB");
        string dependencies = File.ReadAllText(Path.Combine(assemblyDirectory, "ApoSwitch.deps.json"));
        Assert(!dependencies.Contains("ApoSwitch.DeviceDebug") && !dependencies.Contains("ApoSwitch.Debug")
            && !dependencies.Contains("ApoSwitch.DeviceSimulator"), "正式依赖清单无调试模块");
        using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(assemblyDirectory, "ApoSwitch.runtimeconfig.json")));
        Assert(!runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _)
            && runtime.RootElement.GetProperty("runtimeOptions").GetProperty("frameworks").EnumerateArray()
                .Any(f => f.GetProperty("name").GetString() == "Microsoft.WindowsDesktop.App"), "发布物依赖共享桌面运行时，不内置整套环境");
        foreach (var assembly in new[] { "ApoSwitch.dll", "ApoSwitch.Core.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(assemblyDirectory, assembly));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            Assert(metadata.AssemblyReferences.All(h => !metadata.GetString(metadata.GetAssemblyReference(h).Name)
                .Contains("Debug")), assembly + " 不引用调试程序集");
            Assert(metadata.TypeReferences.All(h => metadata.GetString(metadata.GetTypeReference(h).Name)
                != "NamedPipeServerStream"), assembly + " 无命名管道服务端类型依赖");
        }
    }
}
