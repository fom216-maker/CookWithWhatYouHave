namespace CookWithWhatYouHave.Api.Data;

public sealed class GeneratedRecipe
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";

    public string Ingredients { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public string ScreenJson { get; set; } = "";
}
