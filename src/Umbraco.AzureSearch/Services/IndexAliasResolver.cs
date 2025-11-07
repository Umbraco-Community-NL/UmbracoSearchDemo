using Microsoft.Extensions.Options;
using Umbraco.AzureSearch.Configuration;

namespace Umbraco.AzureSearch.Services;

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

