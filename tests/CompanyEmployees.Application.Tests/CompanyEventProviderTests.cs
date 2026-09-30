using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CompanyEmployees.Application.Tests;

public class LeaveContextCompanyEventTests
{
    private (LeaveContext Leave, ICompanyEventGateway EventGateway, IUserGateway UserGateway) CreateContext()
    {
        var userGateway = Substitute.For<IUserGateway>();
        var eventGateway = Substitute.For<ICompanyEventGateway>();
        var eventProvider = Substitute.For<ICompanyEventProvider>();

        var notificationContext = new NotificationContext(
            Substitute.For<INotificationGateway>(),
            Substitute.For<INotificationDispatcher>());
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance,
            Substitute.For<IImpersonationGateway>(),
            Substitute.For<IManagerDelegationGateway>(),
            userGateway);
        var delegationGuard = new DelegationGuard(
            impersonationContext,
            Substitute.For<IDelegatedActionGateway>());

        var leave = new LeaveContext(
            NullLogger<LeaveContext>.Instance,
            Substitute.For<ILeaveRequestGateway>(),
            userGateway,
            Substitute.For<IContractGateway>(),
            Substitute.For<IManagerDelegationGateway>(),
            Substitute.For<IPublicHolidayProvider>(),
            notificationContext,
            delegationGuard,
            eventProvider,
            eventGateway);

        return (leave, eventGateway, userGateway);
    }

    [Fact]
    public async Task LeaveContext_GetCompanyEventsAsync_retrieves_events_from_gateway()
    {
        var (leave, eventGateway, userGateway) = CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Test Employee",
            Region = new Region { Code = "RO", Name = "Romania" },
            Role = UserRole.Employee
        };
        userGateway.GetUserByIdAsync(user.Id).Returns(user);

        var expectedEvents = new List<CompanyEvent>
        {
            new(new DateOnly(2026, 10, 12), "Siemens Anniversary", "Founding celebration", true, "Anniversary", "RO")
        };
        eventGateway.GetEventsAsync("RO", 2026).Returns(expectedEvents);

        var actualEvents = await leave.GetCompanyEventsAsync(user.Id, 2026);

        Assert.Single(actualEvents);
        Assert.Equal("Siemens Anniversary", actualEvents[0].Title);
        Assert.Equal(new DateOnly(2026, 10, 12), actualEvents[0].Date);
    }

    [Fact]
    public async Task LeaveContext_CreateCompanyEventAsync_creates_and_returns_event()
    {
        var (leave, eventGateway, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 20);

        eventGateway.CreateAsync(Arg.Any<CompanyEvent>())
            .Returns(callInfo => callInfo.Arg<CompanyEvent>());

        var created = await leave.CreateCompanyEventAsync(
            hrId,
            "Summer Town Hall",
            date,
            "Mid-year review",
            isAnnual: false,
            "TownHall",
            "RO");

        Assert.NotNull(created);
        Assert.Equal("Summer Town Hall", created.Title);
        Assert.Equal(date, created.Date);
        Assert.Equal("TownHall", created.Category);
        Assert.Equal("RO", created.RegionCode);
        await eventGateway.Received(1).CreateAsync(Arg.Is<CompanyEvent>(e => e.Title == "Summer Town Hall"));
    }

    [Fact]
    public async Task LeaveContext_UpdateCompanyEventAsync_updates_existing_event()
    {
        var (leave, eventGateway, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var existing = new CompanyEvent
        {
            Id = eventId,
            Title = "Old Title",
            Date = new DateOnly(2026, 5, 1),
            Category = "General"
        };
        eventGateway.GetByIdAsync(eventId).Returns(existing);

        var newDate = new DateOnly(2026, 5, 10);
        await leave.UpdateCompanyEventAsync(
            hrId,
            eventId,
            "Updated Title",
            newDate,
            "New description",
            isAnnual: true,
            "Milestone",
            null);

        Assert.Equal("Updated Title", existing.Title);
        Assert.Equal(newDate, existing.Date);
        Assert.Equal("Milestone", existing.Category);
        Assert.Null(existing.RegionCode);
        Assert.True(existing.IsAnnual);
        await eventGateway.Received(1).UpdateAsync(existing);
    }

    [Fact]
    public async Task LeaveContext_DeleteCompanyEventAsync_deletes_event()
    {
        var (leave, eventGateway, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var existing = new CompanyEvent
        {
            Id = eventId,
            Title = "Event to delete"
        };
        eventGateway.GetByIdAsync(eventId).Returns(existing);

        await leave.DeleteCompanyEventAsync(hrId, eventId);

        await eventGateway.Received(1).DeleteAsync(eventId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("This title is definitely way too long because it has more than fifty characters in total")]
    public async Task LeaveContext_CreateCompanyEventAsync_validates_title_length_bounds(string title)
    {
        var (leave, _, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);

        await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, title, date, null, false, "General", null));
    }

    [Fact]
    public async Task LeaveContext_CreateCompanyEventAsync_validates_description_length()
    {
        var (leave, _, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var longDescription = new string('x', CompanyEvent.MaxDescriptionLength + 1);

        await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, "Valid Title", date, longDescription, false, "General", null));
    }

    [Theory]
    [InlineData(1990)]
    [InlineData(2050)]
    public async Task LeaveContext_CreateCompanyEventAsync_validates_date_year_range(int year)
    {
        var (leave, _, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(year, 6, 1);

        await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, "Valid Title", date, null, false, "General", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NonExistentCategory")]
    public async Task LeaveContext_CreateCompanyEventAsync_validates_category(string category)
    {
        var (leave, _, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);

        await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, "Valid Title", date, null, false, category, null));
    }

    [Fact]
    public async Task LeaveContext_CreateCompanyEventAsync_rejects_duplicate_event()
    {
        var (leave, eventGateway, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var existing = new List<CompanyEvent>
        {
            new(date, "Summer Gala", null, false, "General", "RO")
        };
        eventGateway.GetAllAsync().Returns(existing);

        var ex = await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, "summer gala", date, null, false, "General", "ro"));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LeaveContext_CreateCompanyEventAsync_rejects_more_than_max_events_on_same_day()
    {
        var (leave, eventGateway, _) = CreateContext();
        var hrId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        var existing = Enumerable.Range(1, CompanyEvent.MaxEventsPerDatePerRegion)
            .Select(i => new CompanyEvent(date, $"Event {i}", null, false, "General", null))
            .ToList();
        eventGateway.GetAllAsync().Returns(existing);

        var ex = await Assert.ThrowsAsync<CompanyEmployees.Domain.Exceptions.InvalidOperationException>(
            () => leave.CreateCompanyEventAsync(hrId, "One Too Many", date, null, false, "General", null));

        Assert.Contains($"maximum of {CompanyEvent.MaxEventsPerDatePerRegion}", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
