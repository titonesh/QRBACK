using System.Collections.Generic;

namespace MortgageLoanAPI.DTOs;

public class BulkUpdateDto
{
    public List<int> Ids { get; set; } = new List<int>();
    public string? Action { get; set; }
}
