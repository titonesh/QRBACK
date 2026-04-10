using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MortgageLoanAPI.Configurations;
using MortgageLoanAPI.Data;
using MortgageLoanAPI.Middleware;
using MortgageLoanAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/mortgage-api-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();

// Configure DbContext - supports both MySQL and SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "mysql";

Console.WriteLine($"Using database provider: {databaseProvider}");
Console.WriteLine($"Connection string: {connectionString}");

if (databaseProvider.ToLower() == "sqlserver")
{
    builder.Services.AddDbContext<MortgageDbContext>(options =>
        options.UseSqlServer(connectionString, 
            sqlOptions => sqlOptions.MigrationsAssembly("MortgageLoanAPI"))
    );
}
else
{
    builder.Services.AddDbContext<MortgageDbContext>(options =>
        options.UseMySql(connectionString, 
            ServerVersion.AutoDetect(connectionString),
            mySqlOptions => mySqlOptions.MigrationsAssembly("MortgageLoanAPI"))
    );
}

// Configure options
builder.Services.Configure<LoanConfigurationOptions>(
    builder.Configuration.GetSection(LoanConfigurationOptions.SectionName));

// Register services - Dependency Injection
builder.Services.AddScoped<ILoanCalculationService, LoanCalculationService>();
builder.Services.AddScoped<ICallbackRequestService, CallbackRequestService>();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Mortgage Loan Prequalification API",
        Version = "v1",
        Description = "API for calculating mortgage loan eligibility and prequalification",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "API Support"
        }
    });
});

// Add CORS for frontend communication
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:3001",
                "http://localhost:5173",
                "http://localhost:3014",
                "http://localhost:3015",
                "https://localhost:3000",
                "http://10.1.210.47:3000",
                "https://yourdomain.com"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mortgage API v1");
        options.RoutePrefix = string.Empty;
    });
}

// Middleware pipeline
app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseErrorHandling();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Run database migrations automatically (development only)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();
            dbContext.Database.Migrate();
            Log.Information("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply database migrations");
        }
    }
}

Log.Information("Starting Mortgage Loan API");

app.Run();