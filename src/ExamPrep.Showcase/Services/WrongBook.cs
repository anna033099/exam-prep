using System.Text.Json;
using Microsoft.JSInterop;

namespace ExamPrep.Showcase.Services;

/// <summary>
/// 錯題本：存在這台電腦的瀏覽器（localStorage），不會上傳到任何地方。
/// 和完整版一樣，同一題連續答對 2 次就從錯題本移除。
/// </summary>
public sealed class WrongBook(IJSRuntime js)
{
    private const string Key = "examprep.wrongbook.v1";
    private const int MasteredAfter = 2;
    private List<WrongItem>? _items;

    public async Task<List<WrongItem>> AllAsync()
    {
        if (_items is not null) return _items;
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            _items = string.IsNullOrEmpty(raw) ? new() : JsonSerializer.Deserialize<List<WrongItem>>(raw) ?? new();
        }
        catch (Exception)
        {
            _items = new();   // 無痕模式或瀏覽器停用儲存：只在這次瀏覽期間記錄
        }
        return _items;
    }

    public async Task RecordAsync(Paper paper, Question q, string pick, bool correct)
    {
        var items = await AllAsync();
        var item = items.FirstOrDefault(i => i.PaperId == paper.Id && i.Question.No == q.No);
        if (!correct)
        {
            if (item is null)
                items.Insert(0, new WrongItem
                {
                    PaperId = paper.Id, PaperTitle = $"{paper.Year} 年 {paper.Subject}", QuestionUrl = paper.QuestionUrl,
                    Question = q, LastPick = pick, AddedAt = DateTime.UtcNow
                });
            else
            {
                item.LastPick = pick;
                item.Streak = 0;
            }
        }
        else if (item is not null && ++item.Streak >= MasteredAfter)
        {
            items.Remove(item);
        }
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        (await AllAsync()).Clear();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(_items)); }
        catch (Exception) { /* 無法儲存時只保留在記憶體 */ }
    }
}
