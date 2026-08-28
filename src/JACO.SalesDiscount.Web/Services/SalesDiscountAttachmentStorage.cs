namespace JACO.SalesDiscount.Web.Services;

public sealed class SalesDiscountAttachmentStorage(IWebHostEnvironment env)
{
    private string Root =>
        Path.Combine(env.ContentRootPath, "App_Data", "SalesDiscountAttachments");

    public async Task<(string storedFileName, string physicalPath)> SaveAsync(
        long requestId,
        IFormFile file,
        CancellationToken ct = default)
    {
        var folder = Path.Combine(Root, requestId.ToString());
        Directory.CreateDirectory(folder);

        var extension = Path.GetExtension(file.FileName);
        var stored = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(folder, stored);

        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        await file.CopyToAsync(stream, ct);
        return (stored, path);
    }

    public string GetPath(long requestId, string storedFileName) =>
        Path.Combine(Root, requestId.ToString(), storedFileName);
}
