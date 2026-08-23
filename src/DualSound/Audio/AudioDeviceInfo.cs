namespace DualSound.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault)
{
    public string DisplayName => IsDefault ? $"{Name}  ·  Windows default" : Name;
}
