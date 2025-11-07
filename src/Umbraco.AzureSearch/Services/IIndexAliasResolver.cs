namespace Umbraco.AzureSearch.Services;

public interface IIndexAliasResolver
{
    string Resolve(string indexAlias);
}

