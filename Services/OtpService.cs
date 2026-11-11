using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace appdevPUPSCOUT.Services;

public class OtpService(ApplicationDbContext db)
{
    public const string EmailConfirmation = "EmailConfirmation";
    public const string PasswordReset = "PasswordReset";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<string> GenerateAsync(string email, string purpose)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var existing = db.OtpCodes.Where(o => o.Email == email && o.Purpose == purpose && !o.Used);
        db.OtpCodes.RemoveRange(existing);

        db.OtpCodes.Add(new OtpCode
        {
            Email = email,
            Purpose = purpose,
            Code = code,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        });

        await db.SaveChangesAsync();
        return code;
    }

    public async Task<bool> VerifyAsync(string email, string purpose, string code)
    {
        var otp = await db.OtpCodes
            .Where(o => o.Email == email && o.Purpose == purpose && !o.Used)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp is null) return false;
        if (otp.ExpiresAt < DateTime.UtcNow) return false;
        if (!string.Equals(otp.Code, code, StringComparison.Ordinal)) return false;

        otp.Used = true;
        await db.SaveChangesAsync();
        return true;
    }
}
