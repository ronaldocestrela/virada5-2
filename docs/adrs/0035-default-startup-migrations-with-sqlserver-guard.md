# 35. Migrations Automáticas no Arranque por Padrão com Guarda de SQL Server

## Status
Aceito

## Contexto
O [ADR 0034](0034-production-migrations-container-hardening-and-ebook-kit.md) introduziu a variável `RUN_MIGRATIONS_ON_STARTUP` mantendo `"false"` como padrão em `.env.example`, o que gerou dois atritos operacionais:
1. Ao copiar `.env.example` para `.env`, o Docker Compose interpolava `${RUN_MIGRATIONS_ON_STARTUP:-true}` como `"false"`, fazendo a aplicação e o container iniciarem sem aplicar as migrations pendentes.
2. Em execução local (`dotnet run`), a aplicação também não aplicava as migrations no arranque a menos que o desenvolvedor sobrescrevesse manualmente a variável para `"true"`.
3. O bloco inline em `Program.cs` verificava `Database.IsRelational()` sem filtrar SQL Server e não possuía logging estruturado nem retry para falhas transitórias durante o startup do banco.

## Decisão
1. **Habilitado por Padrão (Opt-Out via `"false"`):**
   - `DatabaseMigrationExtensions.ApplyDatabaseMigrationsOnStartupAsync` aplica migrations pendentes no arranque da aplicação sempre que `RUN_MIGRATIONS_ON_STARTUP` não for explicitamente `"false"` (atualizando também `.env.example` para `"true"`).
   - O opt-out (`RUN_MIGRATIONS_ON_STARTUP="false"`) permanece preservado em `infra/main.bicep` para ambientes Azure App Service com múltiplas réplicas que executam `efbundle` antes do deploy.
2. **Guarda Explícita de Provider (`IsSqlServer()`):**
   - Como as migrations em `src/Infrastructure/Data/Migrations/` utilizam tipos específicos do SQL Server, a execução automática no startup é restrita a `dbContext.Database.IsSqlServer()`, mantendo os testes de integração em SQLite (`TenantIsolationWebApplicationFactory`) isolados e funcionais.
3. **Observabilidade e Resiliência no Startup:**
   - Registrar via `ILogger` as migrations pendentes detectadas, aplicar `MigrateAsync()` com retentativas curtas para erros transitórios de conexão na subida do banco e falhar rápido (`fail-fast`) caso todas as tentativas falhem.

## Consequências
- **Benefícios:**
  - `dotnet run` e `docker compose up` sobem com o schema do banco atualizado automaticamente sem passos manuais extras.
  - Deploy em múltiplas réplicas via `efbundle` continua protegido pelo opt-out explícito `RUN_MIGRATIONS_ON_STARTUP="false"`.
- **Custos:**
  - Em cenários com banco propositalmente gerenciado fora da aplicação em desenvolvimento, é necessário definir `RUN_MIGRATIONS_ON_STARTUP="false"` no `.env`.

