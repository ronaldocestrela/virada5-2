namespace Application.Common.Interfaces;

/// <summary>
/// Fornece o contexto do usuário autenticado e do tenant corrente na execução atual.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid TenantId { get; }
    bool IsAuthenticated { get; }
}

