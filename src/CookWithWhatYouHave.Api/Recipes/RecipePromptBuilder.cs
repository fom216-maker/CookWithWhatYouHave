using System.Text;
using CookWithWhatYouHave.Contracts;

namespace CookWithWhatYouHave.Api.Recipes;

public static class RecipePromptBuilder
{
    public static RecipePrompt Build(
        GenerateRecipeRequest request,
        IReadOnlyList<RetrievedRecipe> related,
        ProductFact? product)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Eres un cocinero de cocina casera española. Responde solo con el JSON pedido.");
        prompt.AppendLine("Usa gramos y medidas de un supermercado español. No inventes precios ni datos nutricionales.");
        prompt.AppendLine($"Aparato: {Describe(request.CookingMethod)}.");
        prompt.AppendLine(request.UseLeftovers
            ? "El usuario quiere aprovechar sobras. Propón una transformación, como croquetas, migas o sopa, y explica el aprovechamiento en leftoverPitch."
            : "Prepara una receta normal con lo que ya tiene. leftoverPitch puede ir vacío.");
        prompt.AppendLine($"Ingredientes disponibles: {request.Ingredients.Trim()}");

        if (product is not null)
        {
            prompt.AppendLine(
                $"Producto real de Open Food Facts: {product.Name}. Nutri-Score {product.NutriScore ?? "desconocido"}, NOVA {product.NovaGroup?.ToString() ?? "desconocido"}.");
            if (product.SaltPer100g is >= 1)
            {
                prompt.AppendLine("Ese producto ya aporta mucha sal. No pidas más sal en el sofrito.");
            }
        }

        if (related.Count == 0)
        {
            prompt.AppendLine("No hay recetas tradicionales recuperadas.");
        }
        else
        {
            prompt.AppendLine("Recetas tradicionales recuperadas. Úsalas como base y adáptalas:");
            foreach (var recipe in related)
            {
                prompt.AppendLine($"- {recipe.Title} ({recipe.Region}). Ingredientes: {recipe.Ingredients}. Pasos: {recipe.Steps}");
            }
        }

        prompt.AppendLine("Si un ingrediente es difícil de encontrar en España, añade una sustitución local.");
        return new RecipePrompt(prompt.ToString());
    }

    private static string Describe(CookingMethod method) => method switch
    {
        CookingMethod.AirFryer => "airfryer, con temperatura y aviso de agitar a mitad de tiempo",
        CookingMethod.PressureCooker => "olla rápida, con tiempos cortos de presión",
        _ => "sartén u horno"
    };
}
