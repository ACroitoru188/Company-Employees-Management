using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Web.Services;

public sealed class EmployeeAccountService
{
    private const string EmailDomain = "siemens.com";
    private readonly UserManager<User> _userManager;
    private readonly CompanyEmployeesDbContext _db;
    private readonly IAccountEmailSender _emailSender;

    public EmployeeAccountService(
        UserManager<User> userManager,
        CompanyEmployeesDbContext db,
        IAccountEmailSender emailSender)
    {
        _userManager = userManager;
        _db = db;
        _emailSender = emailSender;
    }

    public async Task<EmployeeAccountResult> CreateAsync(
        Guid adminId,
        string name,
        string invitationEmail,
        Guid departmentId,
        Guid regionId,
        string applicationBaseUri,
        UserRole role = UserRole.Employee)
    {
        // Guest is never assignable from this form, and 2 is the retired ProjectManager gap —
        // both would otherwise pass a bare enum-defined check.
        if (role is not (UserRole.Employee or UserRole.LineManager or UserRole.Admin))
            throw new InvalidOperationException("Select a valid access level.");

        var normalizedName = NormalizeDisplayName(name);
        if (normalizedName.Length < 2)
            throw new InvalidOperationException("Employee name is required.");

        invitationEmail = invitationEmail.Trim();
        if (!MailAddress.TryCreate(invitationEmail, out var parsedInvitationEmail)
            || !string.Equals(parsedInvitationEmail.Address, invitationEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Enter the employee's real email address.");
        }

        var department = await _db.Departments
            .Include(candidate => candidate.Manager)
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == departmentId);
        if (department == null)
            throw new InvalidOperationException("Select a valid department.");

        var region = await _db.Regions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == regionId && candidate.IsActive);
        if (region == null)
            throw new InvalidOperationException("Select a valid active region.");

        var admin = await _userManager.FindByIdAsync(adminId.ToString());
        if (admin == null || admin.Role != UserRole.Admin)
            throw new InvalidOperationException("Only administrators can create employee accounts.");
        if (admin.RegionId != region.Id)
            throw new InvalidOperationException("You can only create accounts in your own region.");

        var email = await GenerateUniqueEmailAsync(normalizedName);
        var employeeId = await GenerateNumericEmployeeIdAsync();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = employeeId,
            Name = normalizedName,
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DepartmentId = department.Id,
            RegionId = region.Id,
            // Do not create a reporting line across regional security boundaries.
            ManagerId = department.Manager?.RegionId == region.Id ? department.ManagerId : null,
            Role = role,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"The employee account could not be created. {message}");
        }

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var setupLink = BuildSetupLink(applicationBaseUri, email, encodedToken);
            var delivery = await _emailSender.SendPasswordSetupAsync(
                normalizedName,
                invitationEmail,
                email,
                setupLink);

            return new EmployeeAccountResult(
                employeeId,
                normalizedName,
                email,
                invitationEmail,
                department.Name,
                region.Name,
                delivery.Delivered,
                delivery.Delivered ? null : setupLink);
        }
        catch
        {
            // Do not leave behind an inaccessible account when email delivery fails.
            await _userManager.DeleteAsync(user);
            throw;
        }
    }

    public async Task<IdentityResult> SetInitialPasswordAsync(
        string email,
        string encodedToken,
        string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "This setup link is invalid or has expired." });

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return IdentityResult.Failed(new IdentityError { Description = "This setup link is invalid or has expired." });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
            return result;

        user.EmailConfirmed = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        return result;
    }

    private static string BuildSetupLink(string applicationBaseUri, string email, string encodedToken)
    {
        var baseUri = new Uri(applicationBaseUri, UriKind.Absolute);
        var pageUri = new Uri(baseUri, "set-password").ToString();
        return QueryHelpers.AddQueryString(pageUri, new Dictionary<string, string?>
        {
            ["email"] = email,
            ["code"] = encodedToken
        });
    }

    private async Task<string> GenerateUniqueEmailAsync(string name)
    {
        var slug = CreateEmailSlug(name);
        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("The employee name cannot be converted into an email address.");

        for (var suffix = 1; suffix <= 999; suffix++)
        {
            var localPart = suffix == 1 ? slug : $"{slug}{suffix}";
            var candidate = $"{localPart}@{EmailDomain}";

            if (await _userManager.FindByNameAsync(candidate) == null
                && await _userManager.FindByEmailAsync(candidate) == null)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique email address could not be generated for this employee.");
    }

    private async Task<Guid> GenerateNumericEmployeeIdAsync()
    {
        // Keep the GUID database type while using decimal digits only, matching
        // the visual style of the seeded demo-account IDs.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var digits = new char[32];
            for (var index = 0; index < digits.Length; index++)
                digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));

            var raw = new string(digits);
            var formatted = $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}";

            var candidate = Guid.ParseExact(formatted, "D");
            if (candidate != Guid.Empty
                && !await _db.Users.AnyAsync(user => user.Id == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique employee ID could not be generated.");
    }

    private static string NormalizeDisplayName(string? name) =>
        string.Join(
            ' ',
            (name ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string CreateEmailSlug(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder();
        var needsSeparator = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (needsSeparator && slug.Length > 0)
                    slug.Append('.');

                slug.Append(char.ToLowerInvariant(character));
                needsSeparator = false;
            }
            else
            {
                needsSeparator = slug.Length > 0;
            }
        }

        return slug.ToString().Trim('.');
    }

}

public sealed record EmployeeAccountResult(
    Guid EmployeeId,
    string Name,
    string Email,
    string InvitationEmail,
    string Department,
    string Region,
    bool EmailDelivered,
    string? DevelopmentSetupLink);
