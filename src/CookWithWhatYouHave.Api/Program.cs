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

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<OpenFoodFactsOptions>(builder.Configuration.GetSection(OpenFoodFactsOptions.SectionName));
builder.Services.Configure<SuperfudisOptions>(builder.Configuration.GetSection(SuperfudisOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

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
    await database.Database.MigrateAsync();
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
