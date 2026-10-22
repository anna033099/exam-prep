using System.Net.Http.Json;

namespace ExamPrep.Showcase.Services;

/// <summary>讀取靜態題庫 JSON（GitHub Pages 上的 data/ 資料夾），讀過的檔案留在記憶體。</summary>
public sealed class DataService(HttpClient http)
{
    private Catalog? _catalog;
    private readonly Dictionary<string, List<PaperRow>> _lists = new();
    private readonly Dictionary<string, Dictionary<string, Paper>> _chunks = new();

    public async Task<Catalog> CatalogAsync() =>
        _catalog ??= await http.GetFromJsonAsync<Catalog>("data/catalog.json") ?? new Catalog();

    public async Task<List<PaperRow>> PapersAsync(string slug)
    {
        if (!_lists.TryGetValue(slug, out var rows))
            _lists[slug] = rows = await http.GetFromJsonAsync<List<PaperRow>>($"data/exams/{Uri.EscapeDataString(slug)}.json") ?? new();
        return rows;
    }

    /// <summary>試卷代號格式為「考試代碼_試題代號」，題目放在 data/papers/&lt;考試代碼&gt;.json。</summary>
    public async Task<Paper?> PaperAsync(string file, string id)
    {
        if (!_chunks.TryGetValue(file, out var chunk))
            _chunks[file] = chunk = await http.GetFromJsonAsync<Dictionary<string, Paper>>($"data/papers/{Uri.EscapeDataString(file)}.json") ?? new();
        return chunk.GetValueOrDefault(id);
    }
}
