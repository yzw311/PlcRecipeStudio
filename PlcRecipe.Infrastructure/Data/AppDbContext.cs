using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Infrastructure.Data;

/// <summary>SQLite 数据库上下文（经 IDbContextFactory 使用，线程安全）。</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PlcDevice> PlcDevices => Set<PlcDevice>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OpLog> OpLogs => Set<OpLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<PlcDevice>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        mb.Entity<Recipe>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(x => new { x.DeviceId, x.Name }).IsUnique();
            e.HasOne(x => x.Device)
             .WithMany()
             .HasForeignKey(x => x.DeviceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<RecipeItem>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Address).IsRequired().HasMaxLength(100);
            e.HasIndex(x => new { x.RecipeId, x.Name }).IsUnique();
            e.HasOne(x => x.Recipe)
             .WithMany(r => r.Items)
             .HasForeignKey(x => x.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);
        });


        mb.Entity<User>(e =>
        {
            e.Property(x => x.UserName).IsRequired().HasMaxLength(50);
            e.HasIndex(x => x.UserName).IsUnique();
        });

        mb.Entity<OpLog>(e =>
        {
            e.HasIndex(x => x.Time);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.UserName).HasMaxLength(50);
            e.Property(x => x.Target).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(2000);
        });
    }
}
