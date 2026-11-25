namespace CookWithWhatYouHave.Contracts;

/// <summary>A saved recipe screen made of ordered UI blocks.</summary>
public sealed record RecipeScreenResponse(Guid Id, string Title, IReadOnlyList<UiBlock> Blocks);

/// <summary>A saved recipe shown in the recent list.</summary>
public sealed record RecipeListItemResponse(Guid Id, string Title, DateTimeOffset CreatedAt);

/// <summary>Recent generated recipes.</summary>
public sealed record RecipeListResponse(IReadOnlyList<RecipeListItemResponse> Items);
