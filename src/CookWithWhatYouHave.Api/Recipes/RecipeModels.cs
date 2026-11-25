namespace CookWithWhatYouHave.Api.Recipes;

public sealed record RetrievedRecipe(Guid Id, string Title, string Ingredients, string Steps, string Region);

public sealed record ProductFact(
    string Name,
    string? NutriScore,
    int? NovaGroup,
    string? Allergens,
    double? SaltPer100g,
    double? SugarsPer100g);

public sealed record RetailerQuote(string Retailer, decimal Total);

public sealed record PriceQuote(string CheapestRetailer, decimal Total, IReadOnlyList<RetailerQuote> Retailers);

public sealed class RecipeDraft
{
    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public string LeftoverPitch { get; set; } = "";

    public List<SubstitutionDraft> Substitutions { get; set; } = [];

    public List<string> UsedIngredients { get; set; } = [];

    public List<string> MissingIngredients { get; set; } = [];

    public List<StepDraft> Steps { get; set; } = [];
}

public sealed class SubstitutionDraft
{
    public string Original { get; set; } = "";

    public string LocalEquivalent { get; set; } = "";
}

public sealed class StepDraft
{
    public int Order { get; set; }

    public string Instruction { get; set; } = "";

    public string? Temperature { get; set; }

    public string? Duration { get; set; }
}

public sealed record RecipePrompt(string Text);

public sealed class RecipeGenerationUnavailableException(string message) : Exception(message);
