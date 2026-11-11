# 20. Padronizar Shell de Navegação, Autorização por Papel e Integração Application-Blazor na Fase 5

## Status
Aceito

## Contexto
As Fases 1 a 4 entregaram o núcleo de domínio (`Company`, `DemandSlot`, `Employee`, `Roster`, `Shift`, `ShiftSwapRequest`), a persistência multi-tenant no SQL Server com ASP.NET Core Identity, os casos de uso de demanda/finanças/escala e o Design System atômico em Blazor Server com tokens CSS (`tokens.css`) testado via `bunit`.

Ao iniciar a **Fase 5 (Interface do Usuário — Telas do SaaS)** pela **Subfase 5.1 (Tela 1: Onboarding & Configurações da Casa)**, é necessário padronizar como as páginas interativas do Blazor Server consomem os casos de uso da camada `Application`, como o shell estrutural de navegação é organizado sem ferir a regra de tokens CSS nativos e como o controle de acesso por papel (`Owner`, `Manager`, `Employee`) protege rotas administrativas sem quebrar a acessibilidade da rota inicial (`/`).

Além disso, identificou-se que a camada `Application` ainda não possuía casos de uso dedicados para leitura e persistência da agregação `Company` (onboarding inicial vs. edição das configurações da casa).

## Decisão
1. **Casos de Uso de `Company` na Camada `Application`:**
   - Criar `GetCurrentCompanySettingsUseCase` e `SaveCompanySettingsUseCase` (com `SaveCompanySettingsCommand` e `CompanySettingsDto`) em `src/Core/Application/UseCases/Companies/` e `src/Core/Application/DTOs/Companies/`.
   - As páginas Blazor nunca injetam `ICompanyRepository` ou `IUnitOfWork` diretamente; toda orquestração de negócio retorna `Result<T>` imutável sem exceções de fluxo.
   - `GetCurrentCompanySettingsUseCase` retorna `Error.NotFound("Company.NotFound", ...)` quando o tenant autenticado ainda não concluiu o onboarding, permitindo que a UI alterne automaticamente entre modo **Onboarding** e modo **Configurações da Casa**.
2. **Roteamento Unificado e Autorização por Papel:**
   - Expor a Tela 1 em `/onboarding` e `/settings/company` no mesmo componente `Components/Pages/Company/CompanySettings.razor`, protegido por `[Authorize(Roles = "Owner,Manager")]` (`ApplicationRoles.Owner` e `ApplicationRoles.Manager`).
   - Manter a rota `/` (`Home.razor`) acessível com `<AuthorizeView>` para exibir o resumo do tenant autenticado ou o ponto de entrada para `/Account/Login` quando anônimo.
3. **Shell de Navegação (`MainLayout` + `NavMenu`):**
   - Criar `Components/Layout/NavMenu.razor` e `NavMenu.razor.css` integrados ao `MainLayout.razor`, consumindo exclusivamente variáveis declaradas em `wwwroot/css/tokens.css` (adicionando tokens de layout `--layout-sidebar-width`, `--layout-content-max-width` e `--layout-topbar-height` em `tokens.css`).
   - Integrar `EditForm` + `DataAnnotationsValidator` com os componentes do Design System (`AppInputText`, `AppInputSelect`, `AppCard`, `AppBadge`, `AppButton`) e propagar os retornos `Result<T>` para `IToastService` / `<AppToast />`.
4. **Cobertura Automatizada de Páginas e Auditoria CSS:**
   - Estender `DesignSystemArchitectureTests` para auditar também `Components/Pages/**/*.razor` e `*.razor.css`, bloqueando estilos inline e cores fora de `tokens.css`.
   - Testar as páginas Razor em ciclo TDD (`Red -> Green -> Refactor`) com `bunit` (`tests/WebApp.Blazor.UnitTests/Pages/`) e autorização HTTP via `WebApplicationFactory` (`tests/WebApp.Blazor.UnitTests/Integration/`).

## Consequências
- **Benefícios:**
  - Separação estrita entre apresentação Blazor e regras de negócio/persistência, mantendo 100% de testabilidade em `Application.UnitTests` e `WebApp.Blazor.UnitTests`.
  - Prevenção automática de regressões visuais (zero cores literais ou `style=` inline) em toda a Fase 5 via `Architecture.Tests`.
  - Experiência consistente entre primeiro acesso (Onboarding) e manutenção contínua (Configurações da Casa).
- **Custos:**
  - Cada nova tela das Subfases 5.2–5.5 demandará o respectivo conjunto de UseCases/DTOs em `Application` (caso ainda não existam para leitura/CRUD) antes da construção da página Razor.

