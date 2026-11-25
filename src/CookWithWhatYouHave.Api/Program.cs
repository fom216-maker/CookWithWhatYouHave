using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using CookWithWhatYouHave.Api.Data;
using CookWithWhatYouHave.Api.Recipes;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RecipeExceptionHandler>();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<CloudflareOptions>(builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<OpenFoodFactsOptions>(builder.Configuration.GetSection(OpenFoodFactsOptions.SectionName));
builder.Services.Configure<SuperfudisOptions>(builder.Configuration.GetSection(SuperfudisOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var aiProvider = builder.Configuration[$"{AiOptions.SectionName}:Provider"] ?? "Gemini";
if (aiProvider.Equals("Cloudflare", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<CloudflareClient>(client =>
    {
        var accountId = builder.Configuration[$"{CloudflareOptions.SectionName}:AccountId"];
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = "missing";
        }

        client.BaseAddress = new Uri($"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/");
        client.Timeout = TimeSpan.FromSeconds(45);
        var apiToken = builder.Configuration[$"{CloudflareOptions.SectionName}:ApiToken"];
        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }
    });
    builder.Services.AddScoped<IEmbeddingClient>(services => services.GetRequiredService<CloudflareClient>());
    builder.Services.AddScoped<IRecipeGenerator>(services => services.GetRequiredService<CloudflareClient>());
    builder.Services.AddScoped<IRecipeModel>(services => services.GetRequiredService<CloudflareClient>());
}
else if (aiProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<GeminiClient>(client =>
    {
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
        client.Timeout = TimeSpan.FromSeconds(45);
        var apiKey = builder.Configuration[$"{GeminiOptions.SectionName}:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        }
    });
    builder.Services.AddScoped<IEmbeddingClient>(services => services.GetRequiredService<GeminiClient>());
    builder.Services.AddScoped<IRecipeGenerator>(services => services.GetRequiredService<GeminiClient>());
    builder.Services.AddScoped<IRecipeModel>(services => services.GetRequiredService<GeminiClient>());
}
else
{
    var unavailable = new UnavailableRecipeModel("AI_PROVIDER debe ser Gemini o Cloudflare.");
    builder.Services.AddSingleton(unavailable);
    builder.Services.AddSingleton<IEmbeddingClient>(unavailable);
    builder.Services.AddSingleton<IRecipeGenerator>(unavailable);
    builder.Services.AddSingleton<IRecipeModel>(unavailable);
}

builder.Services.AddHttpClient<IOpenFoodFactsClient, OpenFoodFactsClient>((services, client) =>
{
    var settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenFoodFactsOptions>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
});

builder.Services.AddHttpClient<ISuperfudisClient, SuperfudisClient>((services, client) =>
{
    var settings = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SuperfudisOptions>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddScoped<IRecipeRetriever, RecipeRetriever>();
builder.Services.AddScoped<IGeneratedRecipeStore, GeneratedRecipeStore>();
builder.Services.AddScoped<IRecipeOrchestrator, RecipeOrchestrator>();
builder.Services.AddScoped<RecipeSeedService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");
app.MapRecipeEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    const int attempts = 8;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await database.Database.MigrateAsync();
            break;
        }
        catch (Exception exception) when (attempt < attempts)
        {
            app.Logger.LogWarning(exception, "Database is not ready. Retry {Attempt} of {Attempts}", attempt, attempts);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
    try
    {
        await scope.ServiceProvider.GetRequiredService<RecipeSeedService>().SeedAsync(CancellationToken.None);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Traditional recipe seed failed");
    }
}

app.Run();

public partial class Program;
