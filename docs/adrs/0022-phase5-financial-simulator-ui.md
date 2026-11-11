# 22. Padronizar Contexto de Leitura Financeira, Parametrização What-If e CTA de Escala na Tela 3

## Status
Aceito

## Contexto
Na **Subfase 5.3 (Tela 3: Simulador de Impacto Financeiro)**, a camada `Presentation` (`WebApp.Blazor`) precisa conectar o caso de uso [`SimulateFinancialImpactUseCase`](../../src/Core/Application/UseCases/Financial/SimulateFinancialImpactUseCase.cs) (ADR 0015) e o componente [`FinancialComparisonCard`](../../src/Presentation/WebApp.Blazor/Components/DesignSystem/FinancialComparisonCard/FinancialComparisonCard.razor) a uma página interativa de simulação financeira para perfis `Owner` e `Manager`.

Três pontos arquiteturais exigem padronização explícita antes da codificação:
1. **Isolamento de repositórios e carregamento inicial sem mutação (ADR 0020):** A página Blazor não pode injetar `ICompanyRepository`, `IEmployeeRepository` ou `IDemandSlotRepository` diretamente para descobrir se o onboarding foi concluído, quantos colaboradores ativos já existem ou se há matriz de demanda aberta para contextualização.
2. **Ordem de onboarding vs. operação contínua (ADR 0015):** A Tela 3 precede a Tela 4 (`Gestão da Equipe & Polivalência`, `/employees`). Logo, o formulário precisa exigir quantidade de colaboradores (`Headcount`) quando ainda não houver equipe ativa cadastrada, mas aproveitar automaticamente a equipe ativa (com opção de simulação "what-if" manual) quando ela já existir.
3. **Comportamento do CTA de geração imediata da escala antes das Subfases 5.4 e 5.5:** Como as rotas `/employees` (Subfase 5.4) e `/roster` (Subfase 5.5) ainda não foram implementadas, o botão primário de geração de escala no `FinancialComparisonCard` não pode navegar para uma rota inexistente (404) nem falhar com erro técnico quando não houver colaboradores cadastrados.

## Decisão
1. **Caso de Uso Somente Leitura `GetCurrentFinancialSimulationContextUseCase`:**
   - Adicionar `FinancialSimulationContextDto` em `src/Core/Application/DTOs/Financial/FinancialSimulationDtos.cs` e `GetCurrentFinancialSimulationContextUseCase` em `src/Core/Application/UseCases/Financial/`.
   - O caso de uso resolve o tenant/estabelecimento via `CompanyResolutionHelper` (retornando `Error.NotFound("Company.NotFound", ...)` caso o onboarding esteja pendente), contabiliza os colaboradores ativos via `IEmployeeRepository.GetActiveByCompanyAsync`, verifica se existe ao menos um `DemandSlot` dentro da janela `Company.IsOpenAt` e expõe `SimulateFinancialImpactUseCase.DefaultAssumptions`.
2. **Roteamento, Autorização e Formulário Parametrizável (`FinancialSimulator.razor`):**
   - Expor a Tela 3 em `/financial` e `/financial/simulator` com `@rendermode InteractiveServer` e `[Authorize(Roles = "Owner,Manager")]`, vinculada ao `NavMenu.razor`, `Home.razor` e ao próximo passo de `DemandMapping.razor`.
   - Apresentar inputs primários (salário base médio mensal, horas extras mensais por colaborador e seleção automática/manual de `Headcount`) e seção expansível com as premissas trabalhistas e operacionais avançadas pré-preenchidas a partir de `DefaultAssumptions` (com ação de restauração).
   - Toda estilização consome estritamente variáveis declaradas em `wwwroot/css/tokens.css`, sem estilos inline ou cores literais.
3. **Estratégia do CTA "Gerar Escala 5x2" na Subfase 5.3:**
   - Quando `ActiveEmployeesCount > 0`, o CTA executa imediatamente `GenerateRoster5x2UseCase` para a próxima segunda-feira (tratando conflitos de rascunho existente `RosterGeneration.DraftAlreadyExists` via `AppModal` de confirmação para `ReplaceExistingDraft = true`) e exibe um resumo estruturado da escala gerada (`GeneratedRosterResultDto`) na própria tela.
   - Quando `ActiveEmployeesCount == 0`, o CTA abre um `AppModal` orientativo informando que o cadastro de colaboradores ativos (Etapa 4) é pré-requisito para materializar a escala 5x2.

## Consequências
- **Benefícios:**
  - Mantém zero acoplamento entre Blazor e repositórios/EF Core, preservando testabilidade completa via `Application.UnitTests`, `bUnit` e `WebApplicationFactory`.
  - Entrega o fluxo completo da Tela 3 (incluindo o CTA funcional de geração imediata da escala) sem criar rotas quebradas enquanto as Telas 4 e 5 não são entregues.
- **Custos:**
  - Ao implementar a Tela 5 (`/roster`) na Subfase 5.5, o resumo exibido após a geração imediata poderá incluir um link direto para a grade da escala recém-criada.

