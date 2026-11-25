using System.ComponentModel.DataAnnotations;

namespace CookWithWhatYouHave.Contracts;

/// <summary>Ingredients and cooking preferences used to generate a recipe.</summary>
public sealed record GenerateRecipeRequest
{
    /// <summary>Food the cook already has at home.</summary>
    [Required(ErrorMessage = "Cuéntanos qué tienes en casa.")]
    [MinLength(2, ErrorMessage = "Escribe al menos un ingrediente.")]
    [MaxLength(2000)]
    public required string Ingredients { get; init; }

    /// <summary>When true, the recipe should transform leftovers.</summary>
    public bool UseLeftovers { get; init; }

    /// <summary>Appliance that should drive times and temperatures.</summary>
    [Required]
    public required CookingMethod CookingMethod { get; init; }

    /// <summary>Optional product barcode from the Spanish pantry.</summary>
    [MaxLength(32)]
    public string? Barcode { get; init; }
}
