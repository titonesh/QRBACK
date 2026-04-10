namespace MortgageLoanAPI.DTOs;

/// <summary>
/// DTO for loan calculation request from client
/// </summary>
public class LoanRequestDto
{
    public decimal MonthlySalaryIncome { get; set; }
    public decimal MonthlyBusinessIncome { get; set; }
    public decimal MonthlyRentalPayments { get; set; }
    public decimal ExistingLoanObligations { get; set; }
    public int PreferredLoanTenorYears { get; set; }
    // Optional fields used by business calculation
    public decimal CreditCardLimit { get; set; } = 0m;
    public decimal OverdraftLimit { get; set; } = 0m;
}
