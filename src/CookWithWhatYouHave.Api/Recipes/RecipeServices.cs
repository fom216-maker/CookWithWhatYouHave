using CookWithWhatYouHave.Contracts;

namespace CookWithWhatYouHave.Api.Recipes;

public interface IEmbeddingClient
{
    Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public interface IRecipeGenerator
{
    Task<RecipeDraft> GenerateAsync(RecipePrompt prompt, CancellationToken cancellationToken);
}

public interface IRecipeModel
{
    bool IsConfigured { get; }

    string MissingKeyMessage { get; }
}

public interface IRecipeRetriever
{
    Task<IReadOnlyList<RetrievedRecipe>> SearchAsync(string query, float[] embedding, CancellationToken cancellationToken);
}

public interface IOpenFoodFactsClient
{
    Task<ProductFact?> FindAsync(string ingredients, string? barcode, CancellationToken cancellationToken);
}

public interface ISuperfudisClient
{
    Task<PriceQuote?> CompareMissingAsync(IReadOnlyList<string> missingIngredients, CancellationToken cancellationToken);
}

public interface IGeneratedRecipeStore
{
    Task SaveAsync(RecipeScreenResponse screen, string ingredients, CancellationToken cancellationToken);

    Task<RecipeScreenResponse?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecipeListItemResponse>> ListAsync(int take, CancellationToken cancellationToken);
}

public interface IRecipeOrchestrator
{
    Task<RecipeScreenResponse> GenerateAsync(GenerateRecipeRequest request, CancellationToken cancellationToken);
}
