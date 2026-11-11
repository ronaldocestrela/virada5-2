# Roadmap de Desenvolvimento: SaaS de Transição 5x2

Este documento estabelece o cronograma em fases e subfases estruturadas para o desenvolvimento do MVP do SaaS. Cada entrega segue rigorosamente os princípios do `agents.md`: **TDD estrito (Red-Green-Refactor)**, **documentação viva via ADRs**, **Clean Architecture com solução única**, **Result<T> nativo**, **multi-tenancy lógico** e **design tokens em Blazor Server**.

---

## Visão Geral das Fases

```
[Fase 0: Fundação & ADRs] 
           ↓
[Fase 1: Núcleo de Domínio & Multi-tenancy (TDD)]
           ↓
[Fase 2: Infraestrutura de Persistência & Identity]
           ↓
[Fase 3: Casos de Uso & Motor de Otimização 5x2]
           ↓
[Fase 4: Design System & Tokens no Blazor Server]
           ↓
[Fase 5: Interface do Usuário (Telas do MVP)]
           ↓
[Fase 6: Visualização Mobile do Colaborador & Trocas]
           ↓
[Fase 7: Validação E2E, Testes de Carga & Lançamento]
```

---

## Fase 0: Fundação de Arquitetura & Governança (Semana 1)
**Objetivo:** Estabelecer o repositório, convenções de código, pipeline de CI e registrar formalmente as decisões fundamentais em ADRs.

### Subfases:
* **0.1 Registro dos ADRs Iniciais**
  * `0001-record-architecture-decisions.md`: Adoção de ADRs vivos.
  * `0002-logical-multitenancy-with-query-filters.md`: Segregação por `TenantId` e filtros globais do EF Core.
  * `0003-blazor-server-with-custom-css-tokens.md`: Decisão por Blazor Server puro com design tokens CSS sem libs externas.
  * `0004-native-result-pattern-implementation.md`: Utilização da classe imutável `Result<T>` sem exceções de fluxo.
  * `0005-specific-repositories-pattern.md`: Repositórios especializados por agregação.
* **0.2 Setup da Solução e Estrutura de Pastas**
  * Criação da solução `Scale5x2Migration.sln` em .NET 10.
  * Configuração dos projetos: `Domain`, `Application`, `Infrastructure.Data`, `WebApp.Blazor` e suítes de testes (`Domain.UnitTests`, `Application.UnitTests`, `Architecture.Tests`).
  * Configuração do `.editorconfig` com convenções de C# 13 / .NET 10 (Nullable enabled, treat warnings as errors).
* **0.3 Pipeline de CI & Architecture Tests**
  * Configuração de GitHub Actions / pipeline para rodar `dotnet test` e barrar commits sem cobertura ou com testes quebrados.
  * Implementação de testes arquiteturais (NetArchTest) validando que `Domain` não referencia nenhuma camada externa e que repositórios genéricos são proibidos.

---

## Fase 1: Núcleo de Domínio & Modelagem (TDD) (Semanas 1-2)
**Objetivo:** Construir as entidades centrais, regras de negócio puras e tipos utilitários sob ciclo estrito de TDD (Red-Green-Refactor).

### Subfases:
* **1.1 Primitive Types & Padrão Result**
  * Implementação testada de `Result`, `Result<T>` e `Error` (validação, conflito, not found).
  * Criação de interfaces base: `ITenantEntity`, `BaseEntity`, `AggregateRoot`.
* **1.2 Agregação: Estabelecimento & Curva de Demanda (`Company` & `DemandSlot`)**
  * *(TDD)* Testes e implementação da entidade `Company` (tipos de negócio, horários de funcionamento, modelo de escala).
  * *(TDD)* Testes e implementação de `DemandSlot` (matriz de dia/hora, classificação de ociosidade e cálculo de volume relativo).
* **1.3 Agregação: Equipe & Polivalência (`Employee`)**
  * *(TDD)* Testes e implementação de `Employee` (função principal, matriz de competências secundárias, preferências de folga).
  * *(TDD)* Regras de validação de elegibilidade para cobertura de praça por polivalência.
* **1.4 Agregação: Escala & Turnos (`Roster` & `Shift`)**
  * *(TDD)* Testes e implementação de `Roster` e `Shift`.
  * *(TDD)* Regras de integridade de jornada CLT no domínio:
    * Intervalo interjornada mínimo de 11 horas consecutivas.
    * Garantia estrita de 2 folgas por ciclo semanal (5x2).
    * Revezamento dominical periódico.
* **1.5 Contratos de Repositórios Específicos**
  * Criação das interfaces no projeto `Domain`: `ICompanyRepository`, `IEmployeeRepository`, `IRosterRepository`, `IDemandSlotRepository`.

---

## Fase 2: Infraestrutura de Persistência & Autenticação (Semanas 2-3)
**Objetivo:** Implementar persistência no SQL Server com multi-tenancy automático e autenticação ASP.NET Core Identity.

### Subfases:
* **2.1 DbContext & Global Query Filters**
  * Configuração do `AppDbContext` no projeto `Infrastructure.Data`.
  * Aplicação automática de `HasQueryFilter` para toda entidade que herda `ITenantEntity`.
  * Interceptação de `SaveChangesAsync` para atribuição automática do `TenantId` via `ICurrentUserService`.
* **2.2 Mapeamento EF Core & Migrations Iniciais**
  * Fluent API Configurations para todas as tabelas (sem Data Annotations poluindo o domínio).
  * Criação da migration inicial no SQL Server.
* **2.3 Implementação dos Repositórios Específicos**
  * Implementação concreta de `CompanyRepository`, `EmployeeRepository`, `RosterRepository` e `DemandSlotRepository` utilizando EF Core otimizado (Split Queries, NoTracking onde aplicável).
* **2.4 Setup do ASP.NET Core Identity**
  * Configuração das classes `ApplicationUser` e `ApplicationRole`.
  * Configuração dos serviços e cookies de sessão integrados ao contexto do Blazor Server.

---

## Fase 3: Casos de Uso & Motor de Otimização 5x2 (Semanas 3-4)
**Objetivo:** Desenvolver os serviços de aplicação, orquestração de regras e o algoritmo heurístico de geração de escala.

### Subfases:
* **3.1 Importação & Diagnóstico de Demanda**
  * *(TDD)* `ParseSalesHistoryUseCase`: Importação e parsing de CSV/XLSX de PDV ou entrada manual da matriz.
  * *(TDD)* `GenerateDemandDiagnosisUseCase`: Mapeamento de picos, vales e ociosidade da casa.
* **3.2 Simulador de Viabilidade Financeira**
  * *(TDD)* `SimulateFinancialImpactUseCase`: Comparativo matemático do cenário 6x1 (horas extras, turnover, passivo trabalhista) vs. 5x2 otimizado.
* **3.3 Motor Gerador de Escalas 5x2 (Core Algorithm)**
  * *(TDD)* Algoritmo de alocação com pesos e restrições:
    * *Duras (Hard Constraints):* Regras legais CLT (11h interjornada, 2 folgas semanais, domingos).
    * *Médias (Soft Constraints):* Alocação de folgas nos vales de demanda (segunda/terça) e 100% de cobertura nos picos.
    * *Leves (Flexibility):* Uso da matriz de apoio (polivalência) para fechar postos em dias médios.
  * Validador de conformidade e integrador de alertas de inconsistência.
* **3.4 Gestão de Trocas de Turno**
  * *(TDD)* `RequestShiftSwapUseCase`: Solicitação de troca entre colaboradores.
  * *(TDD)* `ApproveShiftSwapUseCase`: Validação de regras CLT antes de aprovar a troca.

---

## Fase 4: Design System & Tokens no Blazor Server (Semanas 4-5)
**Objetivo:** Construir a base visual do Blazor com arquitetura atômica e tokens CSS puros.

### Subfases:
* **4.1 Definição do `tokens.css`**
  * Cores (Primary, Neutral, Status/Alertas de conformidade CLT).
  * Escala de espaçamento, tipografia, bordas e sombras.
* **4.2 Componentes Atômicos Básicos**
  * `AppButton` (Primary, Secondary, Ghost, Danger).
  * `AppCard` (com suporte a elevação e border-token).
  * `AppInputText`, `AppInputSelect`, `AppBadge`.
  * `AppModal` e `AppToast` para alertas de feedback.
* **4.3 Componentes Complexos do Domínio**
  * `DemandHeatmap`: Matriz visual de calor para picos e vales de clientes.
  * `FinancialComparisonCard`: Exibição visual de economia 6x1 vs. 5x2.
  * `RosterGrid`: Grade semanal interativa com indicadores de cobertura e alertas de infração CLT.

---

## Fase 5: Interface do Usuário (Telas do SaaS) (Semanas 5-6)
**Objetivo:** Conectar a camada de aplicação às páginas interativas do Blazor Server.

### Subfases:
* **5.1 Tela 1: Onboarding & Configurações da Casa**
  * Cadastro de tipo de operação, janelas de funcionamento e modelo de folga pretendido (fechamento fixo vs. folga rotativa).
* **5.2 Tela 2: Importação e Mapa de Demanda**
  * Upload de arquivo de vendas com drag-and-drop e fallback para preenchimento manual da matriz em 3 minutos.
  * Exibição reativa do mapa de calor.
* **5.3 Tela 3: Simulador de Impacto Financeiro**
  * Inputs salariais e de horas extras.
  * Apresentação da economia projetada com CTA para geração imediata da escala.
* **5.4 Tela 4: Gestão da Equipe & Polivalência**
  * Grid rápido para inclusão de funcionários, funções primárias, tags de polivalência e restrições individuais.
* **5.5 Tela 5: Painel de Escala Interativo**
  * Visualização da escala gerada com suporte a ajustes manuais.
  * Barra de status de cobertura de pico por turno.
  * Botão de exportação e geração de link para compartilhamento.

---

## Fase 6: Visualização Mobile do Colaborador & Trocas (Semanas 6-7)
**Objetivo:** Entregar a experiência simplificada para a equipe operacional via link seguro/PWA.

### Subfases:
* **6.1 Tela Pública Segura (Token Link)**
  * Acesso mobile ultraleve via token criptografado (sem necessidade de login complexo para a equipe de base).
  * Exibição clara dos turnos da semana e dos dias exatos de folga.
* **6.2 Fluxo de Solicitação de Troca de Turno**
  * Seleção de par compatível (mesma função ou compatível via polivalência).
  * Envio de solicitação para o colega e posterior notificação ao gestor.
* **6.3 Aprovação pelo Gestor em 1 Clique**
  * Notificação na interface do gestor para autorizar a troca com verificação automática de conformidade CLT.

---

## Fase 7: Qualidade, Testes E2E, Carga & Lançamento (Semanas 7-8)
**Objetivo:** Homologação final, validação de segurança e publicação do MVP.

### Subfases:
* **7.1 Testes de Integração & Isolamento de Tenant**
  * Testes com WebApplicationFactory garantindo que nenhum tenant enxerga dados de outro em nenhuma query ou mutação.
* **7.2 Testes de Stress do Motor de Escala**
  * Benchmark com operações de 10 a 50 funcionários sob cenários complexos de restrição de horários.
* **7.3 Telemetria & Monitoramento**
  * Configuração de logs estruturados com OpenTelemetry e Serilog.
  * Rastreamento de falhas e erros de validação retornados pelo `Result<T>`.
* **7.4 Deploy & Integração com o Ebook (GTM)**
  * Publicação da infraestrutura em ambiente de produção (App Service + Azure SQL ou similar).
  * Criação dos links de conversão e QR codes integrados ao Ebook educativo.

---

## Matriz de Rastreabilidade: Fases vs. Regras do `agents.md`

| Item do `agents.md` | Onde é Aplicado no Roadmap | Critério de Aceite |
| :--- | :--- | :--- |
| **TDD Estrito** | Fases 1, 3 e 7 | Nenhum código de domínio ou caso de uso sem teste falhando prévio (`Red`). |
| **ADRs Vivos** | Fases 0, 1 e 2 | Decisões estruturantes registradas em `docs/adrs/` antes da codificação. |
| **Padrão `Result<T>`** | Fases 1 e 3 | Zero exceções de negócio lançadas; todas as operações retornam `Result<T>`. |
| **Multi-tenancy Lógico** | Fases 1, 2 e 7 | Global Query Filters em 100% das entidades `ITenantEntity`. |
| **Repositórios Específicos** | Fases 1 e 2 | Zero repositórios genéricos; interfaces especializadas no projeto `Domain`. |
| **Tokens CSS no Blazor** | Fases 4, 5 e 6 | Zero estilos inline ou cores fora das propriedades declaradas em `tokens.css`. |