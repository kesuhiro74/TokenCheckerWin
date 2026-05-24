namespace TokenChecker.Core;

public enum ProviderStatus
{
    Unknown = 0,
    Available = 1,
    NotInstalled = 2,
    NotLoggedIn = 3,
    Error = 4,
    Unauthorized = 5,
    RateLimited = 6
}
