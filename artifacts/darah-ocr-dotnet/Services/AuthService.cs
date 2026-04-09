using DarahOcr.Data;
using DarahOcr.Models;
using Microsoft.EntityFrameworkCore;

namespace DarahOcr.Services;

public class AuthService(AppDbContext db, ILogger<AuthService> logger)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, 12);

    public static bool VerifyPassword(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);

    public static bool IsPasswordStrong(string password, out string? error)
    {
        if (password.Length < 8) { error = "كلمة المرور يجب أن تكون 8 أحرف على الأقل."; return false; }
        if (!password.Any(char.IsUpper)) { error = "كلمة المرور يجب أن تحتوي على حرف كبير."; return false; }
        if (!password.Any(char.IsLower)) { error = "كلمة المرور يجب أن تحتوي على حرف صغير."; return false; }
        if (!password.Any(char.IsDigit)) { error = "كلمة المرور يجب أن تحتوي على رقم."; return false; }
        if (password.All(c => char.IsLetterOrDigit(c))) { error = "كلمة المرور يجب أن تحتوي على رمز خاص."; return false; }
        error = null;
        return true;
    }

    public async Task<(User? user, string? error)> LoginAsync(string username, string password)
    {
        if (username.Length > 64 || password.Length > 128)
            return (null, "بيانات الدخول غير صالحة.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || !user.IsActive)
            return (null, "اسم المستخدم أو كلمة المرور غير صحيحة.");

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var minutes = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            return (null, $"الحساب مقفل. حاول مرة أخرى بعد {minutes} دقيقة.");
        }

        if (!VerifyPassword(password, user.PasswordHash))
        {
            var attempts = user.FailedLoginAttempts + 1;
            var locked = attempts >= MaxFailedAttempts;
            user.FailedLoginAttempts = attempts;
            user.LockedUntil = locked ? DateTime.UtcNow.Add(LockDuration) : null;
            await db.SaveChangesAsync();

            if (locked)
                return (null, "تم قفل الحساب بعد محاولات متكررة. حاول بعد 15 دقيقة.");

            var remaining = MaxFailedAttempts - attempts;
            return (null, $"كلمة المرور غير صحيحة. ({remaining} محاولات متبقية)");
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return (user, null);
    }

    public async Task<User> CreateUserAsync(string username, string email, string password, string role, string[] permissions)
    {
        var hash = HashPassword(password);
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = hash,
            Role = role,
            Permissions = permissions,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task SeedDefaultUsersAsync()
    {
        if (!await db.Users.AnyAsync(u => u.Username == "admin"))
        {
            var admin = new User
            {
                Username = "admin",
                Email = "admin@internal.local",
                PasswordHash = HashPassword("Admin@1234"),
                Role = "admin",
                Permissions = ["upload", "review", "approve"],
                IsActive = true
            };
            db.Users.Add(admin);
            logger.LogInformation("Default admin user created (admin / Admin@1234)");
        }

        if (!await db.Users.AnyAsync(u => u.Username == "operator"))
        {
            var op = new User
            {
                Username = "operator",
                Email = "operator@internal.local",
                PasswordHash = HashPassword("Operator@1234"),
                Role = "user",
                Permissions = ["upload"],
                IsActive = true
            };
            db.Users.Add(op);
            logger.LogInformation("Default operator user created (operator / Operator@1234)");
        }

        await db.SaveChangesAsync();
    }
}
