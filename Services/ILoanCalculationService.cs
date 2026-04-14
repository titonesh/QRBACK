using System;
using MortgageLoanAPI.Configurations;
using MortgageLoanAPI.Data;
using MortgageLoanAPI.DTOs;
using MortgageLoanAPI.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace MortgageLoanAPI.Services;

/// <summary>
/// Interface for loan calculation service
/// </summary>
public interface ILoanCalculationService
{
    Task<LoanResponseDto> CalculateLoanAsync(LoanRequestDto request);
}

/// <summary>
/// Service that performs mortgage loan calculations
/// Implements clean architecture and SOLID principles
/// </summary>
public class LoanCalculationService : ILoanCalculationService
{
    private const decimal StandardMortgageAnnualRate = 14.02m;
    private readonly MortgageDbContext _dbContext;
    private readonly LoanConfigurationOptions _config;
    private readonly ILogger<LoanCalculationService> _logger;

    public LoanCalculationService(
        MortgageDbContext dbContext,
        IOptions<LoanConfigurationOptions> configOptions,
        ILogger<LoanCalculationService> logger)
    {
        _dbContext = dbContext;
        _config = configOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Calculates loan eligibility and maximum loan amount
    /// </summary>
    public async Task<LoanResponseDto> CalculateLoanAsync(LoanRequestDto request)
    {
        _logger.LogInformation("Starting loan calculation for customer");

        // Validate input
        ValidateInput(request);

        try
        {
            // Step 1: Create and save LoanRequest
            var loanRequest = new LoanRequest
            {
                MonthlySalaryIncome = request.MonthlySalaryIncome,
                MonthlyBusinessIncome = request.MonthlyBusinessIncome,
                MonthlyRentalPayments = request.MonthlyRentalPayments,
                ExistingLoanObligations = request.ExistingLoanObligations,
                PreferredLoanTenorYears = ValidateLoanTenor(request.PreferredLoanTenorYears)
            };

            _dbContext.LoanRequests.Add(loanRequest);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation($"Created LoanRequest with ID: {loanRequest.Id}");

            // Decide calculation path: Business-specific logic when salary is zero and business income provided
            LoanResponseDto response;
            if (request.MonthlySalaryIncome <= 0 && request.MonthlyBusinessIncome > 0)
            {
                response = CalculateBusinessQualification(request, loanRequest);
            }
            else
            {
                // If there's no business income treat as employed-only and use the simpler employed logic
                if (request.MonthlyBusinessIncome <= 0)
                {
                    response = CalculateEmployedQualification(request, loanRequest);
                }
                else
                {
                    // Step 2: Calculate adjusted income (existing salary/business blended logic)
                    var adjustedIncome = CalculateAdjustedIncome(
                        request.MonthlySalaryIncome,
                        request.MonthlyBusinessIncome,
                        request.MonthlyRentalPayments,
                        request.ExistingLoanObligations
                    );

                    _logger.LogInformation($"Calculated adjusted income: {adjustedIncome:C}");

                    // Step 3: Calculate maximum loan amount
                    var loanTenorMonths = loanRequest.PreferredLoanTenorYears * 12;
                    var monthlyInterestRate = _config.InterestRate / 12;
                    var maximumLoanAmount = CalculateMaximumLoanAmount(
                        adjustedIncome,
                        monthlyInterestRate,
                        loanTenorMonths
                    );

                    // Step 4: Calculate monthly repayments
                    var estimatedMonthlyRepayment = CalculateMonthlyRepayment(
                        maximumLoanAmount,
                        monthlyInterestRate,
                        loanTenorMonths
                    );

                    // Step 5: Calculate stress-tested repayment
                    var monthlyStressTestRate = _config.StressTestRate / 12;
                    var stressTestedRepayment = CalculateMonthlyRepayment(
                        maximumLoanAmount,
                        monthlyStressTestRate,
                        loanTenorMonths
                    );

                    _logger.LogInformation(
                        $"Loan calculations: Max Loan: {maximumLoanAmount:C}, " +
                        $"Monthly Payment: {estimatedMonthlyRepayment:C}, " +
                        $"Stressed Payment: {stressTestedRepayment:C}"
                    );

                    // Step 6: Save LoanResult
                    var loanResult = new LoanResult
                    {
                        LoanRequestId = loanRequest.Id,
                        AdjustedIncome = adjustedIncome,
                        MaximumLoanAmount = maximumLoanAmount,
                        EstimatedMonthlyRepayment = estimatedMonthlyRepayment,
                        StressTestedRepayment = stressTestedRepayment,
                        AppliedInterestRate = _config.InterestRate,
                        AppliedStressTestRate = _config.StressTestRate,
                        LoanTenorMonths = loanTenorMonths
                    };

                    _dbContext.LoanResults.Add(loanResult);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation($"Created LoanResult with ID: {loanResult.Id}");

                    // Step 7: Build assumptions string
                    var assumptions = BuildAssumptions(request, loanResult);

                    // Step 8: Build response for blended path
                    response = new LoanResponseDto
                    {
                        LoanRequestId = loanRequest.Id,
                        LoanResultId = loanResult.Id,
                        AdjustedIncome = adjustedIncome,
                        MaximumLoanAmount = maximumLoanAmount,
                        EstimatedMonthlyRepayment = estimatedMonthlyRepayment,
                        StressTestedRepayment = stressTestedRepayment,
                        AppliedInterestRate = _config.InterestRate,
                        AppliedStressTestRate = _config.StressTestRate,
                        LoanTenorMonths = loanTenorMonths,
                        Assumptions = assumptions,
                        Qualifies = maximumLoanAmount > 0
                    };
                }
            }

            _logger.LogInformation("Loan calculation completed successfully");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during loan calculation: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Implements the business-specific qualification logic from the provided pseudocode.
    /// </summary>
    private LoanResponseDto CalculateBusinessQualification(LoanRequestDto request, LoanRequest savedRequest)
    {
        // Constants per pseudocode
        const decimal DBR_RATE = 0.40m;
        const decimal CARD_UTIL = 0.10m;
        const decimal OD_UTIL = 0.05m;
        const decimal PROFIT_MARGIN = 0.50m;

        // Treat MonthlyBusinessIncome as monthly turnover
        var monthlyTurnover = request.MonthlyBusinessIncome;

        // Step 1: Net income from turnover
        var netIncome = monthlyTurnover * PROFIT_MARGIN;

        // Step 2: Existing obligations — include monthly rental payments as part of obligations
        var existingObligations = request.ExistingLoanObligations + request.MonthlyRentalPayments;

        // Step 3: DBR cap (40% of net income)
        var dbrCap = netIncome * DBR_RATE;

        // Step 4: Available EMI (DBR cap less existing obligations)
        var availableEMI = dbrCap - existingObligations;

        // Prepare base response
        var loanTenorMonths = savedRequest.PreferredLoanTenorYears * 12;

        if (availableEMI <= 0)
        {
            return new LoanResponseDto
            {
                LoanRequestId = savedRequest.Id,
                LoanResultId = 0,
                AdjustedIncome = netIncome,
                MaximumLoanAmount = 0,
                EstimatedMonthlyRepayment = 0,
                AppliedInterestRate = 0,
                LoanTenorMonths = loanTenorMonths,
                Qualifies = false,
                Message = "Existing obligations exceed 40% of income",
                Deficit = Math.Abs(availableEMI),
                NetMonthlyIncome = Math.Round(netIncome),
                ExistingObligations = Math.Round(existingObligations),
                DbrCap40Percent = Math.Round(dbrCap),
                AvailableEMI = Math.Round(availableEMI),
                DbrUsedPercent = ((existingObligations + Math.Max(0, availableEMI)) / (netIncome == 0 ? 1 : netIncome) * 100).ToString("0.0") + "%"
            };
        }

        // Step 5: Interest Rate based on Turnover (per pseudocode)
        decimal annualRate = monthlyTurnover < 2000000m ? 9.5m : 9.9m;
        
        // Step 6: Monthly Rate
        var monthlyRate = (annualRate / 100m) / 12m;

        // Step 7: n already computed
        var n = loanTenorMonths;

        // Step 8: Calculate loan amount using annuity inversion
        var loanAmount = CalculateMaximumLoanAmount(availableEMI, monthlyRate, n);

        // Round results
        var roundedLoan = Math.Round(loanAmount);
        var roundedAvailableEMI = Math.Round(availableEMI);

        // Persist LoanResult
        var loanResult = new LoanResult
        {
            LoanRequestId = savedRequest.Id,
            AdjustedIncome = netIncome,
            MaximumLoanAmount = roundedLoan,
            EstimatedMonthlyRepayment = roundedAvailableEMI,
            StressTestedRepayment = 0,
            AppliedInterestRate = annualRate,
            AppliedStressTestRate = _config.StressTestRate,
            LoanTenorMonths = n
        };

        _dbContext.LoanResults.Add(loanResult);
        _dbContext.SaveChanges();

        return new LoanResponseDto
        {
            LoanRequestId = savedRequest.Id,
            LoanResultId = loanResult.Id,
            AdjustedIncome = Math.Round(netIncome),
            MaximumLoanAmount = roundedLoan,
            EstimatedMonthlyRepayment = roundedAvailableEMI,
            AppliedInterestRate = annualRate,
            LoanTenorMonths = n,
            Qualifies = true,
            NetMonthlyIncome = Math.Round(netIncome),
            ExistingObligations = Math.Round(existingObligations),
            DbrCap40Percent = Math.Round(dbrCap),
            AvailableEMI = roundedAvailableEMI,
            DbrUsedPercent = ((existingObligations + roundedAvailableEMI) / (netIncome == 0 ? 1 : netIncome) * 100).ToString("0.0") + "%",
            Assumptions = $"Business flow: ProfitMargin={PROFIT_MARGIN:P}, CardUtil={CARD_UTIL:P}, ODUtil={OD_UTIL:P}, DBR={DBR_RATE:P}, AnnualRate={annualRate}%"
        };
    }

    /// <summary>
    /// Calculates adjusted income based on salary, business income, and rental contributions
    /// Formula: (Salary * 0.6) + (Business * 0.2 * (1 - discount)) + Rental - Existing Obligations
    /// </summary>
    private decimal CalculateAdjustedIncome(
        decimal salaryIncome,
        decimal businessIncome,
        decimal rentalPayments,
        decimal existingObligations)
    {
        // Salary contribution: 60% of salary
        var salaryContribution = salaryIncome * _config.SalaryAffordabilityRatio;

        // Business contribution: 20% of business income, discounted by 25%
        var businessDiscount = 1 - _config.BusinessIncomeDiscount;
        var businessContribution = businessIncome * _config.BusinessAffordabilityRatio * businessDiscount;

        // Rental contribution: included if enabled
        var rentalContribution = _config.RentalContributionEnabled ? rentalPayments : 0;

        // Total affordability
        var totalAffordability = salaryContribution + businessContribution + rentalContribution;

        // Subtract existing obligations
        var adjustedIncome = totalAffordability - existingObligations;

        // Ensure non-negative
        return Math.Max(0, adjustedIncome);
    }

    /// <summary>
    /// Calculates maximum loan amount using the standard mortgage formula (reverse calculation)
    /// M = P * [r(1+r)^n] / [(1+r)^n - 1]
    /// Solving for P: P = M * [(1+r)^n - 1] / [r(1+r)^n]
    /// </summary>
    private decimal CalculateMaximumLoanAmount(
        decimal monthlyAffordability,
        decimal monthlyInterestRate,
        int loanDurationMonths)
    {
        if (monthlyInterestRate == 0)
        {
            // If interest rate is zero, use simple division
            return monthlyAffordability * loanDurationMonths;
        }

        // Calculate (1 + r)^n
        var rPlusOne = 1 + monthlyInterestRate;
        var rPlusOnePowerN = CalculatePower(rPlusOne, loanDurationMonths);

        // P = M * [(1+r)^n - 1] / [r(1+r)^n]
        var numerator = rPlusOnePowerN - 1;
        var denominator = monthlyInterestRate * rPlusOnePowerN;

        var maximumLoanAmount = monthlyAffordability * (numerator / denominator);

        return Math.Max(0, maximumLoanAmount);
    }

    /// <summary>
    /// Calculates monthly repayment using standard mortgage formula
    /// M = P * [r(1+r)^n] / [(1+r)^n - 1]
    /// </summary>
    private decimal CalculateMonthlyRepayment(
        decimal principalAmount,
        decimal monthlyInterestRate,
        int loanDurationMonths)
    {
        if (monthlyInterestRate == 0)
        {
            // If interest rate is zero, use simple division
            return principalAmount / loanDurationMonths;
        }

        // Calculate (1 + r)^n
        var rPlusOne = 1 + monthlyInterestRate;
        var rPlusOnePowerN = CalculatePower(rPlusOne, loanDurationMonths);

        // M = P * [r(1+r)^n] / [(1+r)^n - 1]
        var numerator = monthlyInterestRate * rPlusOnePowerN;
        var denominator = rPlusOnePowerN - 1;

        var monthlyRepayment = principalAmount * (numerator / denominator);

        return Math.Max(0, monthlyRepayment);
    }

    /// <summary>
    /// Helper method to calculate power without using Math.Pow to avoid floating-point precision issues
    /// </summary>
    private decimal CalculatePower(decimal baseValue, int exponent)
    {
        var result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= baseValue;
        }
        return result;
    }

    /// <summary>
    /// Validates loan tenor is within acceptable range
    /// </summary>
    private int ValidateLoanTenor(int preferredTenor)
    {
        if (preferredTenor < _config.MinLoanTenorYears)
            return _config.MinLoanTenorYears;
        if (preferredTenor > _config.MaxLoanTenorYears)
            return _config.MaxLoanTenorYears;
        return preferredTenor;
    }

    /// <summary>
    /// Validates loan request input parameters
    /// </summary>
    private void ValidateInput(LoanRequestDto request)
    {
        if (request.MonthlySalaryIncome < 0 || request.MonthlyBusinessIncome < 0 ||
            request.MonthlyRentalPayments < 0 || request.ExistingLoanObligations < 0)
        {
            throw new ArgumentException("Income values cannot be negative");
        }

        if (request.CreditCardLimit < 0 || request.OverdraftLimit < 0)
        {
            throw new ArgumentException("Credit card or overdraft limits cannot be negative");
        }

        if (request.PreferredLoanTenorYears < 1)
        {
            throw new ArgumentException("Loan tenor must be at least 1 year");
        }
    }

    /// <summary>
    /// Builds assumptions description for transparency
    /// </summary>
    private LoanResponseDto CalculateEmployedQualification(LoanRequestDto request, LoanRequest savedRequest)
    {
        // Step 1: DBR Cap (60%)
        var netMonthlyIncome = request.MonthlySalaryIncome;
        var dbrCap = netMonthlyIncome * 0.60m;

        // Step 2: Available EMI
        var availableEMI = dbrCap - request.ExistingLoanObligations;

        var loanTenorMonths = savedRequest.PreferredLoanTenorYears * 12;

        if (availableEMI <= 0)
        {
            return new LoanResponseDto
            {
                LoanRequestId = savedRequest.Id,
                LoanResultId = 0,
                AdjustedIncome = netMonthlyIncome,
                MaximumLoanAmount = 0,
                EstimatedMonthlyRepayment = 0,
                AppliedInterestRate = 0,
                LoanTenorMonths = loanTenorMonths,
                Qualifies = false,
                Message = "Does not qualify",
                Deficit = Math.Abs(availableEMI),
                NetMonthlyIncome = Math.Round(netMonthlyIncome),
                ExistingObligations = Math.Round(request.ExistingLoanObligations),
                DbrCap40Percent = Math.Round(dbrCap),
                AvailableEMI = Math.Round(availableEMI),
                DbrUsedPercent = ((request.ExistingLoanObligations + Math.Max(0, availableEMI)) / (netMonthlyIncome == 0 ? 1 : netMonthlyIncome) * 100).ToString("0.0") + "%"
            };
        }

        // Standard Mortgage uses a fixed employed rate; AHF remains tenor-based.
        decimal annualRate = IsStandardMortgage(request)
            ? StandardMortgageAnnualRate
            : savedRequest.PreferredLoanTenorYears <= 20 ? 9.5m : 9.9m;

        // Step 5: Monthly Rate
        var monthlyRate = (annualRate / 100m) / 12m;

        // Step 6: Tenor in months
        var n = loanTenorMonths;

        // Step 7: Loan Amount (use existing helper)
        var loanAmount = CalculateMaximumLoanAmount(availableEMI, monthlyRate, n);

        var roundedLoan = Math.Round(loanAmount);
        var roundedAvailableEMI = Math.Round(availableEMI);

        // Persist LoanResult
        var loanResult = new LoanResult
        {
            LoanRequestId = savedRequest.Id,
            AdjustedIncome = netMonthlyIncome,
            MaximumLoanAmount = roundedLoan,
            EstimatedMonthlyRepayment = roundedAvailableEMI,
            StressTestedRepayment = 0,
            AppliedInterestRate = annualRate,
            AppliedStressTestRate = _config.StressTestRate,
            LoanTenorMonths = n
        };

        _dbContext.LoanResults.Add(loanResult);
        _dbContext.SaveChanges();

        return new LoanResponseDto
        {
            LoanRequestId = savedRequest.Id,
            LoanResultId = loanResult.Id,
            AdjustedIncome = Math.Round(netMonthlyIncome),
            MaximumLoanAmount = roundedLoan,
            EstimatedMonthlyRepayment = roundedAvailableEMI,
            AppliedInterestRate = annualRate,
            LoanTenorMonths = n,
            Qualifies = true,
            NetMonthlyIncome = Math.Round(netMonthlyIncome),
            ExistingObligations = Math.Round(request.ExistingLoanObligations),
            DbrCap40Percent = Math.Round(dbrCap),
            AvailableEMI = roundedAvailableEMI,
            DbrUsedPercent = ((request.ExistingLoanObligations + roundedAvailableEMI) / (netMonthlyIncome == 0 ? 1 : netMonthlyIncome) * 100).ToString("0.0") + "%",
            Assumptions = $"Employed flow: ProductType={request.ProductType ?? "unspecified"}, DBR=60%, AnnualRate={annualRate}%"
        };
    }

    private static bool IsStandardMortgage(LoanRequestDto request)
    {
        return string.Equals(request.ProductType, "standard", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildAssumptions(LoanRequestDto request, LoanResult result)
    {
        return $"Assumptions: " +
               $"Salary Affordability Ratio: {_config.SalaryAffordabilityRatio:P}, " +
               $"Business Affordability Ratio: {_config.BusinessAffordabilityRatio:P}, " +
               $"Business Income Discount: {_config.BusinessIncomeDiscount:P}, " +
               $"Interest Rate: {_config.InterestRate:P2}, " +
               $"Stress Test Rate: {_config.StressTestRate:P2}, " +
               $"Loan Tenor: {result.LoanTenorMonths / 12} years, " +
               $"Rental Contribution Enabled: {_config.RentalContributionEnabled}";
    }
}
