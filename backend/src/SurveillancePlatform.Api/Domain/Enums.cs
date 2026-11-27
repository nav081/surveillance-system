namespace SurveillancePlatform.Api.Domain;

public enum UserRole
{
    Admin,
    Viewer
}

public enum CameraStatus
{
    Active,
    Inactive
}

/// <summary>
/// Direction a person must cross the virtual counting line to be counted as an entry.
/// TopToBottom: frame top -> frame bottom is an entry. BottomToTop: frame bottom -> frame top is an entry.
/// </summary>
public enum EntryDirection
{
    TopToBottom,
    BottomToTop
}
