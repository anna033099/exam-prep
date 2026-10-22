using System.IO.Compression;
using ExamPrep.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

/// <summary>
/// 把同一科目多年度的考選部原卷（試題＋標準答案 PDF）打包成 ZIP。
/// 只處理考選部網址；檔案下載後快取在本機，避免重複向考選部要求。
/// </summary>
public class PaperZipService(AppDbContext db, HttpClient http, ILogger<PaperZipService> log)
{
    public const int MaxPapers = 40;
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "examprep-pdf");
    private static readonly SemaphoreSlim Gate = new(2);

    public record Item(int Year, string Subject, string PaperCode, string QuestionUrl, string AnswerUrl);

    public async Task<List<Item>> ListAsync(string subject, string? examSlug)
    {
        var q = db.Papers.Where(p => p.SubjectName == subject && !p.ContentWithheld && p.SourceQuestionUrl.StartsWith("https://wwwq.moex.gov.tw/"));
        if (!string.IsNullOrEmpty(examSlug)) q = q.Where(p => p.ExamLinks.Any(l => l.Exam!.Slug == examSlug));
        var rows = await q.OrderByDescending(p => p.RocYear).ThenBy(p => p.PaperCode)
            .Select(p => new Item(p.RocYear, p.SubjectName, p.PaperCode, p.SourceQuestionUrl, p.SourceAnswerUrl))
            .Take(MaxPapers).ToListAsync();
        return rows;
    }

    public async Task WriteZipAsync(IEnumerable<Item> items, Stream output, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var readme = new System.Text.StringBuilder("本壓縮檔內的 PDF 皆下載自考選部考畢試題查詢平臺：\r\n");
        foreach (var it in items)
        {
            var safe = string.Concat(it.Subject.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (safe.Length > 40) safe = safe[..40];
            foreach (var (url, kind) in new[] { (it.QuestionUrl, "試題"), (it.AnswerUrl, "答案") })
            {
                var bytes = await FetchAsync(url, ct);
                if (bytes is null) continue;
                var entry = zip.CreateEntry($"{it.Year}_{safe}_{it.PaperCode}_{kind}.pdf", CompressionLevel.Fastest);
                await using var es = entry.Open();
                await es.WriteAsync(bytes, ct);
                readme.Append($"{it.Year} {it.Subject}（{it.PaperCode}）{kind}：{url}\r\n");
            }
        }
        var r = zip.CreateEntry("來源說明.txt");
        await using (var w = new StreamWriter(r.Open(), new System.Text.UTF8Encoding(true)))
            await w.WriteAsync(readme.ToString());
    }

    private async Task<byte[]?> FetchAsync(string url, CancellationToken ct)
    {
        if (!url.StartsWith("https://wwwq.moex.gov.tw/")) return null;
        var file = Path.Combine(CacheDir, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".pdf");
        if (File.Exists(file)) return await File.ReadAllBytesAsync(file, ct);
        await Gate.WaitAsync(ct);
        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P') return null;
            await File.WriteAllBytesAsync(file, bytes, ct);
            return bytes;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "下載原卷失敗：{Url}", url);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
