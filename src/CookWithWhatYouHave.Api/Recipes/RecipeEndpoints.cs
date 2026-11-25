using CookWithWhatYouHave.Contracts;

namespace CookWithWhatYouHave.Api.Recipes;

public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this WebApplication app)
    {
        app.MapPost("/recipes/generate", async (
                GenerateRecipeRequest request,
                IRecipeOrchestrator orchestrator,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Ingredients) || request.Ingredients.Trim().Length < 2)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["Ingredients"] = ["Escribe al menos un ingrediente."]
                    });
                }

                var screen = await orchestrator.GenerateAsync(request, cancellationToken);
                return Results.Ok(screen);
            })
            .WithName("GenerateRecipe")
            .WithSummary("Generate a Spanish recipe from the ingredients at hand.")
            .Produces<RecipeScreenResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/recipes/{id:guid}", async (
                Guid id,
                IGeneratedRecipeStore store,
                CancellationToken cancellationToken) =>
            {
                var screen = await store.GetAsync(id, cancellationToken);
                return screen is null ? Results.NotFound() : Results.Ok(screen);
            })
            .WithName("GetRecipe")
            .WithSummary("Get a previously generated recipe screen.")
            .Produces<RecipeScreenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/recipes", async (
                IGeneratedRecipeStore store,
                CancellationToken cancellationToken) =>
            {
                var items = await store.ListAsync(12, cancellationToken);
                return Results.Ok(new RecipeListResponse(items));
            })
            .WithName("ListRecipes")
            .WithSummary("List recently generated recipes.")
            .Produces<RecipeListResponse>(StatusCodes.Status200OK);
    }
}
