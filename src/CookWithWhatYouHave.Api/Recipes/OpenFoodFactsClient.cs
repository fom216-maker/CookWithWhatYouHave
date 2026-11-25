using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class OpenFoodFactsClient(
    HttpClient httpClient,
    IOptions<OpenFoodFactsOptions> options) : IOpenFoodFactsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductFact?> FindAsync(
        string ingredients,
        string? barcode,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (IsBarcode(barcode))
        {
            var product = await ReadProductAsync(
                $"{settings.BaseUrl.TrimEnd('/')}/api/v2/product/{barcode}.json",
                cancellationToken);
            if (product is not null)
            {
                return product;
            }
        }

        var query = Uri.EscapeDataString(ingredients.Length > 120 ? ingredients[..120] : ingredients);
        return await ReadSearchAsync(
            $"{settings.BaseUrl.TrimEnd('/')}/cgi/search.pl?search_terms={query}&search_simple=1&action=process&json=1&page_size=1",
            cancellationToken);
    }

    private async Task<ProductFact?> ReadProductAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<ProductEnvelope>(JsonOptions, cancellationToken);
        return body?.Status == 1 ? Map(body.Product) : null;
    }

    private async Task<ProductFact?> ReadSearchAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<SearchEnvelope>(JsonOptions, cancellationToken);
        return Map(body?.Products.FirstOrDefault());
    }

    private static ProductFact? Map(OffProduct? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.ProductName))
        {
            return null;
        }

        return new ProductFact(
            product.ProductName.Trim(),
            string.IsNullOrWhiteSpace(product.NutriscoreGrade) ? null : product.NutriscoreGrade.Trim(),
            product.NovaGroup,
            string.IsNullOrWhiteSpace(product.Allergens) ? null : product.Allergens.Replace("en:", "", StringComparison.Ordinal),
            product.Nutriments?.Salt100g,
            product.Nutriments?.Sugars100g);
    }

    private static bool IsBarcode(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 8 and <= 14
        && value.All(char.IsDigit);

    private sealed class ProductEnvelope
    {
        public int Status { get; set; }

        public OffProduct? Product { get; set; }
    }

    private sealed class SearchEnvelope
    {
        public List<OffProduct> Products { get; set; } = [];
    }

    private sealed class OffProduct
    {
        [JsonPropertyName("product_name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("nutriscore_grade")]
        public string? NutriscoreGrade { get; set; }

        [JsonPropertyName("nova_group")]
        public int? NovaGroup { get; set; }

        public string? Allergens { get; set; }

        public OffNutriments? Nutriments { get; set; }
    }

    private sealed class OffNutriments
    {
        [JsonPropertyName("salt_100g")]
        public double? Salt100g { get; set; }

        [JsonPropertyName("sugars_100g")]
        public double? Sugars100g { get; set; }
    }
}
