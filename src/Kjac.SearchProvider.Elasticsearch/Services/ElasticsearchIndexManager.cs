using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Kjac.SearchProvider.Elasticsearch.Constants;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Sync;
using ExistsResponse = Elastic.Clients.Elasticsearch.IndexManagement.ExistsResponse;

namespace Kjac.SearchProvider.Elasticsearch.Services;

internal sealed class ElasticsearchIndexManager : ElasticsearchIndexManagingServiceBase, IElasticsearchIndexManager
{
    private readonly IElasticsearchClientFactory _clientFactory;
    private readonly IIndexAliasResolver _indexAliasResolver;
    private readonly ILogger<ElasticsearchIndexManager> _logger;

    public ElasticsearchIndexManager(
        IServerRoleAccessor serverRoleAccessor,
        IElasticsearchClientFactory clientFactory,
        IIndexAliasResolver indexAliasResolver,
        ILogger<ElasticsearchIndexManager> logger)
        : base(serverRoleAccessor)
    {
        _clientFactory = clientFactory;
        _indexAliasResolver = indexAliasResolver;
        _logger = logger;
    }

    public async Task EnsureAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        indexAlias = _indexAliasResolver.Resolve(indexAlias);

        ElasticsearchClient client = _clientFactory.GetClient();

        ExistsResponse existsResponse = await client.Indices.ExistsAsync(indexAlias);
        if (existsResponse.Exists)
        {
            return;
        }

        _logger.LogInformation("Creating index {indexAlias}...", indexAlias);
        CreateIndexResponse createResponse = await client.Indices.CreateAsync(
            indexAlias,
            cd => cd
                .Mappings(
                    md => md
                        .Properties(
                            new Properties(
                                new Dictionary<PropertyName, IProperty>
                                {
                                    { IndexConstants.FieldNames.Key, new KeywordProperty() },
                                    { IndexConstants.FieldNames.ObjectType, new KeywordProperty() },
                                    { IndexConstants.FieldNames.Culture, new KeywordProperty() },
                                    { IndexConstants.FieldNames.Segment, new KeywordProperty() },
                                    { IndexConstants.FieldNames.AccessKeys, new KeywordProperty() },
                                }
                            )
                        )
                        .DynamicTemplates(
                            [
                                new KeyValuePair<string, DynamicTemplate>(
                                    "keyword_fields_as_keywords",
                                    new DynamicTemplate { Mapping = new KeywordProperty(), Match = ["*_keywords"] }
                                ),
                                new KeyValuePair<string, DynamicTemplate>(
                                    "decimal_fields_as_doubles",
                                    new DynamicTemplate { Mapping = new DoubleNumberProperty(), Match = ["*_decimals"] }
                                )
                            ]
                        )
                )
        );

        if (createResponse.Acknowledged)
        {
            _logger.LogInformation("Index {indexAlias} has been created.", indexAlias);
        }
        else
        {
            LogFailedElasticResponse(_logger, "Index could not be created", indexAlias, createResponse);
        }
    }

    public async Task ResetAsync(string indexAlias)
    {
        if (ShouldNotManipulateIndexes())
        {
            return;
        }

        indexAlias = _indexAliasResolver.Resolve(indexAlias);

        ElasticsearchClient client = _clientFactory.GetClient();

        ExistsResponse existsResponse = await client.Indices.ExistsAsync(indexAlias);
        if (existsResponse.Exists)
        {
            DeleteIndexResponse result = await client.Indices.DeleteAsync(indexAlias);

            if (result.IsValidResponse is false)
            {
                LogFailedElasticResponse(_logger, indexAlias, "Could not reset the index", result);
                return;
            }
        }

        await EnsureAsync(indexAlias);
    }
}
