namespace ExamPrep.Web.Services;

/// <summary>網站層級設定（appsettings 的 "Site" 區段）。</summary>
public class SiteOptions
{
    /// <summary>
    /// 公開模式：非依法令舉行之考試（公司自辦甄試）的試題可能受著作權保護，
    /// 公開網站只列試卷清單與原始來源連結，不提供題目內文。
    /// </summary>
    public bool PublicMode { get; set; }

    /// <summary>公開模式下不提供內文的考試代碼（Exam.Slug）。</summary>
    public string[] WithheldExamSlugs { get; set; } = ["post", "cht"];

    /// <summary>部署在反向代理後面（例如 Hugging Face Spaces），信任 X-Forwarded-* 標頭。</summary>
    public bool BehindProxy { get; set; }

    /// <summary>允許被指定網域以 iframe 嵌入（Hugging Face 的 Space 頁面）；cookie 會改為 SameSite=None; Secure。</summary>
    public string[] FrameAncestors { get; set; } = [];

    public bool IsWithheld(string? examSlug) => PublicMode && examSlug is not null && WithheldExamSlugs.Contains(examSlug);
}
