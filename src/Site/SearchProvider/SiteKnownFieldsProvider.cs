// file: Site/SearchProvider/Services/SiteKnownFieldsProvider.cs

using Microsoft.Extensions.Options;
using Umbraco.AzureSearch.Abstractions;
using Umbraco.AzureSearch.Configuration;

namespace Site.SearchProvider;

public sealed class SiteKnownFieldsProvider(IOptionsMonitor<KnownFieldsOptions> options) : IKnownFieldsProvider
{
    public IReadOnlyCollection<string> GetKnownFields(string indexAlias)
    {
        var knownFieldsOptions = options.CurrentValue;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in knownFieldsOptions.Global)
        {
            if (!string.IsNullOrWhiteSpace(f)) set.Add(f);
        }

        if (!string.IsNullOrWhiteSpace(indexAlias)
            && knownFieldsOptions.ByIndexAlias.TryGetValue(indexAlias, out var aliasFields))
        {
            foreach (var f in aliasFields)
            {
                if (!string.IsNullOrWhiteSpace(f)) set.Add(f);
            }
        }

        if (knownFieldsOptions.IncludeNameField)
        {
            set.Add(Umbraco.Cms.Search.Core.Constants.FieldNames.Name);
        }

        return set.ToArray();
    }
}