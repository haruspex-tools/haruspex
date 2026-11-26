using System.Collections.Concurrent;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The databases that write sessions run in, each a copy of the test database deployed and seeded alike, and each held by one
/// session at a time (README.md, "Runs that write"). A session that writes holds its locks until it rolls back, so two in one
/// database could wait on each other, or deadlock (error 1205, which GitHub's CI hit on 9 Nov 2026); alone in a database of its
/// own, a write session waits on nobody and nobody waits on it, so write sessions run side by side, one to a room.
/// </summary>
/// <remarks>
/// A room is made the first time every room made so far is taken, up to <see cref="Limit"/>; after that a session waits for one
/// to be given back. A room given back is used again, so a run makes no more rooms than it has write sessions at once. A wait
/// that lasts longer than <see cref="Patience"/> fails loudly: the likely cause is a test holding as many sessions as there are
/// rooms and asking for one more, which would otherwise wait for ever.
/// </remarks>
internal sealed class RoomPool : IDisposable
{
    private readonly Func<CancellationToken, Task<string>> make;
    private readonly SemaphoreSlim places;
    private readonly ConcurrentBag<string> free = [];
    private int made;

    /// <param name="limit">The most rooms there may be, and so the most write sessions at once.</param>
    /// <param name="patience">How long a session waits for a room before failing.</param>
    /// <param name="make">Makes a room, deployed and seeded, and gives its database's name.</param>
    public RoomPool(int limit, TimeSpan patience, Func<CancellationToken, Task<string>> make)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        Limit = limit;
        Patience = patience;
        this.make = make;
        places = new(limit, limit);
    }

    /// <summary>The most rooms there may be.</summary>
    public int Limit { get; }

    /// <summary>How long a session waits for a room before failing.</summary>
    public TimeSpan Patience { get; }

    /// <summary>How many rooms have been made.</summary>
    public int Made => Volatile.Read(ref made);

    /// <summary>
    /// A room of the caller's own until it disposes the result: one given back earlier if there is one, else a new one if fewer
    /// than <see cref="Limit"/> have been made, else the next given back.
    /// </summary>
    public async Task<Room> EnterAsync(CancellationToken token)
    {
        if (!await places.WaitAsync(Patience, token).ConfigureAwait(false))
        {
            throw new TimeoutException(
                $"Waited {Patience.TotalMinutes} minutes for one of the {Limit} write databases. Is a test holding {Limit} write sessions and asking for another?");
        }

        // A place is held, so either a room is free or fewer than Limit have been made: every room made and not free is held
        // by a session that holds a place.
        if (free.TryTake(out var name))
            return new Room(name, this);
        try
        {
            name = await make(token).ConfigureAwait(false);
            Interlocked.Increment(ref made);
            return new Room(name, this);
        }
        catch
        {
            places.Release();
            throw;
        }
    }

    public void Dispose() => places.Dispose();

    private void Leave(string name)
    {
        free.Add(name);
        places.Release();
    }

    /// <summary>A room held by one session; disposing it gives it back, once, however often it is disposed.</summary>
    internal sealed class Room(string database, RoomPool pool) : IDisposable
    {
        private int left;

        /// <summary>The room's database.</summary>
        public string Database { get; } = database;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref left, 1) == 0)
                pool.Leave(Database);
        }
    }
}
