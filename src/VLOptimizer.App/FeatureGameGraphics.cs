using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private GameGraphicsService? _gameGraphicsService;
    private GameGraphicsService GameGraphics => _gameGraphicsService ??= new(_backend.DataDirectory);
    private IReadOnlyList<GameGraphicsBackup> _gameGraphicsBackups = [];

    private void BuildGameGraphicsActions(StackPanel content, ToolkitGame game)
    {
        if (game.Executables.Count == 0) return;
        var group = Stack();
        group.Children.Add(Text("Preferência de GPU por executável", 14, weight: FontWeights.SemiBold));
        group.Children.Add(Text("Escolha o executável real do jogo. Outros tokens gráficos serão preservados. A preferência de alto desempenho não comprova que existe uma segunda GPU nem força o driver a usá-la.", 12, Muted, margin: new Thickness(0, 8, 0, 10)));
        foreach (string executable in game.Executables.Take(12))
            group.Children.Add(FeatureButton("Revisar GPU · " + Path.GetFileName(executable), () => _ = ReviewGameGraphicsAsync(executable)));
        content.Children.Add(new Expander { Header = "Preferência de GPU", Content = group, Foreground = Muted, Margin = new Thickness(0, 10, 0, 0) });
    }
    private UIElement BuildGameGraphicsHistory()
    {
        var root = Stack(); root.Children.Add(SectionTitle("Preferências gráficas por jogo", "Estado anterior preservado por executável"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Selecionar executável de jogo…", PickGameGraphicsExecutable));
        actions.Children.Add(FeatureButton("Ler backups gráficos", () => _ = ReadGameGraphicsBackupsAsync())); root.Children.Add(actions);
        foreach (var backup in _gameGraphicsBackups.Take(30))
        {
            var row = Stack(); row.Children.Add(Text(Path.GetFileName(backup.Executable), 15, weight: FontWeights.SemiBold));
            row.Children.Add(Text(backup.Executable, 11, Muted, margin: new Thickness(0, 6, 0, 7)));
            row.Children.Add(Text(backup.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") + (backup.Restored ? " · restaurado" : backup.Verified ? " · aplicado e verificado" : " · aplicação não confirmada"), 12, Muted));
            if (!backup.Restored) row.Children.Add(FeatureButton("Revisar restauração gráfica", () => _ = RestoreGameGraphicsAsync(backup)));
            root.Children.Add(Card(row));
        }
        return root;
    }
    private void PickGameGraphicsExecutable()
    {
        if (_busy || _smoke) return;
        var picker = new OpenFileDialog { Title = "Selecionar executável real do jogo", Filter = "Executável Windows (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) _ = ReviewGameGraphicsAsync(picker.FileName);
    }
    private async Task ReadGameGraphicsBackupsAsync() => await RunFeatureAsync("Lendo backups gráficos…", async token => _gameGraphicsBackups = await GameGraphics.GetBackupsAsync(token));
    private async Task ReviewGameGraphicsAsync(string executable)
    {
        if (_busy || _smoke) return; GameGraphicsPreview? preview = null;
        await RunFeatureAsync("Preparando preferência gráfica…", async token => preview = await GameGraphics.PreviewAsync(executable, token));
        if (preview is null || !ConfirmFeature("Preferir GPU de alto desempenho?", executable + "\n\nAntes: " + preview.Before.Display + "\nDepois: " + preview.Desired.Display + "\n\nO Windows interpreta esta preferência ao iniciar o jogo. Outros tokens serão mantidos; seleção real depende do hardware, driver e jogo. O backup poderá ser restaurado nesta página.", "Aplicar preferência e verificar")) return;
        await RunFeatureAsync("Gravando preferência gráfica…", async token =>
        {
            var result = await GameGraphics.ApplyAsync(preview, token);
            await RecordFeatureActivityAsync("game-gpu", "Preferência gráfica · " + Path.GetFileName(executable), StatusLabel(result.Status), result.Message + " Restauração na página Jogos, em backups gráficos.", result.Status);
            _gameGraphicsBackups = await GameGraphics.GetBackupsAsync(CancellationToken.None); Notice(result.Message, result.Status != OperationStatus.Completed);
        });
    }
    private async Task RestoreGameGraphicsAsync(GameGraphicsBackup backup)
    {
        if (_busy || _smoke || !ConfirmFeature("Restaurar preferência gráfica?", backup.Executable + "\n\nO executável e a preferência atual serão conferidos. Edições posteriores de outros programas serão preservadas.", "Restaurar valor anterior")) return;
        await RunFeatureAsync("Restaurando preferência gráfica…", async token =>
        {
            var result = await GameGraphics.RestoreAsync(backup.Id, token);
            await RecordFeatureActivityAsync("game-gpu-restore", "Restauração gráfica · " + Path.GetFileName(backup.Executable), StatusLabel(result.Status), result.Message, result.Status);
            _gameGraphicsBackups = await GameGraphics.GetBackupsAsync(CancellationToken.None); Notice(result.Message, result.Status != OperationStatus.Reverted);
        });
    }
}
