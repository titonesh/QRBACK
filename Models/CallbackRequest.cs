// Models/CallbackRequest.cs
namespace MortgageLoanAPI.Models;

/// <summary>
/// Represents a callback request from a customer
/// </summary>
public class CallbackRequest
{
    public int Id { get; set; }
    public int? LoanResultId { get; set; }
    public LoanResult? LoanResult { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Message { get; set; }
    // JSON-serialized representations to preserve request context for RMs
    public string? LoanInputsJson { get; set; }
    public string? LoanResultJson { get; set; }
    // Optional referral number for tracking which RM engaged the customer
    public string? ReferralNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
