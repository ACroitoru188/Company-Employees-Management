using Bunit;
using CompanyEmployees.Web.Components.Employee;
using CompanyEmployees.Web.Models;
using CompanyEmployees.Web.Services;

namespace CompanyEmployees.Web.Tests;

// The pop-up behind a Team row. It decides three things: whether the person is shown as away
// or available, whether the Manager chip appears, and that "Show profile" stays disabled —
// that button is a placeholder for a page that does not exist yet, and a button that silently
// goes nowhere is worse than one that says it is not ready.
public class TeamMemberDialogTests : WebTestContext
{
    [Fact]
    public void Shows_the_leave_period_for_somebody_who_is_away()
    {
        var component = Render(Member(
            type: LeaveType.Annual,
            start: new DateOnly(2026, 9, 22),
            end: new DateOnly(2026, 9, 26)));

        Assert.Contains("Sep 22", component.Markup);
        Assert.Contains("Sep 26, 2026", component.Markup);
        Assert.DoesNotContain("Available", component.Markup);
    }

    [Fact]
    public void Says_Available_when_there_is_no_upcoming_leave()
    {
        var component = Render(Member());

        Assert.Contains("Available", component.Markup);
    }

    [Fact]
    public void Shows_the_Manager_chip_only_for_the_manager()
    {
        var manager = Render(Member(isManager: true));
        var colleague = Render(Member(isManager: false));

        Assert.Contains("Manager", manager.Markup);
        Assert.DoesNotContain("<fluent-badge", colleague.Markup);
    }

    [Fact]
    public void Show_profile_is_rendered_but_disabled()
    {
        // The whole point of the button existing: it marks where the profile page will go.
        // If it ever becomes clickable by accident, this fails.
        var component = Render(Member());

        var button = component.FindAll("fluent-button")
            .Single(element => element.TextContent.Contains("Show profile"));

        Assert.True(button.HasAttribute("disabled"));
    }

    [Fact]
    public void Shows_the_name_and_the_role_label_it_was_given()
    {
        // The caller composes and translates the label; the dialog only prints it.
        var component = Render(Member(), roleLabel: "Employee · Design");

        Assert.Contains("Demo Colleague", component.Markup);
        Assert.Contains("Employee · Design", component.Markup);
    }

    private IRenderedComponent<TeamMemberDialog> Render(
        TeamRosterEntry member, string roleLabel = "Employee · Design") =>
        RenderComponent<TeamMemberDialog>(parameters => parameters
            .Add(dialog => dialog.Content, new TeamMemberDialog.Model
            {
                Member = member,
                RoleLabel = roleLabel
            }));

    private static TeamRosterEntry Member(
        bool isManager = false,
        LeaveType? type = null,
        DateOnly? start = null,
        DateOnly? end = null) =>
        new(Guid.NewGuid(), "Demo Colleague", "DC", "Employee · Design", isManager, type, start, end);
}
