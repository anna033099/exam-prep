using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Data;

/// <summary>啟動時套用資料庫遷移、匯入考試資訊與官方試題包、建立管理員角色。</summary>
public static class AppInitializer
{
    public const string AdminRole = "Admin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Init");

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var seed = sp.GetRequiredService<SeedDataService>();
        await seed.ImportExamInfoAsync();
        if (config.GetValue("Seed:ImportOfficialPapersOnStartup", true))
            await seed.ImportOfficialPapersAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AdminRole))
            await roles.CreateAsync(new IdentityRole(AdminRole));

        // 管理員帳號只從設定檔／環境變數讀取，程式碼中不放任何密碼
        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(email);
            if (admin is null)
            {
                admin = new ApplicationUser { UserName = email, Email = email, DisplayName = "管理員", EmailConfirmed = true };
                var r = await users.CreateAsync(admin, password);
                if (!r.Succeeded)
                {
                    log.LogWarning("建立管理員失敗：{Errors}", string.Join("; ", r.Errors.Select(e => e.Description)));
                    return;
                }
            }
            if (!await users.IsInRoleAsync(admin, AdminRole))
                await users.AddToRoleAsync(admin, AdminRole);
        }
    }
}
