# 26. Canal de Notificação Reativa de Trocas e Consistência de Coberturas Pendentes na Subfase 6.2

## Status
Aceito

## Contexto
Na **Subfase 6.2 (`Fluxo de Solicitação de Troca de Turno`)**, após o colaborador selecionar um par compatível (função principal ou polivalência) e enviar a solicitação pelo link público `/shared/roster?token=...`, o aceite do colega precisa refletir-se de maneira reativa nas notificações do gestor autenticado (`NavMenu.razor`, `Home.razor` e `RosterPanel.razor`), sem depender de recarregamento manual da página (`F5`) ou de bibliotecas/serviços externos.

Adicionalmente, a descoberta de opções compatíveis (`GetCompatibleSwapOptionsUseCase`) e a criação de solicitações (`RequestShiftSwapUseCase`) precisam considerar solicitações **unilaterais pendentes** (`TargetShiftId == null`, `IsPending`), evitando que o mesmo colaborador receba múltiplas propostas incompatíveis para o mesmo dia ou acima do limite semanal 5x2 antes da decisão do gestor.

## Decisão
1. **Canal de Notificação In-Process por Tenant (`IShiftSwapNotificationService`):**
   - Definir a interface `IShiftSwapNotificationService` em `Application/Common/Interfaces/IShiftSwapNotificationService.cs` (`event Action<Guid>? OnTenantSwapsChanged`, `void NotifyTenantSwapsChanged(Guid tenantId)`), mantendo a camada `Application` livre de dependências de infraestrutura ou apresentação.
   - Implementar `ShiftSwapNotificationService` como singleton thread-safe em `WebApp.Blazor/Services/ShiftSwapNotificationService.cs`.
   - Disparar `NotifyTenantSwapsChanged(tenantId)` a partir de `RespondShiftSwapPeerUseCase` (aceite/recusa do colega), `ApproveShiftSwapUseCase`, `RejectShiftSwapUseCase` (recusa do gestor ou cancelamento do solicitante) e `RequestShiftSwapUseCase` (quando `RequirePeerApproval == false`).
   - Assinar o evento em `NavMenu.razor`, `Home.razor` e `RosterPanel.razor` filtrando estritamente pelo `TenantId` do usuário autenticado, além de atualizar `NavMenu.razor` em `NavigationManager.LocationChanged`.
2. **Proteção Preventiva para Coberturas Unilaterais Pendentes:**
   - Em `GetCompatibleSwapOptionsUseCase`, carregar o contexto da escala em lote (eliminando leituras repetidas N+1) e excluir opções de cobertura unilateral quando o colaborador candidato já possuir solicitação unilateral pendente na mesma data ou atingir `Roster.MaxWorkDaysPerWeek` somando dias trabalhados e coberturas unilaterais pendentes.
   - Em `RequestShiftSwapUseCase`, rejeitar com `Result.Failure(Error.Conflict(...))` novas solicitações de cobertura unilateral (`TargetShiftId == null`) caso o colaborador destinatário já possua outra cobertura unilateral pendente na mesma data ou já atinja o limite de 5 dias com as pendências acumuladas.
3. **Transparência de Polivalência e Link Direto para o Colega em `SharedRoster.razor`:**
   - Exibir a função principal do colega e destacar quando o solicitante cobrirá o turno do colega via polivalência (`TargetReplacementCoverageOrigin == CoverageOrigin.SecondarySkill`), bem como oferecer ação rápida de cópia do link direto (`/shared/roster?token=...&employeeId={TargetEmployeeId}`) para solicitações em `PendingPeerApproval`.

## Consequências
- **Benefícios:**
  - Notificação imediata ao gestor em circuitos Blazor Server ativos sem novos pacotes NuGet ou infraestrutura de mensageria externa.
  - Eliminação de conflitos silenciosos entre múltiplas coberturas unilaterais pendentes antes da aprovação em 1 clique.
  - Experiência mobile completa para envio da solicitação ao colega e visibilidade clara de polivalência.
- **Custos:**
  - Por ser um canal in-process, sua abrangência é por instância da aplicação; em um cenário futuro com múltiplas réplicas (scale-out horizontal), o mesmo contrato `IShiftSwapNotificationService` poderá ser respaldado por Redis backplane / Azure SignalR Service sem alterar a camada `Application` ou os componentes Razor.

