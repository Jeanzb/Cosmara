namespace NasaExplorer.Application.Common.Exceptions;

public sealed class UpstreamServiceUnavailableException : Exception
{
    public UpstreamServiceUnavailableException()
        : base("The NASA image service is temporarily unavailable.")
    {
    }
}
