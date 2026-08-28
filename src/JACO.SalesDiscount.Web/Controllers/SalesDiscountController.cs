using System.Security.Claims;
using System.Text.Json;
using JACO.SalesDiscount.Web.Data;
using JACO.SalesDiscount.Web.Models;
using JACO.SalesDiscount.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JACO.SalesDiscount.Web.Controllers;

[Authorize]
public sealed class SalesDiscountController(
    SalesDiscountDbContext db,
    ApprovalApiClient approvalApi,
    SalesDiscountLookupService lookups,
    SalesDiscountAttachmentStorage attachmentStorage,
    IConfiguration configuration) : Controller
{
    string ApprovalTypeCode => configuration["Approval:TypeCode"] ?? "SALES_DISCOUNT";

    int CurrentUserId => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    static bool IsEditable(string status) => status is "Draft" or "Sent Back";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status, string? branch, string? sort, string dir = "asc")
    {
        var mine = MineQuery();

        var model = new SalesDiscountListViewModel
        {
            TotalCount = await mine.CountAsync(),
            DraftCount = await mine.CountAsync(x => x.Status == "Draft"),
            PendingApprovalCount = await mine.CountAsync(x => x.Status == "Pending Approval"),
            CompletedCount = await mine.CountAsync(x => x.Status == "Completed"),
            Search = search,
            Status = status,
            Branch = branch,
            Sort = sort,
            Dir = dir,
            Branches = await lookups.GetBranchesAsync()
        };

        model.Rows = await FilteredQuery(mine, search, status, branch, sort, dir).ToListAsync();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? search, string? status, string? branch, string? sort, string dir = "asc")
    {
        var rows = await FilteredQuery(MineQuery(), search, status, branch, sort, dir).ToListAsync();
        var bytes = CsvHelper.ToCsvBytes(rows,
            ["Request No.", "Branch", "Customer", "Vehicle Model", "Discount %", "Status", "Submitted On"],
            x => [x.RequestNumber, x.Branch, x.CustomerName, x.VehicleModel, x.RequestedDiscountPercent.ToString("0.##"), x.Status, x.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")]);
        return File(bytes, "text/csv", $"sales-discount-requests-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    IQueryable<SalesDiscountRequest> MineQuery()
    {
        var userName = User.Identity!.Name!;
        return db.SalesDiscountRequests.AsNoTracking().Where(x => x.CreatorUserName == userName);
    }

    static IQueryable<SalesDiscountRequest> FilteredQuery(IQueryable<SalesDiscountRequest> mine, string? search, string? status, string? branch, string? sort, string dir)
    {
        var query = mine;
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(branch)) query = query.Where(x => x.Branch == branch);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.RequestNumber.Contains(search) || x.CustomerName.Contains(search) || x.VehicleModel.Contains(search));

        var desc = dir == "desc";
        return sort switch
        {
            "RequestNumber" => desc ? query.OrderByDescending(x => x.RequestNumber) : query.OrderBy(x => x.RequestNumber),
            "Branch" => desc ? query.OrderByDescending(x => x.Branch) : query.OrderBy(x => x.Branch),
            "CustomerName" => desc ? query.OrderByDescending(x => x.CustomerName) : query.OrderBy(x => x.CustomerName),
            "RequestedDiscountPercent" => desc ? query.OrderByDescending(x => x.RequestedDiscountPercent) : query.OrderBy(x => x.RequestedDiscountPercent),
            "Status" => desc ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            "CreatedAt" => desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
    }

    async Task PopulateLookupsAsync()
    {
        ViewBag.Branches = await lookups.GetBranchesAsync();
        ViewBag.DiscountReasons = await lookups.GetAsync("DiscountReason");
        ViewBag.SalesChannels = await lookups.GetAsync("SalesChannel");
        ViewBag.OrderTypes = await lookups.GetAsync("OrderType");
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateLookupsAsync();
        return View(new SalesDiscountRequest
        {
            Branch = "",
            SalesChannel = "",
            OrderType = "",
            CreatorUserId = CurrentUserId,
            CreatorUserName = User.Identity!.Name!
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SalesDiscountRequest model, List<IFormFile>? attachments)
    {
        model.CreatorUserId = CurrentUserId;
        model.CreatorUserName = User.Identity!.Name!;

        await ValidateAndDeriveAsync(model);

        if (!ModelState.IsValid)
        {
            if (ModelState.Values.Any(v => v.Errors.Count > 0))
                TempData["Error"] = "Please correct the highlighted mandatory fields.";
            await PopulateLookupsAsync();
            return View(model);
        }

        var now = DateTime.Now;
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR dbo.SalesDiscountIdSequence;";
            var value = await command.ExecuteScalarAsync();
            if (value is null || value == DBNull.Value)
                throw new InvalidOperationException("SalesDiscountIdSequence did not return a value.");
            model.RequestId = Convert.ToInt64(value);
        }
        finally
        {
            await connection.CloseAsync();
        }

        model.Status = "Draft";
        model.CreatedAt = now;
        model.UpdatedAt = now;

        db.SalesDiscountRequests.Add(model);
        await db.SaveChangesAsync();

        if (attachments is not null)
            await SaveAttachmentsAsync(model, attachments);

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id)
    {
        var model = await db.SalesDiscountRequests.FindAsync(id);
        if (model is null) return NotFound();

        // A decision made elsewhere (e.g. Send Back) only lands in this row's Status
        // once something re-syncs from Approval -- Details already does this on every
        // view, but Edit is often reached directly (a bookmark, a reload, a link from
        // outside this session) without visiting Details first, so it needs the same
        // refresh or it judges editability against a stale status.
        if (!string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
            await RefreshStatusFromApprovalAsync(model);

        if (!IsEditable(model.Status) || model.CreatorUserName != User.Identity!.Name)
        {
            TempData["Error"] = "Only your own draft or sent-back requests can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await PopulateLookupsAsync();
        ViewBag.Attachments = await db.SalesDiscountAttachments
            .Where(x => x.SalesDiscountRequestId == model.Id)
            .OrderByDescending(x => x.UploadedAt)
            .ToListAsync();

        if (model.Status == "Sent Back" && !string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            var timeline = await approvalApi.GetTimelineAsync(model.ApprovalWorkflowNo);
            ViewBag.SentBackComment = timeline?
                .SelectMany(l => l.Decisions)
                .Where(d => d.ActionCode == "SendBack" && !string.IsNullOrWhiteSpace(d.Comments))
                .OrderByDescending(d => d.AtUtc)
                .FirstOrDefault()?.Comments;
        }

        return View("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(long id, SalesDiscountRequest model, List<IFormFile>? attachments, string? clarification)
    {
        var existing = await db.SalesDiscountRequests.FindAsync(id);
        if (existing is null) return NotFound();

        if (!IsEditable(existing.Status) || existing.CreatorUserName != User.Identity!.Name)
        {
            TempData["Error"] = "Only your own draft or sent-back requests can be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await ValidateAndDeriveAsync(model);

        if (!ModelState.IsValid)
        {
            if (ModelState.Values.Any(v => v.Errors.Count > 0))
                TempData["Error"] = "Please correct the highlighted mandatory fields.";

            model.Id = id;
            model.RequestId = existing.RequestId;
            model.Status = existing.Status;
            ViewBag.RequestNumber = existing.RequestNumber;
            await PopulateLookupsAsync();
            ViewBag.Attachments = await db.SalesDiscountAttachments
                .Where(x => x.SalesDiscountRequestId == existing.Id)
                .OrderByDescending(x => x.UploadedAt)
                .ToListAsync();
            return View("Create", model);
        }

        var now = DateTime.Now;
        CopyBusinessFields(model, existing);
        existing.UpdatedAt = now;
        existing.CreatorUserId = existing.CreatorUserId == 0 ? CurrentUserId : existing.CreatorUserId;

        if (attachments is not null)
            await SaveAttachmentsAsync(existing, attachments);

        if (existing.Status == "Sent Back" && !string.IsNullOrWhiteSpace(existing.ApprovalWorkflowNo))
        {
            var data = BuildApprovalData(existing);
            var branchAccountEmail = (await lookups.GetBranchesAsync())
                .FirstOrDefault(b => b.Name == existing.Branch || b.Code == existing.Branch)?.AccountEmail;
            if (!string.IsNullOrWhiteSpace(branchAccountEmail))
                data["accountEmail"] = JsonDocument.Parse(JsonSerializer.Serialize(branchAccountEmail)).RootElement.Clone();

            var (ok, message) = await approvalApi.ResubmitAsync(existing.ApprovalWorkflowNo,
                new ApprovalResubmitRequest(existing.CreatorUserName, new Dictionary<string, JsonElement>(data), data, clarification));

            if (!ok)
            {
                await db.SaveChangesAsync();
                TempData["Error"] = $"Changes were saved, but resubmitting to the approver failed: {message}";
                return RedirectToAction(nameof(Details), new { id = existing.Id });
            }

            existing.Status = "Pending Approval";
            existing.ApprovalStatus = "Pending";

            var pendingAttachments = await db.SalesDiscountAttachments
                .Where(x => x.SalesDiscountRequestId == existing.Id && (x.TransferStatus != "Transferred" || !x.ApprovalAttachmentId.HasValue))
                .ToListAsync();
            foreach (var attachment in pendingAttachments)
                await TransferAttachmentAsync(existing, attachment);

            TempData["Success"] = "Request resubmitted for approval.";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = existing.Id });
    }

    static void CopyBusinessFields(SalesDiscountRequest from, SalesDiscountRequest to)
    {
        to.Branch = from.Branch;
        to.Company = from.Company;
        to.CustomerName = from.CustomerName;
        to.DiscountReason = from.DiscountReason;
        to.DiscountNotes = from.DiscountNotes;
        to.VehicleModel = from.VehicleModel;
        to.ModelYear = from.ModelYear;
        to.CommissionNumber = from.CommissionNumber;
        to.Vin = from.Vin;
        to.SalesChannel = from.SalesChannel;
        to.OrderType = from.OrderType;
        to.SpecialOrder = from.SpecialOrder;
        to.DaysInStock = from.DaysInStock;
        to.DaysReserved = from.DaysReserved;
        to.SellingPrice = from.SellingPrice;
        to.CostPrice = from.CostPrice;
        to.RequestedDiscountPercent = from.RequestedDiscountPercent;
        to.RequestedDiscountAmount = from.RequestedDiscountAmount;
        to.CustomerFinalOffer = from.CustomerFinalOffer;
        to.NetMargin = from.NetMargin;
    }

    // Validates lookup-backed fields (Branch/SalesChannel/OrderType/DiscountReason) and
    // derives Company from the selected Branch -- Company is never typed by the user, so
    // it can never mismatch what's actually on file for that branch.
    async Task ValidateAndDeriveAsync(SalesDiscountRequest model)
    {
        var branch = (await lookups.GetBranchesAsync()).FirstOrDefault(b => b.Name == model.Branch || b.Code == model.Branch);
        if (branch is null)
            ModelState.AddModelError(nameof(model.Branch), "Select a valid Branch.");
        else
            model.Company = branch.CompanyName;

        if (!await lookups.IsAllowedAsync("SalesChannel", model.SalesChannel))
            ModelState.AddModelError(nameof(model.SalesChannel), "Select a valid Sales Channel.");

        if (!await lookups.IsAllowedAsync("OrderType", model.OrderType))
            ModelState.AddModelError(nameof(model.OrderType), "Select a valid Order Type.");

        if (!string.IsNullOrWhiteSpace(model.DiscountReason) &&
            !await lookups.IsAllowedAsync("DiscountReason", model.DiscountReason))
            ModelState.AddModelError(nameof(model.DiscountReason), "Select a valid Discount Reason.");

        if (model.RequestedDiscountPercent <= 0)
            ModelState.AddModelError(nameof(model.RequestedDiscountPercent), "Requested Discount % must be greater than zero.");
    }

    async Task SaveAttachmentsAsync(SalesDiscountRequest request, List<IFormFile> files)
    {
        foreach (var file in files.Where(x => x is not null && x.Length > 0))
        {
            var saved = await attachmentStorage.SaveAsync(request.RequestId, file);
            db.SalesDiscountAttachments.Add(new SalesDiscountAttachment
            {
                SalesDiscountRequestId = request.Id,
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = saved.storedFileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = file.Length,
                UploadedByUserName = request.CreatorUserName,
                UploadedAt = DateTime.Now,
                TransferStatus = "Pending"
            });
        }
        await db.SaveChangesAsync();
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id)
    {
        var model = await db.SalesDiscountRequests.FindAsync(id);
        if (model is null) return NotFound();

        ViewBag.Attachments = await db.SalesDiscountAttachments
            .Where(x => x.SalesDiscountRequestId == model.Id)
            .OrderByDescending(x => x.UploadedAt)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            ViewBag.Timeline = await approvalApi.GetTimelineAsync(model.ApprovalWorkflowNo);
            await RefreshStatusFromApprovalAsync(model);
        }
        return View(model);
    }

    // Approval is the source of truth for Status/ApprovalStatus/CurrentLevel once a
    // request is submitted -- this pulls the live value and persists it locally so any
    // page that reads the row directly (Edit's editability check, the list, exports)
    // sees an up-to-date status without every one of them needing its own API call.
    async Task RefreshStatusFromApprovalAsync(SalesDiscountRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo)) return;

        var approval = await approvalApi.GetAsync(model.ApprovalWorkflowNo);
        if (approval is null) return;

        model.ApprovalStatus = approval.Status;
        model.ApprovalCurrentLevel = approval.CurrentLevelNo;
        model.Status = approval.Status == "Approved" ? "Completed" :
                        approval.Status == "Rejected" ? "Rejected" :
                        approval.Status == "Sent Back" ? "Sent Back" :
                        approval.Status == "Withdrawn" ? "Withdrawn" :
                        model.Status;
        model.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(long id, IFormFile file)
    {
        var model = await db.SalesDiscountRequests.FindAsync(id);
        if (model is null) return NotFound();

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Select a document to upload.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var saved = await attachmentStorage.SaveAsync(model.RequestId, file);
        db.SalesDiscountAttachments.Add(new SalesDiscountAttachment
        {
            SalesDiscountRequestId = model.Id,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = saved.storedFileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileSize = file.Length,
            UploadedByUserName = model.CreatorUserName,
            UploadedAt = DateTime.Now,
            TransferStatus = "Pending"
        });
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            var savedAttachment = await db.SalesDiscountAttachments.OrderByDescending(x => x.Id).FirstAsync();
            var transferred = await TransferAttachmentAsync(model, savedAttachment);
            TempData[transferred ? "Success" : "Error"] = transferred
                ? $"{savedAttachment.OriginalFileName} transferred to the Approval work item."
                : $"Upload succeeded, but transfer to the Approval work item failed for {savedAttachment.OriginalFileName}.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryAttachmentTransfer(long id)
    {
        var attachment = await db.SalesDiscountAttachments.FindAsync(id);
        if (attachment is null) return NotFound();

        var model = await db.SalesDiscountRequests.FindAsync(attachment.SalesDiscountRequestId);
        if (model is null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            TempData["Error"] = "This request has not been submitted for approval yet.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var transferred = await TransferAttachmentAsync(model, attachment);
        TempData[transferred ? "Success" : "Error"] = transferred
            ? $"{attachment.OriginalFileName} transferred to the Approval work item."
            : $"Transfer failed for {attachment.OriginalFileName}. Please retry.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(long id)
    {
        var attachment = await db.SalesDiscountAttachments.FindAsync(id);
        if (attachment is null) return NotFound();

        var request = await db.SalesDiscountRequests.FindAsync(attachment.SalesDiscountRequestId);
        if (request is null) return NotFound();

        var path = attachmentStorage.GetPath(request.RequestId, attachment.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    async Task<bool> TransferAttachmentAsync(SalesDiscountRequest model, SalesDiscountAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            attachment.TransferStatus = "Failed";
            await db.SaveChangesAsync();
            return false;
        }

        var path = attachmentStorage.GetPath(model.RequestId, attachment.StoredFileName);
        if (!System.IO.File.Exists(path))
        {
            attachment.TransferStatus = "Failed";
            await db.SaveChangesAsync();
            return false;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(model.CreatorUserName), "uploadedByUserName");

            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType);
            form.Add(content, "file", attachment.OriginalFileName);

            var response = await approvalApi.UploadAttachmentAsync(model.ApprovalWorkflowNo, form);
            if (response is null)
            {
                attachment.TransferStatus = "Failed";
                attachment.ApprovalAttachmentId = null;
                attachment.TransferredAt = null;
                await db.SaveChangesAsync();
                return false;
            }

            attachment.TransferStatus = "Transferred";
            attachment.ApprovalAttachmentId = response.Id;
            attachment.TransferredAt = DateTime.Now;
            await db.SaveChangesAsync();
            return true;
        }
        catch
        {
            attachment.TransferStatus = "Failed";
            attachment.ApprovalAttachmentId = null;
            attachment.TransferredAt = null;
            await db.SaveChangesAsync();
            return false;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(long id)
    {
        var model = await db.SalesDiscountRequests.FindAsync(id);
        if (model is null) return NotFound();

        if (model.Status is "Completed" or "Rejected")
        {
            TempData["Error"] = "Closed requests cannot be submitted.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var data = BuildApprovalData(model);

        // Resolved here (not stored on the request itself) so a branch's account email
        // can change later without needing to touch already-submitted requests.
        var branchAccountEmail = (await lookups.GetBranchesAsync())
            .FirstOrDefault(b => b.Name == model.Branch || b.Code == model.Branch)?.AccountEmail;
        if (!string.IsNullOrWhiteSpace(branchAccountEmail))
            data["accountEmail"] = JsonDocument.Parse(JsonSerializer.Serialize(branchAccountEmail)).RootElement.Clone();

        var routing = new Dictionary<string, JsonElement>(data);

        var (response, errorMessage) = await approvalApi.CreateAsync(new ApprovalCreateRequest(
            ApprovalTypeCode,
            "JACO-SalesDiscount",
            model.RequestNumber,
            model.CreatorUserName,
            $"{model.VehicleModel} - {model.CustomerName}",
            routing,
            data));

        if (response is null)
        {
            TempData["Error"] = errorMessage ?? "Approval service could not create the workflow.";
            return RedirectToAction(nameof(Details), new { id });
        }

        model.ApprovalWorkflowNo = response.WorkflowNo;

        var pendingAttachments = await db.SalesDiscountAttachments
            .Where(x => x.SalesDiscountRequestId == model.Id && (x.TransferStatus != "Transferred" || !x.ApprovalAttachmentId.HasValue))
            .ToListAsync();
        foreach (var attachment in pendingAttachments)
            await TransferAttachmentAsync(model, attachment);

        model.ApprovalStatus = response.Status;
        model.ApprovalCurrentLevel = response.CurrentLevelNo;
        model.Status = "Pending Approval";
        model.UpdatedAt = DateTime.Now;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(long id, string? reason)
    {
        var model = await db.SalesDiscountRequests.FindAsync(id);
        if (model is null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.ApprovalWorkflowNo))
        {
            TempData["Error"] = "This request has not been submitted for approval yet.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var (ok, message) = await approvalApi.WithdrawAsync(model.ApprovalWorkflowNo,
            new ApprovalWithdrawRequest(model.CreatorUserName, string.IsNullOrWhiteSpace(reason) ? "Withdrawn by requester" : reason));

        if (ok)
        {
            model.Status = "Withdrawn";
            model.ApprovalStatus = "Withdrawn";
            model.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }

        TempData[ok ? "Success" : "Error"] = ok ? "Request withdrawn." : $"Could not withdraw: {message}";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Every field the Rule Builder can offer as a criteria FIELD (see Approval's
    // WorkflowFields catalog for ApprovalTypeId=SALES_DISCOUNT) must actually be present
    // here, or a rule written against it can never match anything. accountEmail rides
    // along as a plain decisionData field too -- not shown on the form, resolved from
    // the selected Branch -- so PPF's generic "Field" recipient mode can read it without
    // Approval ever needing to know about this app's own Branches table.
    static Dictionary<string, JsonElement> BuildApprovalData(SalesDiscountRequest model)
    {
        JsonElement J<T>(T value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();

        return new Dictionary<string, JsonElement>
        {
            ["branch"] = J(model.Branch),
            ["company"] = J(model.Company),
            ["customerName"] = J(model.CustomerName),
            ["discountReason"] = J(model.DiscountReason),
            ["discountNotes"] = J(model.DiscountNotes),
            ["vehicleModel"] = J(model.VehicleModel),
            ["modelYear"] = J(model.ModelYear?.ToString()),
            ["commissionNumber"] = J(model.CommissionNumber),
            ["vin"] = J(model.Vin),
            ["salesChannel"] = J(model.SalesChannel),
            ["orderType"] = J(model.OrderType),
            ["specialOrder"] = J(model.SpecialOrder),
            ["daysInStock"] = J(model.DaysInStock?.ToString()),
            ["daysReserved"] = J(model.DaysReserved?.ToString()),
            ["sellingPrice"] = J(model.SellingPrice?.ToString("0.##")),
            ["costPrice"] = J(model.CostPrice?.ToString("0.##")),
            ["requestedDiscountPercent"] = J(model.RequestedDiscountPercent.ToString("0.####")),
            ["requestedDiscountAmount"] = J(model.RequestedDiscountAmount?.ToString("0.##")),
            ["customerFinalOffer"] = J(model.CustomerFinalOffer?.ToString("0.##")),
            ["netMargin"] = J(model.NetMargin?.ToString("0.####"))
        };
    }
}
