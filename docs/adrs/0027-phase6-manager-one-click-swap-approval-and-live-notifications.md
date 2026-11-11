# 27. Consolidar Aprovação pelo Gestor em 1 Clique, Avaliação CLT em Lote e Notificações SignalR Isoladas na Subfase 6.3

## Status
Aceito

## Contexto
Na **Subfase 6.3 (`Aprovação pelo Gestor em 1 Clique`)**, embora os contratos iniciais da fila de trocas (`GetPendingShiftSwapsUseCase`, `ApproveShiftSwapUseCase`, `RejectShiftSwapUseCase`) e o canal `IShiftSwapNotificationService` tenham sido introduzidos nas subfases anteriores, três problemas estruturais impediam a operação completa e confiável em produção:
1. **Fronteira de Interatividade no Blazor Web App:** `MainLayout.razor`, `NavMenu.razor`, `AppToast.razor` e `Home.razor` operavam em Static SSR. Como resultado, as assinaturas em `IShiftSwapNotificationService.OnTenantSwapsChanged` e `IToastService.OnChange` eram descartadas ao fim da resposta HTTP inicial, impedindo notificações reativas sem recarregamento manual (`F5`). Ao mesmo tempo, as rotas `/Account/Login` e `/Account/Logout` dependem de Static SSR / formulário antifalsificação.
2. **Concorrência sobre `AppDbContext` Scoped:** Eventos simultâneos de notificação (`OnTenantSwapsChanged` e `NavigationManager.LocationChanged`) no mesmo circuito SignalR podem disparar execuções concorrentes de `GetPendingShiftSwapsUseCase` sobre a mesma instância scoped de `AppDbContext`.
3. **Consultas N+1 e Transparência Operacional na Fila do Gestor:** `GetPendingShiftSwapsUseCase` executava `ShiftSwapValidationHelper.ValidateAsync(...)` dentro do loop de pendências (N+1 roundtrips ao banco), e o `ShiftSwapValidationSummaryDto` calculado não era exibido no `ShiftSwapRequestCard` (ocultando indicadores críticos de **Polivalência**, **Turno de Pico** e link direto para semanas distintas da selecionada no painel).

## Decisão
1. **Ilhas `@rendermode InteractiveServer` Preservando SSR de Autenticação:**
   - Extrair o indicador numérico de pendências do menu lateral para o componente `Components/Layout/ManagerPendingSwapsBadge.razor` com `@rendermode InteractiveServer`, mantendo `NavMenu.razor` e o formulário `/Account/Logout` compatíveis com Static SSR.
   - Ativar `@rendermode InteractiveServer` em `Home.razor` e nas instâncias de `<AppToast @rendermode="InteractiveServer" />` em `MainLayout.razor` e `SharedMobileLayout.razor`, compartilhando o circuito e o escopo DI das páginas interativas.
2. **Serialização de Leituras Reativas por Circuito (`SemaphoreSlim`):**
   - Proteger as atualizações reativas em `ManagerPendingSwapsBadge.razor`, `Home.razor` e `RosterPanel.razor` com `SemaphoreSlim(1, 1)`, garantindo que `AppDbContext` nunca receba duas operações assíncronas simultâneas a partir de eventos do `IShiftSwapNotificationService` ou de navegação.
   - Em `RosterPanel.razor`, disparar feedback `IToastService` informativo sempre que a contagem `PendingManagerApprovalCount` aumentar por notificação externa.
3. **Avaliação CLT em Lote e Transparência no `ShiftSwapRequestCard`:**
   - Refatorar `GetPendingShiftSwapsUseCase` para carregar em lote `Company`, `DemandSlot`, `Employee` e `Roster` adjacentes uma única vez por execução, avaliando cada pendência em memória via `ShiftSwapValidationHelper.ValidateLoadedContext(...)` e tratando graciosamente escalas ausentes/arquivadas.
   - Estender `ShiftSwapRequestCard` com `ValidationSummary` (`ShiftSwapValidationSummaryDto`) e `WeekHref` opcionais estilizados exclusivamente via `tokens.css`, exibindo selos de **Polivalência**, **Turno de Pico**, resumo da auditoria CLT, horário do aceite do colega e atalho para a grade da semana quando aplicável.

## Consequências
- **Benefícios:**
  - Notificações ao vivo (`NavMenu`, `Home`, `RosterPanel` e `AppToast`) funcionam de ponta a ponta no navegador sem recarregar a página e sem quebrar cookies/antiforgery do ASP.NET Core Identity.
  - Eliminação de queries N+1 na fila do gestor e prevenção de exceções de concorrência no `AppDbContext` durante rajadas de eventos SignalR.
  - Decisão em 1 clique com visibilidade imediata de impacto operacional (polivalência e horários de pico) e conformidade CLT.
- **Custos:**
  - Páginas SSR com `<AppToast @rendermode="InteractiveServer" />` e `<ManagerPendingSwapsBadge @rendermode="InteractiveServer" />` mantêm uma conexão SignalR leve enquanto o usuário autenticado estiver com a aba aberta.

