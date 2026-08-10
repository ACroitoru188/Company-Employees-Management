namespace CompanyEmployees.Web.Components;

/// <summary>
/// Section names for Fluent's <c>IMessageService</c>.
///
/// The service holds one list per circuit and a <c>FluentMessageBarProvider</c> without a
/// <c>Section</c> renders every message in it — including the ones meant for the bell. Naming
/// both providers is what keeps the notification centre out of the page-level message bars.
/// </summary>
internal static class MessageSections
{
    /// <summary>Page-level bars, rendered by the provider in <c>EmployeeProviders</c>.</summary>
    public const string Page = "page";

    /// <summary>The bell's notification centre.</summary>
    public const string NotificationCenter = "notification-center";
}
