using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Messaging;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using DomainInvalidOperationException = CompanyEmployees.Domain.Exceptions.InvalidOperationException;

namespace CompanyEmployees.Application.Tests;

// Chat is scoped to the Team page's definition of a team, and that rule has to hold on reads
// as well as writes: the page hides people you may not message, but /employee/messages/{id}
// takes the id straight from the URL. LeaveContext is built for real here rather than faked,
// so these exercise the actual team rule instead of a restatement of it.
public class MessagingContextTests
{
    private readonly IMessageGateway _messages = Substitute.For<IMessageGateway>();
    private readonly IUserGateway _users = Substitute.For<IUserGateway>();
    private readonly IMessageDispatcher _dispatcher = Substitute.For<IMessageDispatcher>();
    private readonly ILeaveRequestGateway _requests = Substitute.For<ILeaveRequestGateway>();
    private readonly IContractGateway _contracts = Substitute.For<IContractGateway>();
    private readonly IManagerDelegationGateway _delegations = Substitute.For<IManagerDelegationGateway>();
    private readonly IPublicHolidayProvider _holidays = Substitute.For<IPublicHolidayProvider>();
    private readonly INotificationGateway _notificationGateway = Substitute.For<INotificationGateway>();
    private readonly INotificationDispatcher _notificationDispatcher = Substitute.For<INotificationDispatcher>();
    private readonly IImpersonationGateway _sessions = Substitute.For<IImpersonationGateway>();
    private readonly IDelegatedActionGateway _delegatedActions = Substitute.For<IDelegatedActionGateway>();

    private static readonly Region Romania = new() { Id = Guid.NewGuid(), Name = "Romania", Code = "RO" };
    private static readonly Region Pakistan = new() { Id = Guid.NewGuid(), Name = "Pakistan", Code = "PK" };

    private readonly List<User> _everyone = new();
    private readonly User _manager;

    public MessagingContextTests()
    {
        _users.GetAllUsersAsync().Returns(_ => _everyone);

        // Returns everyone pointing at that manager, inactive included, so the context's own
        // Active filter is what these tests exercise rather than the gateway's.
        _users.GetDirectReportsAsync(Arg.Any<Guid>()).Returns(call =>
            _everyone.Where(user => user.ManagerId == call.Arg<Guid>()).ToList());

        _manager = NewUser("Elena Manager", UserRole.LineManager);
    }

    // --- sending ----------------------------------------------------------------------

    [Fact]
    public async Task Sends_a_message_to_a_teammate()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        var message = await context.SendMessageAsync(me.Id, colleague.Id, "Poti sa ma acoperi?");

        Assert.Equal(me.Id, message.SenderId);
        Assert.Equal(colleague.Id, message.RecipientId);
        Assert.Equal("Poti sa ma acoperi?", message.Body);
        Assert.Null(message.ReadAt);
        await _messages.Received(1).AddAsync(Arg.Is<ChatMessage>(m => m.Body == "Poti sa ma acoperi?"));
    }

    [Fact]
    public async Task Can_message_your_own_manager()
    {
        // The manager is the first entry the Team page shows, so they must be reachable.
        var me = NewTeamMember("Ion Angajat");
        var context = CreateContext();

        await context.SendMessageAsync(me.Id, _manager.Id, "O intrebare despre concediu.");

        await _messages.Received(1).AddAsync(Arg.Any<ChatMessage>());
    }

    [Fact]
    public async Task A_manager_can_message_their_own_report()
    {
        // The Team roster is not symmetric: your manager is on it, but you are not on theirs.
        // Reusing it directly let a report write to their manager and left the manager unable
        // to reply, which is how this was found.
        var report = NewTeamMember("Ion Angajat");
        var context = CreateContext();

        await context.SendMessageAsync(_manager.Id, report.Id, "Sigur, e in regula.");

        await _messages.Received(1).AddAsync(Arg.Any<ChatMessage>());
    }

    [Fact]
    public async Task Messaging_is_symmetric_in_both_directions()
    {
        var report = NewTeamMember("Ion Angajat");
        var context = CreateContext();

        var mine = await context.GetChatContactsAsync(report.Id);
        var theirs = await context.GetChatContactsAsync(_manager.Id);

        Assert.Contains(mine, contact => contact.Id == _manager.Id);
        Assert.Contains(theirs, contact => contact.Id == report.Id);
    }

    [Fact]
    public async Task A_report_in_another_region_is_not_a_contact()
    {
        // Acting stays regional, reports included.
        var abroad = NewTeamMember("Ahmed Khan");
        abroad.Region = Pakistan;
        abroad.RegionId = Pakistan.Id;
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.SendMessageAsync(_manager.Id, abroad.Id, "Salut"));
    }

    [Fact]
    public async Task An_inactive_report_is_not_a_contact()
    {
        var gone = NewTeamMember("Fost Coleg");
        gone.Status = UserStatus.Inactive;
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.SendMessageAsync(_manager.Id, gone.Id, "Salut"));
    }

    [Fact]
    public async Task Trims_the_body()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        var message = await context.SendMessageAsync(me.Id, colleague.Id, "   salut   ");

        Assert.Equal("salut", message.Body);
    }

    [Fact]
    public async Task Refuses_an_empty_body()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.SendMessageAsync(me.Id, colleague.Id, "   "));

        await _messages.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Refuses_a_body_over_the_limit()
    {
        // The column is nvarchar(2000); rejecting here turns a database truncation error
        // into a message the page can show.
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.SendMessageAsync(me.Id, colleague.Id, new string('x', MessagingContext.MaxBodyLength + 1)));

        await _messages.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Refuses_somebody_outside_your_team()
    {
        var me = NewTeamMember("Ion Angajat");
        var stranger = NewUser("Mihai Strain");   // different manager, so not a teammate
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.SendMessageAsync(me.Id, stranger.Id, "Salut"));

        await _messages.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Refuses_a_teammate_in_another_region()
    {
        // The team rule is region-scoped, so a shared manager across regions is not a team.
        var me = NewTeamMember("Ion Angajat");
        var abroad = NewTeamMember("Ahmed Khan");
        abroad.Region = Pakistan;
        abroad.RegionId = Pakistan.Id;
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.SendMessageAsync(me.Id, abroad.Id, "Salut"));

        await _messages.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Refuses_an_inactive_teammate()
    {
        var me = NewTeamMember("Ion Angajat");
        var gone = NewTeamMember("Fost Coleg");
        gone.Status = UserStatus.Inactive;
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.SendMessageAsync(me.Id, gone.Id, "Salut"));
    }

    [Fact]
    public async Task Refuses_messaging_yourself()
    {
        var me = NewTeamMember("Ion Angajat");
        var context = CreateContext();

        await Assert.ThrowsAsync<DomainInvalidOperationException>(() =>
            context.SendMessageAsync(me.Id, me.Id, "Nota personala"));

        await _messages.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Publishes_the_message_to_the_recipient()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        await context.SendMessageAsync(me.Id, colleague.Id, "Salut");

        await _dispatcher.Received(1).PublishCreatedAsync(colleague.Id, Arg.Any<ChatMessage>());
    }

    [Fact]
    public async Task Still_saves_when_live_delivery_fails()
    {
        // Same contract as the notification paths: the row is the thing that must survive.
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        _dispatcher.PublishCreatedAsync(Arg.Any<Guid>(), Arg.Any<ChatMessage>())
            .Returns<Task>(_ => throw new Exception("circuit gone"));
        var context = CreateContext();

        await context.SendMessageAsync(me.Id, colleague.Id, "Salut");

        await _messages.Received(1).AddAsync(Arg.Any<ChatMessage>());
    }

    // --- reading ----------------------------------------------------------------------

    [Fact]
    public async Task Reading_a_thread_refuses_somebody_outside_your_team()
    {
        // The id comes from the URL, so hiding the person in the UI proves nothing.
        var me = NewTeamMember("Ion Angajat");
        var stranger = NewUser("Mihai Strain");
        var context = CreateContext();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            context.GetConversationAsync(me.Id, stranger.Id));

        await _messages.DidNotReceiveWithAnyArgs()
            .GetConversationAsync(default, default, default, default);
    }

    [Fact]
    public async Task Returns_the_thread_oldest_first()
    {
        // The gateway pages from the newest end because that is the cheap end; a thread is
        // read the other way round.
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");

        var newest = new ChatMessage { Id = Guid.NewGuid(), Body = "trei", CreatedAt = DateTime.UtcNow };
        var middle = new ChatMessage { Id = Guid.NewGuid(), Body = "doi", CreatedAt = DateTime.UtcNow.AddMinutes(-5) };
        var oldest = new ChatMessage { Id = Guid.NewGuid(), Body = "unu", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };

        _messages.GetConversationAsync(me.Id, colleague.Id, Arg.Any<int>(), Arg.Any<DateTime?>())
            .Returns(new List<ChatMessage> { newest, middle, oldest });

        var context = CreateContext();

        var thread = await context.GetConversationAsync(me.Id, colleague.Id);

        Assert.Equal(new[] { "unu", "doi", "trei" }, thread.Select(m => m.Body));
    }

    [Fact]
    public async Task Marking_read_works_even_for_somebody_who_left_your_team()
    {
        // Otherwise their messages would sit unread forever and the badge would never clear.
        var me = NewTeamMember("Ion Angajat");
        var stranger = NewUser("Fost Coleg");
        var context = CreateContext();

        await context.MarkConversationReadAsync(me.Id, stranger.Id);

        await _messages.Received(1).MarkConversationReadAsync(me.Id, stranger.Id);
    }

    [Fact]
    public async Task Marking_read_signals_this_users_other_components()
    {
        // The drawer badge is rendered by a different component on a different page, so
        // without this signal it keeps the count it read just before the thread was opened.
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        await context.MarkConversationReadAsync(me.Id, colleague.Id);

        await _dispatcher.Received(1).PublishReadStateChangedAsync(me.Id);
    }

    [Fact]
    public async Task Marking_read_survives_a_failed_signal()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        _dispatcher.PublishReadStateChangedAsync(Arg.Any<Guid>())
            .Returns<Task>(_ => throw new Exception("circuit gone"));
        var context = CreateContext();

        await context.MarkConversationReadAsync(me.Id, colleague.Id);

        await _messages.Received(1).MarkConversationReadAsync(me.Id, colleague.Id);
    }

    // --- delegation audit -------------------------------------------------------------

    [Fact]
    public async Task Records_the_human_behind_a_borrowed_account()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var stand_in = NewUser("Andrei Delegat");

        var delegation = new ManagerDelegation
        {
            Id = Guid.NewGuid(),
            ManagerId = me.Id,
            Manager = me,
            DelegateId = stand_in.Id,
            Delegate = stand_in,
            StartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-1),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(7),
            IsActive = true
        };
        _delegations.GetByIdAsync(delegation.Id).Returns(delegation);

        var context = CreateContext();

        await context.SendMessageAsync(
            me.Id, colleague.Id, "Scriu in numele lui",
            new ActingOnBehalf(stand_in.Id, delegation.Id));

        await _delegatedActions.Received(1).CreateAsync(Arg.Is<DelegatedAction>(action =>
            action != null
            && action.RealUserId == stand_in.Id
            && action.ActedAsUserId == me.Id
            && action.TargetUserId == colleague.Id
            && action.ActionType == DelegatedActionType.MessageSent));
    }

    [Fact]
    public async Task Writes_no_audit_row_when_nobody_is_borrowing()
    {
        var me = NewTeamMember("Ion Angajat");
        var colleague = NewTeamMember("Ana Popescu");
        var context = CreateContext();

        await context.SendMessageAsync(me.Id, colleague.Id, "Salut");

        await _delegatedActions.DidNotReceive().CreateAsync(Arg.Any<DelegatedAction>());
    }

    // --- fixture ----------------------------------------------------------------------

    // A teammate is an active user in the same region sharing the same manager — that is the
    // rule LeaveContext applies, and these tests run the real one.
    private User NewTeamMember(string name)
    {
        var user = NewUser(name);
        user.Manager = _manager;
        user.ManagerId = _manager.Id;
        return user;
    }

    private User NewUser(string name, UserRole role = UserRole.Employee)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = $"{Guid.NewGuid():N}@siemens.com",
            Role = role,
            Status = UserStatus.Active,
            Region = Romania,
            RegionId = Romania.Id
        };

        _users.GetUserByIdAsync(user.Id).Returns(user);
        _everyone.Add(user);
        return user;
    }

    private MessagingContext CreateContext()
    {
        var notificationContext = new NotificationContext(_notificationGateway, _notificationDispatcher);
        var impersonationContext = new ImpersonationContext(
            NullLogger<ImpersonationContext>.Instance, _sessions, _delegations, _users);
        var delegationGuard = new DelegationGuard(impersonationContext, _delegatedActions);

        var leave = new LeaveContext(
            NullLogger<LeaveContext>.Instance,
            _requests,
            _users,
            _contracts,
            _delegations,
            _holidays,
            notificationContext,
            delegationGuard);

        return new MessagingContext(
            NullLogger<MessagingContext>.Instance,
            _messages,
            _users,
            leave,
            _dispatcher,
            delegationGuard);
    }
}
