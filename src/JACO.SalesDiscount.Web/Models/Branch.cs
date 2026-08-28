namespace JACO.SalesDiscount.Web.Models;

public sealed class Branch
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? AccountEmail { get; set; }
    public bool Active { get; set; } = true;
}
