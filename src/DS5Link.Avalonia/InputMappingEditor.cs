using System.Collections.Generic;

namespace DS5Link;

internal sealed partial class InputSettingsWindow
{
    readonly List<(ComboBox Target, CheckBox Suppress)> buttonRows = new();
    InputMapping mappingDraft = new();
    InputPreview latestInputPreview = new();
    ComboBox leftStageTarget = null!, rightStageTarget = null!;
    Slider leftStageThreshold = null!, rightStageThreshold = null!;
    Button calibrationButton = null!, resetCalibrationButton = null!;
    TextBlock calibrationStatus = null!;
    int[]? measuredCenters;
    int calibrationRun;
    bool calibrating, loadingMapping;

    void InitializeInputMapping(InputMapping initial)
    {
        var calibration = new StackPanel { Spacing = 8 };
        calibrationStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        calibrationButton = MappingButton(L.T("Измерить центр стиков"));
        resetCalibrationButton = MappingButton(L.T("Сбросить калибровку"));
        calibrationButton.Click += (_, _) => CalibrateSticks();
        resetCalibrationButton.Click += (_, _) =>
        {
            LoadInputMapping(ReadInputMapping() with { LeftX = new(), LeftY = new(), RightX = new(), RightY = new() });
            QueueAutoApply(false);
        };
        var actions = new WrapPanel();
        actions.Children.Add(calibrationButton);
        actions.Children.Add(resetCalibrationButton);
        calibration.Children.Add(actions);
        calibration.Children.Add(calibrationStatus);
        InputMappingTools.Children.Add(MappingSection(L.T("Калибровка стиков"), calibration));

        var remaps = new StackPanel { Spacing = 6 };
        foreach (var name in InputMapping.ButtonNames)
        {
            var target = ButtonPicker(name);
            var suppress = new CheckBox { Content = L.T("Подавить исходную") };
            target.SelectionChanged += (_, _) =>
            {
                suppress.IsEnabled = target.SelectedIndex > 0;
                MappingChanged();
            };
            suppress.IsCheckedChanged += (_, _) => MappingChanged();
            buttonRows.Add((target, suppress));
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = name, Width = 72, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(target);
            row.Children.Add(suppress);
            remaps.Children.Add(row);
        }
        InputMappingTools.Children.Add(MappingSection(L.T("Переназначение кнопок"), remaps));

        var stages = new StackPanel { Spacing = 10 };
        (leftStageTarget, leftStageThreshold) = AddTriggerStage(stages, "L2");
        (rightStageTarget, rightStageThreshold) = AddTriggerStage(stages, "R2");
        InputMappingTools.Children.Add(MappingSection(L.T("Двухступенчатые курки"), stages));
        LoadInputMapping(initial);
    }

    Button MappingButton(string text) => new()
    {
        Content = text, Theme = this.RequireTheme("CompactButton"), Margin = new Thickness(0, 0, 8, 4)
    };

    Border MappingSection(string title, Control content)
    {
        var disclosure = MappingButton(title + "  ▾");
        disclosure.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.IsVisible = false;
        disclosure.Click += (_, _) =>
        {
            content.IsVisible = !content.IsVisible;
            disclosure.Content = title + (content.IsVisible ? "  ▴" : "  ▾");
        };
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(disclosure);
        section.Children.Add(content);
        return new Border { Theme = this.RequireTheme("SharedCard"), Padding = new Thickness(12), Child = section };
    }

    ComboBox ButtonPicker(string name)
    {
        var picker = new ComboBox
        {
            ItemsSource = new[] { L.T("Нет назначения") }.Concat(InputMapping.ButtonNames).ToArray(),
            MinWidth = 140, Margin = new Thickness(0, 0, 8, 0), Theme = this.RequireTheme("DarkCombo")
        };
        AutomationProperties.SetName(picker, name);
        return picker;
    }

    (ComboBox, Slider) AddTriggerStage(Panel container, string name)
    {
        var target = ButtonPicker(name);
        var threshold = new Slider { Minimum = 32, Maximum = 255, TickFrequency = 1,
            IsSnapToTickEnabled = true, Theme = this.RequireTheme("InputSlider"), MinWidth = 150 };
        var label = new TextBlock();
        void UpdateThreshold()
        {
            label.Text = L.F("Вторая ступень: {0}%", Math.Round(threshold.Value / 255 * 100));
            threshold.IsEnabled = target.SelectedIndex > 0;
        }
        AutomationProperties.SetName(threshold, name + " " + L.T("Порог второй ступени"));
        target.SelectionChanged += (_, _) => { UpdateThreshold(); MappingChanged(); };
        threshold.ValueChanged += (_, _) => { UpdateThreshold(); MappingChanged(); };
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = name });
        row.Children.Add(target);
        row.Children.Add(label);
        row.Children.Add(threshold);
        container.Children.Add(row);
        return (target, threshold);
    }

    void MappingChanged()
    {
        if (!loadingMapping) QueueAutoApply(false);
    }

    InputMapping ReadInputMapping()
    {
        var targets = new char[buttonRows.Count];
        int suppress = 0;
        for (int i = 0; i < buttonRows.Count; i++)
        {
            var (target, consume) = buttonRows[i];
            targets[i] = target.SelectedIndex > 0 ? (char)('A' + target.SelectedIndex - 1) : '-';
            if (targets[i] != '-' && consume.IsChecked == true) suppress |= 1 << i;
        }
        return mappingDraft with
        {
            Buttons = new string(targets), Suppress = suppress,
            LeftStage = new(leftStageTarget.SelectedIndex - 1, (int)leftStageThreshold.Value),
            RightStage = new(rightStageTarget.SelectedIndex - 1, (int)rightStageThreshold.Value)
        };
    }

    void LoadInputMapping(InputMapping mapping)
    {
        ++calibrationRun;
        measuredCenters = null;
        calibrating = false;
        mappingDraft = mapping;
        loadingMapping = true;
        try
        {
            for (int i = 0; i < buttonRows.Count; i++)
            {
                var (target, suppress) = buttonRows[i];
                target.SelectedIndex = mapping.Buttons[i] == '-' ? 0 : mapping.Buttons[i] - 'A' + 1;
                suppress.IsChecked = (mapping.Suppress & (1 << i)) != 0;
            }
            leftStageThreshold.Value = mapping.LeftStage.Threshold;
            rightStageThreshold.Value = mapping.RightStage.Threshold;
            leftStageTarget.SelectedIndex = mapping.LeftStage.Target + 1;
            rightStageTarget.SelectedIndex = mapping.RightStage.Target + 1;
        }
        finally { loadingMapping = false; }
        calibrationButton.Content = L.T("Измерить центр стиков");
        calibrationButton.IsEnabled = !preview;
        resetCalibrationButton.IsEnabled = !preview;
        calibrationStatus.Text = L.T("Белая точка — сигнал стика, синяя — результат коррекции.");
    }

    async void CalibrateSticks()
    {
        if (preview || calibrating) return;
        int run = ++calibrationRun;
        calibrating = true;
        calibrationButton.IsEnabled = resetCalibrationButton.IsEnabled = false;
        bool measuringRange = measuredCenters != null;
        calibrationStatus.Text = measuringRange
            ? L.T("Вращайте оба стика по краю до конца замера.")
            : L.T("Отпустите оба стика. Измеряем центр…");
        try
        {
            var samples = new List<int[]>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < (measuringRange ? 5500 : 1400))
            {
                await Task.Delay(45);
                if (closed || run != calibrationRun) return;
                var sample = latestInputPreview;
                if (!sample.Fresh || sample.AgeMs + sampleAge.ElapsedMilliseconds > 200)
                    throw new InvalidOperationException(L.T("Нет свежих данных. Подключите контроллер и повторите."));
                samples.Add(new[] { sample.LeftX, sample.LeftY, sample.RightX, sample.RightY });
            }
            if (!measuringRange)
            {
                if (Enumerable.Range(0, 4).Any(i => samples.Max(s => s[i]) - samples.Min(s => s[i]) > 12))
                    throw new InvalidOperationException(L.T("Стики двигались. Отпустите их и повторите замер центра."));
                measuredCenters = Enumerable.Range(0, 4).Select(i => (int)Math.Round(samples.Average(s => s[i]))).ToArray();
                calibrationButton.Content = L.T("Измерить полный ход");
                calibrationStatus.Text = L.T("Центр записан. Запустите замер и вращайте оба стика по краю.");
                return;
            }
            var axes = Enumerable.Range(0, 4).Select(i =>
                new AxisCalibration(samples.Min(s => s[i]), measuredCenters![i], samples.Max(s => s[i]))).ToArray();
            if (axes.Any(axis => !axis.Valid))
                throw new InvalidOperationException(L.T("Не удалось измерить полный ход. Повторите и доводите стики до края."));
            LoadInputMapping(ReadInputMapping() with { LeftX = axes[0], LeftY = axes[1], RightX = axes[2], RightY = axes[3] });
            calibrationStatus.Text = L.T("Калибровка рассчитана. Сравните точки на графиках.");
            QueueAutoApply(false);
        }
        catch (InvalidOperationException error)
        {
            calibrationStatus.Text = error.Message;
        }
        finally
        {
            if (run == calibrationRun && !closed)
            {
                calibrating = false;
                calibrationButton.IsEnabled = resetCalibrationButton.IsEnabled = true;
            }
        }
    }
}
