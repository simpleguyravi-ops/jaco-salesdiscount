using JACO.SalesDiscount.Web.Data;
using JACO.SalesDiscount.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace JACO.SalesDiscount.Web.Services;

public sealed class SalesDiscountLookupService(SalesDiscountDbContext db)
{
    public async Task<IReadOnlyList<SalesDiscountLookupValue>> GetAsync(string type) =>
        await db.SalesDiscountLookupValues
            .Where(x => x.LookupType == type && x.Active)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayText)
            .ToListAsync();

    public Task<bool> IsAllowedAsync(string type, string value) =>
        db.SalesDiscountLookupValues.AnyAsync(x =>
            x.LookupType == type &&
            x.Value == value &&
            x.Active);

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() =>
        await db.Branches.Where(x => x.Active).OrderBy(x => x.Name).ToListAsync();
}
