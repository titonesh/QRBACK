namespace MortgageLoanAPI.DTOs;

/// <summary>
/// DTO for callback request from client
/// </summary>
public class CallbackRequestDto
{
    public int LoanResultId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Message { get; set; }
    // Optional JSON blobs containing the original loan inputs and the persisted loan result
    public string? LoanInputsJson { get; set; }
    public string? LoanResultJson { get; set; }
    // Optional referral number to identify the RM who engaged the customer
    public string? ReferralNumber { get; set; }
}
