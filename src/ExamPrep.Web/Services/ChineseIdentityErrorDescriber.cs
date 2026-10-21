using Microsoft.AspNetCore.Identity;

namespace ExamPrep.Web.Services;

public class ChineseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = $"電子郵件 {email} 已被註冊。" };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = $"帳號 {userName} 已被使用。" };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "電子郵件格式不正確。" };
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"密碼至少需要 {length} 個字元。" };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "密碼需包含至少一個數字。" };
    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "密碼需包含至少一個小寫英文字母。" };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "密碼需包含至少一個大寫英文字母。" };
    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "密碼需包含至少一個符號。" };
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "密碼不正確。" };
}
