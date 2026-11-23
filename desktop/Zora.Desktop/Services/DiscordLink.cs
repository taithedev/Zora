namespace Zora.Desktop.Services;

public sealed class DiscordLink
{
    public bool Linked { get; set; }
    public DiscordIdentity? Discord { get; set; }
}
public sealed class DiscordIdentity
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? AvatarHash { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
public sealed class DiscordStartResult
{
    public string Url { get; set; } = "";
    public int ExpiresIn { get; set; }
}
