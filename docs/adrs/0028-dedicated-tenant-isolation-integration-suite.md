# 28. Suíte Dedicada de Testes de Integração e Isolamento Multi-Tenant na Subfase 7.1

## Status
Aceito

## Contexto
Embora a suíte `WebApp.Blazor.UnitTests` já contenha testes pontuais com `WebApplicationFactory<Program>` para fluxos de identidade, exportação de escala e compartilhamento público, a Subfase 7.1 do roadmap e a regra inegociável 1.4 do `agents.md` exigem garantia sistemática de que nenhum tenant consiga consultar, listar, exportar, alterar ou excluir dados pertencentes a outro tenant em **nenhuma** agregação (`Company`, `DemandSlot`, `Employee`, `Roster`, `Shift`, `ShiftSwapRequest`) ou contexto anônimo (`TenantId == Guid.Empty`).

## Decisão
1. Criar o projeto dedicado `tests/WebApp.IntegrationTests/WebApp.IntegrationTests.csproj` e integrá-lo à solução `Scale5x2Migration.sln` e ao workflow de CI (`.github/workflows/ci.yml`).
2. Implementar `TenantIsolationWebApplicationFactory` baseado em `WebApplicationFactory<Program>`, executando por padrão sobre SQLite em memória isolado por instância de teste e permitindo execução opcional contra SQL Server real quando `INTEGRATION_TEST_SQLSERVER_CONNECTION` estiver configurada no ambiente.
3. Implementar `CrossTenantReadAndMutationIsolationTests` cobrindo, de ponta a ponta (HTTP endpoints, Blazor routes, casos de uso `Application` e `AppDbContext`), tentativas cruzadas entre Tenant A e Tenant B e tentativas fail-closed com `TenantId == Guid.Empty` em todas as entidades `ITenantEntity`.

## Consequências
- **Benefícios:**
  - Regressões nos Global Query Filters, no `SaveChangesAsync` fail-closed ou em novos endpoints/casos de uso são bloqueadas automaticamente no CI antes de qualquer merge.
  - Separação clara entre testes de componentes/páginas (`WebApp.Blazor.UnitTests`) e testes de segurança multi-tenant ponta a ponta (`WebApp.IntegrationTests`).
- **Custos:**
  - Tempo adicional de execução de build/testes no CI (~3–5 segundos por execução completa da suíte).

