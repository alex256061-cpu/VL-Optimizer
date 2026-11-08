using System.Windows;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private async Task UndoAllConfigurationsAsync()
    {
        if (_busy || _smoke) return;
        await LoadHistoryAsync();
        var candidates = _history.Where(r => r.CanUndo).OrderByDescending(r => r.StartedAt).ToArray();
        if (candidates.Length == 0) { Notice("Não há configurações reversíveis disponíveis no histórico."); return; }
        var window = Dialog("Revisar restauração geral", out var panel, out var footer);
        panel.Children.Add(Text($"Restaurar {candidates.Length} configurações?", 25, weight: FontWeights.Bold));
        panel.Children.Add(Text("A ordem começa pelas alterações mais recentes para preservar ajustes que compartilham o mesmo alvo. Cada configuração será consultada antes da reversão. Conflitos e falhas permanecem no histórico e não são sobrescritos silenciosamente.", 14, Muted, margin: new Thickness(0, 14, 0, 16)));
        foreach (var candidate in candidates) panel.Children.Add(Text(candidate.Title + " · " + candidate.StartedAt.ToLocalTime().ToString("dd/MM HH:mm:ss"), 13, margin: new Thickness(0, 7, 0, 0)));
        panel.Children.Add(Text("Esta ação não recupera arquivos excluídos pela limpeza. Backups FiveM e OptiScaler são revisados separadamente.", 12, AccentSoft, margin: new Thickness(0, 18, 0, 0)));
        var consent = Consent("Revisei a lista e aprovo restaurar estas configurações."); panel.Children.Add(consent);
        var apply = Button("Restaurar configurações revisadas", () => window.DialogResult = true, true); apply.IsEnabled = false;
        consent.Checked += (_, _) => apply.IsEnabled = true; consent.Unchecked += (_, _) => apply.IsEnabled = false;
        var cancel = Button("Voltar", () => window.DialogResult = false); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); footer.Children.Add(cancel); footer.Children.Add(apply);
        if (window.ShowDialog() != true) return;
        await RunFeatureAsync("Restaurando configurações em ordem…", async token =>
        {
            var restored = 0; var other = 0;
            try
            {
                foreach (var candidate in candidates)
                {
                    token.ThrowIfCancellationRequested(); _analysisStatus.Text = "◌  Restaurando: " + candidate.Title;
                    var result = await _backend.UndoAsync(candidate.Id, token);
                    if (result.Status == OperationStatus.Reverted) restored++; else other++;
                }
                Notice($"{restored} configurações restauradas · {other} operações com outro resultado. Confira cada registro no histórico.", other > 0);
            }
            finally { await LoadHistoryAsync(); }
        });
    }
}
