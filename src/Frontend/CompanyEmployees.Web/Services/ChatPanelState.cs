namespace CompanyEmployees.Web.Services;

/// <summary>
/// Lets a page ask the floating chat panel to open on somebody. Scoped, so the page and the
/// panel — which lives in the layout — share one instance per circuit.
/// </summary>
/// <remarks>
/// A service rather than a query-string parameter: the panel is not a page, so putting the open
/// conversation in the URL would make every chat a navigation, reload the page behind it, and
/// leave the address bar pointing at a conversation long after it was closed.
/// </remarks>
public sealed class ChatPanelState
{
    /// <summary>Raised when something asks for the panel to open on a particular person.</summary>
    public event Func<Guid, Task>? OpenRequested;

    public Task OpenAsync(Guid userId) =>
        OpenRequested is null ? Task.CompletedTask : OpenRequested.Invoke(userId);
}
