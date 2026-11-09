namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// A reader–writer gate for the test database: any number of readers at once, or one writer alone. A run whose setup
/// deletes rows, or a test that creates a table inside its own transaction, holds its write locks until it rolls back,
/// and a run reading the same tables at the same time waits on them; with one more lock taken the other way round,
/// SQL Server kills one of the two with error 1205 (GitHub's CI did, on 9 Nov 2026). Readers only ever take shared
/// locks, which never conflict, so the gate is all the isolation the runs need. See README.md, "Runs that write".
/// </summary>
/// <remarks>
/// Writers are preferred: a writer waiting holds the turnstile, so readers that come later queue behind it rather
/// than keep the database busy for ever. A wait that lasts longer than <see cref="Patience"/> fails loudly: the
/// usual cause is a test asking for a session while it still holds one, which would otherwise wait for ever.
/// </remarks>
internal sealed class DatabaseGate : IDisposable
{
    /// <summary>Far longer than every write of a run put together (seconds), so only a mistake waits this long.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim turnstile = new(1, 1);
    private readonly SemaphoreSlim room = new(1, 1);
    private readonly SemaphoreSlim counter = new(1, 1);
    private int readers;

    /// <summary>Enters as one of any number of readers; disposing the result leaves.</summary>
    public async Task<IDisposable> EnterReadAsync(CancellationToken token)
    {
        await WaitAsync(turnstile, "read", token).ConfigureAwait(false);
        turnstile.Release();

        await WaitAsync(counter, "read", token).ConfigureAwait(false);
        try
        {
            // The first reader in takes the room for all of them; the last one out gives it back.
            if (readers == 0)
                await WaitAsync(room, "read", token).ConfigureAwait(false);
            readers++;
        }
        finally
        {
            counter.Release();
        }

        return new Exit(LeaveRead);
    }

    /// <summary>Enters alone, once every reader and writer already in has left; disposing the result leaves.</summary>
    public async Task<IDisposable> EnterWriteAsync(CancellationToken token)
    {
        await WaitAsync(turnstile, "write", token).ConfigureAwait(false);
        try
        {
            await WaitAsync(room, "write", token).ConfigureAwait(false);
        }
        finally
        {
            turnstile.Release();
        }

        return new Exit(() => room.Release());
    }

    public void Dispose()
    {
        turnstile.Dispose();
        room.Dispose();
        counter.Dispose();
    }

    private void LeaveRead()
    {
        counter.Wait();
        try
        {
            if (--readers == 0)
                room.Release();
        }
        finally
        {
            counter.Release();
        }
    }

    private static async Task WaitAsync(SemaphoreSlim semaphore, string purpose, CancellationToken token)
    {
        if (!await semaphore.WaitAsync(Patience, token).ConfigureAwait(false))
        {
            throw new TimeoutException(
                $"Waited {Patience.TotalMinutes} minutes to {purpose} the test database. Is a test asking for a session while it still holds one? The gate is not re-entrant.");
        }
    }

    /// <summary>Leaves the gate once, however often it is disposed.</summary>
    private sealed class Exit(Action leave) : IDisposable
    {
        private int left;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref left, 1) == 0)
                leave();
        }
    }
}
