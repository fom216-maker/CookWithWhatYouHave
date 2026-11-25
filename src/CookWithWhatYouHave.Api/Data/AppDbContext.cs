using Microsoft.EntityFrameworkCore;

namespace CookWithWhatYouHave.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();

    public DbSet<GeneratedRecipe> GeneratedRecipes => Set<GeneratedRecipe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.ToTable("recipes");
            entity.Property(recipe => recipe.Id).HasColumnName("id");
            entity.Property(recipe => recipe.Title).HasColumnName("title").HasMaxLength(200);
            entity.Property(recipe => recipe.Ingredients).HasColumnName("ingredients");
            entity.Property(recipe => recipe.Steps).HasColumnName("steps");
            entity.Property(recipe => recipe.Region).HasColumnName("region").HasMaxLength(80);
            entity.Property(recipe => recipe.SearchableText).HasColumnName("searchable_text");
            entity.Property(recipe => recipe.Embedding).HasColumnName("embedding").HasColumnType("vector(768)");
        });

        modelBuilder.Entity<GeneratedRecipe>(entity =>
        {
            entity.ToTable("generated_recipes");
            entity.Property(recipe => recipe.Id).HasColumnName("id");
            entity.Property(recipe => recipe.Title).HasColumnName("title").HasMaxLength(200);
            entity.Property(recipe => recipe.Ingredients).HasColumnName("ingredients").HasMaxLength(2000);
            entity.Property(recipe => recipe.CreatedAt).HasColumnName("created_at");
            entity.Property(recipe => recipe.ScreenJson).HasColumnName("screen_json");
            entity.HasIndex(recipe => recipe.CreatedAt);
        });
    }
}
