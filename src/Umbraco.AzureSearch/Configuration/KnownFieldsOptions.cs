// file: Site/SearchProvider/Configuration/KnownFieldsOptions.cs
namespace Umbraco.AzureSearch.Configuration;

// Options to configure known fields for Azure Search indexing.
// Global applies to all indexes, ByIndexAlias can override/extend per index alias.
public sealed class KnownFieldsOptions
{
    // Optional: always include the Umbraco Name field
    public bool IncludeNameField { get; set; } = true;

    // Fields applied to all indexes
    public string[] Global { get; set; } = [];

    // Additional fields per index alias (key = resolved index alias)
    public Dictionary<string, string[]> ByIndexAlias { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}