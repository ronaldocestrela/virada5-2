namespace Infrastructure.Data.Identity;

/// <summary>
/// Nomes padronizados das claims emitidas pelo ASP.NET Core Identity para contexto multi-tenant.
/// </summary>
public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string FullName = "full_name";
}

