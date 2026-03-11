using System;

namespace Pclt.EidAttendance.Eid.Services;

public class EidReaderException : Exception
{
    public EidReaderException(string message) : base(message)
    {
    }
}

public sealed class EidReaderNotFoundException : EidReaderException
{
    public EidReaderNotFoundException(string message) : base(message)
    {
    }
}

public sealed class EidIntegrationNotConfiguredException : EidReaderException
{
    public EidIntegrationNotConfiguredException(string message) : base(message)
    {
    }
}

public sealed class EidValidationException : EidReaderException
{
    public EidValidationException(string message) : base(message)
    {
    }
}
