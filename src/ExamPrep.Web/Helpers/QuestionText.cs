using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace ExamPrep.Web.Helpers;

/// <summary>題目文字輸出：先完整 HTML 編碼，再把解析器標記的底線 [u]..[/u] 轉為 &lt;u&gt;。</summary>
public static class QuestionText
{
    public static IHtmlContent Render(string? text)
    {
        if (string.IsNullOrEmpty(text)) return HtmlString.Empty;
        var encoded = HtmlEncoder.Default.Encode(text)
            .Replace("[u]", "<u>")
            .Replace("[/u]", "</u>");
        return new HtmlString(encoded);
    }
}
