using Kjac.SearchProvider.Elasticsearch.Configuration;
using Kjac.SearchProvider.Elasticsearch.Extensions;
using Microsoft.Extensions.Options;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal sealed class IndexAliasResolver : IIndexAliasResolver
{
    private readonly string? _environment;

    public IndexAliasResolver(IOptions<ClientOptions> options)
        => _environment = options.Value.Environment;

    public string Resolve(string indexAlias)
        => ValidIndexAlias(_environment is null ? indexAlias : $"{indexAlias}_{_environment}");

    private static string ValidIndexAlias(string indexAlias)
        => indexAlias.ToLowerInvariant();
}
