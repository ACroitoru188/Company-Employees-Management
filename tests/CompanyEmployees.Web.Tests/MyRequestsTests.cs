using Bunit;
using Bunit.TestDoubles;
using CompanyEmployees.Web.Components.Employee.Pages;
using CompanyEmployees.Web.Models;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CompanyEmployees.Web.Tests;

// The Actions column is a set of rules about which button a row may offer, and getting them
// wrong is invisible until somebody stares at the grid. All three states shipped broken at
// some point: an approved row offered nothing, a cancelled row still claimed a cancellation
// was pending, and the button was an unlabelled icon nobody could find.
public class MyRequestsTests : WebTestContext
{
    private readonly ITimeOffService _timeOff = Substitute.For<ITimeOffService>();

    // All of this has to happen before the provider locks, and AddTestAuthorization registers
    // services of its own — so it belongs here rather than in a constructor that runs later.
    protected override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(_timeOff);

        // PageGuard returns false unless the render is interactive, and an unauthenticated
        // visitor is redirected instead of rendered — the page needs both to draw anything.
        //
        // Order matters: AddTestAuthorization registers services, while SetRendererInfo
        // resolves from the provider and locks it, so the registration has to come first.
        this.AddTestAuthorization().SetAuthorized("ion@siemens.com");
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [Fact]
    public async Task An_approved_request_offers_to_ask_HR_for_a_cancellation()
    {
        var page = await RenderWith(Request(RequestStatus.Approved));

        Assert.Contains("Request cancellation", page.Markup);
    }

    [Fact]
    public async Task A_pending_request_can_be_withdrawn_outright()
    {
        // Nobody has decided it yet, so it needs no one's permission — a different button.
        var page = await RenderWith(Request(RequestStatus.Pending));

        Assert.Contains("Cancel request", page.Markup);
        Assert.DoesNotContain("Request cancellation", page.Markup);
    }

    [Fact]
    public async Task A_request_waiting_on_HR_shows_the_pending_marker_and_no_button()
    {
        var page = await RenderWith(Request(
            RequestStatus.Approved, cancellationRequestedAt: DateTime.UtcNow));

        Assert.Contains("cancellation pending", page.Markup);
        Assert.DoesNotContain("Request cancellation", page.Markup);
    }

    [Fact]
    public async Task A_cancelled_request_offers_nothing_and_claims_nothing()
    {
        // CancellationRequestedAt survives the decision, and reading it alone used to leave
        // the pending marker lit on a row that was already cancelled.
        var page = await RenderWith(Request(
            RequestStatus.Cancelled, cancellationRequestedAt: DateTime.UtcNow.AddHours(-2)));

        Assert.DoesNotContain("cancellation pending", page.Markup);
        Assert.DoesNotContain("Request cancellation", page.Markup);
        Assert.DoesNotContain("Cancel request", page.Markup);
    }

    [Fact]
    public async Task Approved_leave_that_has_already_ended_can_no_longer_be_asked_back()
    {
        var page = await RenderWith(Request(
            RequestStatus.Approved,
            start: DateOnly.FromDateTime(DateTime.Today).AddDays(-10),
            end: DateOnly.FromDateTime(DateTime.Today).AddDays(-5)));

        Assert.DoesNotContain("Request cancellation", page.Markup);
    }

    [Fact]
    public async Task A_rejected_request_offers_nothing()
    {
        var page = await RenderWith(Request(RequestStatus.Rejected));

        Assert.DoesNotContain("Request cancellation", page.Markup);
        Assert.DoesNotContain("Cancel request", page.Markup);
    }

    private async Task<IRenderedComponent<MyRequests>> RenderWith(TimeOffRequest request)
    {
        _timeOff.GetMyRequestsAsync().Returns(new[] { request });

        var page = RenderComponent<MyRequests>();

        // The page loads its rows in OnInitializedAsync, so the first render is still empty.
        await page.InvokeAsync(() => { });
        return page;
    }

    private static TimeOffRequest Request(
        RequestStatus status,
        DateTime? cancellationRequestedAt = null,
        DateOnly? start = null,
        DateOnly? end = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = LeaveType.Annual,
        StartDate = start ?? DateOnly.FromDateTime(DateTime.Today).AddDays(7),
        EndDate = end ?? DateOnly.FromDateTime(DateTime.Today).AddDays(9),
        Status = status,
        SubmittedAt = DateTime.UtcNow.AddDays(-1),
        CancellationRequestedAt = cancellationRequestedAt
    };
}
