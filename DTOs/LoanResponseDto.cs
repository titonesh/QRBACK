namespace MortgageLoanAPI.DTOs;

/// <summary>
/// DTO for loan calculation response to client
/// </summary>
public class LoanResponseDto
{
    public int LoanRequestId { get; set; }
    public int LoanResultId { get; set; }
    public decimal AdjustedIncome { get; set; }
    public decimal MaximumLoanAmount { get; set; }
    public decimal EstimatedMonthlyRepayment { get; set; }
    public decimal StressTestedRepayment { get; set; }
    public decimal AppliedInterestRate { get; set; }
    public decimal AppliedStressTestRate { get; set; }
    public int LoanTenorMonths { get; set; }
    public string Assumptions { get; set; } = string.Empty;
    // Business-specific flags and diagnostics
    public bool Qualifies { get; set; } = true;
    public string? Message { get; set; }
    public decimal? Deficit { get; set; }
    public decimal? NetMonthlyIncome { get; set; }
    public decimal? ExistingObligations { get; set; }
    public decimal? DbrCap40Percent { get; set; }
    public decimal? AvailableEMI { get; set; }
    public string? DbrUsedPercent { get; set; }
}
