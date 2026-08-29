using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SwitchBotMeter.Services;
using SwitchBotMeter.ViewModels;

namespace SwitchBotMeter;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private MainViewModel viewModel;
    private readonly WindowSettingsStore windowSettingsStore = new();

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            viewModel = new MainViewModel();
            DataContext = viewModel;
            ApplyWindowSettings();
            Title = $"{Title} v{AppVersion.Version}";

            // グラフはウィンドウが実際のサイズを持ってから初期化する（コンストラクタ内で
            // 呼ぶとチャートのプロット領域がゼロサイズのまま初期化され、以後崩れるため）
            Loaded += (_, _) => viewModel.RefreshGraphSeries();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainWindow Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            MessageBox.Show($"エラーが発生しました:\n{ex.Message}", "SwitchBotMeter");
            throw;
        }
    }

    private void ApplyWindowSettings()
    {
        var settings = windowSettingsStore.Load();
        if (settings == null || settings.Width <= 0 || settings.Height <= 0)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        Width = Math.Max(settings.Width, MinWidth);
        Height = Math.Max(settings.Height, MinHeight);

        // 保存時と画面構成が変わっている場合に画面外に出ないようチェック
        bool withinBounds =
            settings.Left >= SystemParameters.VirtualScreenLeft &&
            settings.Top >= SystemParameters.VirtualScreenTop &&
            settings.Left + 50 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            settings.Top + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

        if (withinBounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.Left;
            Top = settings.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        var settings = new WindowSettings { IsMaximized = WindowState == WindowState.Maximized };

        if (WindowState == WindowState.Maximized)
        {
            settings.Left = RestoreBounds.Left;
            settings.Top = RestoreBounds.Top;
            settings.Width = RestoreBounds.Width;
            settings.Height = RestoreBounds.Height;
        }
        else
        {
            settings.Left = Left;
            settings.Top = Top;
            settings.Width = Width;
            settings.Height = Height;
        }

        windowSettingsStore.Save(settings);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int useImmersiveDarkMode = 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useImmersiveDarkMode, sizeof(int));
        }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsScanning)
        {
            viewModel.StopScanning();
        }
        else
        {
            viewModel.StartScanning();
        }
    }

    private void MonitorStartButton_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ToggleMonitoring();
    }

    private void MonitorCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        viewModel.UpdateMonitoredDevice();
    }

    private void BrowseOutputFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            OverwritePrompt = false
        };

        try
        {
            var currentPath = viewModel.OutputFilePath;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                dialog.FileName = System.IO.Path.GetFileName(currentPath);
                var dir = System.IO.Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }
        }
        catch
        {
            // 現在のパスが不正な場合はダイアログのデフォルト動作に任せる
        }

        if (dialog.ShowDialog() == true)
        {
            viewModel.OutputFilePath = dialog.FileName;
        }
    }

    private void AliasTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        viewModel.SaveAliasForSelectedDevice();
    }

    private void GraphColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedDevice == null) return;
        if (sender is System.Windows.Controls.Button button && button.Tag is string tag && int.TryParse(tag, out var paletteIndex))
        {
            viewModel.SetDeviceColor(viewModel.SelectedDevice, paletteIndex);
        }
    }

    private void DebugLogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        DebugLogTextBox.ScrollToEnd();
    }

    private bool isLogExpanded = true;

    private void ToggleLogButton_Click(object sender, RoutedEventArgs e)
    {
        isLogExpanded = !isLogExpanded;

        if (isLogExpanded)
        {
            DebugLogRow.Height = new GridLength(200);
            DebugLogPanel.Visibility = Visibility.Visible;
            ToggleLogButton.Content = "▼ スキャン受信状況";
        }
        else
        {
            DebugLogRow.Height = new GridLength(0);
            DebugLogPanel.Visibility = Visibility.Collapsed;
            ToggleLogButton.Content = "▶ スキャン受信状況";
        }
    }

    private void GraphCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        viewModel.RefreshGraphSeries();
    }

    private void GraphPauseButton_Click(object sender, RoutedEventArgs e)
    {
        viewModel.IsGraphPaused = !viewModel.IsGraphPaused;
    }

    private void TimeRangeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // ComboBox の SelectedIndex="0" は InitializeComponent 中（viewModel生成前）に
        // このイベントを発火させるため、null の場合は無視する
        if (viewModel == null) return;

        if (TimeRangeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse<Models.GraphTimeRange>(tag, out var range))
        {
            viewModel.SelectedTimeRange = range;
        }
    }
}
