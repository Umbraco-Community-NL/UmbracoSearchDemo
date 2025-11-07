using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Umbraco.AzureSearch.Abstractions;
using Umbraco.AzureSearch.Constants;
using Umbraco.Cms.Core.Sync;
using CoreConstants = Umbraco.Cms.Search.Core.Constants;

namespace Umbraco.AzureSearch.Services;

internal sealed class AzureSearchIndexManager(
    IServerRoleAccessor serverRoleAccessor,
    SearchIndexClient indexClient,
    IIndexAliasResolver indexAliasResolver,
    ILogger<AzureSearchIndexManager> logger,
    IKnownFieldsProvider knownFieldsProvider)
    : AzureSearchIndexManagingServiceBase(serverRoleAccessor), IAzureSearchIndexManager
{
    public async Task EnsureAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        // Keep the original logical alias for configuration lookup
        var logicalAlias = indexAlias;
        indexAlias = indexAliasResolver.Resolve(indexAlias);

        try
        {
            await indexClient.GetIndexAsync(indexAlias);
            return; // Index already exists
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Index does not exist, create it
        }

        logger.LogInformation("Creating Azure Search index {indexAlias}...", indexAlias);

        var index = new SearchIndex(indexAlias)
        {
            Fields =
            {
                // System fields - these are always present
                new SimpleField(IndexConstants.FieldNames.Id, SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.ObjectType, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Key, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Culture, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.Segment, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(IndexConstants.FieldNames.AccessKeys, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },

                // All texts fields for full-text search with boost
                new SearchableField(IndexConstants.FieldNames.AllTexts) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR1) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR2) { IsFilterable = false },
                new SearchableField(IndexConstants.FieldNames.AllTextsR3) { IsFilterable = false },

                // Common Umbraco fields - all text relevance levels for Name
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.Texts)) { IsSortable = true, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR1)) { IsSortable = false, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR2)) { IsSortable = false, IsFacetable = false },
                new SearchableField(FieldName(CoreConstants.FieldNames.Name, IndexConstants.FieldTypePostfix.TextsR3)) { IsSortable = false, IsFacetable = false },
                new SimpleField(FieldName(CoreConstants.FieldNames.ContentTypeId, IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = false },
                new SimpleField(FieldName(CoreConstants.FieldNames.PathIds, IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            }
        };

        // Add configured known fields – for each, create all supported value shapes
        // (texts in 4 relevances, integers (+sortable), decimals, datetimeoffsets, and keywords)
        var knownFields = knownFieldsProvider.GetKnownFields(logicalAlias);
        foreach (var field in knownFields)
        {
            // Avoid duplicating the Name fields which are already added explicitly above
            if (string.Equals(field, CoreConstants.FieldNames.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Texts (searchable)
            index.Fields.Add(new SearchableField(FieldName(field, IndexConstants.FieldTypePostfix.Texts)) { IsFilterable = false, IsFacetable = false });
            index.Fields.Add(new SearchableField(FieldName(field, IndexConstants.FieldTypePostfix.TextsR1)) { IsFilterable = false, IsFacetable = false });
            index.Fields.Add(new SearchableField(FieldName(field, IndexConstants.FieldTypePostfix.TextsR2)) { IsFilterable = false, IsFacetable = false });
            index.Fields.Add(new SearchableField(FieldName(field, IndexConstants.FieldTypePostfix.TextsR3)) { IsFilterable = false, IsFacetable = false });

            // Integers (filterable + facetable) + single sortable projection
            index.Fields.Add(new SimpleField(FieldName(field, IndexConstants.FieldTypePostfix.Integers), SearchFieldDataType.Collection(SearchFieldDataType.Int32)) { IsFilterable = true, IsFacetable = true, IsSortable = false });
            index.Fields.Add(new SimpleField(FieldName(field, $"{IndexConstants.FieldTypePostfix.Integers}{IndexConstants.FieldTypePostfix.Sortable}"), SearchFieldDataType.Int32) { IsSortable = true, IsFacetable = true });

            // Decimals (filterable + facetable)
            index.Fields.Add(new SimpleField(FieldName(field, IndexConstants.FieldTypePostfix.Decimals), SearchFieldDataType.Collection(SearchFieldDataType.Double)) { IsFilterable = true, IsFacetable = true });

            // DateTimeOffsets (filterable + facetable)
            index.Fields.Add(new SimpleField(FieldName(field, IndexConstants.FieldTypePostfix.DateTimeOffsets), SearchFieldDataType.Collection(SearchFieldDataType.DateTimeOffset)) { IsFilterable = true, IsFacetable = true });

            // Keywords (filterable + facetable)
            index.Fields.Add(new SimpleField(FieldName(field, IndexConstants.FieldTypePostfix.Keywords), SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true });
        }

        try
        {
            await indexClient.CreateIndexAsync(index);
            logger.LogInformation("Azure Search index {indexAlias} has been created.", indexAlias);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to create Azure Search index {indexAlias}", indexAlias);
            throw;
        }
    }

    public async Task ResetAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        indexAlias = indexAliasResolver.Resolve(indexAlias);

        try
        {
            await indexClient.DeleteIndexAsync(indexAlias);
            logger.LogInformation("Deleted Azure Search index {indexAlias}", indexAlias);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Index doesn't exist, that's fine
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to delete Azure Search index {indexAlias}", indexAlias);
            return;
        }

        await EnsureAsync(indexAlias);
    }
}

