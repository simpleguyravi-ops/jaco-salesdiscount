namespace JACO.SalesDiscount.Web.Models;

public class SalesDiscountListViewModel
{
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public int CompletedCount { get; set; }

    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Branch { get; set; }
    public string? Sort { get; set; }
    public string Dir { get; set; } = "asc";

    public IReadOnlyList<Branch> Branches { get; set; } = new List<Branch>();
    public List<SalesDiscountRequest> Rows { get; set; } = new();

    public static readonly (string Value, string Label)[] StatusTabs =
    [
        ("", "All"),
        ("Draft", "Draft"),
        ("Pending Approval", "Pending"),
        ("Completed", "Approved"),
        ("Rejected", "Rejected"),
        ("Sent Back", "Sent Back"),
        ("Withdrawn", "Withdrawn"),
    ];
}
