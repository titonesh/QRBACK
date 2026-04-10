// Models/LoanResult.cs
namespace MortgageLoanAPI.Models;

/// <summary>
/// Represents the result of a loan calculation
/// </summary>
public class LoanResult
{
    public int Id { get; set; }
    public int LoanRequestId { get; set; }
    public LoanRequest? LoanRequest { get; set; }
    public decimal AdjustedIncome { get; set; }
    public decimal MaximumLoanAmount { get; set; }
    public decimal EstimatedMonthlyRepayment { get; set; }
    public decimal StressTestedRepayment { get; set; }
    public decimal AppliedInterestRate { get; set; }
    public decimal AppliedStressTestRate { get; set; }
    public int LoanTenorMonths { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<CallbackRequest> CallbackRequests { get; set; } = new List<CallbackRequest>();
}
