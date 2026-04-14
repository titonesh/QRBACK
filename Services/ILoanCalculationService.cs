using System;
using System.Threading.Tasks;
using MortgageLoanAPI.Configurations;
using MortgageLoanAPI.Data;
using MortgageLoanAPI.DTOs;
using MortgageLoanAPI.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace MortgageLoanAPI.Services
{
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

                // Route based on ProductType and IncomeSourceType
                var productType = (request.ProductType ?? string.Empty).Trim().ToLowerInvariant();
                var incomeSourceType = (request.IncomeSourceType ?? string.Empty).Trim().ToLowerInvariant();

                LoanResponseDto response;

                if (productType == "stdmortgage")
                {
                    // Std Mortgage path (salaried only)
                    response = CalculateStandardMortgage(request, loanRequest);
                }
                else if (productType == "affordablehousing")
                {
                    // Affordable Housing path - depends on income source
                    if (incomeSourceType == "business")
                    {
                        response = CalculateAffordableBusiness(request, loanRequest);
                    }
                    else // default to employed
                    {
                        response = CalculateAffordableEmployed(request, loanRequest);
                    }
                }
                else
                {
                    // Default to affordable housing employed
                    response = CalculateAffordableEmployed(request, loanRequest);
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
        /// Std Mortgage calculation (salaried employees)
        /// </summary>
        private LoanResponseDto CalculateStandardMortgage(LoanRequestDto request, LoanRequest savedRequest)
        {
            // Constants
            const decimal DIR_RATE = 0.60m;
            const decimal ANNUAL_RATE = 14.02m;
            const int MAX_TENOR_YEARS = 25;
            const decimal CARD_UTIL = 0.10m;
            const decimal OD_UTIL = 0.05m;

            // Step 0: Tenor Validation
            if (savedRequest.PreferredLoanTenorYears > MAX_TENOR_YEARS)
            {
                return new LoanResponseDto
                {
                    LoanRequestId = savedRequest.Id,
                    LoanResultId = 0,
                    Qualifies = false,
                    Message = $"Tenor cannot exceed {MAX_TENOR_YEARS} years"
                };
            }

            // Step 1: Credit Card Obligation
            decimal cardObligation = request.CreditCardLimit * CARD_UTIL;

            // Step 2: Overdraft Obligation
            decimal odObligation = request.OverdraftLimit * OD_UTIL;

            // Step 3: Total Existing Obligations (includes Card + OD)
            decimal existingObligations = request.ExistingLoanObligations + cardObligation + odObligation;

            // Step 4: DIR Cap (60%)
            decimal dirCap = request.MonthlySalaryIncome * DIR_RATE;

            // Step 5: Available EMI
            decimal availableEMI = dirCap - existingObligations;

            // Step 6: Qualification Check
            if (availableEMI <= 0)
            {
                return new LoanResponseDto
                {
                    LoanRequestId = savedRequest.Id,
                    LoanResultId = 0,
                    Qualifies = false,
                    Message = "Existing obligations exceed 60% DIR",
                    NetMonthlyIncome = request.MonthlySalaryIncome,
                    ExistingObligations = Math.Round(existingObligations),
                    DbrCap40Percent = Math.Round(dirCap),
                    AvailableEMI = Math.Round(availableEMI)
                };
            }

            // Step 7: Monthly Rate
            decimal monthlyRate = (ANNUAL_RATE / 100m) / 12m;

            // Step 8: Tenor in months
            int n = savedRequest.PreferredLoanTenorYears * 12;

            // Step 9: Calculate Loan Amount (No cap)
            double r = (double)monthlyRate;
            double emi = (double)availableEMI;
            double loanAmount = emi * (Math.Pow(1 + r, n) - 1) / (r * Math.Pow(1 + r, n));

            var loanResult = new LoanResult
            {
                LoanRequestId = savedRequest.Id,
                AdjustedIncome = request.MonthlySalaryIncome,
                MaximumLoanAmount = Math.Round((decimal)loanAmount),
                EstimatedMonthlyRepayment = Math.Round(availableEMI),
                StressTestedRepayment = 0,
                AppliedInterestRate = ANNUAL_RATE / 100m,
                AppliedStressTestRate = 0,
                LoanTenorMonths = n
            };
            _dbContext.LoanResults.Add(loanResult);
            _dbContext.SaveChanges();

            // Step 10: Return Results
            return new LoanResponseDto
            {
                LoanRequestId = savedRequest.Id,
                LoanResultId = loanResult.Id,
                Qualifies = true,
                MaximumLoanAmount = Math.Round((decimal)loanAmount),
                EstimatedMonthlyRepayment = Math.Round(availableEMI),
                AppliedInterestRate = ANNUAL_RATE / 100m,
                LoanTenorMonths = n,
                AdjustedIncome = request.MonthlySalaryIncome,
                NetMonthlyIncome = request.MonthlySalaryIncome,
                DbrCap40Percent = Math.Round(dirCap),
                ExistingObligations = Math.Round(existingObligations),
                AvailableEMI = Math.Round(availableEMI),
                DbrUsedPercent = ((existingObligations + availableEMI) / request.MonthlySalaryIncome * 100).ToString("F1") + "%",
                CappingApplied = false,
                Assumptions = $"Std Mortgage: DIR=60%, Rate={ANNUAL_RATE}%, Tenor={savedRequest.PreferredLoanTenorYears}yrs"
            };
        }

        /// <summary>
        /// Affordable Housing - Business income path
        /// </summary>
        private LoanResponseDto CalculateAffordableBusiness(LoanRequestDto request, LoanRequest savedRequest)
        {
            // Constants
            const decimal PROFIT_MARGIN = 0.50m;
            const decimal DBR_RATE = 0.40m;
            const decimal CARD_UTIL = 0.10m;
            const decimal OD_UTIL = 0.05m;
            const decimal MAX_LOAN_AMOUNT = 10500000m;

            // Step 1: Net Monthly Income
            decimal netIncome = request.MonthlyBusinessIncome * PROFIT_MARGIN;

            // Step 2: Credit Card Obligation
            decimal cardObligation = request.CreditCardLimit * CARD_UTIL;

            // Step 3: Overdraft Obligation
            decimal odObligation = request.OverdraftLimit * OD_UTIL;

            // Step 4: Total Existing Obligations (includes OD)
            decimal existingObligations = request.ExistingLoanObligations + cardObligation + odObligation;

            // Step 5: DBR Cap (40%)
            decimal dbrCap = netIncome * DBR_RATE;

            // Step 6: Available EMI
            decimal availableEMI = dbrCap - existingObligations;

            // Step 7: Qualification Check
            if (availableEMI <= 0)
            {
                return new LoanResponseDto
                {
                    LoanRequestId = savedRequest.Id,
                    LoanResultId = 0,
                    Qualifies = false,
                    Message = "Existing obligations exceed 40% of income",
                    NetMonthlyIncome = Math.Round(netIncome),
                    ExistingObligations = Math.Round(existingObligations),
                    DbrCap40Percent = Math.Round(dbrCap),
                    AvailableEMI = Math.Round(availableEMI)
                };
            }

            // Step 8: Interest Rate based on Turnover
            decimal annualRate = request.MonthlyBusinessIncome < 2000000m ? 9.5m : 9.9m;

            // Step 9: Monthly Rate
            decimal monthlyRate = (annualRate / 100m) / 12m;

            // Step 10: Tenor in months
            int n = savedRequest.PreferredLoanTenorYears * 12;

            // Step 11: Calculate Loan Amount
            double r = (double)monthlyRate;
            double emi = (double)availableEMI;
            double calculatedLoan = emi * (Math.Pow(1 + r, n) - 1) / (r * Math.Pow(1 + r, n));
            decimal calculatedLoanDecimal = (decimal)calculatedLoan;

            // Step 12: Apply Capping Logic
            decimal finalLoanAmount;
            decimal monthlyInstalment;
            bool cappingApplied;

            if (calculatedLoanDecimal > MAX_LOAN_AMOUNT)
            {
                finalLoanAmount = MAX_LOAN_AMOUNT;
                cappingApplied = true;
                double p = (double)finalLoanAmount;
                double recalculatedEMI = p * (r * Math.Pow(1 + r, n)) / (Math.Pow(1 + r, n) - 1);
                monthlyInstalment = (decimal)Math.Round(recalculatedEMI);
            }
            else
            {
                finalLoanAmount = Math.Round(calculatedLoanDecimal);
                monthlyInstalment = Math.Round(availableEMI);
                cappingApplied = false;
            }

            var loanResult = new LoanResult
            {
                LoanRequestId = savedRequest.Id,
                AdjustedIncome = Math.Round(netIncome),
                MaximumLoanAmount = finalLoanAmount,
                EstimatedMonthlyRepayment = monthlyInstalment,
                StressTestedRepayment = 0,
                AppliedInterestRate = annualRate / 100m,
                AppliedStressTestRate = 0,
                LoanTenorMonths = n
            };
            _dbContext.LoanResults.Add(loanResult);
            _dbContext.SaveChanges();

            return new LoanResponseDto
            {
                LoanRequestId = savedRequest.Id,
                LoanResultId = loanResult.Id,
                Qualifies = true,
                MaximumLoanAmount = finalLoanAmount,
                EstimatedMonthlyRepayment = monthlyInstalment,
                CappingApplied = cappingApplied,
                CalculatedLoanBeforeCap = (int)Math.Round(calculatedLoanDecimal),
                AdjustedIncome = Math.Round(netIncome),
                NetMonthlyIncome = Math.Round(netIncome),
                ExistingObligations = Math.Round(existingObligations),
                DbrCap40Percent = Math.Round(dbrCap),
                AvailableEMI = Math.Round(availableEMI),
                AppliedInterestRate = annualRate / 100m,
                LoanTenorMonths = n,
                Message = cappingApplied ? "Loan amount capped to policy maximum of KES 10,500,000" : null,
                Assumptions = $"Affordable Business: ProfitMargin=50%, DBR=40%, Rate={annualRate}%, Tenor={savedRequest.PreferredLoanTenorYears}yrs"
            };
        }

        /// <summary>
        /// Affordable Housing - Employed income path
        /// </summary>
        private LoanResponseDto CalculateAffordableEmployed(LoanRequestDto request, LoanRequest savedRequest)
        {
            // Constants
            const decimal MAX_NET_INCOME = 658279.65m;
            const decimal DIR_RATE = 0.60m;
            const decimal CARD_UTIL = 0.10m;
            const decimal OD_UTIL = 0.05m;
            const decimal MAX_LOAN_AMOUNT = 10500000m;

            // Step 0: Income Cap Check
            if (request.MonthlySalaryIncome > MAX_NET_INCOME)
            {
                return new LoanResponseDto
                {
                    LoanRequestId = savedRequest.Id,
                    LoanResultId = 0,
                    Qualifies = false,
                    Message = $"Net income exceeds maximum allowed of {MAX_NET_INCOME:F2}",
                    NetMonthlyIncome = request.MonthlySalaryIncome,
                    AdjustedIncome = request.MonthlySalaryIncome
                };
            }

            // Step 1: Credit Card Obligation
            decimal cardObligation = request.CreditCardLimit * CARD_UTIL;

            // Step 2: Overdraft Obligation
            decimal odObligation = request.OverdraftLimit * OD_UTIL;

            // Step 3: Total Existing Obligations (includes Card + OD)
            decimal existingObligations = request.ExistingLoanObligations + cardObligation + odObligation;

            // Step 4: DIR Cap (60%)
            decimal dirCap = request.MonthlySalaryIncome * DIR_RATE;

            // Step 5: Available EMI
            decimal availableEMI = dirCap - existingObligations;

            // Step 6: Qualification Check
            if (availableEMI <= 0)
            {
                return new LoanResponseDto
                {
                    LoanRequestId = savedRequest.Id,
                    LoanResultId = 0,
                    Qualifies = false,
                    Message = "Existing obligations exceed 60% DIR",
                    NetMonthlyIncome = request.MonthlySalaryIncome,
                    ExistingObligations = Math.Round(existingObligations),
                    DbrCap40Percent = Math.Round(dirCap),
                    AvailableEMI = Math.Round(availableEMI)
                };
            }

            // Step 7: Interest Rate based on Tenor
            decimal annualRate = savedRequest.PreferredLoanTenorYears <= 20 ? 9.5m : 9.9m;

            // Step 8: Monthly Rate
            decimal monthlyRate = (annualRate / 100m) / 12m;

            // Step 9: Tenor in months
            int n = savedRequest.PreferredLoanTenorYears * 12;

            // Step 10: Calculate Loan Amount
            double r = (double)monthlyRate;
            double emi = (double)availableEMI;
            double calculatedLoan = emi * (Math.Pow(1 + r, n) - 1) / (r * Math.Pow(1 + r, n));
            decimal calculatedLoanDecimal = (decimal)calculatedLoan;

            // Step 11: Apply Capping Logic
            decimal finalLoanAmount;
            decimal monthlyInstalment;
            bool cappingApplied;

            if (calculatedLoanDecimal > MAX_LOAN_AMOUNT)
            {
                finalLoanAmount = MAX_LOAN_AMOUNT;
                cappingApplied = true;
                double p = (double)finalLoanAmount;
                double recalculatedEMI = p * (r * Math.Pow(1 + r, n)) / (Math.Pow(1 + r, n) - 1);
                monthlyInstalment = (decimal)Math.Round(recalculatedEMI);
            }
            else
            {
                finalLoanAmount = Math.Round(calculatedLoanDecimal);
                monthlyInstalment = Math.Round(availableEMI);
                cappingApplied = false;
            }

            var loanResult = new LoanResult
            {
                LoanRequestId = savedRequest.Id,
                AdjustedIncome = request.MonthlySalaryIncome,
                MaximumLoanAmount = finalLoanAmount,
                EstimatedMonthlyRepayment = monthlyInstalment,
                StressTestedRepayment = 0,
                AppliedInterestRate = annualRate / 100m,
                AppliedStressTestRate = 0,
                LoanTenorMonths = n
            };
            _dbContext.LoanResults.Add(loanResult);
            _dbContext.SaveChanges();

            return new LoanResponseDto
            {
                LoanRequestId = savedRequest.Id,
                LoanResultId = loanResult.Id,
                Qualifies = true,
                MaximumLoanAmount = finalLoanAmount,
                EstimatedMonthlyRepayment = monthlyInstalment,
                CappingApplied = cappingApplied,
                CalculatedLoanBeforeCap = (int)Math.Round(calculatedLoanDecimal),
                AdjustedIncome = request.MonthlySalaryIncome,
                NetMonthlyIncome = request.MonthlySalaryIncome,
                DbrCap40Percent = Math.Round(dirCap),
                ExistingObligations = Math.Round(existingObligations),
                AvailableEMI = Math.Round(availableEMI),
                AppliedInterestRate = annualRate / 100m,
                LoanTenorMonths = n,
                DbrUsedPercent = ((existingObligations + availableEMI) / request.MonthlySalaryIncome * 100).ToString("F1") + "%",
                Message = cappingApplied ? "Loan amount capped to policy maximum of KES 10,500,000" : null,
                Assumptions = $"Affordable Employed: DIR=60%, MaxIncome={MAX_NET_INCOME:N2}, Rate={annualRate}%, Tenor={savedRequest.PreferredLoanTenorYears}yrs"
            };
        }

        /// <summary>
        /// Calculates adjusted income based on salary, business income, and rental contributions
        /// </summary>
        private decimal CalculateAdjustedIncome(
            decimal salaryIncome,
            decimal businessIncome,
            decimal rentalPayments,
            decimal existingObligations)
        {
            var salaryContribution = salaryIncome * _config.SalaryAffordabilityRatio;
            var businessDiscount = 1 - _config.BusinessIncomeDiscount;
            var businessContribution = businessIncome * _config.BusinessAffordabilityRatio * businessDiscount;
            var rentalContribution = _config.RentalContributionEnabled ? rentalPayments : 0;
            var totalAffordability = salaryContribution + businessContribution + rentalContribution;
            var adjustedIncome = totalAffordability - existingObligations;
            return Math.Max(0, adjustedIncome);
        }

        /// <summary>
        /// Calculates maximum loan amount using the standard mortgage formula (reverse calculation)
        /// </summary>
        private decimal CalculateMaximumLoanAmount(
            decimal monthlyAffordability,
            decimal monthlyInterestRate,
            int loanDurationMonths)
        {
            if (monthlyInterestRate == 0)
            {
                return monthlyAffordability * loanDurationMonths;
            }

            var rPlusOne = 1 + monthlyInterestRate;
            var rPlusOnePowerN = CalculatePower(rPlusOne, loanDurationMonths);
            var numerator = rPlusOnePowerN - 1;
            var denominator = monthlyInterestRate * rPlusOnePowerN;
            var maximumLoanAmount = monthlyAffordability * (numerator / denominator);
            return Math.Max(0, maximumLoanAmount);
        }

        /// <summary>
        /// Calculates monthly repayment using standard mortgage formula
        /// </summary>
        private decimal CalculateMonthlyRepayment(
            decimal principalAmount,
            decimal monthlyInterestRate,
            int loanDurationMonths)
        {
            if (monthlyInterestRate == 0)
            {
                return principalAmount / loanDurationMonths;
            }

            var rPlusOne = 1 + monthlyInterestRate;
            var rPlusOnePowerN = CalculatePower(rPlusOne, loanDurationMonths);
            var numerator = monthlyInterestRate * rPlusOnePowerN;
            var denominator = rPlusOnePowerN - 1;
            var monthlyRepayment = principalAmount * (numerator / denominator);
            return Math.Max(0, monthlyRepayment);
        }

        private decimal CalculatePower(decimal baseValue, int exponent)
        {
            var result = 1m;
            for (int i = 0; i < exponent; i++) result *= baseValue;
            return result;
        }

        private int ValidateLoanTenor(int preferredTenor)
        {
            if (preferredTenor < _config.MinLoanTenorYears)
                return _config.MinLoanTenorYears;
            if (preferredTenor > _config.MaxLoanTenorYears)
                return _config.MaxLoanTenorYears;
            return preferredTenor;
        }

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
}