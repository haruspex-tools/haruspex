using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Haruspex.DifferentialTests.Server;

/// <summary>
/// The pool's rules, without a server. A wait that should not end is given a tenth of a second
/// to, and one that should a second or more, so a loaded runner can't fail it.
/// </summary>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "Each test gives back what it holds by hand, at the moment the rule it tests needs; a using would give it back too late.")]
public class RoomPoolTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A pool whose rooms are named <c>room1</c>, <c>room2</c> and on, in the order they are made.</summary>
    private static RoomPool Pool(int limit, ConcurrentQueue<string>? made = null, TimeSpan? patience = null)
    {
        var count = 0;
        return new RoomPool(limit, patience ?? TimeSpan.FromMinutes(1), _ =>
        {
            var name = $"room{Interlocked.Increment(ref count)}";
            made?.Enqueue(name);
            return Task.FromResult(name);
        });
    }

    [Fact]
    public async Task Sessions_at_once_each_have_a_room_of_their_own()
    {
        using var pool = Pool(3);

        var held = await Task.WhenAll(pool.EnterAsync(Token), pool.EnterAsync(Token), pool.EnterAsync(Token)).WaitAsync(Moment * 10, Token);

        Assert.Equal(["room1", "room2", "room3"], held.Select(r => r.Database).Order(StringComparer.Ordinal));
        foreach (var room in held)
            room.Dispose();
    }

    [Fact]
    public async Task A_room_given_back_is_used_again_before_another_is_made()
    {
        var made = new ConcurrentQueue<string>();
        using var pool = Pool(3, made);

        var first = await pool.EnterAsync(Token);
        first.Dispose();
        var again = await pool.EnterAsync(Token);

        Assert.Equal(first.Database, again.Database);
        Assert.Equal(["room1"], made);
        Assert.Equal(1, pool.Made);
        again.Dispose();
    }

    [Fact]
    public async Task Past_the_limit_a_session_waits_for_a_room_to_be_given_back()
    {
        using var pool = Pool(2);
        var first = await pool.EnterAsync(Token);
        var second = await pool.EnterAsync(Token);

        var third = pool.EnterAsync(Token);
        await Task.Delay(Moment, Token);
        Assert.False(third.IsCompleted, "a third session was let in beside two, in a pool of two");

        second.Dispose();
        var given = await third.WaitAsync(Moment * 10, Token);
        Assert.Equal(second.Database, given.Database);
        Assert.Equal(2, pool.Made);
        given.Dispose();
        first.Dispose();
    }

    [Fact]
    public async Task Giving_back_twice_gives_back_once()
    {
        using var pool = Pool(1);
        var room = await pool.EnterAsync(Token);
        room.Dispose();
        room.Dispose();

        var again = await pool.EnterAsync(Token);
        var other = pool.EnterAsync(Token);
        await Task.Delay(Moment, Token);

        Assert.False(other.IsCompleted, "a room given back twice was handed to two sessions");
        again.Dispose();
        (await other.WaitAsync(Moment * 10, Token)).Dispose();
    }

    [Fact]
    public async Task A_wait_past_patience_fails_and_names_the_likely_cause()
    {
        using var pool = Pool(1, patience: Moment);
        var held = await pool.EnterAsync(Token);

        var e = await Assert.ThrowsAsync<TimeoutException>(() => pool.EnterAsync(Token));

        Assert.Contains("holding 1 write sessions and asking for another", e.Message, StringComparison.Ordinal);
        held.Dispose();
    }

    [Fact]
    public async Task A_room_that_could_not_be_made_takes_no_place()
    {
        var attempts = 0;
        using var pool = new RoomPool(1, TimeSpan.FromMinutes(1), _ =>
            Interlocked.Increment(ref attempts) == 1 ? throw new InvalidOperationException("no server") : Task.FromResult("room"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.EnterAsync(Token));
        var room = await pool.EnterAsync(Token).WaitAsync(Moment * 10, Token);

        Assert.Equal("room", room.Database);
        Assert.Equal(1, pool.Made);
        room.Dispose();
    }
}
