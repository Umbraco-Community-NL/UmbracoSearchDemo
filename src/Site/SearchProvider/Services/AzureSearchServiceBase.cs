using Site.SearchProvider.Constants;

namespace Site.SearchProvider.Services;

internal abstract class AzureSearchServiceBase
{
    protected static string FieldName(string fieldName, string postfix)
        => $"{IndexConstants.FieldNames.Fields}_{fieldName}{postfix}";
}

