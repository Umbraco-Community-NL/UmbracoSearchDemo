// file: Umbraco.AzureSearch/Abstractions/IKnownFieldsProvider.cs

using Umbraco.Cms.Search.Core.Models.Indexing;

namespace Umbraco.AzureSearch.Abstractions;

public interface IKnownFieldsProvider
{
    // Returns the list of known field names for a given index alias.
    // Implementations can use configuration or conventions.
    IReadOnlyCollection<string> GetKnownFields(string indexAlias);
    
}
