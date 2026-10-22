namespace ExamPrep.Web.Services;

/// <summary>網站層級設定（appsettings 的 "Site" 區段）。</summary>
public class SiteOptions
{
    /// <summary>
    /// 公開模式：非考選部辦理的考試（國營事業、證照、升學考試）試題著作權屬主辦單位，
    /// 公開網站只列試卷清單與原始來源連結，不提供題目內文。
    /// </summary>
    public bool PublicMode { get; set; }

    /// <summary>公開模式下不提供內文的考試代碼（Exam.Slug）。</summary>
    public string[] WithheldExamSlugs { get; set; } = ["post", "cht"];

    /// <summary>部署在反向代理後面（例如雲端主機的 HTTPS 入口），信任 X-Forwarded-* 標頭。</summary>
    public bool BehindProxy { get; set; }

    public bool IsWithheld(string? examSlug) => PublicMode && examSlug is not null && WithheldExamSlugs.Contains(examSlug);
}
