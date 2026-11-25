using System.Globalization;
using CookWithWhatYouHave.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CookWithWhatYouHave.Api.Recipes;

public sealed class RecipeRetriever(AppDbContext context) : IRecipeRetriever
{
    private const string Sql = """
        WITH vector_hits AS (
            SELECT id, ROW_NUMBER() OVER (ORDER BY embedding <=> CAST(@embedding AS vector)) AS rank
            FROM recipes
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> CAST(@embedding AS vector)
            LIMIT 20
        ),
        text_hits AS (
            SELECT id,
                   ROW_NUMBER() OVER (
                       ORDER BY ts_rank(search_vector, websearch_to_tsquery('spanish', @query)) DESC
                   ) AS rank
            FROM recipes
            WHERE search_vector @@ websearch_to_tsquery('spanish', @query)
            LIMIT 20
        )
        SELECT recipes.id, recipes.title, recipes.ingredients, recipes.steps, recipes.region
        FROM recipes
        LEFT JOIN vector_hits ON vector_hits.id = recipes.id
        LEFT JOIN text_hits ON text_hits.id = recipes.id
        WHERE vector_hits.id IS NOT NULL OR text_hits.id IS NOT NULL
        ORDER BY COALESCE(1.0 / (60 + vector_hits.rank), 0) + COALESCE(1.0 / (60 + text_hits.rank), 0) DESC
        LIMIT 5
        """;

    public async Task<IReadOnlyList<RetrievedRecipe>> SearchAsync(
        string query,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var settings = new NpgsqlCommand("SET LOCAL hnsw.ef_search = 100", connection, transaction))
        {
            await settings.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        command.Parameters.AddWithValue("query", query);

        var matches = new List<RetrievedRecipe>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new RetrievedRecipe(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        await reader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        return matches;
    }

    private static string ToVectorLiteral(float[] embedding) =>
        "[" + string.Join(',', embedding.Select(value => value.ToString("G9", CultureInfo.InvariantCulture))) + "]";
}
