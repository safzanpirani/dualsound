using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DualSound.Audio;
using DualSound.Services;

namespace DualSound;

public partial class MainWindow : Window
{
    private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(100, 107, 100));
    private static readonly Brush SignalBrush = new SolidColorBrush(Color.FromRgb(233, 255, 106));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 116, 96));

    private readonly AudioMirrorService _audioMirror = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _statusTimer;
    private List<SelectableAudioDevice> _targetDevices = [];
    private AppSettings _settings = new();
    private DateTimeOffset? _startedAt;
    private bool _isClosing;
    private bool _isBusy;
    private bool _suppressSelectionChange;

    public MainWindow()
    {
        InitializeComponent();

        _audioMirror.Faulted += AudioMirror_Faulted;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        try
        {
            var devices = _audioMirror.GetActiveRenderDevices();
            var primary = devices.FirstOrDefault(device => device.IsDefault);
            var targets = devices.Where(device => !device.IsDefault).ToArray();

            PrimaryDeviceName.Text = primary?.Name ?? "No default output found";

            _suppressSelectionChange = true;
            var rememberedIds = _settings.TargetDeviceIds
                .Take(AudioMirrorService.MaxAdditionalOutputs)
                .ToHashSet(StringComparer.Ordinal);
            _targetDevices = targets
                .Select(target => new SelectableAudioDevice(target, rememberedIds.Contains(target.Id)))
                .ToList();
            TargetDeviceRack.ItemsSource = _targetDevices;
            _suppressSelectionChange = false;
            UpdateSelectionCount();

            if (primary is null)
            {
                SetError("Windows has no active default playback device.");
            }
            else if (targets.Length == 0)
            {
                SetError("Connect another playback device, then press Refresh.");
            }
            else
            {
                SetIdle();
            }
        }
        catch (Exception ex)
        {
            _suppressSelectionChange = false;
            PrimaryDeviceName.Text = "Audio devices unavailable";
            _targetDevices = [];
            TargetDeviceRack.ItemsSource = null;
            SetError(ex.Message);
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        ToggleButton.IsEnabled = false;

        try
        {
            if (_audioMirror.IsRunning)
            {
                await StopMirroringAsync();
            }
            else
            {
                var targets = SelectedTargets;
                if (targets.Count == 0)
                {
                    return;
                }

                _audioMirror.Start(targets.Select(target => target.Id));
                _startedAt = DateTimeOffset.Now;
                _statusTimer.Start();
                TargetDeviceRack.IsEnabled = false;
                RefreshButton.IsEnabled = false;
                StatusDot.Fill = SignalBrush;
                StatusTitle.Text = "MIRRORING";
                StatusTitle.Foreground = SignalBrush;
                StatusDetail.Text = $"Sending audio to {targets.Count} additional output{(targets.Count == 1 ? string.Empty : "s")}";
                NoticeText.Text = "Keep this app open. Changing the Windows default output will stop this session safely.";
                ToggleButton.Content = "STOP MIRRORING";
                ToggleButton.Background = new SolidColorBrush(Color.FromRgb(232, 228, 216));
            }
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _isBusy = false;
            ToggleButton.IsEnabled = _audioMirror.IsRunning || SelectedTargets.Count > 0;
        }
    }

    private async Task StopMirroringAsync()
    {
        _statusTimer.Stop();
        await _audioMirror.StopAsync();
        _startedAt = null;
        UptimeText.Text = "00:00:00";
        TargetDeviceRack.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        ToggleButton.Content = "START MIRRORING";
        ToggleButton.Background = SignalBrush;
        NoticeText.Text = "Bluetooth devices can add their own delay. Speakers and wired headphones work best together.";
        SetIdle();
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (_startedAt is not null)
        {
            UptimeText.Text = (DateTimeOffset.Now - _startedAt.Value).ToString(@"hh\:mm\:ss");
        }

        try
        {
            if (_audioMirror.IsRunning && _audioMirror.SourceDeviceId != _audioMirror.GetDefaultRenderDeviceId())
            {
                _ = StopForDeviceChangeAsync();
            }
        }
        catch (Exception ex)
        {
            _ = StopForDeviceFailureAsync(ex.Message);
        }
    }

    private async Task StopForDeviceChangeAsync()
    {
        await StopMirroringAsync();
        RefreshDevices();
        SetError("Windows changed the default output. Check the rack and start again.");
    }

    private async Task StopForDeviceFailureAsync(string message)
    {
        await StopMirroringAsync();
        SetError(message);
    }

    private void AudioMirror_Faulted(object? sender, string message)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_audioMirror.IsRunning)
            {
                await StopMirroringAsync();
            }

            SetError(message);
        });
    }

    private void TargetDevice_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionChange)
        {
            return;
        }

        if (SelectedTargets.Count > AudioMirrorService.MaxAdditionalOutputs && sender is System.Windows.Controls.CheckBox checkBox)
        {
            _suppressSelectionChange = true;
            checkBox.IsChecked = false;
            _suppressSelectionChange = false;
            NoticeText.Text = "The Windows default plus seven selected outputs is the eight-device limit.";
        }

        SaveTargetSelection();
    }

    private void TargetDevice_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_suppressSelectionChange)
        {
            SaveTargetSelection();
        }
    }

    private void SaveTargetSelection()
    {
        _settings.TargetDeviceIds = SelectedTargets.Select(target => target.Id).ToList();
        _settingsStore.Save(_settings);
        UpdateSelectionCount();

        if (!_audioMirror.IsRunning && !_isBusy)
        {
            SetIdle();
        }
    }

    private IReadOnlyList<AudioDeviceInfo> SelectedTargets =>
        _targetDevices.Where(target => target.IsSelected).Select(target => target.Device).ToArray();

    private void UpdateSelectionCount()
    {
        SelectionCountText.Text = $"{SelectedTargets.Count} / {AudioMirrorService.MaxAdditionalOutputs} SELECTED";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void SetIdle()
    {
        StatusDot.Fill = IdleBrush;
        StatusTitle.Text = "READY";
        StatusTitle.Foreground = (Brush)FindResource("Paper");
        StatusDetail.Text = SelectedTargets.Count > 0
            ? $"{SelectedTargets.Count + 1} total outputs ready"
            : "Choose at least one additional output";
        ToggleButton.IsEnabled = SelectedTargets.Count > 0;
    }

    private void SetError(string message)
    {
        StatusDot.Fill = ErrorBrush;
        StatusTitle.Text = "CHECK SETUP";
        StatusTitle.Foreground = ErrorBrush;
        StatusDetail.Text = message;
        ToggleButton.IsEnabled = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        await _audioMirror.DisposeAsync();
        Close();
    }

    private sealed class SelectableAudioDevice(AudioDeviceInfo device, bool isSelected)
    {
        public AudioDeviceInfo Device { get; } = device;
        public string Id => Device.Id;
        public string Name => Device.Name;
        public bool IsSelected { get; set; } = isSelected;
    }
}
