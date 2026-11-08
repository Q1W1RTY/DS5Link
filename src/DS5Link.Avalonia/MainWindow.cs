using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;
using Brush = Avalonia.Media.IBrush;
using Color = Avalonia.Media.Color;

namespace DS5Link;

internal sealed partial class MainWindow : Window {
    readonly Dictionary<string, TextBlock> metrics = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Forms.NotifyIcon? tray;
    Forms.ToolStripMenuItem? startupMenu;
    readonly bool preview;
    BridgeState current = new();
    bool updating, commanding, exiting, exitRequested, lowBatteryNotified, diagnosticsOpen, exporting, syncingStartup;
    int stateGeneration;
    string notifiedError = "", trayState = "";
    DateTime lastDiagnostics = DateTime.MinValue;
    string[] diagnosticLines = Array.Empty<string>();
    DiagnosticEvent[] eventRows = Array.Empty<DiagnosticEvent>();
    DispatcherTimer? footerResetTimer;

    static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
    static readonly Brush BlueBrush = new SolidColorBrush(Color.FromRgb(10, 132, 255));
    static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(255, 159, 10));
    static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(255, 69, 58));
    static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
    static readonly Brush ActiveSegmentBg = new SolidColorBrush(Color.FromArgb(43, 255, 255, 255));
    static readonly Brush InactiveSegmentBg = Avalonia.Media.Brushes.Transparent;

    public MainWindow(bool preview) {
        this.preview = preview;
        InitializeComponent();

        UpdateLanguageButton();
        InitializeInputControls();
        InitializeEffects();
        InitializeGameProfiles();
        VersionText.Text = "v" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        MaxHeight = 1080;

        AddMetric(ConnectionMetrics, L.T("Пакеты / с"));
        AddMetric(ConnectionMetrics, L.T("Паузы > 10 мс"));
        AddMetric(ConnectionMetrics, L.T("Макс. пауза"));
        AddMetric(ConnectionMetrics, L.T("Окно наблюдения"));
        AddMetric(AudioMetrics, L.T("Игровое аудио"));
        AddMetric(AudioMetrics, L.T("Микрофон"));
        AddMetric(AudioMetrics, L.T("Наушники"));
        AddMetric(AudioMetrics, "RSSI");

        Opened += (_,_) => { NativeWindowChrome.Attach(this,DragHeader); UpdateWorkArea(); };
        KeyDown += (_, e) => {
            if (e.Key == Key.F12) {
                SetDiagnosticsOpen(!diagnosticsOpen);
                e.Handled = true;
            }
        };
        Closing += (_, e) => {
            if (!exiting && !preview) {
                e.Cancel = true;
                Hide();
            }
        };
        PropertyChanged += (_, _) => {
            if (WindowState == WindowState.Minimized) Hide();
        };

        if (preview) {
            Display(new());
            return;
        }

        StartupSwitch.IsChecked = Settings.AutoStart;
        StartupSwitch.IsCheckedChanged += (_, _) => ChangeStartup(StartupSwitch.IsChecked==true);


        tray = new Forms.NotifyIcon { Visible = true, Icon = MakeIcon("Waiting"), Text = L.T("DS5Link · ожидание") };
        tray.DoubleClick += (_, _) => Open();

        tray.ContextMenuStrip = CreateTrayMenu();

        timer.Tick += async (_, _) => await Refresh();
        Display(new());
        _ = Initialize();
    }

    void AddMetric(StackPanel panel, string name) {
        var row = new Grid { Height = 22 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = name, Foreground = MutedBrush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = "—", Foreground = Avalonia.Media.Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        metrics[name] = value;
        panel.Children.Add(row);
    }

    public void Display(BridgeState state) {
        current = state;
        AutoDisconnectSwitch.IsChecked = state.AutoDisconnectEnabled == true;
        AutoDisconnectSwitch.IsEnabled = state.AutoDisconnectEnabled.HasValue && !commanding && !exiting && !exitRequested;
        SyncAudioEffects(state);
        inputSettingsWindow?.SetEffectState(state.Effects,audioEffects.Error,audioEffects.Level,audioEffects.SignalPeak,audioEffects.SourceName,state.Mode=="xbox");

        var name = state.State switch {
            "Active" => L.T("Подключён"),
            "Paused" => state.BluetoothDisconnected ? L.T("Отключён") : L.T("Приостановлено"),
            "Wired" => L.T("Кабель USB"),
            "Conflict" => L.T("Занят DSX"),
            "Stopping" => L.T("Отключение…"),
            "Connecting" => L.T("Подключение…"),
            "Error" => L.HasPrefix(state.Error,"Нет связи со службой") ? L.T("Нет связи") : L.T("Ошибка"),
            _ => L.T("Ожидание")
        };

        var dotBrush = state.State switch {
            "Active" => GreenBrush,
            "Connecting" or "Stopping" or "Paused" => AmberBrush,
            "Error" => RedBrush,
            _ => MutedBrush
        };

        StatusDot.Fill = dotBrush;
        BadgeText.Text = state.State switch {
            "Active" => state.Transport == "USB" ? "USB" : "Bluetooth",
            "Connecting" => L.T("Подключение…"),
            "Wired" => "USB",
            "Conflict" => L.T("Занят DSX"),
            "Stopping" => L.T("Отключение…"),
            "Error" => L.HasPrefix(state.Error,"Нет связи") ? L.T("Нет связи") : L.T("Сбой"),
            _ => state.BluetoothDisconnected ? L.T("Отключён") : L.T("Ожидание")
        };

        ConnectionText.Text = state.State is "Active" or "Wired" ? state.Transport : "—";
        SignalText.Text = state.State != "Active" ? "—" : state.Rssi is int rssi ? $"{rssi} dBm" : L.T("Нет данных");

        // Battery Widget
        BatteryText.Text = state.BatteryPercent is int battery ? $"{battery}%" : "—";
        ToolTip.SetTip(BatteryText, L.T("Приблизительный уровень по данным контроллера"));
        BatteryIndicator.Level=state.BatteryPercent; BatteryIndicator.Charging=state.Charging;
        if (state.Charging) {
            BatterySubtext.Text = L.T("Заряжается");
            BatterySubtext.Foreground = GreenBrush;
        } else if (state.BatteryPercent != null) {
            BatterySubtext.Text = L.T("От батареи");
            BatterySubtext.Foreground = MutedBrush;
        } else {
            BatterySubtext.Text = "";
        }

        // Emulation state
        bool emuActive = state.Enabled;
        MainEmuSwitch.IsChecked = emuActive;
        MainEmuSwitch.IsEnabled = !commanding && !exitRequested && (state.Enabled || !state.UsbConnected || state.Mode=="xbox");
        if (emuActive) {
            EmuBadge.Background = BlueBrush;
            EmuTitle.Text = state.State switch {
                "Active" => L.T("Эмуляция включена"),
                "Wired" or "Conflict" => L.T("Эмуляция приостановлена"),
                "Error" => L.T("Эмуляция недоступна"),
                "Connecting" or "Stopping" => L.T("Переключение эмуляции…"),
                _ => L.T("Ожидание контроллера")
            };
            ToggleButton.Content = L.T("Выключить эмуляцию");
            SetSegmentSelection(state.Mode == "xbox" ? 2 : 1);
        } else {
            EmuBadge.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            EmuTitle.Text = L.T("Эмуляция выключена");
            ToggleButton.Content = L.T("Включить эмуляцию");
            SetSegmentSelection(0);
        }

        FixHidHideButton.IsVisible = NeedsHidHideRepair(state.Error) ? true : false;
        FixHidHideButton.IsEnabled = !commanding && !exitRequested && state.State != "Stopping";

        ToggleButton.IsEnabled = !commanding && !exitRequested && state.State != "Stopping" && (state.Enabled || !state.UsbConnected || state.Mode=="xbox");
        ReconnectButton.IsEnabled = state.Enabled && !commanding && !exitRequested && state.State is not ("Wired" or "Conflict" or "Stopping");
        DisconnectButton.IsEnabled = !commanding && !exitRequested && !state.BluetoothDisconnected && !state.UsbConnected && state.State is not ("Wired" or "Conflict" or "Stopping" or "Error");

        // Diagnostics
        metrics[L.T("Пакеты / с")].Text = state.PacketsPerSecond.ToString("F1");
        metrics[L.T("Паузы > 10 мс")].Text = state.GapsOver10ms.ToString();
        metrics[L.T("Макс. пауза")].Text = L.F("{0:F1} мс",state.MaxGapMs);
        metrics[L.T("Окно наблюдения")].Text = L.F("{0:F0} с",state.ObservedSeconds);
        metrics[L.T("Игровое аудио")].Text = state.AudioActive ? L.T("есть поток") : L.T("нет потока");
        metrics[L.T("Микрофон")].Text = state.Muted ? "mute" : state.MicrophoneActive ? L.T("есть поток") : L.T("нет потока");
        metrics[L.T("Наушники")].Text = state.State == "Active" ? (state.Headphones ? L.T("подключены") : L.T("не подключены")) : "—";
        metrics["RSSI"].Text = state.Rssi is int signal ? $"{signal} dBm" : L.T("недоступен");

        DiagnosticError.Text = Diagnostics.Mask(state.Error);
        DiagnosticError.IsVisible = string.IsNullOrEmpty(state.Error) ? false : true;

        if (state.LogLines.Length > 0) {
            diagnosticLines = state.LogLines;
            var events = Diagnostics.RecentEvents(state.LogLines);
            if (!eventRows.SequenceEqual(events)) {
                eventRows = events;
                EventList.ItemsSource = events;
                EventsEmpty.IsVisible = events.Length == 0 ? true : false;
            }
            DiagnosticsUpdated.Text = L.T("Обновлено ") + DateTime.Now.ToString("HH:mm:ss");
        }

        if (tray != null) {
            tray.Text = "DS5Link · " + name + (state.BatteryPercent is int b ? $" · ≈{b}%" : "");
            if (trayState != state.State) {
                var old = tray.Icon;
                tray.Icon = MakeIcon(state.State);
                old?.Dispose();
                trayState = state.State;
            }
            if (state.BatteryPercent is > 30 || state.Charging) lowBatteryNotified = false;
            if (state.BatteryPercent is <= 20 && !state.Charging && !lowBatteryNotified) {
                lowBatteryNotified = true;
                Notify(L.T("Низкий заряд DualSense"), L.F("Осталось приблизительно {0}%.",state.BatteryPercent));
            }
            if (state.State == "Error" && state.Error != notifiedError) {
                notifiedError = state.Error;
                Notify("DS5Link", FriendlyError(state.Error));
            }
        }
    }

    void SetSegmentSelection(int index) {
        SegmentOff.Theme = this.RequireTheme(index == 0 ? "SegmentButtonActive" : "SegmentButton");
        SegmentPS5.Theme = this.RequireTheme(index == 1 ? "SegmentButtonActive" : "SegmentButton");
        SegmentXInput.Theme = this.RequireTheme(index == 2 ? "SegmentButtonActive" : "SegmentButton");
        SegmentOff.IsEnabled = SegmentXInput.IsEnabled = !commanding && !exitRequested && current.State != "Stopping";
        SegmentPS5.IsEnabled = SegmentXInput.IsEnabled && !current.UsbConnected;
        ToolTip.SetTip(SegmentPS5,current.UsbConnected ? L.T("По USB DualSense уже работает как PS5. Для эмуляции выберите Xbox.") : null);
    }

    internal void SetDiagnosticsOpen(bool open) {
        diagnosticsOpen = open;
        DisclosurePanel.IsVisible = open ? true : false;
        if(DisclosureChevron.RenderTransform is RotateTransform rotation)rotation.Angle=open?180:0;

        if (open) {
            lastDiagnostics = DateTime.MinValue;
            if (!preview) _ = Refresh();
        }
    }

    void ChangeStartup(bool enabled) {
        if (syncingStartup) return;
        syncingStartup = true;
        try {
            Settings.AutoStart = enabled;
            StartupSwitch.IsChecked = enabled;
            if (startupMenu != null) startupMenu.Checked = enabled;
            SetFooterMessage(enabled ? L.T("Автозапуск с Windows включён") : L.T("Автозапуск отключён"));
        } catch (Exception e) {
            var actual = Settings.AutoStart;
            StartupSwitch.IsChecked = actual;
            if (startupMenu != null) startupMenu.Checked = actual;
            Notice(L.T("Не удалось изменить автозапуск: ") + e.Message);
        } finally {
            syncingStartup = false;
        }
    }

    internal static bool NeedsHidHideRepair(string error) => error.Contains("HidHide inverse mode is incompatible", StringComparison.OrdinalIgnoreCase);

    static string FriendlyError(string message) {
        if(L.IsTranslated(message))return message;
        if (message.Contains("Enabling HidHide would affect")) return L.T("В HidHide настроены другие устройства. Подробности в диагностике.");
        if (message.Contains("HIDHIDE_ACCESS_DENIED") || message.Contains("HidHideCLI failed with exit code 5")) return L.T("HidHide недоступен. Закройте HidHide Configuration Client и повторите включение.");
        if (message.Contains("inverse mode")) return L.T("В HidHide включён обратный режим. Нажмите «Исправить HidHide».");
        if (message.Contains("HidHide recovery pending")) return L.T("Закройте окно настройки HidHide и нажмите «Восстановить HidHide» в диагностике.");
        if (message.Contains("recovery deferred")) return L.T("Проверьте восстановление в диагностике.");
        if (message.Contains("ViGEmBus")) return L.T("Для Xbox нужен ViGEmBus. Установите его через установщик DS5Link.");
        if (message == "BRIDGE_PAUSED") return L.T("Сначала включите эмуляцию.");
        if (message == "BLUETOOTH_DISCONNECT_PENDING") return L.T("Дождитесь отключения Bluetooth.");
        if (message == "CONTROLLER_BUSY") return L.T("Контроллер подключён по USB или используется DSX.");
        if (message == "CONTROLLER_NOT_CONFIGURED") return L.T("Сначала подключите один DualSense по Bluetooth.");
        return Regex.IsMatch(message, "[А-Яа-я]") ? L.T(message) : L.T("Сбой подключения. Подробности в диагностике.");
    }

    internal static Icon MakeIcon(string state) {
        using var bitmap = new Bitmap(24, 24);
        using (var graphics = Graphics.FromImage(bitmap)) {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://DS5Link/Assets/DS5Link.Tray.ico"));
            using var brand = new System.Drawing.Icon(stream, 24, 24);
            graphics.DrawIcon(brand, new Rectangle(0, 0, 24, 24));
            var color = state switch {
                "Active" => System.Drawing.Color.FromArgb(48, 209, 88),
                "Error" => System.Drawing.Color.FromArgb(255, 69, 58),
                "Connecting" or "Stopping" or "Paused" => System.Drawing.Color.Goldenrod,
                _ => System.Drawing.Color.SlateGray
            };
            using var outline = new SolidBrush(System.Drawing.Color.FromArgb(28, 33, 37));
            graphics.FillEllipse(outline, 15, 15, 9, 9);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 17, 17, 5, 5);
        }
        var handle = bitmap.GetHicon();
        try {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        } finally {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NativeRect { public int Left,Top,Right,Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public NativeRect Monitor,Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Auto)]
    static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    void UpdateWorkArea() {
        MaxHeight=(Screens.ScreenFromWindow(this)?.WorkingArea.Height??1080)/RenderScaling;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal void Open() {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void Notify(string title, string text) {
        if (tray != null) {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text.Length > 240 ? text[..240] : text;
            tray.ShowBalloonTip(5000);
        }
    }

    void Notice(string text) {
        DiagnosticsNotice.Text = text;
        DiagnosticsNotice.IsVisible = true;
        if (!IsVisible || !diagnosticsOpen) Notify("DS5Link", text);
        else Dispatcher.UIThread.Post(new Action(() => DiagnosticsNotice.BringIntoView()), DispatcherPriority.Background);
    }

    void SetFooterMessage(string text) {
        footerResetTimer?.Stop();
        FooterStatus.Text = text;
        FooterStatus.Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        footerResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        footerResetTimer.Tick += (_, _) => {
            footerResetTimer.Stop();
            FooterStatus.Text = "";
            FooterStatus.Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
        };
        footerResetTimer.Start();
    }

    async Task Initialize() {
        await Refresh();
        timer.Start();
    }

    async Task Refresh() {
        if (updating || exiting || commanding || exitRequested) return;
        updating = true;
        var generation = stateGeneration;
        try {
            bool diagnostics = diagnosticsOpen && IsVisible && DateTime.UtcNow - lastDiagnostics > TimeSpan.FromSeconds(3);
            var state = await BridgeClient.SendAsync(diagnostics ? "getDiagnostics" : "getState",
                gameInForeground: ForegroundExecutable.GameInForeground(profiles));
            if (diagnostics) lastDiagnostics = DateTime.UtcNow;
            if (generation == stateGeneration && !commanding) {
                if(state.InputOptions!=inputProfile.RuntimeOptions || (state.Effects.Options with {AudioDeviceId=effectOptions.AudioDeviceId})!=effectOptions)profileApplied=false;
                Display(state);
            }
        } catch (Exception) {
            if (generation == stateGeneration && !commanding)
                Display(current with {
                    State = "Error",
                    Error = L.T("Нет связи со службой. Ожидаем автоматическое восстановление…"),
                    BatteryPercent = null,
                    Charging = false,
                    Rssi = null,
                    AudioActive = false,
                    MicrophoneActive = false,
                    Headphones = false,
                    PacketsPerSecond = 0,
                    GapsOver10ms = 0,
                    MaxGapMs = 0,
                    ObservedSeconds = 0
                });
        } finally {
            updating = false;
        }
    }

    async Task Command(string command, bool? autoDisconnect = null) {
        if (commanding || exiting || exitRequested) return;
        commanding = true;
        if (!autoDisconnect.HasValue) {
            inputCursor.Reset();
            KeyboardOutput.ReleaseAll();
        }
        ++stateGeneration;
        Display(current);
        try {
            Display(await (autoDisconnect is bool enabled
                ? BridgeClient.SetAutoDisconnectAsync(enabled)
                : BridgeClient.SendAsync(command)));
        } catch (Exception e) {
            if (autoDisconnect.HasValue)
                SetFooterMessage(L.T("Не удалось сохранить настройку автоотключения"));
            else
                Display(current with { State = "Error", Error = e.Message });
        } finally {
            commanding = false;
            Display(current);
        }
    }

    async Task Exit() {
        if (exiting || commanding || exitRequested) return;
        exitRequested = true;
        ++stateGeneration;
        Display(current);
        try {
            var result = await BridgeShutdown.AttemptAsync(command => BridgeClient.SendAsync(command), ServiceState.StoppedOrMissing);
            if (!result.Released && MessageBox.Show(this,
                L.T("Не удалось подтвердить остановку эмуляции. Закрыть только интерфейс?\n\nСлужба DS5Link может продолжить работу. Состояние контроллера не подтверждено.\n\n") + result.Error,
                L.T("Выход из DS5Link"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            exiting = true;
            profileTimer.Stop();
            effectTimer.Stop();
            audioEffects.Dispose();
            inputTimer.Stop();
            KeyboardOutput.ReleaseAll();
            timer.Stop();
            tray?.Icon?.Dispose();
            tray?.Dispose();
            Close();
            DesktopLifetime.Current.Shutdown();
        } finally {
            exitRequested = false;
            if (!exiting) Display(current);
        }
    }

    async Task ExportDiagnostics() {
        if (exporting) return;
        exporting = true;
        ExportButton.IsEnabled = false;
        try {
            ExportButton.Content = L.T("Собираем…");
            var folder = Path.Combine(Settings.DirectoryPath, "Diagnostics");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "DS5Link-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".zip");
            await DiagnosticArchive.SaveAsync(path, current with { LogLines = diagnosticLines },
                () => BridgeClient.SendAsync("getDiagnostics"));
            Notice(L.T("Сохранено: ") + Path.GetFileName(path));
            SetFooterMessage(L.T("Диагностика сохранена"));
        } catch (Exception e) {
            Notice(L.T("Не удалось сохранить диагностику: ") + e.Message);
        } finally {
            exporting = false;
            ExportButton.IsEnabled = true;
            ExportButton.Content = L.T("Сохранить ZIP");
        }
    }

    // Window controls & dragging
    void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void CloseWindow(object sender, RoutedEventArgs e) => Hide();

    // Emulation controls
    async void MainEmuSwitch_Click(object sender, RoutedEventArgs e) => await Command(current.Enabled ? "pause" : "enable");
    async void SelectSegment_Off(object sender, RoutedEventArgs e) { if (current.Enabled) await Command("pause"); }
    async void SelectSegment_PS5(object sender, RoutedEventArgs e) { if (!current.Enabled || current.Mode != "ps5") await Command("enablePS5"); }
    async void SelectSegment_Xbox(object sender, RoutedEventArgs e) { if (!current.Enabled || current.Mode != "xbox") await Command("enableXbox"); }
    async void ToggleBridge(object sender, RoutedEventArgs e) => await Command(current.Enabled ? "pause" : "enable");
    async void ToggleAutoDisconnect(object sender, RoutedEventArgs e) {
        var enabled = AutoDisconnectSwitch.IsChecked == true;
        if (current.AutoDisconnectEnabled is bool previous && enabled != previous)
            await Command("setAutoDisconnect", enabled);
    }

    // Quick Actions
    async void ReconnectBridge(object sender, RoutedEventArgs e) {
        SetFooterMessage(L.T("Переподключение Bluetooth..."));
        await Command("reconnect");
    }

    async void DisconnectController(object sender, RoutedEventArgs e) {
        SetFooterMessage(L.T("Геймпад отключён"));
        await Command("disconnect");
    }

    async void FixHidHide(object sender, RoutedEventArgs e) {
        if (commanding || exiting || exitRequested || !NeedsHidHideRepair(current.Error)) return;
        if (MessageBox.Show(this,
            L.T("Для DS5Link нужен обычный режим HidHide. Отключить обратный режим?\n\nЭто общая настройка для всех программ. Списки устройств и приложений сохранятся. Другим программам эмуляции может потребоваться переподключение."),
            L.T("Исправить HidHide"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        bool repaired = false;
        var resume = current.Enabled;
        var mode = current.Mode;
        commanding = true;
        inputCursor.Reset();
        KeyboardOutput.ReleaseAll();
        ++stateGeneration;
        Display(current);
        try {
            Display(await BridgeClient.SendAsync("pause"));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (current.State == "Stopping" && DateTime.UtcNow < deadline) {
                await Task.Delay(100);
                Display(await BridgeClient.SendAsync("getState"));
            }
            if (current.State != "Paused") throw new IOException(L.T("Сначала нужно завершить освобождение контроллера и восстановление HidHide."));
            Display(await BridgeClient.SendAsync("normalizeHidHide"));
            repaired = true;
            if (resume) Display(await BridgeClient.SendAsync(mode == "xbox" ? "enableXbox" : "enablePS5"));
            SetFooterMessage(L.T("HidHide исправлен"));
        } catch (Exception error) {
            Display(current with { State = "Error", Error = error.Message });
            Notice((repaired ? L.T("HidHide исправлен, но эмуляция не включилась: ") : L.T("Не удалось исправить HidHide: ")) + error.Message);
        } finally {
            commanding = false;
            Display(current);
        }
    }

    async void RecoverController(object sender, RoutedEventArgs e) => await Command("recover");
    async void SelectController(object sender, RoutedEventArgs e) {
        if (MessageBox.Show(L.T("Сначала выключите эмуляцию и подключите только тот DualSense, который хотите использовать. Сохранить его вместо прежнего?"), L.T("Сменить контроллер"), MessageBoxButton.YesNo) == MessageBoxResult.Yes) await Command("selectController");
    }
    void ToggleAdvanced(object sender, RoutedEventArgs e) => SetDiagnosticsOpen(!diagnosticsOpen);
    async void SaveDiagnostics(object sender, RoutedEventArgs e) => await ExportDiagnostics();
    async void ExitApplication(object sender, RoutedEventArgs e) => await Exit();
    void OpenDiagnosticsFolder(object sender, RoutedEventArgs e) {
        try {
            var folder = Path.Combine(Settings.DirectoryPath, "Diagnostics");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        } catch (Exception error) {
            Notice(error.Message);
        }
    }
}
