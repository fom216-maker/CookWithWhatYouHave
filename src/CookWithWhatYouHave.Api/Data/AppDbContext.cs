using Microsoft.EntityFrameworkCore;

namespace CookWithWhatYouHave.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
