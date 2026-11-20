using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using DynamicIslandWin.Services;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly Dictionary<AiAgentHooks.Agent, DateTime> _agentCompletion = [];
    private DateTime _nextAgentCheck;

    private void RefreshAiAgentsPage()
    {
        AiGeminiTitle.Text = L("Голосовой ассистент · Gemini Live");
        AiGeminiKeyLabel.Text = L("API-ключ Gemini");
        AiGeminiGainLabel.Text = L("Громкость ИИ-ассистента");
        BtnAiGeminiSave.Content = L("Сохранить ключ");
        BtnAiGeminiDelete.Content = L("Удалить ключ");
        AiGeminiStatusText.Text = L(string.IsNullOrWhiteSpace(GeminiKeyStore.Read())
            ? "Ключ не задан. Можно использовать GEMINI_API_KEY." : "Ключ Gemini доступен.");
        AiGeminiGainSlider.Value = _settings.GeminiGain;
        RefreshAgent(AiAgentHooks.Agent.Claude);
        RefreshAgent(AiAgentHooks.Agent.Codex);
    }

    private void RefreshAgent(AiAgentHooks.Agent agent)
    {
        var (button, status) = agent == AiAgentHooks.Agent.Claude
            ? (BtnAiClaude, AiClaudeStatusText) : (BtnAiCodex, AiCodexStatusText);
        try
        {
            bool enabled = AiAgentHooks.Configured(agent);
            if (enabled) _agentCompletion.TryAdd(agent, DateTime.UtcNow);
            else _agentCompletion.Remove(agent);
            button.IsEnabled = true;
            button.Content = L(enabled ? "Отключить" : "Подключить");
            status.Text = L(enabled
                ? "Хук уведомлений записан. Codex может запросить доверие через /hooks."
                : "Островок покажет уведомление, когда агент завершит ответ.");
            if (enabled && agent == AiAgentHooks.Agent.Claude)
                status.Text = L("Хук уведомлений записан. Проверьте его в /hooks.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            button.IsEnabled = false;
            button.Content = L("Подключить");
            status.Text = L("Не удалось прочитать настройки агента.");
        }
    }

    private void TickAiAgents()
    {
        var now = DateTime.UtcNow;
        if (_agentCompletion.Count == 0 || now < _nextAgentCheck) return;
        _nextAgentCheck = now.AddSeconds(3);
        foreach (var agent in _agentCompletion.Keys.ToArray())
        {
            try
            {
                var completed = AiAgentHooks.LastCompletion(agent);
                if (completed <= _agentCompletion[agent]) continue;
                _agentCompletion[agent] = completed;
                FeatureNotice(string.Format(L("{0} завершил ответ"), AiAgentHooks.Name(agent)), () =>
                {
                    ShowIsland();
                    MorphToState(IslandState.Settings);
                    NavAi_Click(NavAiBtn, new RoutedEventArgs());
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
    }

    private void AiGeminiSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AiGeminiKeyBox.Password)) return;
        ChangeGeminiKey(AiGeminiKeyBox.Password);
    }

    private void AiGeminiDelete_Click(object sender, RoutedEventArgs e) => ChangeGeminiKey("");

    private void ChangeGeminiKey(string key)
    {
        try
        {
            GeminiKeyStore.Save(key);
            AiGeminiKeyBox.Clear();
            RefreshAiAgentsPage();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            AiGeminiStatusText.Text = L("Не удалось изменить ключ Gemini.");
        }
    }

    private void AiGeminiGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings || _settings.GeminiGain == e.NewValue) return;
        _settings.GeminiGain = e.NewValue;
        if (_voice != null) _voice.Gain = (float)e.NewValue;
        SaveSettings();
    }

    private void AiClaude_Click(object sender, RoutedEventArgs e) => ToggleAgent(AiAgentHooks.Agent.Claude);
    private void AiCodex_Click(object sender, RoutedEventArgs e) => ToggleAgent(AiAgentHooks.Agent.Codex);

    private void ToggleAgent(AiAgentHooks.Agent agent)
    {
        try
        {
            AiAgentHooks.Toggle(agent);
            RefreshAgent(agent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            var status = agent == AiAgentHooks.Agent.Claude ? AiClaudeStatusText : AiCodexStatusText;
            status.Text = L("Не удалось изменить настройки агента.");
        }
    }
}
