using System;
using Microsoft.AspNetCore.Mvc;
using MortgageLoanAPI.DTOs;
using System.Text.Json;
using MortgageLoanAPI.Services;

namespace MortgageLoanAPI.Controllers;

/// <summary>
/// Controller for loan-related API endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LoanController : ControllerBase
{
    private readonly ILoanCalculationService _loanCalculationService;
    private readonly ICallbackRequestService _callbackRequestService;
    private readonly ILogger<LoanController> _logger;

    public LoanController(
        ILoanCalculationService loanCalculationService,
        ICallbackRequestService callbackRequestService,
        ILogger<LoanController> logger)
    {
        _loanCalculationService = loanCalculationService;
        _callbackRequestService = callbackRequestService;
        _logger = logger;
    }

    /// <summary>
    /// PATCH api/loan/callback-requests/{id}
    /// Update a callback request (notes/status)
    /// </summary>
    [HttpPatch("callback-requests/{id}")]
    public async Task<ActionResult<object>> UpdateCallbackRequest(int id, [FromBody] CallbackRequestUpdateDto dto)
    {
        try
        {
            var updated = await _callbackRequestService.UpdateCallbackRequestAsync(id, dto);
            return Ok(new { id = updated.Id, isProcessed = updated.IsProcessed, processedAt = updated.ProcessedAt, message = updated.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Callback request not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating callback request");
            return StatusCode(500, new { message = "Failed to update callback request" });
        }
    }

    /// <summary>
    /// PATCH api/loan/callback-requests/bulk
    /// Bulk update callback requests (e.g., mark contacted)
    /// </summary>
    [HttpPatch("callback-requests/bulk")]
    public async Task<ActionResult<object>> BulkUpdateCallbackRequests([FromBody] BulkUpdateDto dto)
    {
        try
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0) return BadRequest(new { message = "No ids provided" });
            if (string.Equals(dto.Action, "markContacted", StringComparison.OrdinalIgnoreCase))
            {
                await _callbackRequestService.BulkMarkContactedAsync(dto.Ids);
                return Ok(new { message = "Marked as contacted", count = dto.Ids.Count });
            }
            return BadRequest(new { message = "Unknown action" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk update failed");
            return StatusCode(500, new { message = "Bulk update failed" });
        }
    }

    /// <summary>
    /// POST api/loan/calculate
    /// Calculates loan eligibility and maximum loan amount based on customer income
    /// </summary>
    /// <param name="request">Loan calculation request with customer financial details</param>
    /// <returns>Loan calculation response with results and assumptions</returns>
    /// <response code="200">Returns calculated loan details</response>
    /// <response code="400">If request data is invalid</response>
    /// <response code="500">If server error occurs during calculation</response>
    [HttpPost("calculate")]
    public async Task<ActionResult<LoanResponseDto>> CalculateLoan(
        [FromBody] LoanRequestDto request)
    {
        try
        {
            _logger.LogInformation("Received loan calculation request");

            // Validate request
            if (!ModelState.IsValid)
            {
                return BadRequest(new 
                { 
                    message = "Invalid request data",
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                });
            }

            // Call service
            var result = await _loanCalculationService.CalculateLoanAsync(request);

            _logger.LogInformation(
                $"Loan calculation completed. LoanRequestId: {result.LoanRequestId}");

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning($"Bad request: {ex.Message}");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating loan");
            return StatusCode(500, new 
            { 
                message = "An error occurred while calculating loan",
                details = "An internal error occurred"
            });
        }
    }

    /// <summary>
    /// POST api/loan/callback-request
    /// Creates a callback request from customer for further assistance
    /// </summary>
    /// <param name="request">Callback request with customer contact information</param>
    /// <returns>Confirmation of callback request creation</returns>
    /// <response code="201">If callback request is created successfully</response>
    /// <response code="400">If request data is invalid</response>
    /// <response code="404">If loan result not found</response>
    /// <response code="500">If server error occurs</response>
    [HttpPost("callback-request")]
    public async Task<ActionResult<object>> CreateCallbackRequest(
        [FromBody] CallbackRequestDto request)
    {
        try
        {
            _logger.LogInformation(
                $"Received callback request for LoanResultId: {request.LoanResultId}");

            try
            {
                var payloadJson = JsonSerializer.Serialize(request);
                _logger.LogInformation($"Callback request payload: {payloadJson}");
            }
            catch { }

            if (!ModelState.IsValid)
            {
                return BadRequest(new 
                { 
                    message = "Invalid request data",
                    errors = ModelState.Values.SelectMany(v => v.Errors)
                });
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.PhoneNumber) ||
                string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new 
                { 
                    message = "FullName, PhoneNumber, and Email are required"
                });
            }

            var callbackRequest = await _callbackRequestService
                .CreateCallbackRequestAsync(request);

            _logger.LogInformation(
                $"Callback request created. CallbackRequestId: {callbackRequest.Id}");

            return CreatedAtAction(nameof(CreateCallbackRequest), 
                new { id = callbackRequest.Id },
                new
                {
                    id = callbackRequest.Id,
                    message = "Callback request created successfully. We will contact you shortly.",
                    loanResultId = callbackRequest.LoanResultId,
                    createdAt = callbackRequest.CreatedAt
                });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Loan result not found: {ex.Message}");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating callback request");
            return StatusCode(500, new 
            { 
                message = "An error occurred while creating callback request",
                details = "An internal error occurred"
            });
        }
    }

    /// <summary>
    /// GET api/loan/callback-requests
    /// Returns all callback requests (admin view)
    /// </summary>
    [HttpGet("callback-requests")]
    public async Task<ActionResult<object>> GetCallbackRequests([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? q = null, [FromQuery] string? status = "all", [FromQuery] string? dateRange = "7d")
    {
        try
        {
            var (items, total) = await _callbackRequestService.GetCallbackRequestsPagedAsync(page, pageSize, q, status, dateRange);

            var payload = items.Select(c => new {
                id = c.Id,
                loanResultId = c.LoanResultId,
                fullName = c.FullName,
                phoneNumber = c.PhoneNumber,
                referralNumber = c.ReferralNumber,
                email = c.Email,
                message = c.Message,
                loanInputsJson = c.LoanInputsJson,
                loanResultJson = string.IsNullOrWhiteSpace(c.LoanResultJson)
                    ? (c.LoanResult == null
                        ? null
                        : JsonSerializer.Serialize(new
                        {
                            loanResultId = c.LoanResult.Id,
                            loanRequestId = c.LoanResult.LoanRequestId,
                            adjustedIncome = c.LoanResult.AdjustedIncome,
                            maximumLoanAmount = c.LoanResult.MaximumLoanAmount,
                            estimatedMonthlyRepayment = c.LoanResult.EstimatedMonthlyRepayment,
                            stressTestedRepayment = c.LoanResult.StressTestedRepayment,
                            appliedInterestRate = c.LoanResult.AppliedInterestRate,
                            appliedStressTestRate = c.LoanResult.AppliedStressTestRate,
                            loanTenorMonths = c.LoanResult.LoanTenorMonths,
                            createdAt = c.LoanResult.CreatedAt
                        }))
                    : c.LoanResultJson,
                createdAt = c.CreatedAt,
                isProcessed = c.IsProcessed
            });

            return Ok(new { items = payload, total, page, pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching callback requests: {ex.Message}");
            return StatusCode(500, new { message = "Failed to fetch callback requests" });
        }
    }

    /// <summary>
    /// GET api/loan/health
    /// Health check endpoint
    /// </summary>
    /// <returns>Health status</returns>
    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        return Ok(new 
        { 
            status = "healthy",
            timestamp = DateTime.UtcNow
        });
    }
}
