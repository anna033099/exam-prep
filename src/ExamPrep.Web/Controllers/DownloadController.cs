using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamPrep.Web.Controllers;

/// <summary>多年度原卷批次下載（ZIP），只包含考選部公開的 PDF。</summary>
public class DownloadController(PaperZipService zips) : Controller
{
    [EnableRateLimiting("zip")]
    public async Task<IActionResult> Subject(string subject, string? exam, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject)) return BadRequest();
        var items = await zips.ListAsync(subject, exam);
        if (items.Count == 0)
        {
            TempData["Flash"] = "這個科目沒有可下載的考選部原卷。";
            return RedirectToAction("Index", "Practice", new { q = subject });
        }
        var name = $"{subject}_{items.Min(i => i.Year)}-{items.Max(i => i.Year)}_原卷.zip";
        // 先在暫存檔組好 ZIP 再送出：避免同步寫入回應，也避免下載中途失敗送出不完整的檔案
        var tmp = Path.Combine(Path.GetTempPath(), $"examprep-{Guid.NewGuid():N}.zip");
        try
        {
            await using var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await zips.WriteZipAsync(items, fs, ct);
        }
        catch
        {
            System.IO.File.Delete(tmp);
            throw;
        }
        var stream = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return File(stream, "application/zip", name);
    }
}
