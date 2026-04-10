using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MortgageLoanAPI.Models;

namespace MortgageLoanAPI.Data;

/// <summary>
/// DbContext for Mortgage Loan API
/// Supports MySQL and SQL Server via connection string
/// </summary>
public class MortgageDbContext : DbContext
{
    public MortgageDbContext(DbContextOptions<MortgageDbContext> options) 
        : base(options)
    {
    }

    public DbSet<LoanRequest> LoanRequests { get; set; } = null!;
    public DbSet<LoanResult> LoanResults { get; set; } = null!;
    public DbSet<CallbackRequest> CallbackRequests { get; set; } = null!;
    public DbSet<ConfigurationSetting> ConfigurationSettings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure LoanRequest
        modelBuilder.Entity<LoanRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MonthlySalaryIncome)
                .HasPrecision(18, 2);
            entity.Property(e => e.MonthlyBusinessIncome)
                .HasPrecision(18, 2);
            entity.Property(e => e.MonthlyRentalPayments)
                .HasPrecision(18, 2);
            entity.Property(e => e.ExistingLoanObligations)
                .HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
            entity.HasMany(e => e.LoanResults)
                .WithOne(lr => lr.LoanRequest)
                .HasForeignKey(lr => lr.LoanRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure LoanResult
        modelBuilder.Entity<LoanResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AdjustedIncome)
                .HasPrecision(18, 2);
            entity.Property(e => e.MaximumLoanAmount)
                .HasPrecision(18, 2);
            entity.Property(e => e.EstimatedMonthlyRepayment)
                .HasPrecision(18, 2);
            entity.Property(e => e.StressTestedRepayment)
                .HasPrecision(18, 2);
            entity.Property(e => e.AppliedInterestRate)
                .HasPrecision(5, 4);
            entity.Property(e => e.AppliedStressTestRate)
                .HasPrecision(5, 4);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
        });

        // Configure CallbackRequest
        modelBuilder.Entity<CallbackRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FullName)
                .HasMaxLength(255);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20);
            entity.Property(e => e.Email)
                .HasMaxLength(255);
            entity.Property(e => e.Message)
                .HasMaxLength(1000);
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("datetime");
        });

        // Configure ConfigurationSetting
        modelBuilder.Entity<ConfigurationSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Key)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.Value)
                .IsRequired();
            entity.HasIndex(e => e.Key)
                .IsUnique();
        });

        // Seed initial configuration
        SeedConfigurationDefaults(modelBuilder);
    }

    private static void SeedConfigurationDefaults(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConfigurationSetting>().HasData(
            new ConfigurationSetting 
            { 
                Id = 1, 
                Key = "SalaryAffordabilityRatio", 
                Value = "0.60", 
                Description = "Percentage of salary income that can be used for loan affordability" 
            },
            new ConfigurationSetting 
            { 
                Id = 2, 
                Key = "BusinessAffordabilityRatio", 
                Value = "0.20", 
                Description = "Percentage of business income that can be used for loan affordability" 
            },
            new ConfigurationSetting 
            { 
                Id = 3, 
                Key = "BusinessIncomeDiscount", 
                Value = "0.25", 
                Description = "Discount factor applied to business income (25%)" 
            },
            new ConfigurationSetting 
            { 
                Id = 4, 
                Key = "InterestRate", 
                Value = "0.1302", 
                Description = "Base interest rate for loan calculation (13.02%)" 
            },
            new ConfigurationSetting 
            { 
                Id = 5, 
                Key = "StressTestRate", 
                Value = "0.1502", 
                Description = "Stress test interest rate (15.02%)" 
            }
        );
    }
}

/// <summary>
/// Design-time DbContext factory for EF Core migrations
/// </summary>
public class MortgageDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MortgageDbContext>
{
    public MortgageDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var databaseProvider = configuration["DatabaseProvider"] ?? "mysql";

        var optionsBuilder = new DbContextOptionsBuilder<MortgageDbContext>();
        
        if (databaseProvider.ToLower() == "sqlserver")
        {
            optionsBuilder.UseSqlServer(connectionString);
        }
        else
        {
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }

        return new MortgageDbContext(optionsBuilder.Options);
    }
}