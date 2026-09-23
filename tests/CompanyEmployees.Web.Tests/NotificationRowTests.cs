using Bunit;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Web.Components;

namespace CompanyEmployees.Web.Tests;

// The shared row behind the bell and the history page. Three things it decides on its own:
// whether the unread dot is drawn, whether the navigation chevron is, and that the message
// actually reaches the markup.
//
// Selectors rather than substring matching, and they follow what Fluent actually emits:
// FluentBadge is a <fluent-badge> custom element, but FluentIcon is an inline <svg> — checked
// against the rendered markup, not assumed from the component name.
public class NotificationRowTests : WebTestContext
{
    [Fact]
    public void Shows_the_unread_dot_on_an_unread_notification()
    {
        var component = Render(Unread());

        Assert.Single(component.FindAll("fluent-badge"));
    }

    [Fact]
    public void Draws_no_dot_once_the_notification_has_been_read()
    {
        var component = Render(Unread(read: true));

        Assert.Empty(component.FindAll("fluent-badge"));
    }

    [Fact]
    public void Renders_the_message_text()
    {
        var component = Render(Unread());

        Assert.Contains("Your leave was approved", component.Markup);
    }

    [Fact]
    public void Offers_the_navigation_chevron_only_when_there_is_somewhere_to_go()
    {
        var withUrl = Render(Unread(actionUrl: "/employee/my-requests"), showHint: true);
        var withoutUrl = Render(Unread(actionUrl: null), showHint: true);

        Assert.Single(withUrl.FindAll("svg"));
        Assert.Empty(withoutUrl.FindAll("svg"));
    }

    [Fact]
    public void Hides_the_chevron_when_the_caller_does_not_want_it()
    {
        // The bell renders its own affordance, so the row must be able to stay plain even
        // when the notification does have a target.
        var component = Render(Unread(actionUrl: "/employee/my-requests"), showHint: false);

        Assert.Empty(component.FindAll("svg"));
    }

    private IRenderedComponent<NotificationRow> Render(Notification notification, bool showHint = false) =>
        RenderComponent<NotificationRow>(parameters => parameters
            .Add(row => row.Notification, notification)
            .Add(row => row.ShowNavigationHint, showHint));

    private static Notification Unread(bool read = false, string? actionUrl = "/employee/my-requests") => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Message = "Your leave was approved",
        IsRead = read,
        CreatedAt = new DateTime(2026, 9, 17, 8, 40, 0, DateTimeKind.Utc),
        ActionUrl = actionUrl
    };
}
