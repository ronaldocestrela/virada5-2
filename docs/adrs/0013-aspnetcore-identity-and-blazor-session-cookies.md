# 13. Adotar ASP.NET Core Identity com IdentityDbContext, Claims Multi-Tenant e Cookies HTTP-Only no Blazor Server

## Status
Aceito

## Contexto
Na subfase 2.4 do roadmap (`Setup do ASP.NET Core Identity`), é necessário estruturar a autenticação e autorização de usuários e papéis (`ApplicationUser` e `ApplicationRole`) integrando o `AppDbContext` no SQL Server aos serviços de sessão e cookies do **Blazor Server** (`WebApp.Blazor`), mantendo aderência estrita à Clean Architecture (ADR 0009), ao isolamento lógico por `TenantId` (ADRs 0002 e 0010) e à regra de não armazenar segredos fora de `.env`.

Três desafios técnicos exigem decisão explícita:
1. **Autenticação antes da definição do Tenant no contexto:** No `AppDbContext`, toda entidade `ITenantEntity` sofre filtro global *fail-closed* (`CurrentTenantId != Guid.Empty && entity.TenantId == CurrentTenantId`). Durante a etapa de login, o usuário ainda não está autenticado (`CurrentTenantId == Guid.Empty`). Se a tabela `AspNetUsers` fosse filtrada por `ITenantEntity`, o `UserManager` e o `SignInManager` não encontrariam nenhum registro e ninguém conseguiria autenticar-se.
2. **Cookies de sessão em circuitos SignalR do Blazor Server:** Circuitos interativos `InteractiveServer` operam sobre WebSocket/SignalR após a resposta HTTP inicial, não podendo emitir cabeçalhos `Set-Cookie` diretamente durante eventos de UI. Além disso, o circuito precisa revalidar periodicamente o `SecurityStamp` do usuário para invalidar sessões revogadas ou usuários desativados.
3. **Carregamento seguro de conexão no runtime da aplicação web:** A conexão com o SQL Server (`DB_CONNECTION_STRING`) não pode constar em `appsettings*.json` nem no código-fonte, devendo ser carregada a partir de `.env` / variáveis de ambiente tanto na inicialização da aplicação quanto em design-time.

## Decisão
1. **Pacote NuGet e Camada de Identidade:**
   - Adicionar `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (`10.0.11`) exclusivamente no projeto `Infrastructure.Data`, mantendo `Domain` e `Application` totalmente desacoplados de `Microsoft.AspNetCore.Identity`.
   - Definir `ApplicationUser : IdentityUser<Guid>` (com `TenantId`, `FullName`, `IsActive`, `CreatedAtUtc` e fábrica `Create` retornando `Result<ApplicationUser>`), `ApplicationRole : IdentityRole<Guid>` (com `Description`), `ApplicationRoles` (`Owner`, `Manager`, `Employee`) e `TenantClaimTypes` em `src/Infrastructure/Data/Identity/`.
2. **Evolução do `AppDbContext` e Multi-tenancy em `ApplicationUser`:**
   - Alterar `AppDbContext` para herdar de `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, mantendo `IUnitOfWork` e uma única história de migrations no SQL Server (`AddAspNetIdentity`).
   - `ApplicationUser` possui `TenantId` obrigatório (`Guid`) e índice `IX_AspNetUsers_TenantId`, mas **não** implementa `ITenantEntity`, permitindo buscas de login pelo `UserManager`/`SignInManager` mesmo quando `CurrentTenantId == Guid.Empty`. Todas as entidades de negócio (`Company`, `DemandSlot`, `Employee`, `Roster`, `Shift`) permanecem estritamente protegidas pelos Global Query Filters *fail-closed*.
   - Registrar `ApplicationUserClaimsPrincipalFactory` para injetar a claim `"tenant_id"` (`TenantClaimTypes.TenantId`) e `"full_name"` no `ClaimsPrincipal` somente quando `TenantId != Guid.Empty`.
3. **Sessão por Cookies HTTP-Only e Blazor Server (`WebApp.Blazor`):**
   - Configurar `AddAuthentication` com `IdentityConstants.ApplicationScheme` e `.AddIdentityCookies()`, definindo cookie HTTP-only (`Scale5x2.Auth`, `SameSite=Lax`, `SecurePolicy=SameAsRequest`, `SlidingExpiration=true`, expiração de 8 horas) e rotas `/Account/Login`, `/Account/Logout` e `/Account/AccessDenied`.
   - Implementar `CurrentUserService` (`ICurrentUserService`) consumindo `IHttpContextAccessor` com fallback seguro não-bloqueante para `AuthenticationStateProvider`, retornando `Guid.Empty` (*fail-closed*) caso o usuário não esteja autenticado ou não possua claim `"tenant_id"` válida.
   - Implementar `IdentityRevalidatingAuthenticationStateProvider` (`RevalidatingServerAuthenticationStateProvider`) revalidando `IsActive` e `SecurityStamp` a cada 30 minutos no circuito SignalR.
   - Executar login via página Static SSR (`/Account/Login` sem `@rendermode InteractiveServer`) e logout via endpoint HTTP `POST /Account/Logout` com validação Antiforgery, mantendo `<AuthorizeRouteView>` + `<CascadingAuthenticationState>` em `Routes.razor` sem qualquer estilo CSS hardcoded até a construção do `tokens.css` na Fase 4.
4. **Carregamento de `.env` compartilhado:**
   - Extrair `DotEnvLoader` em `Infrastructure.Data` para carregar `.env` sem sobrescrever variáveis de ambiente do SO, utilizado por `AppDbContextFactory` e `WebApp.Blazor/Program.cs`.

## Consequências
- **Benefícios:**
  - Fluxo de autenticação completo e compatível com Blazor Server interativo e Multi-tenancy lógico *fail-closed*.
  - Zero dependência de Identity nas camadas `Domain` e `Application`.
  - Proteção contra sessões obsoletas ou usuários desativados via revalidação de `SecurityStamp`.
- **Custos:**
  - Exige geração da migration `AddAspNetIdentity` sobre o schema inicial e cuidado para que endpoints públicos/Static SSR de `Account` não dependam de interatividade SignalR para gravar cookies.

