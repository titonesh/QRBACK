# Mortgage Loan API - Backend Runbook

## Quick Start

### Prerequisites
- .NET 8 SDK or later
- MySQL Server (or SQL Server)
- Visual Studio 2022 / VS Code / JetBrains Rider

### Step 1: Configure Database Connection

Edit `appsettings.json` and update the connection string:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Port=3306;Database=MortgageDB;User=root;Password=root;"
}
```

### Step 2: Create Database and Apply Migrations

```bash
# Navigate to backend directory
cd MortgageLoanAPI

# Restore NuGet packages
dotnet restore

# Apply database migrations
dotnet ef database update

# Or manually create migration if needed:
# dotnet ef migrations add InitialCreate
# dotnet ef database update
```

### Step 3: Run the Application

```bash
# Development
dotnet run

# With hot reload
dotnet watch run
```

The API will be available at: `http://localhost:5000`
Swagger UI: `http://localhost:5000`

---

## API Endpoints

### Health Check
```
GET /api/loan/health
```

### Calculate Loan
```
POST /api/loan/calculate
Content-Type: application/json

{
  "monthlySalaryIncome": 5000,
  "monthlyBusinessIncome": 2000,
  "monthlyRentalPayments": 800,
  "existingLoanObligations": 500,
  "preferredLoanTenorYears": 20
}
```

**Response:**
```json
{
  "loanRequestId": 1,
  "loanResultId": 1,
  "adjustedIncome": 6820,
  "maximumLoanAmount": 850000,
  "estimatedMonthlyRepayment": 12450.75,
  "stressTestedRepayment": 13520.25,
  "appliedInterestRate": 0.1302,
  "appliedStressTestRate": 0.1502,
  "loanTenorMonths": 240,
  "assumptions": "Assumptions: Salary Affordability Ratio: 60.00%, Business Affordability Ratio: 20.00%, Business Income Discount: 25.00%, Interest Rate: 13.02%, Stress Test Rate: 15.02%, Loan Tenor: 20 years, Rental Contribution Enabled: True"
}
```

### Request Callback
```
POST /api/loan/callback-request
Content-Type: application/json

{
  "loanResultId": 1,
  "fullName": "John Doe",
  "phoneNumber": "+1234567890",
  "email": "john@example.com",
  "message": "Please call me between 9-5 on weekdays"
}
```

**Response:**
```json
{
  "id": 1,
  "message": "Callback request created successfully. We will contact you shortly.",
  "loanResultId": 1,
  "createdAt": "2026-03-22T10:30:00"
}
```

---

## Configuration Parameters

Edit `appsettings.json` to adjust:

```json
"LoanConfiguration": {
  "SalaryAffordabilityRatio": 0.60,        // 60% of salary
  "BusinessAffordabilityRatio": 0.20,      // 20% of business income
  "BusinessIncomeDiscount": 0.25,          // 25% discount on business income
  "InterestRate": 0.1302,                  // 13.02% annual rate
  "StressTestRate": 0.1502,                // 15.02% stress test rate
  "MinLoanTenorYears": 20,                 // Minimum 20 years
  "MaxLoanTenorYears": 25,                 // Maximum 25 years
  "DefaultLoanTenorYears": 20,             // Default 20 years
  "RentalContributionEnabled": true        // Include rental in affordability
}
```

---

## Switching from MySQL to SQL Server

### Step 1: Update `appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=MortgageDB;Integrated Security=true;TrustServerCertificate=true;"
  },
  "DatabaseProvider": "sqlserver"
}
```

### Step 2: Delete Migrations and Create New Ones

```bash
# Remove existing migrations folder - keep the entire Migrations folder but we'll create new ones
# Delete the existing migration files from Migrations folder

# Create new migration for SQL Server
dotnet ef migrations remove --force
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Step 3: Update Connection String Format

**SQL Server (Windows Auth):**
```
Server=.\SQLEXPRESS;Database=MortgageDB;Integrated Security=true;TrustServerCertificate=true;
```

**SQL Server (SQL Auth):**
```
Server=localhost;Database=MortgageDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true;
```

---

## Logging

Logs are written to:
- Console (development)
- File: `logs/mortgage-api-YYYYMMDD.txt` (daily rolling files)

Configure in `appsettings.json`:
```json
"Serilog": {
  "MinimumLevel": "Information",
  "WriteTo": [
    { "Name": "Console" },
    { 
      "Name": "File",
      "Args": {
        "path": "logs/mortgage-api-.txt",
        "rollingInterval": "Day"
      }
    }
  ]
}
```

---

## CORS Configuration

Add allowed frontend URLs in `Program.cs`:
```csharp
.WithOrigins(
    "http://localhost:3000",      // Development
    "https://yourdomain.com"      // Production
)
```

---

## Database Tables

### LoanRequests
Stores customer loan request inputs
- Id (PK)
- MonthlySalaryIncome (decimal)
- MonthlyBusinessIncome (decimal)
- MonthlyRentalPayments (decimal)
- ExistingLoanObligations (decimal)
- PreferredLoanTenorYears (int)
- CreatedAt (datetime)
- UpdatedAt (datetime)

### LoanResults
Stores calculated loan results
- Id (PK)
- LoanRequestId (FK)
- AdjustedIncome (decimal)
- MaximumLoanAmount (decimal)
- EstimatedMonthlyRepayment (decimal)
- StressTestedRepayment (decimal)
- AppliedInterestRate (decimal)
- AppliedStressTestRate (decimal)
- LoanTenorMonths (int)
- CreatedAt (datetime)

### CallbackRequests
Stores customer callback requests
- Id (PK)
- LoanResultId (FK)
- FullName (varchar)
- PhoneNumber (varchar)
- Email (varchar)
- Message (varchar)
- CreatedAt (datetime)
- IsProcessed (bool)
- ProcessedAt (datetime)

### ConfigurationSettings
Stores configurable parameters
- Id (PK)
- Key (varchar unique)
- Value (longtext)
- Description (longtext)
- UpdatedAt (datetime)

---

## Troubleshooting

### Database Connection Error
- Check MySQL/SQL Server is running
- Verify connection string format
- Ensure database user has correct permissions

### Port 5000 Already in Use
```bash
dotnet run --urls "http://localhost:5001"
```

### Migrations Not Applied
```bash
dotnet ef database update --verbose
```

### Entity Framework Issues
```bash
# Clear cache and rebuild
dotnet clean
dotnet build
dotnet ef database update
```

---

## Deployment

### Production Build
```bash
dotnet publish -c Release -o ./publish
```

### Docker Support (Optional)
Create `Dockerfile` for containerization:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY publish .
ENTRYPOINT ["dotnet", "MortgageLoanAPI.dll"]
```

---

## Security Considerations

- Enable HTTPS in production
- Set proper CORS policies
- Validate all inputs
- Use HTTPS for database connections
- Implement rate limiting
- Add authentication/authorization as needed
- Encrypt sensitive configuration values
