using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExamPrep.Web.Controllers;

public class LeaderboardController(AppDbContext db, UserManager<ApplicationUser> users, StatsService stats) : Controller
{
    public async Task<IActionResult> Index()
    {
        var me = users.GetUserId(User);
        var (byCount, byRate, weekStart) = await stats.WeeklyAsync(me);
        ViewBag.ByCount = byCount;
        ViewBag.ByRate = byRate;
        ViewBag.WeekStart = weekStart;
        ViewBag.ShowMe = me is not null && ((await db.UserSettings.FindAsync(me))?.ShowOnLeaderboard ?? false);
        ViewBag.SignedIn = me is not null;
        return View();
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Toggle()
    {
        var me = users.GetUserId(User)!;
        var s = await db.UserSettings.FindAsync(me);
        if (s is null) db.UserSettings.Add(s = new UserSetting { UserId = me });
        s.ShowOnLeaderboard = !s.ShowOnLeaderboard;
        await db.SaveChangesAsync();
        TempData["Flash"] = s.ShowOnLeaderboard ? "你的暱稱會出現在排行榜上。" : "已從排行榜隱藏，只有你自己看得到自己的名次。";
        return RedirectToAction(nameof(Index));
    }
}
