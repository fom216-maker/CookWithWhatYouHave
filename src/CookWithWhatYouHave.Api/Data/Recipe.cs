using Pgvector;

namespace CookWithWhatYouHave.Api.Data;

public sealed class Recipe
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Ingredients { get; set; } = "";

    public string Steps { get; set; } = "";

    public string Region { get; set; } = "";

    public string SearchableText { get; set; } = "";

    public Vector Embedding { get; set; } = null!;
}
