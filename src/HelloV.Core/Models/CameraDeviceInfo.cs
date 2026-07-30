namespace HelloV.Models;

public enum CameraFacing
{
    Unknown,
    Front,
    Back,
    External
}

public sealed record CameraDeviceInfo(
    string Id,
    string DisplayName,
    CameraFacing Facing,
    int Index = -1)
{
    public override string ToString() => DisplayName;
}
