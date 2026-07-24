namespace Genasys.Api.Common;

public abstract class DomainException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
}

public class NotFoundException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status404NotFound;
}

public class ConflictException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
}

public class InsufficientInventoryException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
}

public class PaymentFailedException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status402PaymentRequired;
}

public class UpstreamServiceUnavailableException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;
}
