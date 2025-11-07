using Umbraco.AzureSearch.Constants;

namespace Umbraco.AzureSearch.Services;

internal abstract class AzureSearchServiceBase
{
    protected static string FieldName(string fieldName, string postfix)
        => $"{IndexConstants.FieldNames.Fields}_{fieldName}{postfix}";
}

