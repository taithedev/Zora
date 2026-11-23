namespace Zora.Desktop.Services;

public sealed class ZuzifyLink
{
    public bool Linked { get; set; }
    public ZuzifyIdentity? Zuzify { get; set; }
}
public sealed class ZuzifyIdentity
{
    public string UserId { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
}
