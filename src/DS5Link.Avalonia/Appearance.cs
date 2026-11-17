using System.IO;

namespace DS5Link;

internal enum AppearanceTheme { Dark, Light, Gradient }

internal static class AppearancePalette
{
    public static readonly SolidColorBrush Text = new(Color.Parse("#F0F1F3"));
    public static readonly SolidColorBrush Muted = new(Color.Parse("#A5A5A5"));
    public static readonly SolidColorBrush Success = new(Color.Parse("#30D158"));
    public static readonly SolidColorBrush Warning = new(Color.Parse("#FF9F0A"));
    public static readonly SolidColorBrush Danger = new(Color.Parse("#FF453A"));
    public static readonly SolidColorBrush Accent = new(Color.Parse("#0A84FF"));
    public static readonly SolidColorBrush Badge = new(Color.Parse("#0A84FF"));
    public static readonly SolidColorBrush InactiveBadge = new(Color.Parse("#23FFFFFF"));
    public static readonly SolidColorBrush BatteryOutline = new(Color.Parse("#66FFFFFF"));

    private static readonly byte[] OverlaySteps =
        [0x07, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x12, 0x14, 0x15, 0x16,
         0x17, 0x18, 0x1A, 0x1C, 0x1F, 0x20, 0x24, 0x25, 0x2B, 0x2C, 0x30, 0x35];

    // Keep the original neutral tones in Dark, including secondary text.
    private static readonly string[] InkTones =
        ["FFFFFF", "F3F4F6", "EEEEEE", "E8E8E8", "E5E7EB", "E1E1E1", "DEDEDE",
         "D2D2D2", "CBCBCB", "C4C4C4", "C1C1C1", "B3B3B3", "A6A6A6", "A5A5A5", "9C9C9C"];

    public static void Apply(AppearanceTheme theme)
    {
        var resources = Application.Current!.Resources;
        bool light = theme == AppearanceTheme.Light;
        bool gradient = theme == AppearanceTheme.Gradient;
        void Put(string key, string dark, string day, string? tint = null) =>
            resources[key] = new SolidColorBrush(Color.Parse(light ? day : gradient ? tint ?? dark : dark));
        void Update(SolidColorBrush brush, string dark, string day, string? tint = null) =>
            brush.Color = Color.Parse(light ? day : gradient ? tint ?? dark : dark);

        Update(Text, "#F0F1F3", "#262830");
        Update(Muted, "#A5A5A5", "#595E6B", "#BAC2D6");
        Update(Success, "#30D158", "#106639");
        Update(Warning, "#FF9F0A", "#854800");
        Update(Danger, "#FF453A", "#AA2424");
        Update(Accent, "#0A84FF", "#1559A9", "#AFC2FF");
        Update(Badge, "#0A84FF", "#3A69A4", "#485F92");
        Update(InactiveBadge, "#23FFFFFF", "#747D90", "#586782");
        Update(BatteryOutline, "#66FFFFFF", "#6E7686");

        resources["AppearanceText"] = Text;
        resources["AppearanceMuted"] = Muted;
        resources["AppearanceSuccess"] = Success;
        resources["AppearanceAccent"] = Accent;
        resources["ShellFrameBrush"] = gradient
            ? Gradient("#23454C", "#293751", "#503750")
            : new SolidColorBrush(Color.Parse(light ? "#F5F6FA" : "#18181B"));
        Put("ShellBorderBrush", "#26FFFFFF", "#B6BECE", "#70809C");
        Put("ShellCardBrush", "#0EFFFFFF", "#FFFFFF", "#73323A51");
        Put("ShellCardBorderBrush", "#12FFFFFF", "#D9DFEB", "#527F8CAA");
        Put("ShellHighlight", "#14FFFFFF", "#FFFFFFFF", "#38FFFFFF");
        Put("AppearanceInset", "#50000000", "#E2E7F1", "#79222D44");
        Put("AppearanceSelected", "#2BFFFFFF", "#FFFFFF", "#647495");
        Put("AppearanceTrack", "#3D3D42", "#A5AEBF", "#62718C");
        Put("AppearanceSliderThumb", "#F0F1F3", "#52617B", "#EAF0FF");
        Put("AppearanceNavSelected", "#2BFFFFFF", "#E4EDFC", "#56678B");
        Put("AppearanceIcon", "#D9FFFFFF", "#515A6D", "#E2E9FA");
        Put("AppearanceChromeIcon", "#99FFFFFF", "#687385", "#CAD3E8");
        Put("AppearanceSubtleIcon", "#73FFFFFF", "#6E788C", "#B3BED6");
        Put("AppearanceSoftAccent", "#72B4FF", "#1559A9", "#C1D0FF");
        Put("AppearanceErrorText", "#FFB3A7", "#9A2724", "#FFBCBC");
        Put("AppearanceErrorHover", "#FF6961", "#A92D28", "#FFD0CD");
        Put("AppearanceErrorMuted", "#E9B4A8", "#962F25", "#FFBCBC");
        foreach (var tone in InkTones)
        {
            bool primary = Convert.ToByte(tone[..2], 16) >= 211;
            Put("AppearanceInk" + tone, "#" + tone,
                primary ? "#262830" : "#595E6B", primary ? "#" + tone : "#BAC2D6");
        }
        foreach (var alpha in OverlaySteps)
        {
            resources[$"AppearanceOverlay{alpha:X2}"] = new SolidColorBrush(light
                ? Color.FromArgb(alpha, 52, 65, 94)
                : Color.FromArgb(alpha, 255, 255, 255));
        }
    }

    private static LinearGradientBrush Gradient(params string[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        for (int i = 0; i < colors.Length; ++i)
            brush.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
        return brush;
    }
}

internal sealed partial class MainWindow
{
    private AppearanceTheme appearanceTheme;
    private readonly DispatcherTimer appearanceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private void InitializeAppearance()
    {
        var settings = preview ? new Settings() : Settings.Load();
        AppSettingsLabel.Text = L.T("Настройки приложения");
        AutomationProperties.SetName(AppSettingsButton, AppSettingsLabel.Text);
        AppSettingsButton.Click += (_, _) => SetAppSettingsOpen(!AppSettingsPanel.IsVisible);
        AppearanceLabel.Text = L.T("Внешний вид");
        AutomationProperties.SetName(AppearanceButton, AppearanceLabel.Text);
        AppearanceThemeLabel.Text = L.T("Тема");
        AppearanceOpacityLabel.Text = L.T("Непрозрачность окна");
        AutomationProperties.SetName(AppearanceThemePicker, AppearanceThemeLabel.Text);
        AutomationProperties.SetName(AppearanceOpacity, AppearanceOpacityLabel.Text);
        AppearanceThemePicker.ItemsSource = new[] { L.T("Тёмная"), L.T("Светлая"), L.T("Градиент") };
        AppearanceThemePicker.SelectedIndex = (int)settings.PreferredTheme;
        AppearanceOpacity.Value = settings.NormalizedOpacity;
        ApplyAppearance(initializeTheme: true);

        AppearanceButton.Click += (_, _) =>
        {
            AppearancePanel.IsVisible = !AppearancePanel.IsVisible;
            AppearanceChevron.RenderTransform = new RotateTransform(AppearancePanel.IsVisible ? 180 : 0);
        };
        AppearanceThemePicker.SelectionChanged += (_, _) => ChangeAppearance();
        AppearanceOpacity.ValueChanged += (_, _) => ChangeAppearance();
        appearanceSaveTimer.Tick += (_, _) => SaveAppearance();
        Closed += (_, _) => { if (appearanceSaveTimer.IsEnabled) SaveAppearance(); };
    }

    private void ChangeAppearance()
    {
        ApplyAppearance();
        if (preview) return;
        appearanceSaveTimer.Stop();
        appearanceSaveTimer.Start();
    }

    private void ApplyAppearance(bool initializeTheme = false)
    {
        var next = (AppearanceTheme)Math.Clamp(AppearanceThemePicker.SelectedIndex, 0, 2);
        if (initializeTheme || next != appearanceTheme)
        {
            AppearancePalette.Apply(next);
            appearanceTheme = next;
            SetAppearanceTheme(this);
            if (inputSettingsWindow != null)
                SetAppearanceTheme(inputSettingsWindow);
        }
        Opacity = AppearanceOpacity.Value / 100;
        if (inputSettingsWindow != null)
            inputSettingsWindow.Opacity = Opacity;
        AppearanceOpacityValue.Text = L.F("{0}%", (int)AppearanceOpacity.Value);
    }

    private void SetAppearanceTheme(Window window) =>
        window.RequestedThemeVariant = appearanceTheme == AppearanceTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

    private void SetAppSettingsOpen(bool open)
    {
        AppSettingsPanel.IsVisible = open;
        AppSettingsChevron.RenderTransform = new RotateTransform(open ? 180 : 0);
    }

    private void SaveAppearance()
    {
        appearanceSaveTimer.Stop();
        try
        {
            var settings = Settings.Load();
            settings.Theme = appearanceTheme.ToString().ToLowerInvariant();
            settings.WindowOpacity = (int)AppearanceOpacity.Value;
            settings.Save();
        }
        catch (IOException) { Notice(L.T("Не удалось сохранить внешний вид.")); }
        catch (UnauthorizedAccessException) { Notice(L.T("Не удалось сохранить внешний вид.")); }
    }
}
