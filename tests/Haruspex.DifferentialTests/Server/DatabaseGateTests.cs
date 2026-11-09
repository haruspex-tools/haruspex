namespace Haruspex.DifferentialTests.Server;

/// <summary>The gate's rules, without a server. A wait that should not end is given a tenth of a second to.</summary>
public class DatabaseGateTests
{
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readers_share_the_database()
    {
        using var gate = new DatabaseGate();
        using var first = await gate.EnterReadAsync(Token);

        var second = gate.EnterReadAsync(Token);

        Assert.Same(second, await Task.WhenAny(second, Task.Delay(Moment, Token)));
        (await second).Dispose();
    }

    [Fact]
    public async Task A_writer_waits_for_every_reader_to_leave()
    {
        using var gate = new DatabaseGate();
        var first = await gate.EnterReadAsync(Token);
        var second = await gate.EnterReadAsync(Token);

        var write = gate.EnterWriteAsync(Token);
        first.Dispose();
        await Task.Delay(Moment, Token);
        Assert.False(write.IsCompleted);

        second.Dispose();
        (await write.WaitAsync(Moment * 10, Token)).Dispose();
    }

    [Fact]
    public async Task A_writer_is_alone()
    {
        using var gate = new DatabaseGate();
        var write = await gate.EnterWriteAsync(Token);

        var read = gate.EnterReadAsync(Token);
        var other = gate.EnterWriteAsync(Token);
        await Task.Delay(Moment, Token);
        Assert.False(read.IsCompleted);
        Assert.False(other.IsCompleted);

        write.Dispose();
        var first = await Task.WhenAny(read, other).WaitAsync(Moment * 10, Token);
        await Task.Delay(Moment, Token);
        Assert.False(read.IsCompleted && other.IsCompleted, "a reader and a writer were let in together");
        (await first).Dispose();
        (await (first == read ? other : read).WaitAsync(Moment * 10, Token)).Dispose();
    }

    [Fact]
    public async Task A_waiting_writer_is_not_overtaken_by_later_readers()
    {
        using var gate = new DatabaseGate();
        var reading = await gate.EnterReadAsync(Token);

        var write = gate.EnterWriteAsync(Token);
        await Task.Delay(Moment, Token);
        var later = gate.EnterReadAsync(Token);
        await Task.Delay(Moment, Token);
        Assert.False(later.IsCompleted, "a reader overtook a waiting writer, which could then wait for ever");

        reading.Dispose();
        (await write.WaitAsync(Moment * 10, Token)).Dispose();
        (await later.WaitAsync(Moment * 10, Token)).Dispose();
    }

    [Fact]
    public async Task Leaving_twice_leaves_once()
    {
        using var gate = new DatabaseGate();
        var write = await gate.EnterWriteAsync(Token);
        write.Dispose();
        write.Dispose();

        using var again = await gate.EnterWriteAsync(Token);
        var other = gate.EnterWriteAsync(Token);
        await Task.Delay(Moment, Token);

        Assert.False(other.IsCompleted, "a second Dispose let a second writer in");
        again.Dispose();
        (await other).Dispose();
    }
}
