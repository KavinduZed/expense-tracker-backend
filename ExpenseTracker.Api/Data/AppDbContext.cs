using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<User>(options)
{
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Board>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // Restrict: SQL Server disallows multiple cascade paths via User
            b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BoardMember>(b =>
        {
            b.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
            b.HasOne(x => x.Board).WithMany(x => x.Members).HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany(x => x.BoardMemberships).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(50).IsRequired();
            b.Property(x => x.Icon).HasMaxLength(50);
            b.HasData(
                new Category { Id = 1, Name = "Food", Icon = "restaurant", IsDefault = true },
                new Category { Id = 2, Name = "Transport", Icon = "directions_car", IsDefault = true },
                new Category { Id = 3, Name = "Shopping", Icon = "shopping_bag", IsDefault = true },
                new Category { Id = 4, Name = "Bills", Icon = "receipt_long", IsDefault = true },
                new Category { Id = 5, Name = "Entertainment", Icon = "movie", IsDefault = true },
                new Category { Id = 6, Name = "Health", Icon = "favorite", IsDefault = true },
                new Category { Id = 7, Name = "Other", Icon = "category", IsDefault = true });
        });

        builder.Entity<Expense>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.HasIndex(x => new { x.BoardId, x.Date });
            b.HasOne(x => x.Board).WithMany(x => x.Expenses).HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(88).IsRequired();
            b.HasIndex(x => x.TokenHash);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
