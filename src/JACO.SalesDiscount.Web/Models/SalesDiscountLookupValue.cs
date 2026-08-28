namespace JACO.SalesDiscount.Web.Models;

public sealed class SalesDiscountLookupValue
{
    public int Id { get; set; }
    public string LookupType { get; set; } = "";
    public string Value { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Active { get; set; } = true;
}
