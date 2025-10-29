namespace Site.SearchProvider.Services;

public interface IIndexAliasResolver
{
    string Resolve(string indexAlias);
}

