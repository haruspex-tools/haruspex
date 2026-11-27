namespace Haruspex.DifferentialTests.Server;

/// <summary>Every attempt to start the test server's container failed (<see cref="ContainerStart"/>).</summary>
internal sealed class ContainerStartException : Exception
{
    public ContainerStartException()
    {
    }

    public ContainerStartException(string message)
        : base(message)
    {
    }

    public ContainerStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
