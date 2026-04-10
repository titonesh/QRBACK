namespace MortgageLoanAPI.Configurations;

/// <summary>
/// Configuration class for loan calculation parameters
/// Loaded from appsettings.json
/// </summary>
public class LoanConfigurationOptions
{
    public const string SectionName = "LoanConfiguration";
    
    public decimal SalaryAffordabilityRatio { get; set; } = 0.60m;
    public decimal BusinessAffordabilityRatio { get; set; } = 0.20m;
    public decimal BusinessIncomeDiscount { get; set; } = 0.25m;
    public decimal InterestRate { get; set; } = 0.1302m;
    public decimal StressTestRate { get; set; } = 0.1502m;
    public int MinLoanTenorYears { get; set; } = 20;
    public int MaxLoanTenorYears { get; set; } = 25;
    public int DefaultLoanTenorYears { get; set; } = 20;
    public bool RentalContributionEnabled { get; set; } = true;
}
