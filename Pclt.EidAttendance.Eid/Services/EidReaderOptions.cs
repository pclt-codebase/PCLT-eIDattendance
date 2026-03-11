using System;
using System.Collections.Generic;

namespace Pclt.EidAttendance.Eid.Services;

public sealed class EidReaderOptions
{
    public static EidReaderOptions Default { get; } = new()
    {
        AllowMockFallback = true,
        AuthorizedReaderKeywords = new[]
        {
            "eid",
            "belg",
            "omnikey",
            "acr",
            "smart card"
        }
    };

    public bool AllowMockFallback { get; init; }
    public IReadOnlyList<string> AuthorizedReaderKeywords { get; init; } = Array.Empty<string>();
}
