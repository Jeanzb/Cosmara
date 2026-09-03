namespace NasaExplorer.Application.Common.Exceptions;

public sealed class ExpiredCursorException : Exception
{
    public ExpiredCursorException()
        : base("The semantic search cursor has expired.")
    {
    }
}
