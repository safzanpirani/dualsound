namespace DualSound.Services;

public sealed class AppSettings
{
    public List<string> TargetDeviceIds { get; set; } = [];

    // Kept for one release so settings from the original single-output build migrate cleanly.
    public string? TargetDeviceId { get; set; }
}
