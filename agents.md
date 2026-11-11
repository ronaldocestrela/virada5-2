# Diretrizes de Desenvolvimento para Agents de IA (agents.md)

Este documento estabelece o conjunto de regras invioláveis, padrões arquiteturais e fluxos de trabalho que qualquer agente autônomo ou desenvolvedor assistido por IA deve seguir obrigatoriamente neste repositório.

---

## 1. Princípios Inegociáveis (Non-Negotiables)

1. **TDD Estrito (Test-Driven Development):**
   * **Red-Green-Refactor:** Nenhum código de produção deve ser escrito sem que exista previamente um teste unitário falhando que justifique sua criação.
   * Ordem do ciclo:
     1. Escrever o teste unitário que falha (`Red`).
     2. Escrever a implementação mínima necessária para o teste passar (`Green`).
     3. Refatorar mantendo todos os testes verdes (`Refactor`).
   * **Cobertura Mínima de Domínio e Aplicação:** 100% de cobertura nos métodos de entidades de domínio, regras de negócio e casos de uso/serviços.

2. **Documentação Viva com ADRs (Architecture Decision Records):**
   * Toda e qualquer decisão arquitetural estruturante ou mudança de padrão deve obrigatoriamente gerar um ADR em `docs/adrs/` numerado sequencialmente (`000X-titulo-curto.md`).
   * Antes de refatorar ou adicionar dependências fundamentais, o agente deve validar se a decisão possui correspondência com os ADRs aprovados.
   * Não tome decisões que mudem a arquitetura ou o comportamento do sistema sem consultar ou registrar o respectivo ADR.

3. **Arquitetura em Solução Única (`.sln`) com Projetos Isolados:**
   * Uma única solução raiz `.sln`, contendo projetos separados por responsabilidade (Clean Architecture).
   * Proibido acoplamento direto ou referências cruzadas que violem o sentido de dependência de dentro para fora (`Domain` não depende de ninguém; `Application` depende de `Domain`; `Infrastructure` e `Presentation` dependem de `Application` e `Domain`).

4. **Multi-tenancy Lógico (Opção A):**
   * O isolamento entre clientes (estabelecimentos) é lógico, realizado via coluna discriminadora `TenantId` em todas as entidades tenant-specific.
   * Obrigatório o uso de **Global Query Filters** no `DbContext` do Entity Framework Core para garantir que nenhuma consulta acidentalmente vaze dados entre tenants.
   * Ao criar ou persistir entidades, o `TenantId` deve ser injetado automaticamente pelo contexto do usuário logado (via `ICurrentUserService` / `ITenantProvider`).

5. **Apenas Repositórios Específicos:**
   * **Proibido** o uso de repositórios genéricos como `IRepository<T>`.
   * Todo repositório deve ser específico para uma raiz de agregação (`ICompanyRepository`, `IShiftRepository`, `IEmployeeRepository`, etc.), expondo apenas métodos que façam sentido explícito para a regra de negócio daquela agregação.

6. **Padrão `Result<T>` Nativo (Sem bibliotecas de terceiros):**
   * Proibido lançar exceções para controle de fluxo de negócio (`throw new BusinessException(...)`).
   * Exceções são exclusivas para falhas infraestruturais imprevistas.
   * Todo serviço de aplicação e método de regra de domínio que possa falhar deve retornar a estrutura de dados imutável `Result` ou `Result<T>`.

7. **Design System & Tokens Blazor Construídos do Zero:**
   * Proibido o uso de bibliotecas visuais prontas pesadas (MudBlazor, Radzen, AntDesign, etc.).
   * O design system deve ser construído do zero no Blazor Server utilizando **CSS Design Tokens nativos** (`tokens.css` com CSS Custom Properties para cores, espaçamentos, tipografia, elevação e transições).
   * Os componentes Razor devem ser pequenos, desacoplados, altamente reutilizáveis e estilizados estritamente via tokens.

8. **Informações Sensíveis Exclusivamente via `.env`:**
   * **Proibido** inserir credenciais, senhas, chaves de API, tokens, segredos ou connection strings diretamente no código-fonte, em `appsettings*.json` ou em qualquer arquivo versionado no repositório.
   * Toda informação sensível deve ser carregada obrigatoriamente a partir de um arquivo `.env` (ou variáveis de ambiente equivalentes em CI/CD e produção).
   * O arquivo `.env` deve constar no `.gitignore` e nunca ser commitado; mantenha um arquivo `.env.example` versionado contendo apenas os nomes das variáveis e placeholders sem dados reais.

---

## 2. Visão Geral da Stack Tecnológica

| Componente | Tecnologia | Observações |
| :--- | :--- | :--- |
| **Linguagem & Runtime** | C# 13 / .NET 10 | Target `net10.0`, nullable reference types ativado (`<Nullable>enable</Nullable>`). |
| **Banco de Dados** | Microsoft SQL Server | Relacional, suporte a queries com isolamento lógico via `TenantId`. |
| **ORM** | Entity Framework Core 10 | Code-First com Migrations, Global Query Filters e Value Converters. |
| **Autenticação & Identidade** | ASP.NET Core Identity | Identity nativo, com endpoints de login/cookies para o Blazor Server. |
| **Frontend** | Blazor Server-side | Interatividade Server com SignalR, sem misturar WebAssembly no MVP. |
| **Estilização** | CSS Tokens Nativo | `tokens.css` com CSS Custom Properties + CSS Isolado por componente. |
| **Testes** | xUnit + FluentAssertions + Moq/NSubstitute | Execução no padrão Given-When-Then. |

---

## 3. Estrutura Física de Pastas

```text
/
├── .github/
│   └── workflows/
│       └── ci.yml
├── docs/
│   └── adrs/
│       ├── 0001-record-architecture-decisions.md
│       ├── 0002-logical-multitenancy-with-query-filters.md
│       ├── 0003-blazor-server-with-custom-css-tokens.md
│       └── 0004-native-result-pattern-implementation.md
├── src/
│   ├── Core/
│   │   ├── Domain/
│   │   │   ├── Common/
│   │   │   │   ├── BaseEntity.cs
│   │   │   │   ├── ITenantEntity.cs
│   │   │   │   └── AggregateRoot.cs
│   │   │   ├── Entities/
│   │   │   │   ├── Company.cs
│   │   │   │   ├── Employee.cs
│   │   │   │   ├── Roster.cs
│   │   │   │   ├── Shift.cs
│   │   │   │   └── DemandSlot.cs
│   │   │   ├── Enums/
│   │   │   ├── Repositories/
│   │   │   │   ├── ICompanyRepository.cs
│   │   │   │   ├── IEmployeeRepository.cs
│   │   │   │   ├── IRosterRepository.cs
│   │   │   │   └── IDemandSlotRepository.cs
│   │   │   └── Domain.csproj
│   │   │
│   │   └── Application/
│   │       ├── Common/
│   │       │   ├── Result.cs
│   │       │   ├── ResultT.cs
│   │       │   ├── Error.cs
│   │       │   └── Interfaces/
│   │       │       ├── ICurrentUserService.cs
│   │       │       └── IUnitOfWork.cs
│   │       ├── DTOs/
│   │       ├── UseCases/
│   │       │   ├── Rosters/
│   │       │   ├── Employees/
│   │       │   └── Demand/
│   │       └── Application.csproj
│   │
│   ├── Infrastructure/
│   │   └── Data/
│   │       ├── Context/
│   │       │   ├── AppDbContext.cs
│   │       │   └── Configurations/
│   │       ├── Identity/
│   │       │   ├── ApplicationUser.cs
│   │       │   └── ApplicationRole.cs
│   │       ├── Repositories/
│   │       │   ├── CompanyRepository.cs
│   │       │   ├── EmployeeRepository.cs
│   │       │   ├── RosterRepository.cs
│   │       │   └── DemandSlotRepository.cs
│   │       ├── Migrations/
│   │       └── Data.csproj
│   │
│   └── Presentation/
│       └── WebApp.Blazor/
│           ├── Components/
│           │   ├── Layout/
│           │   │   ├── MainLayout.razor
│           │   │   └── NavMenu.razor
│           │   ├── DesignSystem/
│           │   │   ├── Button/
│           │   │   │   ├── AppButton.razor
│           │   │   │   └── AppButton.razor.css
│           │   │   ├── Card/
│           │   │   ├── Modal/
│           │   │   ├── Table/
│           │   │   └── Heatmap/
│           │   └── Pages/
│           │       ├── Account/
│           │       ├── Dashboard/
│           │       ├── Demand/
│           │       ├── Employees/
│           │       └── Roster/
│           ├── Services/
│           │   └── CurrentUserService.cs
│           ├── wwwroot/
│           │   └── css/
│           │       ├── tokens.css
│           │       └── app.css
│           ├── App.razor
│           ├── Program.cs
│           └── WebApp.Blazor.csproj
│
├── tests/
│   ├── Domain.UnitTests/
│   │   ├── Entities/
│   │   └── Domain.UnitTests.csproj
│   ├── Application.UnitTests/
│   │   ├── UseCases/
│   │   └── Application.UnitTests.csproj
│   └── Architecture.Tests/
│       ├── LayerDependencyTests.cs
│       └── Architecture.Tests.csproj
│
├── .editorconfig
├── .env.example
├── .gitignore
├── agents.md
└── Scale5x2Migration.sln
```

---

## 4. Implementação do Padrão `Result<T>` Nativo

Todas as operações de negócio e validações retornam instâncias imutáveis de `Result` ou `Result<T>`.

### Definição do Tipo de Erro
```csharp
namespace Application.Common;

public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "Valor nulo não permitido.");
    
    public static Error Validation(string code, string message) => new(code, message);
    public static Error NotFound(string code, string message) => new(code, message);
    public static Error Conflict(string code, string message) => new(code, message);
    public static Error Failure(string code, string message) => new(code, message);
}
```

### Classe Base `Result` e Genérica `Result<T>`
```csharp
namespace Application.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None || !isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Combinação inválida de sucesso e erro.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Não é possível acessar o valor de um resultado com falha.");

    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
}
```

---

## 5. Diretrizes de Multi-tenancy Lógico no SQL Server

1. **Interface do Tenant:**
   ```csharp
   namespace Domain.Common;

   public interface ITenantEntity
   {
       Guid TenantId { get; set; }
   }
   ```

2. **Interceptação e Global Query Filters no DbContext:**
   * Toda entidade que implementa `ITenantEntity` deve ser filtrada automaticamente:
   ```csharp
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       base.OnModelCreating(modelBuilder);

       foreach (var entityType in modelBuilder.Model.GetEntityTypes())
       {
           if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
           {
               modelBuilder.Entity(entityType.ClrType)
                   .HasQueryFilter(ConvertFilterExpression(entityType.ClrType));
           }
       }
   }
   ```
   * O `SaveChangesAsync` deve garantir que a propriedade `TenantId` da entidade seja preenchida a partir de `ICurrentUserService.TenantId` antes do envio ao SQL Server.

---

## 6. Diretrizes do Padrão Repository Específico

* **Sem Repositórios Genéricos:** Não instanciar ou injetar classes genéricas.
* **Agregação Isolada:** Cada agregação do domínio possui sua própria interface no projeto `Domain` e sua implementação concreta no projeto `Infrastructure.Data`.
* **Exemplo de Contrato:**
  ```csharp
  namespace Domain.Repositories;

  public interface IRosterRepository
  {
      Task<Roster?> GetByIdWithShiftsAsync(Guid id, CancellationToken ct = default);
      Task<IReadOnlyList<Roster>> GetByWeekRangeAsync(DateOnly startDate, DateOnly endDate, CancellationToken ct = default);
      Task AddAsync(Roster roster, CancellationToken ct = default);
      void Update(Roster roster);
  }
  ```

---

## 7. Diretrizes do Frontend Blazor Server & Design Tokens

### Tokenização via CSS Nativo (`tokens.css`)
Todo componente do Blazor deve consumir estritamente as propriedades definidas no `tokens.css`. Cores hardcoded (ex.: `#FFFFFF`, `#1E293B`) são expressamente proibidas no markup e no CSS isolado.

```css
/* wwwroot/css/tokens.css */
:root {
  /* Colors - Brand & Palette */
  --color-primary: #10b981;
  --color-primary-hover: #059669;
  --color-surface-bg: #f8fafc;
  --color-surface-card: #ffffff;
  --color-text-primary: #0f172a;
  --color-text-secondary: #64748b;
  --color-border: #e2e8f0;
  --color-danger: #ef4444;
  --color-warning: #f59e0b;
  --color-success: #10b981;

  /* Spacing Scale */
  --space-1: 0.25rem;
  --space-2: 0.5rem;
  --space-3: 0.75rem;
  --space-4: 1rem;
  --space-6: 1.5rem;
  --space-8: 2rem;

  /* Typography */
  --font-family-base: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-size-xs: 0.75rem;
  --font-size-sm: 0.875rem;
  --font-size-base: 1rem;
  --font-size-lg: 1.125rem;
  --font-size-xl: 1.25rem;
  --font-weight-regular: 400;
  --font-weight-medium: 500;
  --font-weight-semibold: 600;

  /* Radii & Shadows */
  --radius-sm: 0.25rem;
  --radius-md: 0.5rem;
  --radius-lg: 0.75rem;
  --shadow-sm: 0 1px 2px 0 rgb(0 0 0 / 0.05);
  --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.1);
}
```

### Componentização Atômica Blazor
* Todo componente deve ficar em pasta própria sob `Components/DesignSystem/{NomeDoComponente}/`.
* Arquivo Razor e arquivo `.razor.css` isolado correspondente.
* Componentes devem ter props claras (`[Parameter]`) e emitir eventos via `EventCallback`.

---

## 8. Ciclo de Vida TDD Obrigatório

Antes de gerar ou alterar qualquer arquivo em `src/Core/Domain` ou `src/Core/Application`, o Agent deve:

1. **Passo 1 (Red):** Criar a classe de teste correspondente no projeto `tests/...UnitTests`.
   * Padrão de nomenclatura do teste: `MethodName_StateUnderTest_ExpectedBehavior`.
   * Estrutura do método: Arrange, Act, Assert.
2. **Passo 2 (Executar & Confirmar Falha):** O teste deve compilar, rodar e falhar pelo motivo correto (ausência de regra ou resultado esperado divergente).
3. **Passo 3 (Green):** Implementar o código de negócio mínimo no projeto correspondente até que o teste passe com sucesso.
4. **Passo 4 (Refactor):** Melhorar legibilidade, eliminar duplicações e assegurar o cumprimento dos design tokens sem quebrar os testes existentes.

---

## 9. Processo de Decisão Arquitetural (ADR)

* Qualquer proposta de alteração em banco de dados, novos pacotes NuGet, novos protocolos de comunicação ou mudança estrutural exige a criação de um documento em `docs/adrs/`.
* Formato padrão:
  ```markdown
  # [Número]. [Título no Modo Imperativo]

  ## Status
  [Proposto | Aceito | Rejeitado | Depreciado]

  ## Contexto
  Qual problema operacional ou técnico precisa ser resolvido?

  ## Decisão
  Qual a abordagem técnica escolhida e como será aplicada?

  ## Consequências
  Quais os benefícios, custos e impactos no desenvolvimento?
  ```

---

## 10. Checklist de Validação para Agents de IA

Antes de marcar qualquer tarefa ou pull request como concluído, certifique-se de validar os seguintes itens:
- [ ] O teste unitário foi escrito antes do código de produção e cobre os cenários de sucesso e erro?
- [ ] O fluxo de erro retorna `Result<T>` e não lança exceções de negócio?
- [ ] Novas entidades que pertencem ao tenant implementam `ITenantEntity`?
- [ ] Não há repositórios genéricos sendo injetados ou criados?
- [ ] Nenhuma cor ou medida hardcoded foi inserida no Blazor sem usar `tokens.css`?
- [ ] A arquitetura em camadas e o isolamento de dependências foram estritamente respeitados?
- [ ] Há um ADR registrado se houve introdução de nova tecnologia ou padrão estrutural?
- [ ] Todas as informações sensíveis (connection strings, senhas, chaves e tokens) são carregadas via `.env` / variáveis de ambiente, sem segredos hardcoded ou commitados?