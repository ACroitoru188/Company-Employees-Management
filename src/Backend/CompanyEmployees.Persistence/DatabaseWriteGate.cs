namespace CompanyEmployees.Persistence;

public sealed class DatabaseWriteGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public IDisposable Enter()
    {
        gate.Wait();
        return new Releaser(gate);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released)
                return;
            released = true;
            gate.Release();
        }
    }
}
