using System.Text.Json.Serialization;

namespace CookWithWhatYouHave.Contracts;

/// <summary>A generated screen block rendered by the web app.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RecipeHeroBlock), "RecipeHero")]
[JsonDerivedType(typeof(LeftoverRescueBlock), "LeftoverRescue")]
[JsonDerivedType(typeof(PantrySubstitutionBlock), "PantrySubstitution")]
[JsonDerivedType(typeof(IngredientListBlock), "IngredientList")]
[JsonDerivedType(typeof(StepListBlock), "StepList")]
[JsonDerivedType(typeof(ProductAlertBlock), "ProductAlert")]
[JsonDerivedType(typeof(PriceComparisonBlock), "PriceComparison")]
public abstract record UiBlock;

/// <summary>Title and summary of the generated recipe.</summary>
public sealed record RecipeHeroBlock(string Title, string Summary) : UiBlock;

/// <summary>Why the dish is a good way to use leftovers.</summary>
public sealed record LeftoverRescueBlock(string Pitch) : UiBlock;

/// <summary>Local Spanish substitutes for hard-to-find ingredients.</summary>
public sealed record PantrySubstitutionBlock(IReadOnlyList<SubstitutionItem> Items) : UiBlock;

/// <summary>One ingredient rewritten for a Spanish supermarket.</summary>
public sealed record SubstitutionItem(string Original, string LocalEquivalent);

/// <summary>Ingredients already available and ingredients still to buy.</summary>
public sealed record IngredientListBlock(IReadOnlyList<string> Used, IReadOnlyList<string> Missing) : UiBlock;

/// <summary>Ordered cooking steps for the selected appliance.</summary>
public sealed record StepListBlock(IReadOnlyList<RecipeStepItem> Steps) : UiBlock;

/// <summary>A single cooking step.</summary>
public sealed record RecipeStepItem(int Order, string Instruction, string? Temperature, string? Duration);

/// <summary>Nutrition and allergen notice taken from Open Food Facts.</summary>
public sealed record ProductAlertBlock(string ProductName, string? NutriScore, int? NovaGroup, string Message) : UiBlock;

/// <summary>Supermarket price comparison for the missing ingredients.</summary>
public sealed record PriceComparisonBlock(
    string CheapestRetailer,
    decimal Total,
    IReadOnlyList<RetailerPriceItem> Retailers) : UiBlock;

/// <summary>Estimated basket total in one supermarket.</summary>
public sealed record RetailerPriceItem(string Retailer, decimal Total);
