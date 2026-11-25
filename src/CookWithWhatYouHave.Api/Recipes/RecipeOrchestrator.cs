using CookWithWhatYouHave.Contracts;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class RecipeOrchestrator(
    IOptions<GeminiOptions> geminiOptions,
    IEmbeddingClient embeddings,
    IRecipeRetriever retriever,
    IOpenFoodFactsClient openFoodFacts,
    IRecipeGenerator generator,
    ISuperfudisClient superfudis,
    IGeneratedRecipeStore store,
    ILogger<RecipeOrchestrator> logger) : IRecipeOrchestrator
{
    public async Task<RecipeScreenResponse> GenerateAsync(
        GenerateRecipeRequest request,
        CancellationToken cancellationToken)
    {
        if (!geminiOptions.Value.HasApiKey)
        {
            throw new RecipeGenerationUnavailableException(
                "Falta GEMINI_API_KEY. Añádela en .env y reinicia la API.");
        }

        var ingredients = request.Ingredients.Trim();
        var related = await RetrieveAsync(ingredients, cancellationToken);
        var product = await FindProductAsync(ingredients, request.Barcode, cancellationToken);
        var draft = await generator.GenerateAsync(
            RecipePromptBuilder.Build(request, related, product),
            cancellationToken);
        var prices = await ComparePricesAsync(draft.MissingIngredients, cancellationToken);

        var screen = new RecipeScreenResponse(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(draft.Title) ? "Receta" : draft.Title.Trim(),
            BuildBlocks(request, draft, product, prices));

        await store.SaveAsync(screen, ingredients, cancellationToken);
        return screen;
    }

    private async Task<IReadOnlyList<RetrievedRecipe>> RetrieveAsync(string ingredients, CancellationToken cancellationToken)
    {
        try
        {
            var embedding = await embeddings.EmbedQueryAsync(ingredients, cancellationToken);
            return await retriever.SearchAsync(ingredients, embedding, cancellationToken);
        }
        catch (RecipeGenerationUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Recipe retrieval failed");
            return [];
        }
    }

    private async Task<ProductFact?> FindProductAsync(
        string ingredients,
        string? barcode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await openFoodFacts.FindAsync(ingredients, barcode, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Open Food Facts lookup failed");
            return null;
        }
    }

    private async Task<PriceQuote?> ComparePricesAsync(
        IReadOnlyList<string> missingIngredients,
        CancellationToken cancellationToken)
    {
        try
        {
            return await superfudis.CompareMissingAsync(missingIngredients, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Superfudis price lookup failed");
            return null;
        }
    }

    private static IReadOnlyList<UiBlock> BuildBlocks(
        GenerateRecipeRequest request,
        RecipeDraft draft,
        ProductFact? product,
        PriceQuote? prices)
    {
        var blocks = new List<UiBlock>
        {
            new RecipeHeroBlock(
                string.IsNullOrWhiteSpace(draft.Title) ? "Receta" : draft.Title.Trim(),
                string.IsNullOrWhiteSpace(draft.Summary) ? "Receta adaptada a lo que tienes." : draft.Summary.Trim())
        };

        if (request.UseLeftovers && !string.IsNullOrWhiteSpace(draft.LeftoverPitch))
        {
            blocks.Add(new LeftoverRescueBlock(draft.LeftoverPitch.Trim()));
        }

        var substitutions = draft.Substitutions
            .Where(item => !string.IsNullOrWhiteSpace(item.Original) && !string.IsNullOrWhiteSpace(item.LocalEquivalent))
            .Select(item => new SubstitutionItem(item.Original.Trim(), item.LocalEquivalent.Trim()))
            .ToArray();
        if (substitutions.Length > 0)
        {
            blocks.Add(new PantrySubstitutionBlock(substitutions));
        }

        if (product is not null)
        {
            blocks.Add(new ProductAlertBlock(
                product.Name,
                product.NutriScore,
                product.NovaGroup,
                DescribeProduct(product)));
        }

        blocks.Add(new IngredientListBlock(
            Clean(draft.UsedIngredients),
            Clean(draft.MissingIngredients)));
        blocks.Add(new StepListBlock(draft.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.Instruction))
            .Select((step, index) => new RecipeStepItem(
                step.Order > 0 ? step.Order : index + 1,
                step.Instruction.Trim(),
                EmptyToNull(step.Temperature),
                EmptyToNull(step.Duration)))
            .ToArray()));

        if (prices is not null)
        {
            blocks.Add(new PriceComparisonBlock(
                prices.CheapestRetailer,
                prices.Total,
                prices.Retailers.Select(quote => new RetailerPriceItem(quote.Retailer, quote.Total)).ToArray()));
        }

        return blocks;
    }

    private static string DescribeProduct(ProductFact product)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.NutriScore))
        {
            notes.Add($"Nutri-Score {product.NutriScore.ToUpperInvariant()}");
        }

        if (product.NovaGroup is not null)
        {
            notes.Add($"NOVA {product.NovaGroup}");
        }

        if (product.SaltPer100g is >= 1)
        {
            notes.Add("ya aporta bastante sal, así que no añadas más al sofrito");
        }

        if (product.SugarsPer100g is >= 10)
        {
            notes.Add("lleva bastante azúcar, compensa el resto de la receta");
        }

        if (!string.IsNullOrWhiteSpace(product.Allergens))
        {
            notes.Add($"alérgenos: {product.Allergens}");
        }

        return notes.Count == 0
            ? $"{product.Name} está en Open Food Facts."
            : $"{product.Name}: {string.Join(". ", notes)}.";
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string> values) =>
        values.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
