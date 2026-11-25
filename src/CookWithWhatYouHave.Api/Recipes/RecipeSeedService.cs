using System.Text.Json;
using CookWithWhatYouHave.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class RecipeSeedService(
    AppDbContext context,
    IEmbeddingClient embeddings,
    IOptions<GeminiOptions> options,
    ILogger<RecipeSeedService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.HasApiKey || await context.Recipes.AnyAsync(cancellationToken))
        {
            return;
        }

        var seeds = await ReadSeedsAsync(cancellationToken);
        var vectors = await embeddings.EmbedDocumentsAsync(
            seeds.Select(seed => seed.SearchableText).ToArray(),
            cancellationToken);

        for (var index = 0; index < seeds.Count; index++)
        {
            var seed = seeds[index];
            context.Recipes.Add(new Recipe
            {
                Id = Guid.NewGuid(),
                Title = seed.Title,
                Region = seed.Region,
                Ingredients = seed.Ingredients,
                Steps = seed.Steps,
                SearchableText = seed.SearchableText,
                Embedding = new Vector(vectors[index])
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} traditional recipes", seeds.Count);
    }

    private static async Task<IReadOnlyList<SeedRecipe>> ReadSeedsAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(RecipeSeedService).Assembly;
        var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith("traditional-recipes.json", StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        var seeds = await JsonSerializer.DeserializeAsync<List<SeedRecipe>>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The recipe seed file is empty.");
        return seeds;
    }

    private sealed record SeedRecipe(string Title, string Region, string Ingredients, string Steps)
    {
        public string SearchableText => $"{Title}. {Region}. Ingredientes: {Ingredients}. Pasos: {Steps}";
    }
}
