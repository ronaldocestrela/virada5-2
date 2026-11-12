# 34. Estratégia de Migrations em Produção, Hardening de Container e Kit GTM do Ebook

## Status
Aceito

## Contexto
O [ADR 0031](0031-production-deploy-healthchecks-and-ebook-gtm-funnel.md) estruturou os endpoints `/health/live` e `/health/ready`, a containerização inicial e o funil `/ebook` -> `/Account/Register` -> `/onboarding`. Contudo, uma auditoria para o fechamento da Subfase 7.4 identificou lacunas operacionais para publicação em produção e operação do funil GTM:
1. O banco provisionado por `infra/main.bicep` (Azure SQL) e `docker-compose.yml` (SQL Server local) inicia vazio porque nem o pipeline de deploy nem o runtime executam as migrations do `AppDbContext`.
2. O `HEALTHCHECK` do `Dockerfile` invocava `curl`, ausente por padrão na imagem `mcr.microsoft.com/dotnet/aspnet:10.0`, e o diretório de chaves `DataProtection` requer permissão explícita para o usuário não-root (`$APP_UID`).
3. O endpoint `/health/ready` não sinalizava migrations pendentes nem validava permissão de escrita em `DATA_PROTECTION_KEYS_PATH`, e o pacote `AspNetCore.HealthChecks.SqlServer` permanecia sem uso em `WebApp.Blazor.csproj`.
4. A telemetria de conversão (`ApplicationTelemetry.RecordConversion` / `IResultTelemetry.RecordConversionEvent`) descartava `utm_content`, impedindo medir a performance por capítulo/QR Code do Ebook, e os 5 links canônicos existiam apenas em Markdown sem geração programática via `PUBLIC_APP_BASE_URL`.

## Decisão
1. **Estratégia Dual e Segura de EF Core Migrations:**
   - Em deploy Azure App Service (`.github/workflows/deploy.yml`), gerar e executar um **EF Core Migrations Bundle** (`efbundle`) contra o banco de destino antes de liberar o tráfego, evitando race conditions entre múltiplas instâncias.
   - Para execução em container único (`docker-compose.yml`), suportar execução automática de `Database.MigrateAsync()` no startup **somente** quando `RUN_MIGRATIONS_ON_STARTUP=true` (via `.env`), mantendo `false` como padrão seguro.
2. **Hardening de Container, IaC e Health Checks:**
   - Instalar `curl` no estágio runtime do `Dockerfile` (limpando listas APT) e pré-criar `/var/virada5x2/dataprotection-keys` com propriedade de `$APP_UID`.
   - Remover a referência ociosa `AspNetCore.HealthChecks.SqlServer` e evoluir `HealthCheckExtensions` para validar conectividade + ausência de migrations pendentes no `AppDbContext` (em providers relacionais com migrations) e escrita em `DATA_PROTECTION_KEYS_PATH` quando configurado.
   - Injetar `PUBLIC_APP_BASE_URL` no `infra/main.bicep` e `docker-compose.yml`, além de validar `bicep build` e `docker build` no CI (`ci.yml`) e smoke tests pós-deploy no `deploy.yml`.
3. **Atribuição Completa de `utm_content` e Kit GTM Programático do Ebook:**
   - Estender `ApplicationTelemetry.RecordConversion` e `IResultTelemetry.RecordConversionEvent` com `utmContent` opcional, mantendo retrocompatibilidade e uniformizando o default de `utm_medium` em `EbookLanding.razor`.
   - Criar `GetEbookGtmKitUseCase` (com `IResultTelemetry`) expondo o catálogo canônico dos 5 pontos de conversão do Ebook e seus QR Codes SVG a partir de `PUBLIC_APP_BASE_URL`, além da página autenticada `/gtm/ebook-kit` (`EbookGtmKit.razor`) estilizada estritamente via `tokens.css` para visualização e download dos SVGs.

## Consequências
- **Benefícios:**
  - Bancos novos em produção e containers locais sobem com schema atualizado de forma determinística e sem risco de concorrência em múltiplas réplicas.
  - Readiness probes refletem exatamente a saúde real da aplicação (banco migrado + armazenamento de DataProtection gravável).
  - Equipe de produto/GTM passa a medir conversões até o nível de `utm_content` (capítulo/QR Code) e exportar os 5 QR Codes SVG diretamente com a URL pública configurada em `.env`.
- **Custos:**
  - Pequeno aumento no tamanho da imagem Docker pela presença do `curl` para o `HEALTHCHECK` e manutenção do passo de `dotnet ef migrations bundle` no workflow de deploy.

