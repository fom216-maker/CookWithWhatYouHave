using Microsoft.AspNetCore.Diagnostics;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class RecipeExceptionHandler(ILogger<RecipeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not RecipeGenerationUnavailableException unavailable)
        {
            return false;
        }

        logger.LogWarning(unavailable, "Recipe generation is unavailable");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            title = "La generación de recetas no está disponible",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = unavailable.Message
        }, cancellationToken);

        return true;
    }
}
