namespace DS5Link;

internal sealed partial class MainWindow
{
    static readonly int[] IdleMinutes = { 0, 1, 5, 10, 15, 30, 60 };
    readonly DispatcherTimer idleSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    IdlePolicy? shownPolicy;
    bool syncingIdlePolicy;
    ConnectionAction connectionAction;
    bool idleSaveFailed;

    void InitializeConnectionSettings()
    {
        IdlePolicyLabel.Text = L.T("Настройки автоотключения");
        IdleDesktopLabel.Text = L.T("На рабочем столе");
        IdleGameLabel.Text = L.T("В игре");
        IdleChargingLabel.Text = L.T("Не отключать во время зарядки");
        AutomationProperties.SetName(IdleCharging, IdleChargingLabel.Text);
        IdleSave.Content = L.T("Повторить");
        AutomationProperties.SetName(IdleDesktop, IdleDesktopLabel.Text);
        AutomationProperties.SetName(IdleGame, IdleGameLabel.Text);
        var labels = IdleMinutes.Select(minutes => minutes == 0 ? L.T("Никогда") : L.F("{0} мин", minutes)).ToArray();
        IdleDesktop.ItemsSource = labels;
        IdleGame.ItemsSource = labels;
        IdleDesktop.SelectionChanged += (_, _) => QueueIdleSave();
        IdleGame.SelectionChanged += (_, _) => QueueIdleSave();
        IdleCharging.IsCheckedChanged += (_, _) => QueueIdleSave();
        idleSaveTimer.Tick += (_, _) =>
        {
            if (commanding) return;
            idleSaveTimer.Stop();
            SaveIdlePolicy(this, new RoutedEventArgs());
        };
        Closed += (_, _) => idleSaveTimer.Stop();
    }

    void ToggleIdleSettings(object? sender, RoutedEventArgs e)
    {
        IdlePolicyPanel.IsVisible = !IdlePolicyPanel.IsVisible;
        IdlePolicyChevron.RenderTransform = new RotateTransform(IdlePolicyPanel.IsVisible ? 180 : 0);
    }

    void QueueIdleSave()
    {
        if (syncingIdlePolicy || preview || exiting || exitRequested) return;
        idleSaveTimer.Stop();
        idleSaveTimer.Start();
    }

    void DisplayConnectionSettings(BridgeState state)
    {
        var policy = state.IdlePolicy;
        if (policy != null && shownPolicy != policy)
        {
            syncingIdlePolicy = true;
            shownPolicy = policy;
            IdleDesktop.SelectedIndex = Array.IndexOf(IdleMinutes, policy.DesktopMinutes);
            IdleGame.SelectedIndex = Array.IndexOf(IdleMinutes, policy.GameMinutes);
            IdleCharging.IsChecked = policy.KeepConnectedWhileCharging;
            syncingIdlePolicy = false;
        }
        var editable = policy?.SchemaVersion == 1 && state.AutoDisconnectEnabled == true && !commanding && !exitRequested && !exiting;
        IdleDesktop.IsEnabled = IdleGame.IsEnabled = IdleCharging.IsEnabled = IdleSave.IsEnabled = editable;
        IdleSave.IsVisible = idleSaveFailed && policy != null;
        IdleFeedback.IsVisible = idleSaveFailed || policy == null || state.IdlePolicyWarning != null;
        if (!commanding && state.IdlePolicyWarning != null)
            IdleFeedback.Text = L.T("Файл таймеров повреждён. Действуют стандартные интервалы.");
        else if (!commanding && policy == null)
            IdleFeedback.Text = state.AutoDisconnectEnabled.HasValue
                ? L.T("Обновите службу DS5Link, чтобы настроить интервалы.")
                : L.T("Настройки службы недоступны.");

        var presentation = ConnectionPresentation.From(state);
        connectionAction = presentation.Action;
        ConnectionDetail.Text = presentation.Text;
        ConnectionDetail.IsVisible = presentation.Text.Length > 0;
        ConnectionActionButton.IsVisible = connectionAction != ConnectionAction.None;
        ConnectionActionButton.IsEnabled = !commanding && !exitRequested;
        ConnectionActionButton.Content = connectionAction switch
        {
            ConnectionAction.Recover => L.T("Восстановить HidHide"),
            _ => L.T("Диагностика")
        };
    }

    bool CanRunConnectionCommand(string command) => current.State != "Stopping" ||
        command is not ("enable" or "enablePS5" or "enableXbox" or "reconnect" or "disconnect");

    async void RunConnectionAction(object? sender, RoutedEventArgs e)
    {
        switch (connectionAction)
        {
            case ConnectionAction.Recover: await Command("recover"); break;
            case ConnectionAction.Diagnostics: SetDiagnosticsOpen(true); break;
        }
    }

    async void SaveIdlePolicy(object? sender, RoutedEventArgs e)
    {
        if (!IdleSave.IsEnabled || IdleDesktop.SelectedIndex < 0 || IdleGame.SelectedIndex < 0)
            return;
        idleSaveTimer.Stop();
        var draft = new IdlePolicy(1, IdleMinutes[IdleDesktop.SelectedIndex],
            IdleMinutes[IdleGame.SelectedIndex], IdleCharging.IsChecked == true);
        commanding = true;
        ++stateGeneration;
        Display(current);
        IdleFeedback.Text = L.T("Сохранение…");
        IdleFeedback.IsVisible = true;
        try
        {
            Display(await BridgeClient.SetIdlePolicyAsync(draft));
            idleSaveFailed = false;
        }
        catch (Exception)
        {
            idleSaveFailed = true;
            IdleFeedback.Text = L.T("Не удалось сохранить интервалы. Повторите попытку.");
        }
        finally
        {
            commanding = false;
            Display(current);
        }
    }
}
