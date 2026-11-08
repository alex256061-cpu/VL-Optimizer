using System.Diagnostics;
using System.Text.Json;

namespace VLOptimizer.Core;

public sealed record ScalingProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Novo perfil";
    public string ExecutablePath { get; init; } = "";
    public bool AutoScale { get; init; }
    public int AutoScaleDelay { get; init; }
    public string ScalingMode { get; init; } = "Auto";
    public string FitMode { get; init; } = "AspectRatio";
    public string ScalingAlgorithm { get; init; } = "Off";
    public double ScaleFactor { get; init; } = 1.5;
    public bool ResizeBeforeScale { get; init; }
    public bool FsrOptimized { get; init; }
    public bool Ls1Performance { get; init; }
    public string Anime4KQuality { get; init; } = "S";
    public int Sharpness { get; init; } = 5;
    public int Ls1Sharpness { get; init; } = 1;
    public bool Vrs { get; init; }
    public string FrameGeneration { get; init; } = "Off";
    public bool PerformanceMode { get; init; }
    public string FrameGenerationMode { get; init; } = "Fixed";
    public double FrameMultiplier { get; init; } = 2;
    public double TargetFps { get; init; } = 60;
    public int FlowScale { get; init; } = 100;
    public bool ClipCursor { get; init; } = true;
    public bool AdjustCursorSpeed { get; init; }
    public bool HideCursor { get; init; }
    public bool ScaleCursor { get; init; }
    public string SyncMode { get; init; } = "Default";
    public int MaxFrameLatency { get; init; } = 3;
    public bool GsyncSupport { get; init; } = true;
    public bool HdrSupport { get; init; }
    public string CaptureApi { get; init; } = "DXGI";
    public int QueueTarget { get; init; } = 1;
    public bool DrawFps { get; init; } = true;
    public int PreferredGpuIndex { get; init; }
    public int OutputDisplayIndex { get; init; }
    public int CropLeft { get; init; }
    public int CropTop { get; init; }
    public int CropRight { get; init; }
    public int CropBottom { get; init; }
    public bool MultiDisplay { get; init; }
    public int FpsLimit { get; init; }
}

public sealed record ScalingCapabilities(bool IsEngineAvailable, string? EnginePath, string Message, bool CanApplyProfiles = false);

/// <summary>Own profile storage and explicit launcher. It never loads the GDZ graphics library.
/// Profiles describe desired settings; the selected external app remains responsible for rendering.</summary>
public sealed class ScalingProfileService
{
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static readonly IReadOnlyList<string> Algorithms = new[] { "Off", "LS1", "FSR", "NIS", "SGSR", "BicubicCAS", "Anime4K", "XBR", "SharpBilinear", "Integer", "Nearest" };

    public ScalingProfileService(string dataDirectory) => directory = Path.Combine(Path.GetFullPath(dataDirectory), "scaling");

    public async Task<IReadOnlyList<ScalingProfile>> GetProfilesAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return ReadStore().Profiles.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        finally { gate.Release(); }
    }

    public async Task SaveProfileAsync(ScalingProfile profile, CancellationToken token)
    {
        ValidateProfile(profile);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease = Acquire();
            var store = ReadStore();
            store.Profiles.RemoveAll(x => x.Id == profile.Id);
            if (store.Profiles.Count >= 200) throw new InvalidOperationException("Limite de 200 perfis locais atingido.");
            store.Profiles.Add(profile with { Name = profile.Name.Trim() });
            WriteStore(store);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteProfileAsync(Guid id, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var lease = Acquire(); var store = ReadStore(); store.Profiles.RemoveAll(x => x.Id == id); WriteStore(store); }
        finally { gate.Release(); }
    }

    public async Task<ScalingCapabilities> GetCapabilitiesAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = ReadStore().EnginePath;
            bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            return new(exists, path, exists
                ? "Programa externo selecionado. Abrir o programa não aplica estes perfis automaticamente; ajuste as opções nele. O VL não inclui um motor LSFG/FSR próprio."
                : "Motor gráfico não incluído. Você pode guardar e exportar perfis e selecionar um programa de scaling que já possua; as opções gráficas não são aplicadas pelo VL.");
        }
        finally { gate.Release(); }
    }

    public async Task SetEnginePathAsync(string path, CancellationToken token)
    {
        string full = ValidateExecutable(path);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var lease = Acquire(); var store = ReadStore(); store.EnginePath = full; WriteStore(store); }
        finally { gate.Release(); }
    }

    public async Task LaunchEngineAsync(CancellationToken token)
    {
        var capabilities = await GetCapabilitiesAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!capabilities.IsEngineAvailable) throw new FileNotFoundException(capabilities.Message);
        string full = ValidateExecutable(capabilities.EnginePath!);
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(full)! });
    }

    public async Task ExportProfileAsync(Guid id, string outputPath, CancellationToken token)
    {
        var profile = (await GetProfilesAsync(token).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id)
            ?? throw new KeyNotFoundException("Perfil não encontrado.");
        // Own interchange format, explicitly not a claim of an external app's import support.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format = "VL-Scaling-Profile", Version = 1, Profile = profile }, Options);
        await File.WriteAllBytesAsync(Path.GetFullPath(outputPath), bytes, token).ConfigureAwait(false);
    }

    public static void ValidateProfile(ScalingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 100 || profile.Name.Any(char.IsControl))
            throw new ArgumentException("Informe um nome de perfil entre 1 e 100 caracteres.");
        if (profile.ExecutablePath.Length > 32700 || profile.ExecutablePath.Any(char.IsControl)) throw new ArgumentException("Caminho do jogo inválido.");
        if (profile.ExecutablePath.Length > 0 && (!Path.IsPathFullyQualified(profile.ExecutablePath) || !profile.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Selecione o caminho absoluto de um executável de jogo.");
        OneOf(profile.ScalingAlgorithm, Algorithms); OneOf(profile.ScalingMode, ["Auto", "Custom"]);
        OneOf(profile.FitMode, ["AspectRatio", "Fullscreen"]); OneOf(profile.Anime4KQuality, ["S", "M", "L", "VL", "UL"]);
        OneOf(profile.FrameGeneration, ["Off", "LSFG1", "LSFG2", "LSFG3"]); OneOf(profile.FrameGenerationMode, ["Fixed", "Adaptive", "X2", "X3", "X4"]);
        OneOf(profile.CaptureApi, ["DXGI", "WGC"]); OneOf(profile.SyncMode, ["Off", "Default", "VSync", "Half", "Third"]);
        Range(profile.ScaleFactor, 1, 20, "Fator de escala"); Range(profile.FrameMultiplier, 1, 20, "Multiplicador");
        Range(profile.TargetFps, 10, 1000, "FPS alvo"); Range(profile.AutoScaleDelay, 0, 100, "Atraso");
        Range(profile.FlowScale, 25, 100, "Flow scale"); Range(profile.Sharpness, 0, 10, "Nitidez"); Range(profile.Ls1Sharpness, 0, 4, "Nitidez LS1");
        Range(profile.QueueTarget, 0, 2, "Fila"); Range(profile.MaxFrameLatency, 1, 15, "Latência máxima");
        Range(profile.PreferredGpuIndex, 0, 64, "GPU"); Range(profile.OutputDisplayIndex, 0, 64, "Monitor");
        foreach (int value in new[] { profile.CropLeft, profile.CropTop, profile.CropRight, profile.CropBottom }) Range(value, 0, 16384, "Recorte");
        Range(profile.FpsLimit, 0, 1000, "Limite FPS");
    }

    private static void OneOf(string value, IEnumerable<string> allowed) { if (!allowed.Contains(value, StringComparer.Ordinal)) throw new ArgumentException("Opção de perfil inválida: " + value); }
    private static void Range(double value, double min, double max, string label) { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(label, $"{label}: use {min} a {max}."); }
    private static string ValidateExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Selecione um executável local por caminho absoluto.");
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new FileNotFoundException("Executável não encontrado.", full);
        return full;
    }
    private FileStream Acquire() { Directory.CreateDirectory(directory); return new(Path.Combine(directory, "profiles.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private Store ReadStore()
    {
        var path = Path.Combine(directory, "profiles.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Arquivo de perfis excede o limite.");
        var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Arquivo de perfis inválido.");
        if (store.Version != 1 || store.Profiles is null || store.Profiles.Count > 200 || store.Profiles.Select(x => x.Id).Distinct().Count() != store.Profiles.Count)
            throw new InvalidDataException("Estrutura ou versão de perfis inválida; arquivo preservado.");
        foreach (var profile in store.Profiles) ValidateProfile(profile);
        return store;
    }
    private void WriteStore(Store store)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "profiles.json"), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, store, Options); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public sealed class Store { public int Version { get; set; } = 1; public string? EnginePath { get; set; } public List<ScalingProfile> Profiles { get; set; } = []; }
}
