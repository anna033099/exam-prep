using System.Net;
using Microsoft.AspNetCore.Components;

namespace ExamPrep.Showcase.Services;

public static class Text
{
    /// <summary>題目文字：先做 HTML 編碼，再把原卷底線標記 [u]…[/u] 轉成底線。</summary>
    public static MarkupString Render(string? s) =>
        new(WebUtility.HtmlEncode(s ?? "").Replace("[u]", "<u>").Replace("[/u]", "</u>"));

    private static readonly string[] Cn = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];

    public static string CnNumber(int n) => n switch
    {
        <= 10 and > 0 => Cn[n],
        < 20 => "十" + Cn[n - 10],
        < 100 => Cn[n / 10] + "十" + Cn[n % 10],
        _ => n.ToString()
    };

    public static string ExcludedLabel(int no) => no > 1000 ? $"申論第{CnNumber(no - 1000)}題" : $"第 {no} 題";

    public static string Years(int first, int last) => first == last ? $"{first} 年" : $"{first}–{last} 年";
}
