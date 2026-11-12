# Virada 5x2

Plataforma SaaS B2B para apoiar estabelecimentos de alimentação e hospitalidade (restaurantes, bares, cafés/padarias, fast-foods e dark kitchens) na transição da jornada **6x1** para a escala **5x2** em conformidade com a CLT.

A solução combina diagnóstico de curva de demanda, simulação de impacto financeiro, gestão de equipe com polivalência, motor heurístico de geração de escalas 5x2 e portal mobile para colaboradores com fluxo de troca de turnos.

---

## Principais Funcionalidades

- **Onboarding e Configurações da Casa (`/onboarding`, `/settings/company`):** cadastro do tipo de operação (`BusinessType`), modelo de folga (`FixedClosure` ou `RotatingDaysOff`) e janelas de funcionamento.
- **Importação e Mapa de Demanda (`/demand`, `/demand/import`):** parsing de histórico tabular de vendas (ou preenchimento manual da matriz) e geração de diagnóstico com mapa de calor (`DemandHeatmap`) destacando picos, vales e ociosidade.
- **Simulador de Impacto Financeiro (`/financial`, `/financial/simulator`):** comparativo matemático entre o cenário atual 6x1 e o cenário otimizado 5x2 (`SimulateFinancialImpactUseCase`).
- **Gestão de Equipe e Polivalência (`/employees`, `/employees/team`):** cadastro de colaboradores, função principal (`StationRole`), matriz de habilidades secundárias (`SecondarySkill`) e restrições de folga.
- **Motor e Painel da Escala 5x2 (`/roster`, `/roster/panel`):**
  - Geração heurística respeitando regras CLT (intervalo interjornada mínimo de 11 horas, 2 folgas por ciclo semanal e revezamento dominical).
  - Ajustes manuais, validação contínua de conformidade, publicação da escala e exportação CSV (`/rosters/{rosterId}/export`).
  - Geração de link seguro para compartilhamento com a equipe.
- **Portal Mobile do Colaborador e Trocas (`/shared/roster`):** visualização leve dos turnos e folgas via token seguro (sem necessidade de login complexo), solicitação de troca com pares elegíveis, resposta do colega e aprovação em 1 clique pelo gestor com notificação em tempo real (`ManagerPendingSwapsBadge`).
- **Funil Ebook & Kit GTM (`/ebook`, `/convite`, `/gtm/ebook-kit`):** landing page pública com rastreamento UTM, criação de tenant isolado (`RegisterTenantOwnerUseCase`) e painel para cópia de links e download de QR Codes SVG.

---

## Arquitetura e Padrões

O projeto adota **Clean Architecture** em solução única ([`Scale5x2Migration.sln`](Scale5x2Migration.sln)), com validação automatizada de dependências via `NetArchTest.Rules`:

- **Domain (`src/Core/Domain`):** entidades (`Company`, `Employee`, `DemandSlot`, `Roster`, `Shift`, `ShiftSwapRequest`), value objects, enums, contratos de repositórios específicos e tipos base (`Result`, `Result<T>`, `Error`, `BaseEntity`, `AggregateRoot`, `ITenantEntity`). Zero dependências externas.
- **Application (`src/Core/Application`):** casos de uso, DTOs, motor de otimização 5x2 (`Roster5x2OptimizationEngine`), avaliador de conformidade (`RosterComplianceEvaluator`), serviços GTM e contratos de infraestrutura/telemetria.
- **Infrastructure (`src/Infrastructure/Data`):** `AppDbContext` (EF Core 10 + SQL Server), mapeamentos Fluent API, migrations, repositórios específicos por agregação, ASP.NET Core Identity (`ApplicationUser`, `ApplicationRole`) e `DotEnvLoader`.
- **Presentation (`src/Presentation/WebApp.Blazor`):** aplicação Blazor Server (Interactive Server Components), endpoints HTTP mínimos (`/Account/Logout`, `/rosters/{rosterId}/export`, `/health/live`, `/health/ready`), Design System próprio baseado em tokens CSS (`wwwroot/css/tokens.css`) e integração de observabilidade.

### Princípios Estruturantes

1. **Multi-tenancy lógico:** isolamento por `TenantId` via **Global Query Filters** no `AppDbContext` e preenchimento automático em `SaveChangesAsync`.
2. **Padrão `Result` / `Result<T>` nativo:** regras de domínio e casos de uso retornam `Result`/`Result<T>` sem uso de exceções para controle de fluxo de negócio.
3. **Repositórios específicos:** proibição de repositórios genéricos (`IRepository<T>`); cada agregação expõe um contrato próprio no domínio.
4. **Design System sem bibliotecas externas:** componentes Razor atômicos estilizados exclusivamente com CSS Custom Properties (`tokens.css`).
5. **Segredos exclusivamente via ambiente / `.env`:** nenhuma credencial versionada em código ou `appsettings*.json`.
6. **Documentação viva (ADRs):** decisões arquiteturais registradas sequencialmente em `docs/adrs/` (`0001` a `0034`).

---

## Stack Tecnológica

| Camada / Área | Tecnologia |
| :--- | :--- |
| **Linguagem & Runtime** | C# / .NET 10 (`net10.0`, `Nullable` e `TreatWarningsAsErrors` habilitados) |
| **Frontend** | Blazor Server (Interactive Server) + CSS Design Tokens nativos |
| **Persistência** | Entity Framework Core 10 + Microsoft SQL Server |
| **Autenticação** | ASP.NET Core Identity com cookies de sessão |
| **Observabilidade** | Serilog (Console / File / OpenTelemetry) + OpenTelemetry (Tracing, Metrics, OTLP) + Health Checks |
| **Testes** | xUnit, FluentAssertions, NSubstitute, bUnit, NetArchTest.Rules, `Microsoft.AspNetCore.Mvc.Testing` (SQLite) |
| **Infraestrutura & CI/CD** | Docker, Docker Compose, Azure Bicep (`infra/main.bicep`), GitHub Actions |

---

## Estrutura do Repositório

```text
.
├── .github/workflows/       # Pipelines de CI (ci.yml) e deploy Azure (deploy.yml)
├── docs/
│   ├── adrs/                # 34 Architecture Decision Records (0001–0034)
│   ├── benchmarks/          # Baseline de performance do motor 5x2
│   ├── gtm/                 # Catálogo de links UTM e QR Codes do Ebook
│   └── roadmaps/            # Roadmap estruturado do MVP
├── infra/
│   └── main.bicep           # Infraestrutura Azure (App Service Linux + Azure SQL)
├── src/
│   ├── Core/
│   │   ├── Domain/          # Entidades, VOs, Enums, Result<T>, Contratos de Repositório
│   │   └── Application/     # Casos de Uso, DTOs, Motor 5x2, Telemetria
│   ├── Infrastructure/
│   │   └── Data/            # AppDbContext, Migrations, Repositórios, Identity, DotEnvLoader
│   └── Presentation/
│       └── WebApp.Blazor/   # Blazor Server UI, Design System, Endpoints, Observabilidade
├── tests/
│   ├── Domain.UnitTests/
│   ├── Application.UnitTests/
│   ├── Infrastructure.Data.UnitTests/
│   ├── WebApp.Blazor.UnitTests/
│   ├── WebApp.IntegrationTests/
│   └── Architecture.Tests/
├── Directory.Build.props
├── Dockerfile
├── docker-compose.yml
├── .env.example
├── agents.md                # Diretrizes e regras de engenharia do repositório
└── Scale5x2Migration.sln
```

---

## Como Executar Localmente

### Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/) (para execução/testes locais via CLI)
- [Docker](https://www.docker.com/) e Docker Compose (ou uma instância local de SQL Server 2022+)

### 1. Configurar variáveis de ambiente

Copie o arquivo de exemplo para `.env` na raiz do repositório e preencha os valores (nunca faça commit do `.env`):

```bash
cp .env.example .env
```

Principais variáveis definidas em `.env.example`:

| Variável | Descrição |
| :--- | :--- |
| `DB_CONNECTION_STRING` | String de conexão com o SQL Server (obrigatória fora de `Development`) |
| `DB_PORT` | Porta exposta no host para o container SQL Server no `docker-compose.yml` (padrão: `1433`) |
| `MSSQL_SA_PASSWORD` | Senha do usuário `sa` para o container SQL Server no `docker-compose.yml` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente (`Development` ou `Production`) |
| `APP_PORT` | Porta exposta no host para a aplicação Blazor no `docker-compose.yml` (padrão: `8080`) |
| `DATA_PROTECTION_KEYS_PATH` | Diretório persistente para chaves do DataProtection (cookies e tokens) |
| `RUN_MIGRATIONS_ON_STARTUP` | `"true"` por padrão para aplicar migrations automaticamente no arranque (`"false"` desabilita, ex.: quando aplicadas via `efbundle`) |
| `PUBLIC_APP_BASE_URL` | URL pública base usada nos links UTM e QR Codes do Ebook |
| `OTEL_SERVICE_NAME` | Nome do serviço para OpenTelemetry (padrão: `Virada5x2.WebApp`) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Endpoint OTLP opcional para exportação de traces, métricas e logs |
| `OTEL_EXPORTER_OTLP_HEADERS` | Cabeçalhos opcionais de autenticação OTLP |
| `SERILOG_MINIMUM_LEVEL` | Nível mínimo de log do Serilog (padrão: `Information`) |
| `SERILOG_LOG_FILE_PATH` | Caminho opcional para gravação de logs em arquivo rotativo |

### 2. Executar com Docker Compose (recomendado)

Sobe o SQL Server 2022 Express e a aplicação Blazor na porta `8080`, aplicando as migrations automaticamente na inicialização:

```bash
docker compose up --build
```

Acesse `http://localhost:8080`.

### 3. Executar via .NET CLI

Com o SQL Server disponível e `DB_CONNECTION_STRING` configurada no `.env`:

```bash
dotnet restore Scale5x2Migration.sln
dotnet ef database update --project src/Infrastructure/Data/Data.csproj --startup-project src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj
dotnet run --project src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj
```

---

## Testes, Arquitetura e Benchmarks

A solução conta com 6 projetos de teste cobrindo domínio, casos de uso, persistência, componentes Blazor, isolamento multi-tenant em integração e regras arquiteturais:

```bash
# Executar toda a suíte de testes
dotnet test Scale5x2Migration.sln --configuration Release

# Executar os testes de stress/performance do motor 5x2
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --configuration Release --filter RosterEngineStressTests --logger "console;verbosity=detailed"
```

### Gates de Qualidade no CI (`.github/workflows/ci.yml`)

Em cada `push` ou `pull_request` para `main`, o workflow valida:
1. `dotnet build` em modo `Release` com `TreatWarningsAsErrors` e `EnforceCodeStyleInBuild`.
2. `dotnet test` com coleta de cobertura Cobertura exigindo **>= 95%** em `Domain` e **>= 90%** em `Application`.
3. Compilação do template Bicep (`az bicep build --file infra/main.bicep`).
4. Build da imagem Docker (`docker build -t virada5x2-webapp:ci .`).

---

## Observabilidade, Health Checks e Deploy

- **Health Checks:**
  - `GET /health/live` — verificação de liveness da aplicação.
  - `GET /health/ready` — verificação estruturada (JSON) de conectividade com banco de dados, ausência de migrations pendentes e gravabilidade do diretório de chaves `DataProtection`.
- **Deploy em Produção:**
  - Infraestrutura como código em [`infra/main.bicep`](infra/main.bicep) (Azure App Service Linux + Azure SQL Database).
  - Pipeline de publicação em [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) com geração de `efbundle` para migrations, deploy no Azure Web App e smoke tests em `/health/live`, `/health/ready` e `/ebook`.

---

## Documentação Adicional

- [`agents.md`](agents.md) — princípios inegociáveis, convenções de código e padrões arquiteturais.
- [`docs/roadmaps/initial_roadmap.md`](docs/roadmaps/initial_roadmap.md) — fases de construção e rastreabilidade.
- [`docs/adrs/`](docs/adrs) — histórico de decisões arquiteturais (ADRs 0001–0034).
- [`docs/benchmarks/roster-engine-stress-baseline.md`](docs/benchmarks/roster-engine-stress-baseline.md) — baseline de latência e alocação do motor 5x2.
- [`docs/gtm/ebook-links-and-qrcodes.md`](docs/gtm/ebook-links-and-qrcodes.md) — fluxo de conversão e links canônicos do Ebook.

