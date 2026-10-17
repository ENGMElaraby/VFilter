namespace VFilter.Core.Modules;

/// <summary>A command cannot be carried out; the message becomes the command's result.</summary>
public sealed class CommandFailedException : Exception
{
    public CommandFailedException()
    {
    }

    public CommandFailedException(string message)
        : base(message)
    {
    }

    public CommandFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
