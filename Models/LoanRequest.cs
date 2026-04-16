// Models/LoanRequest.cs
namespace MortgageLoanAPI.Models;

/// <summary>
/// Represents a loan request from a customer
/// </summary>
public class LoanRequest
{
    public int Id { get; set; }
    public decimal MonthlySalaryIncome { get; set; }
    public decimal MonthlyBusinessIncome { get; set; }
    public decimal MonthlyRentalPayments { get; set; }
    public decimal ExistingLoanObligations { get; set; }
    public int PreferredLoanTenorYears { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    // Additional customer details for better data collection and customer profile
    public string EmployerName { get; set; } = string.Empty;
    public string NatureOfBusiness { get; set; } = string.Empty;
    public string BusinessLocation { get; set; } = string.Empty;
    // ID number for credit score integration (TransUnion scorecard fetching)
    public string IdNumber { get; set; } = string.Empty;
    public ICollection<LoanResult> LoanResults { get; set; } = new List<LoanResult>();
}
