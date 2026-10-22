using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamPrep.Web.Services;

public class AiOptions
{
    /// <summary>Anthropic API 金鑰；未設定時整個 AI 詳解功能隱藏。</summary>
    public string? AnthropicApiKey { get; set; }

    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>每位使用者每天最多可產生的新詳解數（已快取的詳解不計）。</summary>
    public int DailyLimitPerUser { get; set; } = 10;

    public int MaxTokens { get; set; } = 900;
}

public enum AiResultStatus { Ok, Cached, Disabled, LimitReached, Failed }

public record AiResult(AiResultStatus Status, QuestionExplanation? Explanation, string? Error = null);

/// <summary>
/// 依需求呼叫 Claude 產生詳解，結果快取在資料庫並一律標示為 AI 生成。
/// 提示中明確給出官方標準答案，要求 AI 以官方答案為準、不確定時直接說明，避免與官方答案矛盾。
/// </summary>
public class AiExplanationService(AppDbContext db, HttpClient http, IOptions<AiOptions> options, TimeProvider clock, ILogger<AiExplanationService> log)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(options.Value.AnthropicApiKey);

    public Task<QuestionExplanation?> GetCachedAsync(int questionId) =>
        db.Explanations.AsNoTracking().FirstOrDefaultAsync(e => e.QuestionId == questionId);

    public async Task<AiResult> GetOrCreateAsync(int questionId, string userId, CancellationToken ct = default)
    {
        var cached = await db.Explanations.FirstOrDefaultAsync(e => e.QuestionId == questionId, ct);
        if (cached is not null) return new(AiResultStatus.Cached, cached);
        if (!Enabled) return new(AiResultStatus.Disabled, null);

        var since = clock.GetUtcNow().UtcDateTime.AddDays(-1);
        var used = await db.Explanations.CountAsync(e => e.RequestedByUserId == userId && e.CreatedAt >= since, ct);
        if (used >= options.Value.DailyLimitPerUser) return new(AiResultStatus.LimitReached, null);

        var q = await db.Questions.Include(x => x.Paper).FirstOrDefaultAsync(x => x.Id == questionId && !x.Paper!.ContentWithheld, ct);
        if (q is null) return new(AiResultStatus.Failed, null, "找不到題目");

        try
        {
            var text = await CallAsync(SystemPromptFor(q), BuildPrompt(q), ct);
            if (string.IsNullOrWhiteSpace(text)) return new(AiResultStatus.Failed, null, "AI 沒有回傳內容");
            var e = new QuestionExplanation
            {
                QuestionId = q.Id,
                Model = options.Value.Model,
                Body = text.Trim(),
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                RequestedByUserId = userId
            };
            db.Explanations.Add(e);
            await db.SaveChangesAsync(ct);
            return new(AiResultStatus.Ok, e);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or DbUpdateException)
        {
            log.LogWarning(ex, "AI 詳解產生失敗（題目 {Id}）", questionId);
            return new(AiResultStatus.Failed, null, "AI 服務暫時無法使用，請稍後再試");
        }
    }

    public const string SystemPrompt =
        "你是台灣公職考試的解題老師，用繁體中文、白話地解釋選擇題。" +
        "官方標準答案是確定的，你的解析必須支持官方答案，不可推翻或質疑它。" +
        "逐一說明每個選項為什麼對或錯。引用法條、條號、年份、數據時，只有在你確定時才寫；不確定就說「請查證原始法規」。" +
        "不知道就直說不知道，不要編造。不要使用 Markdown 標題，段落簡短。";

    public const string EssaySystemPrompt =
        "你是台灣公職考試的申論題老師，用繁體中文寫一份「參考答題架構」，不是標準答案。" +
        "先點出題目在考什麼，再列出答題架構（每一段要寫什麼），最後提醒常見失分點。" +
        "引用法條、條號、釋字、學者、年份、數據時，只有在你確定時才寫；不確定就寫「請查證原始法規」。" +
        "不知道就直說不知道，不要編造。不要使用 Markdown 標題，段落簡短。";

    public static string SystemPromptFor(Question q) => q.IsEssay ? EssaySystemPrompt : SystemPrompt;

    public static string BuildPrompt(Question q)
    {
        var sb = new System.Text.StringBuilder();
        if (q.IsEssay)
        {
            sb.AppendLine($"考試：{q.Paper!.RocYear} 年 {q.Paper.ExamTitle}，科目：{q.Paper.SubjectName}，{q.Label}（申論題，沒有官方答案）");
            sb.AppendLine($"題目：{Plain(q.Stem)}");
            sb.AppendLine("請提供參考答題架構。");
            return sb.ToString();
        }
        sb.AppendLine($"考試：{q.Paper!.RocYear} 年 {q.Paper.ExamTitle}，科目：{q.Paper.SubjectName}，第 {q.Number} 題（{(q.Type == QuestionType.Multiple ? "複選" : "單選")}）");
        if (!string.IsNullOrWhiteSpace(q.Passage)) sb.AppendLine($"題組文章：{Plain(q.Passage)}");
        sb.AppendLine($"題目：{Plain(q.Stem)}");
        foreach (var (l, t) in q.Options()) sb.AppendLine($"({l}) {Plain(t)}");
        sb.AppendLine($"官方標準答案：{q.Answer}");
        sb.AppendLine("請先用一句話點出關鍵觀念，再逐一說明各選項。");
        return sb.ToString();
    }

    private static string Plain(string s) => s.Replace("[u]", "").Replace("[/u]", "");

    private async Task<string?> CallAsync(string system, string prompt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model = options.Value.Model,
                max_tokens = options.Value.MaxTokens,
                system,
                messages = new[] { new { role = "user", content = prompt } }
            })
        };
        req.Headers.Add("x-api-key", options.Value.AnthropicApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken: ct);
        return body?.Content is null ? null : string.Concat(body.Content.Where(c => c.Type == "text").Select(c => c.Text));
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
    }

    private sealed class ContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
