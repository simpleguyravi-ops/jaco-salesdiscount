using System.ComponentModel.DataAnnotations;

namespace JACO.SalesDiscount.Web.Models;

public sealed class SalesDiscountRequest
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public string RequestNumber { get; private set; } = "";

    [Required, StringLength(100)] public string Branch { get; set; } = "";
    [StringLength(150)] public string Company { get; set; } = "";
    [Required, StringLength(200)] public string CustomerName { get; set; } = "";
    [StringLength(150)] public string? DiscountReason { get; set; }
    public string? DiscountNotes { get; set; }
    [Required, StringLength(100)] public string VehicleModel { get; set; } = "";
    public int? ModelYear { get; set; }
    [StringLength(50)] public string? CommissionNumber { get; set; }
    [StringLength(50)] public string? Vin { get; set; }
    [Required, StringLength(50)] public string SalesChannel { get; set; } = "";
    [Required, StringLength(50)] public string OrderType { get; set; } = "";
    [StringLength(10)] public string? SpecialOrder { get; set; }
    public int? DaysInStock { get; set; }
    public int? DaysReserved { get; set; }
    public decimal? SellingPrice { get; set; }
    public decimal? CostPrice { get; set; }
    [Required] public decimal RequestedDiscountPercent { get; set; }
    public decimal? RequestedDiscountAmount { get; set; }
    public decimal? CustomerFinalOffer { get; set; }
    public decimal? NetMargin { get; set; }

    public int CreatorUserId { get; set; }
    public string CreatorUserName { get; set; } = "";

    public string Status { get; set; } = "Draft";
    public string? ApprovalWorkflowNo { get; set; }
    public string? ApprovalStatus { get; set; }
    public int? ApprovalCurrentLevel { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SalesDiscountAttachment
{
    public long Id { get; set; }
    public long SalesDiscountRequestId { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    public string UploadedByUserName { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public string TransferStatus { get; set; } = "Pending";
    public long? ApprovalAttachmentId { get; set; }
    public DateTime? TransferredAt { get; set; }
}
