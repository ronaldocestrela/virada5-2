# 18. Adotar bUnit para Testes TDD de Componentes Blazor

## Status
Aceito

## Contexto
A regra inegociável 1.1 do `agents.md` determina ciclo estrito de TDD (Red-Green-Refactor), e a Fase 4 do roadmap exige a construção do Design System atômico e dos componentes complexos do domínio (`DemandHeatmap`, `FinancialComparisonCard`, `RosterGrid`) em `WebApp.Blazor`. Testar componentes Razor apenas via `WebApplicationFactory` (testes de ponta a ponta HTTP) não exercita renderização isolada, dois-way binding (`@bind-Value`), `EventCallback`s, acessibilidade (`aria-*`) e interações de teclado sem subir um servidor completo.

## Decisão
1. Adotar o pacote NuGet **`bunit` (`2.11.3`)** exclusivamente no projeto de testes `tests/WebApp.Blazor.UnitTests/WebApp.Blazor.UnitTests.csproj` (suporte nativo a `net10.0`).
2. Todo componente criado em `src/Presentation/WebApp.Blazor/Components/DesignSystem/` deve ser precedido por uma suíte unitária em `tests/WebApp.Blazor.UnitTests/DesignSystem/` escrita em ciclo `Red -> Green -> Refactor`.
3. Não adicionar dependências de teste aos projetos de produção (`Domain`, `Application`, `Infrastructure.Data`, `WebApp.Blazor`).

## Consequências
- **Benefícios:**
  - Feedback em milissegundos na validação de markup, atributos de acessibilidade, parâmetros `[Parameter]` e callbacks `EventCallback` antes da Fase 5.
  - Preservação do pipeline de CI leve sem necessidade de navegador headless para testes unitários de UI.
- **Custos:**
  - Inclusão de dependência de teste `bunit` no projeto `WebApp.Blazor.UnitTests` (sem impacto em produção).

