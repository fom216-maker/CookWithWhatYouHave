using System.Net.Http.Json;
using System.Text.Json;
using CookWithWhatYouHave.Contracts;

namespace CookWithWhatYouHave.Web.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<RecipeListItemResponse> recent = [];
    private string ingredients = "";
    private string barcode = "";
    private bool useLeftovers;
    private CookingMethod cookingMethod = CookingMethod.PanOven;
    private bool isLoading;
    private string? errorMessage;
    private RecipeScreenResponse? screen;
    private CancellationTokenSource? requestCancellation;

    protected override async Task OnInitializedAsync()
    {
        await LoadRecentAsync();
    }

    private async Task GenerateAsync()
    {
        errorMessage = null;
        screen = null;
        isLoading = true;
        requestCancellation?.Cancel();
        requestCancellation?.Dispose();
        requestCancellation = new CancellationTokenSource();
        var cancellationToken = requestCancellation.Token;

        try
        {
            var client = HttpClientFactory.CreateClient("Api");
            using var response = await client.PostAsJsonAsync(
                "/recipes/generate",
                new GenerateRecipeRequest
                {
                    Ingredients = ingredients,
                    UseLeftovers = useLeftovers,
                    CookingMethod = cookingMethod,
                    Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim()
                },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                errorMessage = await ReadProblemAsync(response, cancellationToken);
                return;
            }

            screen = await response.Content.ReadFromJsonAsync<RecipeScreenResponse>(JsonOptions, cancellationToken);
            await LoadRecentAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException)
        {
            errorMessage = "No se ha podido contactar con la API.";
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            var client = HttpClientFactory.CreateClient("Api");
            var list = await client.GetFromJsonAsync<RecipeListResponse>("/recipes", JsonOptions);
            recent.Clear();
            if (list is not null)
            {
                recent.AddRange(list.Items);
            }
        }
        catch (HttpRequestException)
        {
            recent.Clear();
        }
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }
        catch (JsonException)
        {
        }

        return "No se ha podido generar la receta.";
    }

    public async ValueTask DisposeAsync()
    {
        if (requestCancellation is not null)
        {
            await requestCancellation.CancelAsync();
            requestCancellation.Dispose();
        }
    }

    private sealed class ProblemBody
    {
        public string? Detail { get; set; }
    }
}
