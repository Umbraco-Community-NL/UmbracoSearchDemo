namespace Site.SearchProvider.Services;

internal interface IAzureSearchIndexManager
{
    Task EnsureAsync(string indexAlias);
    
    Task ResetAsync(string indexAlias);
}

