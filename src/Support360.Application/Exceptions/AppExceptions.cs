namespace Support360.Application.Exceptions;

public class Support360Exception : Exception
{
    public Support360Exception(string message) : base(message)
    {
    }

    public Support360Exception(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class NotFoundException : Support360Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public sealed class ConflictException : Support360Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

public sealed class ForbiddenException : Support360Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

public sealed class InvalidCredentialException : Support360Exception
{
    public InvalidCredentialException(string message) : base(message)
    {
    }
}
