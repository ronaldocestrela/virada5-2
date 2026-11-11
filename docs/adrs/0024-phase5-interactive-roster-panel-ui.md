# 24. Padronizar Casos de Uso de Ajustes Manuais, Cobertura de Pico, Exportação CSV e Compartilhamento na Tela 5

## Status
Aceito

## Contexto
Na **Subfase 5.5 (Tela 5: Painel de Escala Interativo)**, o proprietário ou gestor (`Owner` / `Manager`) precisa visualizar a escala semanal 5x2 gerada, realizar ajustes manuais com validação CLT imediata, acompanhar o status de cobertura de pico por turno, publicar a escala, exportar os dados e gerar link de compartilhamento.

Embora a agregação [`Roster`](../../src/Core/Domain/Entities/Roster.cs), os casos de uso [`GenerateRoster5x2UseCase`](../../src/Core/Application/UseCases/Rosters/GenerateRoster5x2UseCase.cs) e [`ValidateRosterComplianceUseCase`](../../src/Core/Application/UseCases/Rosters/ValidateRosterComplianceUseCase.cs) e o componente [`RosterGrid`](../../src/Presentation/WebApp.Blazor/Components/DesignSystem/RosterGrid/RosterGrid.razor) já existam, a camada `Application` ainda não possui casos de uso para leitura do workspace semanal da escala, mutações manuais no rascunho (`Assign`, `Remove`, `Reassign`, `Swap`), publicação (`Publish`), exportação CSV e geração de link de compartilhamento.

Além disso, conforme a **ADR 0020** e a **ADR 0016**, as páginas Blazor não devem injetar repositórios ou `IUnitOfWork` diretamente, devem reutilizar o mesmo motor de avaliação de conformidade (`RosterComplianceEvaluator`) após cada ajuste manual e devem consumir exclusivamente variáveis de `wwwroot/css/tokens.css`.

## Decisão
1. **Casos de Uso de Leitura, Ajustes Manuais e Publicação na Camada `Application` (`Result<T>` Nativo):**
   - Criar `GetCurrentRosterWorkspaceUseCase` (somente-leitura via `CompanyResolutionHelper`, `IRosterRepository`, `IEmployeeRepository` e `IDemandSlotRepository`) para carregar a escala da semana selecionada, lista de semanas próximas e colaboradores ativos disponíveis para ajuste manual.
   - Criar os casos de uso de mutação manual restritos a `RosterStatus.Draft` (preservando escalas `Published` para o fluxo formal de trocas da Fase 6): `AssignManualShiftUseCase`, `RemoveManualShiftUseCase`, `ReassignManualShiftUseCase` e `SwapManualShiftsUseCase`, todos construindo `EmployeeWeekBoundaryContext` via `RosterBoundaryContextBuilder` e reavaliando `RosterComplianceReportDto` via helper compartilhado `RosterResultMapper`.
   - Criar `PublishRosterUseCase` para validar conformidade CLT integral (incluindo fronteiras semanais) e transicionar `Draft -> Published`.
2. **Exportação CSV Nativa e Link de Compartilhamento Stateless:**
   - Criar `ExportRosterCsvUseCase` em C# puro (zero pacotes NuGet externos, UTF-8 com BOM e delimitador `;` compatível com Excel pt-BR, com proteção contra *CSV injection*) servido pelo endpoint HTTP autenticado `GET /rosters/{rosterId:guid}/export` (`Owner` / `Manager`) respeitando o isolamento multi-tenant.
   - Definir a interface `IRosterShareLinkService` em `Application/Common/Interfaces/` e implementá-la em `WebApp.Blazor` via `IDataProtectionProvider` (`Virada5x2.RosterShare.v1`) com `GenerateRosterShareLinkUseCase`, gerando link tokenizado stateless com expiração configurável sem alterar o schema do SQL Server na Fase 5 (preparando a rota pública mobile da Subfase 6.1).
3. **Barra de Status de Cobertura de Pico e Painel Interativo (`PeakCoverageBar` & `RosterPanel.razor`):**
   - Criar o componente atômico `Components/DesignSystem/PeakCoverageBar/` (`PeakCoverageBar.razor` + `PeakCoverageBar.razor.css`) exibindo o progresso e os slots de demanda `Peak` por turno/dia/praça, estilizado exclusivamente com `tokens.css` e sem `style=` inline.
   - Expor a Tela 5 em `/roster` e `/roster/panel` (`RosterPanel.razor`), protegida por `[Authorize(Roles = "Owner,Manager")]`, integrada ao `NavMenu.razor`, `Home.razor`, `TeamManagement.razor` e ao card de escala gerada de `FinancialSimulator.razor`.

## Consequências
- **Benefícios:**
  - Ajustes manuais preservam 100% das garantias CLT do domínio (11h interjornada, <= 5 dias trabalhados, revezamento dominical e elegibilidade por polivalência) e atualizam o diagnóstico de conformidade imediatamente.
  - Exportação CSV e impressão utilizam exclusivamente BCL .NET e CSS nativo com tokens, mantendo zero dependências externas pesadas.
  - Link de compartilhamento tokenizado pronto para ser consumido pela Tela Pública Mobile na Subfase 6.1.
- **Custos:**
  - Como o token de compartilhamento é stateless na Subfase 5.5, eventual requisito futuro de revogação explícita por registro exigirá persistência de coluna ou lista de revogação na Fase 6.

