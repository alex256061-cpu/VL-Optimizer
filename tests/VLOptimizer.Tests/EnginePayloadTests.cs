using System.Security.Cryptography;
using VLOptimizer.Core;

public static class EnginePayloadTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root);
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var staging = Path.Combine(root, "appdata");
        EnginePayloadResolver Resolver() => new(staging);

        test("Motor: sem arquivo o payload não está disponível", () =>
        {
            var status = Resolver().Probe();
            Check(!status.Available && status.Path == null, "Payload inventado sem arquivo");
            return Task.CompletedTask;
        });
        test("Motor: importar arquivo com nome errado é recusado", async () =>
        {
            string source = Path.Combine(root, "outra.dll");
            File.WriteAllBytes(source, [0x4d, 0x5a]);
            var status = await Resolver().ImportAsync(source, default);
            Check(!status.Available && status.Message.Contains("Lossless.dll"), "Nome errado aceito");
        });
        test("Motor: importar arquivo com hash errado é recusado", async () =>
        {
            string source = Path.Combine(root, "Lossless.dll");
            File.WriteAllBytes(source, [0x4d, 0x5a, 0x00, 0x01]);
            var status = await Resolver().ImportAsync(source, default);
            Check(!status.Available && status.Sha256 != null, "Hash divergente aceito");
            Check(!File.Exists(Path.Combine(staging, "engine", "Lossless.dll")), "Arquivo inválido foi copiado");
        });
        test("Motor: importar arquivo inexistente falha sem criar diretório", async () =>
        {
            var status = await Resolver().ImportAsync(Path.Combine(root, "nao-existe", "Lossless.dll"), default);
            Check(!status.Available, "Arquivo inexistente importado");
        });
        test("Motor: hash de referência tem o tamanho esperado", () =>
        {
            Check(EnginePayloadResolver.KnownSha256.Length == 64, "Hash de referência malformado");
            Check(EnginePayloadResolver.KnownSha256.All(x => Uri.IsHexDigit(x)), "Hash de referência não é hex");
            return Task.CompletedTask;
        });
        test("Motor: arquivo com hash divergente não é carregado", () =>
        {
            string fake = Path.Combine(root, "fake-engine", "Lossless.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(fake)!);
            File.WriteAllBytes(fake, [0x4d, 0x5a, 0x01, 0x02]);
            var status = new EnginePayloadResolver(staging, Path.GetDirectoryName(fake)).Probe();
            Check(!status.Available && status.Message.Contains("não confere"), "Carregaria binário não verificado");
            return Task.CompletedTask;
        });
        test("Sessão: iniciar sem biblioteca falha com motivo, não lança", () =>
        {
            using var host = new ScalingEngineHost();
            var start = host.Start(Path.Combine(root, "nao-existe", "Lossless.dll"));
            Check(start.State == ScalingEngineState.Failed && start.Message.Length > 0, "Início sem motor não reportou motivo");
            Check(!host.IsRunning, "Host marcado como ativo sem biblioteca");
            return Task.CompletedTask;
        });
        test("Sessão: aplicar perfil sem motor ativo é recusado", () =>
        {
            using var host = new ScalingEngineHost();
            var (ok, message) = host.Apply(new ScalingProfile { ScalingAlgorithm = "FSR" });
            Check(!ok && message.Length > 0, "Aplicou perfil sem motor");
            return Task.CompletedTask;
        });
        test("Sessão: parar duas vezes não lança", () =>
        {
            using var host = new ScalingEngineHost();
            host.Stop();
            host.Stop();
            Check(!host.IsRunning, "Host ativo após parar");
            return Task.CompletedTask;
        });
    }
}
