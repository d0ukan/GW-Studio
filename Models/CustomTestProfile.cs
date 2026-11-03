namespace Dou_GW_Studio.Models;
public sealed record CustomTestStep(int Channel, double Voltage, double CurrentLimit, bool OutputOn, int DurationMs);
public sealed class CustomTestProfile
{
    public string Name { get; set; } = "Custom profile";
    public string TargetModel { get; set; } = "";
    public ExecutionBackend Backend { get; set; } = ExecutionBackend.PcTimed;
    public List<CustomTestStep> Steps { get; set; } = [];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetModel))
            throw new ArgumentException("This is a legacy profile without a target model. Rebuild and save it after connecting the intended instrument.");
        Validate(DeviceProfiles.Resolve(TargetModel));
    }
    public void Validate(DeviceProfile profile)
    {
        if (!string.Equals(TargetModel, profile.ModelName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Profile targets {TargetModel}, but the connected instrument is {profile.ModelName}.");
        if (Backend != ExecutionBackend.PcTimed) throw new NotSupportedException("Only PC Timed profiles can run in this release.");
        if (Steps is null || Steps.Count is < 1 or > 10000) throw new ArgumentException("A profile must contain 1–10000 steps.");
        foreach (var s in Steps)
        {
            if (s is null) throw new ArgumentException("Invalid null step.");
            profile.Validate(s.Channel, s.Voltage, s.CurrentLimit);
            if (s.DurationMs < 0 || s.DurationMs > 86400000) throw new ArgumentException("Step duration must be 0–86400000 ms.");
        }
        if (Steps.Sum(s => (long)s.DurationMs) > 604800000) throw new ArgumentException("A profile may last at most 7 days.");
    }
}
public sealed record TestProgress(int Step, int TotalSteps, TimeSpan Elapsed, TimeSpan NominalRemaining, CustomTestStep? Requested, string State);

public sealed record SavedProfileInfo(string Name, string FilePath, DateTime LastModified)
{
    public string Display => $"{Name}   •   {LastModified:yyyy-MM-dd HH:mm}";
}
