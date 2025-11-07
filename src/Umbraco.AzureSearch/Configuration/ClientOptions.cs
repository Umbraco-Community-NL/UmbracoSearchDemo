namespace Umbraco.AzureSearch.Configuration;

public sealed class ClientOptions
{
    public string ServiceName { get; set; } = string.Empty;
    
    public string ApiKey { get; set; } = string.Empty;
    
    public string IndexName { get; set; } = string.Empty;
    
    public string? Environment { get; set; }
}

