namespace Kjac.SearchProvider.Elasticsearch.Configuration;

public sealed class ClientOptions
{
    public Uri? Host { get; set; }

    public AuthenticationOptions? Authentication { get; set; }

    public bool EnableDebugMode { get; set; }

    public string? Environment { get; set; }
}
