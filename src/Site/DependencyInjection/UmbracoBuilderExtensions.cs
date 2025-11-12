using System.Text.Json.Serialization.Metadata;
using Elastic.Clients.Elasticsearch;
using Kjac.SearchProvider.Elasticsearch.Configuration;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;

namespace Site.DependencyInjection;

public static class UmbracoBuilderExtensions
{    
    public static IUmbracoBuilder ConfigureJsonOptions(this IUmbracoBuilder builder)
    {
        builder.Services.AddControllers().AddJsonOptions(
            options =>
            {
                options.JsonSerializerOptions.TypeInfoResolver =
                    options.JsonSerializerOptions.TypeInfoResolver!.WithAddedModifier(typeInfo =>
                    {
                        if (typeInfo.Type != typeof(FacetValue))
                        {
                            return;
                        }

                        // allow all the search core facet value types to be serialized as implementations of FacetValue
                        typeInfo.PolymorphismOptions = new()
                        {
                            DerivedTypes =
                            {
                                new JsonDerivedType(typeof(IntegerRangeFacetValue)),
                                new JsonDerivedType(typeof(DecimalRangeFacetValue)),
                                new JsonDerivedType(typeof(DateTimeOffsetRangeFacetValue)),
                                new JsonDerivedType(typeof(IntegerExactFacetValue)),
                                new JsonDerivedType(typeof(DecimalExactFacetValue)),
                                new JsonDerivedType(typeof(DateTimeOffsetExactFacetValue)),
                                new JsonDerivedType(typeof(KeywordFacetValue)),
                            }
                        };
                    });
            });

        return builder;
    }
}