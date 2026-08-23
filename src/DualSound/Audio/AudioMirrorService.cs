using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSound.Audio;

public sealed class AudioMirrorService : IAsyncDisposable
{
    public const int MaxAdditionalOutputs = 7;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly object _gate = new();
    private readonly List<OutputChannel> _outputs = [];
    private WasapiRecorder? _recorder;
    private MMDevice? _sourceDevice;
    private bool _stopping;

    public bool IsRunning { get; private set; }
    public string? SourceDeviceId { get; private set; }
    public IReadOnlyList<string> TargetDeviceIds => _outputs.Select(output => output.Device.ID).ToArray();

    public event EventHandler<string>? Faulted;

    public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        using var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var defaultId = defaultDevice.ID;

        return _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public string GetDefaultRenderDeviceId()
    {
        using var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.ID;
    }

    public void Start(IEnumerable<string> targetDeviceIds)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Audio mirroring is already running.");
        }

        var targetIds = targetDeviceIds.Distinct(StringComparer.Ordinal).ToArray();
        if (targetIds.Length is < 1 or > MaxAdditionalOutputs)
        {
            throw new InvalidOperationException($"Choose between 1 and {MaxAdditionalOutputs} additional output devices.");
        }

        _sourceDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        if (targetIds.Contains(_sourceDevice.ID, StringComparer.Ordinal))
        {
            DisposeSourceDevice();
            throw new InvalidOperationException("Additional outputs must be different from the Windows default output.");
        }

        try
        {
            _recorder = new WasapiRecorderBuilder()
                .WithDevice(_sourceDevice)
                .WithLoopbackCapture()
                .WithBufferLength(30)
                .WithMmcssThreadPriority("Audio")
                .Build();

            foreach (var targetId in targetIds)
            {
                var targetDevice = _deviceEnumerator.GetDevice(targetId);
                WasapiPlayer? player = null;
                try
                {
                    var buffer = new BufferedWaveProvider(_recorder.WaveFormat, TimeSpan.FromMilliseconds(300))
                    {
                        DiscardOnBufferOverflow = true,
                        ReadFully = true
                    };

                    player = new WasapiPlayerBuilder()
                        .WithDevice(targetDevice)
                        .WithSharedMode()
                        .WithEventSync()
                        .WithLatency(40)
                        .WithMmcssThreadPriority("Audio")
                        .Build();

                    var output = new OutputChannel(targetDevice, player, buffer);
                    output.PlaybackStoppedHandler = (_, args) => OnPlaybackStopped(output, args);
                    player.PlaybackStopped += output.PlaybackStoppedHandler;
                    player.Init(buffer);
                    _outputs.Add(output);
                }
                catch
                {
                    player?.Dispose();
                    targetDevice.Dispose();
                    throw;
                }
            }

            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += OnRecordingStopped;

            foreach (var output in _outputs)
            {
                output.Player.Play();
            }

            _recorder.StartRecording();
            SourceDeviceId = _sourceDevice.ID;
            IsRunning = true;
        }
        catch
        {
            CleanupFailedStart();
            throw;
        }
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        try
        {
            IsRunning = false;

            if (_recorder is not null)
            {
                _recorder.DataAvailable -= OnDataAvailable;
                _recorder.RecordingStopped -= OnRecordingStopped;
                _recorder.StopRecording();
                await _recorder.DisposeAsync();
                _recorder = null;
            }

            foreach (var output in _outputs)
            {
                output.Player.PlaybackStopped -= output.PlaybackStoppedHandler;
                output.Player.Stop();
                await output.Player.DisposeAsync();
                output.Device.Dispose();
            }

            _outputs.Clear();
            SourceDeviceId = null;
            DisposeSourceDevice();
        }
        finally
        {
            lock (_gate)
            {
                _stopping = false;
            }
        }
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags _, long __, long ___)
    {
        foreach (var output in _outputs)
        {
            output.Buffer.AddSamples(data);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null && !_stopping)
        {
            Faulted?.Invoke(this, $"Windows stopped audio capture: {args.Exception.Message}");
        }
    }

    private void OnPlaybackStopped(OutputChannel output, StoppedEventArgs args)
    {
        if (args.Exception is not null && !_stopping)
        {
            Faulted?.Invoke(this, $"{output.Device.FriendlyName} stopped: {args.Exception.Message}");
        }
    }

    private void CleanupFailedStart()
    {
        if (_recorder is not null)
        {
            _recorder.DataAvailable -= OnDataAvailable;
            _recorder.RecordingStopped -= OnRecordingStopped;
            _recorder.Dispose();
            _recorder = null;
        }

        foreach (var output in _outputs)
        {
            output.Player.PlaybackStopped -= output.PlaybackStoppedHandler;
            output.Player.Dispose();
            output.Device.Dispose();
        }

        _outputs.Clear();
        DisposeSourceDevice();
    }

    private void DisposeSourceDevice()
    {
        _sourceDevice?.Dispose();
        _sourceDevice = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _deviceEnumerator.Dispose();
    }

    private sealed class OutputChannel(
        MMDevice device,
        WasapiPlayer player,
        BufferedWaveProvider buffer)
    {
        public MMDevice Device { get; } = device;
        public WasapiPlayer Player { get; } = player;
        public BufferedWaveProvider Buffer { get; } = buffer;
        public EventHandler<StoppedEventArgs> PlaybackStoppedHandler { get; set; } = null!;
    }
}
