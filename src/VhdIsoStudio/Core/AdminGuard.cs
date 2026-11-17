using System.Security.Principal;

namespace VhdIsoStudio.Core;

/// <summary>
/// Verifica che il processo giri con token elevato di un account amministratore
/// (locale o di dominio). Il manifest richiede gia' l'elevazione; questo e' il controllo di sicurezza.
/// </summary>
public static class AdminGuard
{
    public sealed record Status(bool IsElevatedAdmin, bool IsDomainAdmin, bool IsDomainAccount, string User);

    public static Status Check()
    {
        using var id = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(id);
        bool admin = principal.IsInRole(WindowsBuiltInRole.Administrator);

        bool domainAdmin = false;
        try
        {
            foreach (var g in id.Groups ?? new IdentityReferenceCollection())
            {
                if (g is SecurityIdentifier sid && sid.IsAccountSid() &&
                    (sid.Value.EndsWith("-512", StringComparison.Ordinal) || sid.Value.EndsWith("-519", StringComparison.Ordinal)))
                {
                    domainAdmin = true;
                    break;
                }
            }
        }
        catch { }

        bool domainAccount = !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        return new Status(admin, domainAdmin, domainAccount, id.Name);
    }
}
