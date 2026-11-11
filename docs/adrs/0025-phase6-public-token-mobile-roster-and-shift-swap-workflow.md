# 25. Padronizar Escopo Multi-Tenant por Token Público, Trocas Mobile Pré-Validadas e Aprovação em 1 Clique na Fase 6

## Status
Aceito

## Contexto
Na **Fase 6 (Visualização Mobile do Colaborador & Trocas)** do roadmap (`Subfases 6.1, 6.2 e 6.3`), colaboradores da equipe operacional precisam acessar sua escala semanal e dias exatos de folga sem fluxo de login complexo, utilizando o link tokenizado gerado pela Subfase 5.5 (`/shared/roster?token=...`), além de consultar pares compatíveis (função principal, polivalência e regras CLT), solicitar trocas com aceite prévio do colega e encaminhar a pendência para aprovação em 1 clique do gestor autenticado.

Dois desafios arquiteturais exigem decisão explícita para preservar as regras invioláveis do `agents.md` e dos ADRs anteriores (`0002`, `0004`, `0005`, `0010`, `0017`, `0020` e `0024`):
1. **Multi-tenancy Lógico com Global Query Filters em Rota Anônima:** O `AppDbContext` filtra todas as entidades `ITenantEntity` e bloqueia persistência quando `ICurrentUserService.TenantId == Guid.Empty` (*fail-closed*). Permitir acesso anônimo sem contornar os Global Query Filters nem adicionar parâmetros manuais de `tenantId` em repositórios (`Architecture.Tests`) requer um mecanismo controlado de escopo temporário de tenant após validação criptográfica do token.
2. **Segurança e Reaproveitamento do Fluxo de Trocas em 2 Etapas:** As ações iniciadas na tela pública devem permanecer estritamente confinadas ao `TenantId`, `CompanyId` e `RosterId` contidos no payload assinado do token DataProtection (`Virada5x2.RosterShare.v1`), evitando adulteração de parâmetros de rota/formulário e reutilizando `ShiftSwapValidationHelper`, `RequestShiftSwapUseCase`, `RespondShiftSwapPeerUseCase`, `ApproveShiftSwapUseCase` e `RejectShiftSwapUseCase`.

## Decisão
1. **Escopo Temporário de Tenant Pós-Validação Criptográfica (`IScopedTenantContext`):**
   - Definir `IScopedTenantContext` em `Application/Common/Interfaces/` (`Guid? ScopedTenantId`, `IDisposable BeginScope(Guid tenantId)`) e implementá-lo em `WebApp.Blazor/Services/ScopedTenantContext.cs` com pilha por circuito/execução assíncrona.
   - Atualizar `CurrentUserService.TenantId` para priorizar `IScopedTenantContext.ScopedTenantId` quando um escopo validado estiver ativo, preservando integralmente o comportamento *fail-closed* (`Guid.Empty`) fora do escopo e mantendo `UserId`/`IsAuthenticated` restritos a usuários autenticados via Identity.
   - Estender `IRosterShareLinkService` com `Result<RosterShareTokenPayloadDto> ValidateToken(string token)` em `Application`. Os casos de uso públicos validam o token via `SharedTokenValidationHelper`, abrem `BeginScope(payload.TenantId)` e conferem vínculo exato com `CompanyId`, `RosterId`, `WeekStartDate` e status não arquivado antes de qualquer leitura ou mutação.
2. **Casos de Uso de Leitura Mobile, Descoberta de Pares Compatíveis e Wrappers Tokenizados:**
   - Criar `GetSharedRosterWorkspaceUseCase` para retornar turnos semanais e dias exatos de folga (diferenciando fechamento fixo da casa e folga 5x2) e histórico de solicitações da escala.
   - Criar `GetCompatibleSwapOptionsUseCase` para pré-validar candidatos elegíveis à troca mútua ou cessão unilateral (`TargetShiftId == null`) usando `ShiftSwapValidationHelper`, exibindo apenas opções conformes à CLT e à matriz de polivalência.
   - Criar `RequestSharedShiftSwapUseCase`, `RespondSharedShiftSwapPeerUseCase` e `CancelSharedShiftSwapUseCase` fixando `RosterId`/`CompanyId` a partir do token validado e delegando às regras existentes de `ShiftSwapRequest`.
3. **Notificações e Aprovação em 1 Clique para o Gestor (`GetPendingShiftSwapsUseCase`):**
   - Criar `GetPendingShiftSwapsUseCase` para listar pendências da empresa (`PendingManagerApproval` e `PendingPeerApproval`) com avaliação em tempo real da conformidade CLT antes da autorização.
   - Integrar badges/notificações em `NavMenu.razor` e `Home.razor` e adicionar a fila de aprovação em 1 clique no `RosterPanel.razor` usando o novo componente atômico `Components/DesignSystem/ShiftSwapRequestCard/` estilizado exclusivamente via `tokens.css`.
4. **Experiência Mobile Ultraleve e Manifest PWA:**
   - Criar `Components/Layout/SharedMobileLayout.razor` (sem barra lateral administrativa) para a página anônima `Components/Pages/Shared/SharedRoster.razor` (`/shared/roster`).
   - Adicionar `wwwroot/manifest.webmanifest` e ícone SVG referenciados em `App.razor` sem Service Worker de cache offline para não servir tokens expirados ou escalas desatualizadas.

## Consequências
- **Benefícios:**
  - Global Query Filters e `ApplyTenantEnforcement()` permanecem 100% ativos em todas as leituras e gravações, inclusive no fluxo anônimo por token.
  - Zero duplicação da lógica trabalhista CLT/polivalência entre o motor de trocas e a interface mobile.
  - Experiência mobile focada para a equipe operacional e fluxo completo até aprovação em 1 clique pelo gestor.
- **Custos:**
  - Sem vínculo explícito `Employee <-> ApplicationUser` nesta fase, a identificação do colaborador na tela pública ocorre por seleção na equipe da escala compartilhada (com suporte opcional a `?employeeId=...`).
  - Caso o negócio passe a exigir revogação antecipada de links antes da expiração do token DataProtection, será necessário persistir identificadores de revogação em uma fase futura.

