using CompanyEmployees.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Notifications
{
    // Singleton: subscribers come from per-circuit components and publishers from scoped
    // contexts, so they only meet in one instance.
    public sealed class NotificationDispatcher : INotificationDispatcher
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, List<Subscription>> _subscribers = new();
        private readonly ILogger<NotificationDispatcher> _logger;

        public NotificationDispatcher(ILogger<NotificationDispatcher> logger)
        {
            _logger = logger;
        }

        public IDisposable Subscribe(Guid userId, Func<NotificationChange, Task> handler)
        {
            var subscription = new Subscription(this, userId, handler);

            lock (_gate)
            {
                if (!_subscribers.TryGetValue(userId, out var handlers))
                {
                    handlers = new List<Subscription>();
                    _subscribers[userId] = handlers;
                }
                handlers.Add(subscription);
            }

            return subscription;
        }

        public Task PublishCreatedAsync(Guid userId, Notification notification) =>
            PublishAsync(userId, NotificationChange.ForCreated(notification));

        public Task PublishReadStateChangedAsync(Guid userId) =>
            PublishAsync(userId, NotificationChange.ReadStateChanged);

        private Task PublishAsync(Guid userId, NotificationChange change)
        {
            Subscription[] targets;

            lock (_gate)
            {
                // Nobody watching: the row is already saved, so the next page load shows it.
                if (!_subscribers.TryGetValue(userId, out var handlers))
                    return Task.CompletedTask;

                targets = handlers.ToArray();
            }

            // Not awaited: a handler ends in a Blazor render, so awaiting would make the
            // manager's approval wait on the requester's browser, and hang on a dead circuit.
            foreach (var target in targets)
                _ = DeliverAsync(target, change);

            return Task.CompletedTask;
        }

        private async Task DeliverAsync(Subscription target, NotificationChange change)
        {
            try
            {
                await target.Handler(change);
            }
            catch (Exception ex)
            {
                // A circuit that faults here is gone; retrying it every publish would keep
                // its captured component graph alive.
                _logger.LogWarning(ex,
                    "Notification delivery to user {UserId} failed; dropping the subscription.",
                    target.UserId);
                target.Dispose();
            }
        }

        private void Remove(Guid userId, Subscription subscription)
        {
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(userId, out var handlers))
                    return;

                handlers.Remove(subscription);
                if (handlers.Count == 0)
                    _subscribers.Remove(userId);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly NotificationDispatcher _owner;
            private int _disposed;

            public Subscription(NotificationDispatcher owner, Guid userId, Func<NotificationChange, Task> handler)
            {
                _owner = owner;
                UserId = userId;
                Handler = handler;
            }

            public Guid UserId { get; }

            public Func<NotificationChange, Task> Handler { get; }

            public void Dispose()
            {
                // Interlocked: the component disposes on the circuit thread, a failed
                // delivery from the thread pool.
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                    return;

                _owner.Remove(UserId, this);
            }
        }
    }
}
