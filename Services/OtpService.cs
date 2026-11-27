using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace appdevPUPSCOUT.Services;

public class OtpService(ApplicationDbContext db, IConfiguration configuration)
{
    public const string EmailConfirmation = "EmailConfirmation";
    public const string PasswordReset = "PasswordReset";

    private int MaxAttempts => configuration.GetValue<int>("Otp:MaxAttempts", 5);
    private TimeSpan Lifetime => TimeSpan.FromMinutes(configuration.GetValue<int>("Otp:ExpiryMinutes", 10));
    private TimeSpan MinRequestInterval => TimeSpan.FromSeconds(configuration.GetValue<int>("Otp:MinRequestSeconds", 60));
    private int DailyEmailCap => configuration.GetValue<int>("Otp:DailyEmailCap", 10);
    private int DailyIpCap => configuration.GetValue<int>("Otp:DailyIpCap", 200);
    private int BurstIpLimit => configuration.GetValue<int>("Otp:BurstIpLimit", 10);
    private TimeSpan BurstWindow => TimeSpan.FromMinutes(configuration.GetValue<int>("Otp:BurstWindowMinutes", 10));

    public async Task<(bool Allowed, string? Error, DateTime? TryAgainAt)> CanRequestAsync(string email, string purpose, string? ipAddress = null)
    {
        email = NormalizeEmail(email);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        var recent = await db.OtpCodes
            .Where(o => o.Email == email && o.Purpose == purpose && !o.Used)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (recent is not null && now - recent.CreatedAt < MinRequestInterval)
        {
            var tryAgainAt = recent.CreatedAt + MinRequestInterval;
            return (false, $"Please wait before requesting another code. You can try again after {tryAgainAt:u}.", tryAgainAt);
        }

        var emailCountToday = await db.OtpCodes.CountAsync(o =>
            o.Email == email && o.Purpose == purpose && o.CreatedAt >= todayStart);
        if (emailCountToday >= DailyEmailCap)
        {
            var tryAgainAt = todayStart.AddDays(1);
            return (false, $"Too many code requests for this email today. You can try again after {tryAgainAt:u}.", tryAgainAt);
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var ipCountToday = await db.OtpCodes.CountAsync(o =>
                o.RequestIp == ipAddress && o.CreatedAt >= todayStart);
            if (ipCountToday >= DailyIpCap)
            {
                var tryAgainAt = todayStart.AddDays(1);
                return (false, $"Too many code requests from this network today. You can try again after {tryAgainAt:u}.", tryAgainAt);
            }

            var burstStart = now - BurstWindow;
            var burstCount = await db.OtpCodes.CountAsync(o =>
                o.RequestIp == ipAddress && o.CreatedAt >= burstStart);
            if (burstCount >= BurstIpLimit)
            {
                var oldestInWindow = await db.OtpCodes
                    .Where(o => o.RequestIp == ipAddress && o.CreatedAt >= burstStart)
                    .OrderBy(o => o.CreatedAt)
                    .Select(o => o.CreatedAt)
                    .FirstOrDefaultAsync();
                var tryAgainAt = oldestInWindow == default ? now + BurstWindow : oldestInWindow + BurstWindow;
                return (false, $"Too many code requests from this network. You can try again after {tryAgainAt:u}.", tryAgainAt);
            }
        }

        return (true, null, null);
    }

    public async Task<(bool Success, string? Code, string? Error)> GenerateAsync(string email, string purpose, string? ipAddress = null)
    {
        email = NormalizeEmail(email);

        var (allowed, error, _) = await CanRequestAsync(email, purpose, ipAddress);
        if (!allowed)
        {
            return (false, null, error);
        }

        var now = DateTime.UtcNow;

        var existing = db.OtpCodes.Where(o => o.Email == email && o.Purpose == purpose && !o.Used);
        db.OtpCodes.RemoveRange(existing);

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hash = HashCode(code, saltBytes);

        db.OtpCodes.Add(new OtpCode
        {
            Email = email,
            Purpose = purpose,
            CodeHash = Convert.ToBase64String(hash),
            Salt = salt,
            Attempts = 0,
            RequestIp = ipAddress,
            ExpiresAt = now.Add(Lifetime),
            CreatedAt = now
        });

        await db.SaveChangesAsync();
        return (true, code, null);
    }

    public async Task<bool> VerifyAsync(string email, string purpose, string code)
    {
        email = NormalizeEmail(email);
        code = (code ?? string.Empty).Trim();

        var otp = await db.OtpCodes
            .Where(o => o.Email == email && o.Purpose == purpose && !o.Used)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp is null) return false;
        if (otp.ExpiresAt < DateTime.UtcNow) return false;
        if (otp.Attempts >= MaxAttempts) return false;

        otp.Attempts++;
        await db.SaveChangesAsync();

        byte[] saltBytes;
        try
        {
            saltBytes = Convert.FromBase64String(otp.Salt);
        }
        catch
        {
            return false;
        }

        var computed = HashCode(code, saltBytes);
        byte[] stored;
        try
        {
            stored = Convert.FromBase64String(otp.CodeHash);
        }
        catch
        {
            return false;
        }

        if (computed.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(computed, stored))
        {
            return false;
        }

        otp.Used = true;
        await db.SaveChangesAsync();
        return true;
    }

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static byte[] HashCode(string code, byte[] salt)
    {
        using var sha = SHA256.Create();
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var input = new byte[salt.Length + codeBytes.Length];
        Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
        Buffer.BlockCopy(codeBytes, 0, input, salt.Length, codeBytes.Length);
        return sha.ComputeHash(input);
    }
}