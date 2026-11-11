# 23. Padronizar Casos de Uso de Equipe e Grid Rápido de Polivalência na Tela 4

## Status
Aceito

## Contexto
Na **Subfase 5.4 (Tela 4: Gestão da Equipe & Polivalência)**, o proprietário ou gestor (`Owner` / `Manager`) precisa cadastrar e manter rapidamente os colaboradores do estabelecimento, definindo função principal (`StationRole`), tags de polivalência (`SecondarySkill` com nível `Support` ou `Autonomous`) e restrições individuais (`UnavailableDays` rígidas e `PreferredDaysOff` flexíveis).

Embora a agregação [`Employee`](../../src/Core/Domain/Entities/Employee.cs), o repositório [`IEmployeeRepository`](../../src/Core/Domain/Repositories/IEmployeeRepository.cs) e o mapeamento EF Core já existam desde as Fases 1 e 2, a camada `Application` ainda não dispõe de casos de uso e DTOs próprios para leitura e manutenção da equipe pela interface Blazor Server. Além disso, conforme a **ADR 0020**, páginas Blazor não devem injetar repositórios ou `IUnitOfWork` diretamente e devem manter conformidade estrita com `wwwroot/css/tokens.css`.

## Decisão
1. **Casos de Uso de Equipe na Camada `Application` (`Result<T>` Nativo):**
   - Criar `EmployeeDtos.cs` (`SecondarySkillDto`, `EmployeeDto`, `EmployeesWorkspaceDto`) em `src/Core/Application/DTOs/Employees/`.
   - Criar `GetCurrentEmployeesWorkspaceUseCase` (somente leitura via `IEmployeeRepository.GetByCompanyAsync` + `CompanyResolutionHelper`), retornando `Error.NotFound("Company.NotFound", ...)` caso o onboarding do estabelecimento ainda esteja pendente.
   - Criar `CreateEmployeeUseCase`, `UpdateEmployeeUseCase` e `SetEmployeeActiveStatusUseCase` em `src/Core/Application/UseCases/Employees/`, encapsulando toda validação e persistência via `IUnitOfWork` sem exceções de fluxo.
   - Em `UpdateEmployeeUseCase`, validar previamente a matriz de `SecondarySkills` (sem duplicidade e sem conflito com a nova `PrimaryRole`) e a disponibilidade antes de mutar a entidade rastreada, preservando atomicidade em caso de erro de validação.
2. **Desativação Lógica em vez de Exclusão Física:**
   - Alinhado às regras do domínio (`Employee.Activate()` / `Employee.Deactivate()`), a interface oferece alternância rápida entre ativo e inativo em vez de exclusão física, preservando integridade referencial com turnos (`Shift`) e solicitações de troca (`ShiftSwapRequest`) históricos.
3. **Grid Rápido e Edição em `TeamManagement.razor` (`/employees`, `/employees/team`):**
   - Proteger as rotas com `[Authorize(Roles = "Owner,Manager")]` e `@rendermode InteractiveServer`.
   - Disponibilizar barra/grid rápido sempre visível para inclusão contínua (nome, função primária, tags de polivalência por praça e chips de indisponibilidade/preferência de folga) e modal (`AppModal`) para edição completa, além de filtros locais por nome, praça e status (`Ativos`, `Inativos`, `Todos`).
   - Vincular a rota `/employees` no `NavMenu.razor`, `Home.razor` e no modal de equipe pendente do `FinancialSimulator.razor`.
   - Toda estilização em `TeamManagement.razor.css` consome exclusivamente variáveis declaradas em `wwwroot/css/tokens.css`.

## Consequências
- **Benefícios:**
  - Fluxo ágil de cadastro sequencial de equipe sem perder o rigor das regras de domínio de `Employee` e `SecondarySkill`.
  - Navegação completa entre o Simulador Financeiro (Tela 3) e a Gestão da Equipe (Tela 4), desbloqueando a geração real da escala 5x2.
  - Testabilidade integral nas camadas `Application.UnitTests`, `WebApp.Blazor.UnitTests` (`bUnit` + `WebApplicationFactory`) e `Architecture.Tests`.
- **Custos:**
  - Alterações futuras de campos do colaborador (ex.: matrícula ou carga horária contratual específica) exigirão evolução coordenada de `Employee`, `EmployeeDtos` e dos comandos de criação/edição.

