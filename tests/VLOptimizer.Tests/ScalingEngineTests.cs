using System.Reflection;
using VLOptimizer.Core;

public static class ScalingEngineTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root);
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        test("Motor: ausência da biblioteca é reportada, não lança", async () =>
        {
            using var bridge = new ScalingEngineBridge();
            var info = await bridge.ReadInfoAsync();
            Check(!info.Available && info.Message.Length > 0, "Probe sem DLL deveria reportar ausência");
            Check(info.Adapters.Count == 0 && info.Displays.Count == 0 && info.DwmRefreshRate == 0, "Info inventada sem motor");
        });
        test("Motor: caminho só é devolvido se o arquivo existir", () =>
        {
            var resolved = ScalingEngineBridge.ResolveEnginePath();
            Check(resolved == null || File.Exists(resolved), "Caminho resolvido sem arquivo");
            return Task.CompletedTask;
        });
        test("Motor: bridge não expõe chamada nativa direta", () =>
        {
            // Binding lives in the internal bundle; the public bridge stays a read-only information
            // source so callers cannot reach ApplySettings by accident.
            var bridge = typeof(ScalingEngineBridge).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Check(!bridge.Any(x => x.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase)), "Bridge expõe aplicação");
            var service = typeof(ScalingProfileService).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(x => x.Name).ToArray();
            Check(!service.Any(x => x.Contains("Native", StringComparison.OrdinalIgnoreCase)), "Serviço expõe chamada nativa direta");
            return Task.CompletedTask;
        });
        test("Motor: nenhuma P/Invoke estática da biblioteca do motor", () =>
        {
            // Loading must be dynamic (NativeLibrary) so a missing DLL cannot abort the process at type load.
            var dllImports = typeof(ScalingProfileService).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .SelectMany(m => m.GetCustomAttributes<System.Runtime.InteropServices.DllImportAttribute>())
                .Select(a => a.Value)
                .ToArray();
            Check(!dllImports.Contains(ScalingEngineBridge.FileName, StringComparer.OrdinalIgnoreCase), "DllImport estático para o motor encontrado");
            return Task.CompletedTask;
        });
        test("Motor: capacidades sem motor não prometem informação", async () =>
        {
            var service = new ScalingProfileService(Path.Combine(root, "engine-missing"));
            var capabilities = await service.GetCapabilitiesAsync();
            Check(!capabilities.CanReportEngineInfo, "Capacidade de reportar informado sem motor");
            var info = await service.ReadEngineInfoAsync(default);
            Check(!info.Available, "Info disponível sem motor");
        });
        test("Motor: perfil continua armazenável e validado sem o motor", async () =>
        {
            var service = new ScalingProfileService(Path.Combine(root, "engine-storage"));
            var profile = new ScalingProfile { Name = "Perfil motor", ScalingAlgorithm = "FSR", FrameGeneration = "LSFG3" };
            await service.SaveProfileAsync(profile, default);
            var saved = (await service.GetProfilesAsync()).Single(x => x.Id == profile.Id);
            Check(saved.ScalingAlgorithm == "FSR" && saved.FrameGeneration == "LSFG3", "Perfil não persistiu");
        });
    }
}
