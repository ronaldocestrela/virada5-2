# 9. Validar Fronteiras Arquiteturais com NetArchTest.Rules

## Status
Aceito

## Contexto
A regra inegociável 1.3 do `agents.md` e a Subfase 0.3 do roadmap exigem garantia automatizada de que a Clean Architecture seja respeitada em toda a solução (`Domain` isolado de camadas externas; `Application` dependendo apenas de `Domain`; `Infrastructure.Data` sem referência à camada de apresentação) e de que repositórios genéricos permaneçam proibidos. Embora verificações pontuais via `System.Reflection` já existam em `Architecture.Tests`, a evolução dos projetos `Infrastructure.Data` e `WebApp.Blazor` exige uma DSL declarativa e padronizada para regras arquiteturais.

## Decisão
1. Adotar o pacote NuGet `NetArchTest.Rules` (`1.3.2`) no projeto de testes `tests/Architecture.Tests/Architecture.Tests.csproj`.
2. Expressar as regras de dependência entre camadas e restrições de nomenclatura/herança com a API fluente `Types.InAssembly(...)` em `LayerDependencyTests`, complementada por invariantes de reflexão específicas do domínio (um repositório por `AggregateRoot` e ausência de parâmetro manual `tenantId`).
3. Executar a suíte `Architecture.Tests` em toda execução de `dotnet test` local e no pipeline de CI (`.github/workflows/ci.yml`), bloqueando builds que violem os limites arquiteturais.

## Consequências
- **Benefícios:**
  - Detecção imediata de violações de dependência entre `Domain`, `Application`, `Infrastructure.Data` e `WebApp.Blazor` ainda em tempo de teste unitário.
  - Regras arquiteturais legíveis e fáceis de expandir conforme novas camadas ou casos de uso sejam adicionados.
- **Custos:**
  - Adição da dependência `NetArchTest.Rules` exclusivamente no projeto de testes `Architecture.Tests` (sem impacto nos assemblies de produção).

