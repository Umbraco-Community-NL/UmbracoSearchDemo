namespace Site.Models;

public class ArticlesSearchRequest
{
    public string? Query { get; init; }

    public string[]? Author { get; init; }

    public string[]? Categories { get; init; }

    public string[]? ArticleYear { get; init; }

    public string? SortBy { get; init; }

    public string? SortDirection { get; init; }

    public int Skip { get; init; } = 0;

    public int Take { get; init; } = 6;
}

