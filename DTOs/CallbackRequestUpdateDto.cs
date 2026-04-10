using System;

namespace MortgageLoanAPI.DTOs;

public class CallbackRequestUpdateDto
{
    public bool? IsProcessed { get; set; }
    public string? Notes { get; set; }
}
