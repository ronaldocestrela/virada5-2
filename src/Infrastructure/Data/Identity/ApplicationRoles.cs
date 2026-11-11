namespace Infrastructure.Data.Identity;

/// <summary>
/// Papéis padrão suportados pela plataforma no MVP.
/// </summary>
public static class ApplicationRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static readonly IReadOnlyList<string> All =
    [
        Owner,
        Manager,
        Employee
    ];
}

