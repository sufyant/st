using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ControlPlane.Infrastructure;

// The first system admin's email address (section ControlPlane), checked on start.
internal sealed class SystemAdminSettings
{
    public const string Section = "ControlPlane";

    public string? FirstSystemAdminEmail { get; set; }

    public bool NamesTheFirstSystemAdmin => !string.IsNullOrWhiteSpace(FirstSystemAdminEmail);

    // Section 6: the setting is required while the staff list is empty, so the check reads the catalog; once the first system admin
    // exists the setting has no effect.
    public sealed class Validation(IServiceScopeFactory scopes) : IValidateOptions<SystemAdminSettings>
    {
        private const string Key = $"{Section}:{nameof(FirstSystemAdminEmail)}";

        public ValidateOptionsResult Validate(string? name, SystemAdminSettings settings)
        {
            if (settings.NamesTheFirstSystemAdmin)
            {
                return MailAddress.TryCreate(settings.FirstSystemAdminEmail, out var address) && address.Address == settings.FirstSystemAdminEmail
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail($"{Key} must be an email address.");
            }

            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SystemAdmins.Any()
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail($"{Key} must name the email address of the first system admin while the staff list is empty.");
        }
    }
}
