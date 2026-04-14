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
    // Product type: 'affordableHousing' or 'stdMortgage'
    public string ProductType { get; set; } = "affordableHousing";
    // Income source type: 'employed' or 'business' (only applicable for affordableHousing)
    public string IncomeSourceType { get; set; } = "employed";
}