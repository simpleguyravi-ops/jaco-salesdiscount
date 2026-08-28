using JACO.SalesDiscount.Web.Data;
using JACO.SalesDiscount.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JACO.SalesDiscount.Web.Controllers;

[Authorize(Policy = "SalesDiscountAdmin")]
public sealed class AdminController(SalesDiscountDbContext db) : Controller
{
    public static readonly string[] SupportedTypes =
    [
        "DiscountReason",
        "SalesChannel",
        "OrderType"
    ];

    [HttpGet]
    public async Task<IActionResult> Index(string? type = null)
    {
        type ??= "DiscountReason";

        if (!SupportedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            type = "DiscountReason";

        ViewBag.LookupTypes = SupportedTypes;

        var values = await db.SalesDiscountLookupValues
            .Where(x => x.LookupType == type)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayText)
            .ToListAsync();

        ViewBag.SelectedType = type;
        return View(values);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string lookupType, string value, string displayText, int sortOrder = 10)
    {
        if (!SupportedTypes.Contains(lookupType, StringComparer.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Invalid lookup type.";
            return RedirectToAction(nameof(Index));
        }

        value = value.Trim();
        displayText = displayText.Trim();

        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(displayText))
        {
            TempData["Error"] = "Value and Display Text are required.";
            return RedirectToAction(nameof(Index), new { type = lookupType });
        }

        if (await db.SalesDiscountLookupValues.AnyAsync(x =>
            x.LookupType == lookupType && x.Value == value))
        {
            TempData["Error"] = "This lookup value already exists.";
            return RedirectToAction(nameof(Index), new { type = lookupType });
        }

        db.SalesDiscountLookupValues.Add(new SalesDiscountLookupValue
        {
            LookupType = lookupType,
            Value = value,
            DisplayText = displayText,
            SortOrder = sortOrder,
            Active = true
        });

        await db.SaveChangesAsync();
        TempData["Success"] = "Lookup value added.";
        return RedirectToAction(nameof(Index), new { type = lookupType });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var item = await db.SalesDiscountLookupValues.FindAsync(id);
        if (item is null) return NotFound();

        item.Active = !item.Active;
        await db.SaveChangesAsync();

        TempData["Success"] = item.Active ? "Lookup value activated." : "Lookup value deactivated.";
        return RedirectToAction(nameof(Index), new { type = item.LookupType });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, string displayText, int sortOrder)
    {
        var item = await db.SalesDiscountLookupValues.FindAsync(id);
        if (item is null) return NotFound();

        displayText = displayText.Trim();
        if (string.IsNullOrWhiteSpace(displayText))
        {
            TempData["Error"] = "Display Text is required.";
            return RedirectToAction(nameof(Index), new { type = item.LookupType });
        }

        item.DisplayText = displayText;
        item.SortOrder = sortOrder;

        await db.SaveChangesAsync();
        TempData["Success"] = "Lookup value updated.";
        return RedirectToAction(nameof(Index), new { type = item.LookupType });
    }

    // -- Branches: name/company/account-email, reused as the request form's Branch
    // dropdown and as the completion email's resolved recipient. --

    [HttpGet]
    public async Task<IActionResult> Branches()
    {
        var branches = await db.Branches.OrderBy(x => x.Name).ToListAsync();
        return View(branches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBranch(string code, string name, string companyCode, string companyName, string? accountEmail)
    {
        code = code.Trim();
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Code and Name are required.";
            return RedirectToAction(nameof(Branches));
        }

        if (await db.Branches.AnyAsync(x => x.Code == code))
        {
            TempData["Error"] = "A branch with this code already exists.";
            return RedirectToAction(nameof(Branches));
        }

        db.Branches.Add(new Branch
        {
            Code = code,
            Name = name,
            CompanyCode = companyCode?.Trim() ?? "",
            CompanyName = companyName?.Trim() ?? "",
            AccountEmail = string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail.Trim(),
            Active = true
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Branch added.";
        return RedirectToAction(nameof(Branches));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBranch(int id, string name, string companyCode, string companyName, string? accountEmail)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();

        branch.Name = name.Trim();
        branch.CompanyCode = companyCode?.Trim() ?? "";
        branch.CompanyName = companyName?.Trim() ?? "";
        branch.AccountEmail = string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail.Trim();

        await db.SaveChangesAsync();
        TempData["Success"] = "Branch updated.";
        return RedirectToAction(nameof(Branches));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBranch(int id)
    {
        var branch = await db.Branches.FindAsync(id);
        if (branch is null) return NotFound();

        branch.Active = !branch.Active;
        await db.SaveChangesAsync();
        TempData["Success"] = branch.Active ? "Branch activated." : "Branch deactivated.";
        return RedirectToAction(nameof(Branches));
    }
}
