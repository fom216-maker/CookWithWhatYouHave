using System.Text.Json;
using CookWithWhatYouHave.Api.Data;
using CookWithWhatYouHave.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class GeneratedRecipeStore(AppDbContext context) : IGeneratedRecipeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(RecipeScreenResponse screen, string ingredients, CancellationToken cancellationToken)
    {
        context.GeneratedRecipes.Add(new GeneratedRecipe
        {
            Id = screen.Id,
            Title = screen.Title,
            Ingredients = ingredients,
            CreatedAt = DateTimeOffset.UtcNow,
            ScreenJson = JsonSerializer.Serialize(screen, JsonOptions)
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecipeScreenResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var json = await context.GeneratedRecipes
            .AsNoTracking()
            .Where(recipe => recipe.Id == id)
            .Select(recipe => recipe.ScreenJson)
            .FirstOrDefaultAsync(cancellationToken);

        return json is null ? null : JsonSerializer.Deserialize<RecipeScreenResponse>(json, JsonOptions);
    }

    public async Task<IReadOnlyList<RecipeListItemResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        return await context.GeneratedRecipes
            .AsNoTracking()
            .OrderByDescending(recipe => recipe.CreatedAt)
            .Take(take)
            .Select(recipe => new RecipeListItemResponse(recipe.Id, recipe.Title, recipe.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
