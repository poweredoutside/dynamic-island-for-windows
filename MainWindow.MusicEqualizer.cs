using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIslandWin.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private bool _installingEqualizer;

    private static readonly (string Label, int[] Gains)[] EqualizerPresets =
    [
        ("Ровный звук", [0, 0, 0, 0, 0]),
        ("Больше баса", [6, 3, 0, 0, 0]),
        ("Чёткий голос", [-3, -1, 3, 4, 0]),
        ("Больше высоких", [0, 0, 0, 3, 5])
    ];

    private void BuildMusicEqualizer(Panel parent)
    {
        var expand = new Expander { Header = L("Эквалайзер"), Margin = new Thickness(0, 10, 0, 0) };
        expand.SetResourceReference(Expander.ForegroundProperty, "ThemeTextBrush");
        var controls = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        expand.Content = controls;
        parent.Children.Add(expand);

        var status = FeatureText("", 11, "ThemeMutedBrush");
        controls.Children.Add(status);
        var enabled = new CheckBox { Content = L("Включить эквалайзер"), IsChecked = _settings.MusicEqualizerEnabled, Margin = new Thickness(0, 0, 0, 8) };
        enabled.SetResourceReference(CheckBox.ForegroundProperty, "ThemeTextBrush");
        controls.Children.Add(enabled);
        var presets = new ComboBox { Style = (Style)FindResource("FeatureCombo"), MinHeight = 34, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(presets, L("Профиль эквалайзера"));
        presets.ItemsSource = new[] { L("Своя настройка") }.Concat(EqualizerPresets.Select(preset => L(preset.Label)));
        controls.Children.Add(presets);

        int[] gains = MusicEqualizer.Normalize(_settings.MusicEqualizerGains);
        var sliders = new Slider[gains.Length];
        var bands = new UniformGrid { Columns = gains.Length };
        controls.Children.Add(bands);
        bool selectingPreset = false;
        for (int index = 0; index < gains.Length; index++)
        {
            int frequency = MusicEqualizer.Frequencies[index];
            string label = string.Format(L("{0} Гц"), frequency);
            var column = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
            var slider = PlayerSlider(label, MusicEqualizer.Maximum);
            sliders[index] = slider;
            slider.Minimum = MusicEqualizer.Minimum;
            slider.TickFrequency = 1;
            slider.IsSnapToTickEnabled = true;
            slider.IsEnabled = true;
            slider.Value = gains[index];
            var caption = FeatureText("", 11, "ThemeMutedBrush");
            RefreshCaption();
            slider.ValueChanged += (_, _) =>
            {
                RefreshCaption();
                if (!selectingPreset) MatchPreset();
            };
            column.Children.Add(caption);
            column.Children.Add(slider);
            bands.Children.Add(column);

            void RefreshCaption() => caption.Text = $"{label} · {slider.Value:+0;-0;0} dB";
        }

        presets.SelectionChanged += (_, _) =>
        {
            if (selectingPreset || presets.SelectedIndex <= 0) return;
            selectingPreset = true;
            var curve = EqualizerPresets[presets.SelectedIndex - 1].Gains;
            for (int index = 0; index < sliders.Length; index++) sliders[index].Value = curve[index];
            selectingPreset = false;
        };
        MatchPreset();

        EqualizerApoInstaller.Installation? installation = null;
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var apply = FeatureButton("Применить эквалайзер");
        var get = FeatureButton("Установить Equalizer APO");
        var devices = FeatureButton("Выходы Equalizer APO", () =>
        {
            if (installation?.DeviceSelector is not { } selector) return;
            Process.Start(new ProcessStartInfo(selector) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(selector)! });
        });
        actions.Children.Add(apply);
        actions.Children.Add(get);
        actions.Children.Add(devices);
        controls.Children.Add(actions);
        apply.Click += (_, e) => { e.Handled = true; Apply(); };
        get.Click += async (_, e) => { e.Handled = true; await DownloadEqualizerAsync(); };
        expand.Expanded += (_, _) => DetectInstallation();

        void MatchPreset()
        {
            int[] curve = ReadCurve();
            selectingPreset = true;
            presets.SelectedIndex = 1 + Array.FindIndex(EqualizerPresets, preset => preset.Gains.SequenceEqual(curve));
            selectingPreset = false;
        }

        int[] ReadCurve() => sliders.Select(slider => (int)slider.Value).ToArray();

        void DetectInstallation()
        {
            installation = EqualizerApoInstaller.FindInstallation();
            apply.Visibility = installation != null ? Visibility.Visible : Visibility.Collapsed;
            get.Visibility = installation == null ? Visibility.Visible : Visibility.Collapsed;
            devices.Visibility = installation?.DeviceSelector != null ? Visibility.Visible : Visibility.Collapsed;
            status.Text = L(installation == null ? "Установите Equalizer APO и выберите устройство воспроизведения."
                : "Эквалайзер действует на все приложения на выходах, выбранных в Equalizer APO.");
        }

        async Task DownloadEqualizerAsync()
        {
            if (_installingEqualizer || _windowClosed) return;
            DetectInstallation();
            if (installation != null) return;
            _installingEqualizer = true;
            get.IsEnabled = false;
            status.Text = L("Скачивание Equalizer APO…");
            try
            {
                using var setup = await EqualizerApoInstaller.DownloadAsync();
                if (_windowClosed) return;
                status.Text = L("Выберите устройство воспроизведения в установщике Equalizer APO.");
                await setup.RunAsync(DesktopProcess.PhysicalFilePath);
                if (!_windowClosed) DetectInstallation();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException
                or System.ComponentModel.Win32Exception or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Error("Install Equalizer APO", ex);
                if (!_windowClosed) status.Text = L("Установка Equalizer APO не завершилась. Нажмите кнопку, чтобы повторить.");
            }
            finally { _installingEqualizer = false; if (!_windowClosed) get.IsEnabled = true; }
        }

        void Apply()
        {
            DetectInstallation();
            if (installation == null) return;
            int[] curve = ReadCurve();
            bool isEnabled = enabled.IsChecked == true;
            try { MusicEqualizer.Apply(installation, curve, isEnabled); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                AppLog.Error("Apply music equalizer", ex);
                status.Text = L("Не удалось записать настройки Equalizer APO.");
                return;
            }
            _settings.MusicEqualizerGains = curve;
            _settings.MusicEqualizerEnabled = isEnabled;
            SaveSettings();
            status.Text = L(isEnabled ? "Настройки переданы в Equalizer APO." : "Эквалайзер островка выключен.");
        }
    }
}
