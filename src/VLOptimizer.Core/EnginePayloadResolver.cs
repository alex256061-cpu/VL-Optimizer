using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace VLOptimizer.Core;

/// <summary>Where the presentation engine comes from and whether it is trusted.
/// The engine is a third-party commercial binary; it is never shipped inside the assembly.
/// A copy is staged on disk once, verified by SHA-256, and loaded from that verified file.</summary>
public sealed record EnginePayloadStatus(bool Available, string? Path, string Message, string? Sha256 = null);

/// <summary>Resolves the engine DLL for this installation, mirroring how the vendor wraps it:
/// the file sits beside the binaries and is loaded from disk, not from memory.
/// <para>Resolution order, first hit wins:
/// <list type="number">
/// <item>An explicitly configured path (user chose a folder that already contains the engine).</item>
/// <item>A previously staged copy in <c>%LOCALAPPDATA%\VLOptimizer\engine</c>, if its hash matches.</item>
/// <item>A file beside the executable (portable layout, same shape the vendor uses).</item>
/// </list>
/// Every candidate is hash-checked against <see cref="KnownSha256"/> before it is used, so a
/// tampered or different-version file is reported rather than loaded.</para></summary>
public sealed class EnginePayloadResolver
{
    /// <summary>SHA-256 of the engine build this code was verified against. A different hash is not
    /// rejected outright: it is surfaced, because a newer engine may still work, but the caller must
    /// opt in to use an unverified build.</summary>
    public const string KnownSha256 = "626B196D799606CD4250B7B29E04228692AB70CF56A5D1BBB56D748C8219F0EB";

    public const string EngineFileName = "Lossless.dll";

    private readonly string stagingDirectory;
    private readonly string? configuredDirectory;

    public EnginePayloadResolver(string appDataDirectory, string? userSelectedDirectory = null)
    {
        stagingDirectory = Path.Combine(Path.GetFullPath(appDataDirectory), "engine");
        configuredDirectory = string.IsNullOrWhiteSpace(userSelectedDirectory) ? null : Path.GetFullPath(userSelectedDirectory);
    }

    public string StagingDirectory => stagingDirectory;

    /// <summary>Looks for the engine without copying anything.</summary>
    public EnginePayloadStatus Probe()
    {
        foreach (var candidate in Candidates())
        {
            if (!File.Exists(candidate)) continue;
            string hash;
            try { hash = Hash(candidate); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (!hash.Equals(KnownSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, candidate, "Arquivo do motor encontrado, mas a versão não confere com a validada. Não será carregado.", hash);
            }
            return new(true, candidate, "Motor encontrado e verificado.", hash);
        }
        return new(false, null, "Motor não encontrado nesta instalação.");
    }

    /// <summary>Copies a user-supplied engine into the staging directory and verifies it. Used by
    /// the import action so the app never depends on a path the user may move or delete.</summary>
    public async Task<EnginePayloadStatus> ImportAsync(string sourcePath, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string full = Path.GetFullPath(sourcePath);
        if (!File.Exists(full)) return new(false, null, "Arquivo não encontrado: " + full);
        if (!Path.GetFileName(full).Equals(EngineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, "O arquivo precisa se chamar " + EngineFileName + ".");
        }

        string hash = Hash(full);
        if (!hash.Equals(KnownSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new(false, null, "Hash diferente do motor validado. Importação recusada para não carregar um binário inesperado.", hash);
        }

        Directory.CreateDirectory(stagingDirectory);
        string destination = Path.Combine(stagingDirectory, EngineFileName);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(target, token).ConfigureAwait(false);
                await target.FlushAsync(token).ConfigureAwait(false);
            }
            if (!Hash(temporary).Equals(KnownSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, null, "A cópia não confere com o original; nada foi mantido.");
            }
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) { try { File.Delete(temporary); } catch (IOException) { } }
        }
        return new(true, destination, "Motor importado e verificado.", KnownSha256);
    }

    private IEnumerable<string> Candidates()
    {
        if (configuredDirectory != null) yield return Path.Combine(configuredDirectory, EngineFileName);
        yield return Path.Combine(stagingDirectory, EngineFileName);
        yield return Path.Combine(AppContext.BaseDirectory, EngineFileName);
        yield return Path.Combine(AppContext.BaseDirectory, "bin", EngineFileName);
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
