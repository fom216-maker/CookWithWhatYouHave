using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class SuperfudisClient(HttpClient httpClient, IOptions<SuperfudisOptions> options) : ISuperfudisClient
{
    private static readonly string[] Retailers =
    [
        "Mercadona", "Carrefour", "Dia", "Consum", "Eroski",
        "El Corte Inglés", "ahorramas", "BM", "Alcampo"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PriceQuote?> CompareMissingAsync(
        IReadOnlyList<string> missingIngredients,
        CancellationToken cancellationToken)
    {
        var items = missingIngredients
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(item => new { query = item.Length > 120 ? item[..120] : item, quantity = 1 })
            .ToArray();

        if (items.Length == 0)
        {
            return null;
        }

        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/api/v1/compare-basket";
        using var response = await httpClient.PostAsJsonAsync(
            url,
            new { items, retailers = Retailers, include_delivery = false },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(document.RootElement);
    }

    public static PriceQuote? Parse(JsonElement root)
    {
        var basket = ParseBasket(root);
        if (basket is not null)
        {
            return basket;
        }

        var quotes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        Collect(root, quotes);
        if (quotes.Count == 0)
        {
            return null;
        }

        var ordered = quotes
            .Select(pair => new RetailerQuote(pair.Key, pair.Value))
            .OrderBy(quote => quote.Total)
            .ToArray();

        var cheapest = ordered[0];
        return new PriceQuote(cheapest.Retailer, cheapest.Total, ordered);
    }

    private static PriceQuote? ParseBasket(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var covered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var itemCount = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array || offers.GetArrayLength() == 0)
            {
                continue;
            }

            itemCount++;
            var quantity = item.TryGetProperty("quantity", out var quantityElement)
                && TryReadDecimal(quantityElement, out var parsedQuantity)
                && parsedQuantity > 0
                    ? parsedQuantity
                    : 1m;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var offer in offers.EnumerateArray())
            {
                if (!offer.TryGetProperty("retailer", out var retailerElement)
                    || retailerElement.GetString() is not { Length: > 0 } retailer
                    || !offer.TryGetProperty("price_eur", out var priceElement)
                    || !TryReadDecimal(priceElement, out var price))
                {
                    continue;
                }

                if (!seen.Add(retailer))
                {
                    continue;
                }

                totals[retailer] = totals.GetValueOrDefault(retailer) + price * quantity;
                covered[retailer] = covered.GetValueOrDefault(retailer) + 1;
            }
        }

        if (itemCount == 0)
        {
            return null;
        }

        if (root.TryGetProperty("best_total_eur", out var bestTotalElement)
            && TryReadDecimal(bestTotalElement, out var bestTotal)
            && root.TryGetProperty("best_retailers", out var bestRetailers)
            && bestRetailers.ValueKind == JsonValueKind.Array
            && bestRetailers.EnumerateArray().FirstOrDefault().GetString() is { Length: > 0 } bestRetailer)
        {
            totals[bestRetailer] = bestTotal;
            covered[bestRetailer] = itemCount;
        }

        var ordered = totals
            .Where(pair => covered.GetValueOrDefault(pair.Key) == itemCount)
            .Select(pair => new RetailerQuote(pair.Key, pair.Value))
            .OrderBy(quote => quote.Total)
            .ToArray();

        if (ordered.Length == 0)
        {
            return null;
        }

        return new PriceQuote(ordered[0].Retailer, ordered[0].Total, ordered);
    }

    private static void Collect(JsonElement element, Dictionary<string, decimal> quotes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (!TryReadQuote(element, quotes))
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        Collect(property.Value, quotes);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, quotes);
                }

                break;
        }
    }

    private static bool TryReadQuote(JsonElement element, Dictionary<string, decimal> quotes)
    {
        string? retailer = null;
        decimal? total = null;
        var namedRetailer = false;

        foreach (var property in element.EnumerateObject())
        {
            if (retailer is null && property.Value.ValueKind == JsonValueKind.String && IsRetailerName(property.Name))
            {
                retailer = property.Value.GetString();
                namedRetailer = !property.Name.Equals("name", StringComparison.OrdinalIgnoreCase);
            }

            if (IsTotalName(property.Name) && TryReadDecimal(property.Value, out var amount))
            {
                var isBasketTotal = property.Name.Contains("total", StringComparison.OrdinalIgnoreCase);
                if (total is null || isBasketTotal)
                {
                    total = amount;
                    namedRetailer = namedRetailer || isBasketTotal;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(retailer) || total is null || total < 0 || !namedRetailer)
        {
            return false;
        }

        quotes[retailer.Trim()] = total.Value;
        return true;
    }

    private static bool IsRetailerName(string name) =>
        name.Equals("retailer", StringComparison.OrdinalIgnoreCase)
        || name.Equals("supermarket", StringComparison.OrdinalIgnoreCase)
        || name.Equals("store", StringComparison.OrdinalIgnoreCase)
        || name.Equals("name", StringComparison.OrdinalIgnoreCase);

    private static bool IsTotalName(string name) =>
        name.Contains("total", StringComparison.OrdinalIgnoreCase)
        || name.Equals("price", StringComparison.OrdinalIgnoreCase)
        || name.Equals("amount", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadDecimal(JsonElement element, out decimal amount)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out amount))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            return true;
        }

        amount = 0;
        return false;
    }
}
