using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Application.Notifications
{
    // Registered as a singleton: subscribers come from many circuits and publishers from
    // many scoped contexts, so they all have to meet in one instance.
    public sealed class NotificationDispatcher : INotificationDispatcher
    {
        // A plain dictionary behind one lock rather than ConcurrentDictionary: the traffic
        // is a handful of operations per minute, and this way "remove the user's entry once
        // the last tab closes" has no race against a tab opening at the same moment.
        private readonly object _gate = new();
        private readonly Dictionary<Guid, List<Subscription>> _subscribers = new();

        public IDisposable Subscribe(Guid userId, Func<Notification, Task> handler)
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

        public async Task PublishAsync(Guid userId, Notification notification)
        {
            Subscription[] targets;

            lock (_gate)
            {
                if (!_subscribers.TryGetValue(userId, out var handlers))
                    return; // Nobody is watching — the row is already in the database,
                            // so the bell picks it up at the next page load.

                targets = handlers.ToArray();
            }

            // Awaited outside the lock: one slow circuit must not hold up the others, and
            // a handler that re-enters Subscribe/Dispose would deadlock on a held lock.
            foreach (var target in targets)
            {
                try
                {
                    await target.Handler(notification);
                }
                catch
                {
                    // A tab that died between the snapshot and this call is not an error
                    // worth failing the whole publish over.
                }
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
            private readonly Guid _userId;
            private bool _disposed;

            public Subscription(NotificationDispatcher owner, Guid userId, Func<Notification, Task> handler)
            {
                _owner = owner;
                _userId = userId;
                Handler = handler;
            }

            public Func<Notification, Task> Handler { get; }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.Remove(_userId, this);
            }
        }
    }
}
