using Site.SearchProvider.Constants;

namespace Site.SearchProvider.Extensions;

internal static class StringExtensions
{
    public static string IndexCulture(this string? culture)
        => culture?.ToLowerInvariant() ?? IndexConstants.Variation.InvariantCulture;
    
    public static string IndexSegment(this string? segment)
        => segment?.ToLowerInvariant() ?? IndexConstants.Variation.DefaultSegment;
}

