using JACO.SalesDiscount.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace JACO.SalesDiscount.Web.Data;

public sealed class SalesDiscountDbContext(DbContextOptions<SalesDiscountDbContext> options) : DbContext(options)
{
    public DbSet<SalesDiscountRequest> SalesDiscountRequests => Set<SalesDiscountRequest>();
    public DbSet<SalesDiscountAttachment> SalesDiscountAttachments => Set<SalesDiscountAttachment>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<SalesDiscountLookupValue> SalesDiscountLookupValues => Set<SalesDiscountLookupValue>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SalesDiscountRequest>().ToTable("SalesDiscountRequests");
        b.Entity<SalesDiscountRequest>().Property(x => x.RequestNumber)
            .HasComputedColumnSql("RIGHT(REPLICATE('0',10) + CONVERT(varchar(10), [RequestId]), 10)", stored: true);
        b.Entity<SalesDiscountRequest>().HasIndex(x => x.RequestId).IsUnique();
        b.Entity<SalesDiscountRequest>().Property(x => x.RequestedDiscountPercent).HasColumnType("decimal(9,4)");
        b.Entity<SalesDiscountRequest>().Property(x => x.NetMargin).HasColumnType("decimal(9,4)");
        b.Entity<SalesDiscountRequest>().Property(x => x.SellingPrice).HasColumnType("decimal(18,2)");
        b.Entity<SalesDiscountRequest>().Property(x => x.CostPrice).HasColumnType("decimal(18,2)");
        b.Entity<SalesDiscountRequest>().Property(x => x.RequestedDiscountAmount).HasColumnType("decimal(18,2)");
        b.Entity<SalesDiscountRequest>().Property(x => x.CustomerFinalOffer).HasColumnType("decimal(18,2)");

        b.Entity<SalesDiscountAttachment>().ToTable("SalesDiscountAttachments");
        b.Entity<SalesDiscountAttachment>().HasIndex(x => x.SalesDiscountRequestId);

        b.Entity<Branch>().ToTable("Branches");
        b.Entity<Branch>().HasIndex(x => x.Code).IsUnique();

        b.Entity<SalesDiscountLookupValue>().ToTable("SalesDiscountLookupValues");
        b.Entity<SalesDiscountLookupValue>().HasIndex(x => new { x.LookupType, x.Value }).IsUnique();
    }
}
