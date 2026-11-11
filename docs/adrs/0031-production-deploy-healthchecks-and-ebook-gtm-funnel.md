# 31. Infraestrutura de Produção, Persistência de Chaves, Health Checks e Funil GTM do Ebook na Subfase 7.4

## Status
Aceito

## Contexto
Para concluir a Subfase 7.4 (Deploy & Integração com o Ebook GTM), a aplicação precisa:
1. Expor endpoints de saúde (`/health/live` e `/health/ready`) verificando o `AppDbContext`/SQL Server para probes do Azure App Service / containers.
2. Persistir chaves de `ASP.NET Core DataProtection` entre reinícios e réplicas para que cookies de sessão e tokens de compartilhamento (`/shared/roster?token=...`) não sejam invalidados.
3. Oferecer infraestrutura reproduzível (`Dockerfile`, `docker-compose.yml`, `infra/main.bicep`, `.github/workflows/deploy.yml`) sem credenciais hardcoded (`agents.md` regra 1.8).
4. Integrar links de conversão UTM e QR Codes SVG ao Ebook educativo, com landing pública (`/ebook`) e autocadastro de novo tenant (`/Account/Register`) atribuindo `ApplicationRoles.Owner` e redirecionando para `/onboarding`.

## Decisão
1. **Health Checks, Proxy Reverso e DataProtection:**
   - Adicionar `AspNetCore.HealthChecks.SqlServer` (`9.0.0`) e `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (`10.0.12`) em `WebApp.Blazor`, mapeando `/health/live` e `/health/ready` em `HealthCheckExtensions` com `AllowAnonymous()`.
   - Configurar `ForwardedHeadersOptions` e persistência opcional de chaves `DataProtection` via `DATA_PROTECTION_KEYS_PATH` (definida via `.env`).
2. **Containerização e IaC Azure:**
   - Criar `Dockerfile` multi-stage (.NET 10, usuário não-root), `.dockerignore`, `docker-compose.yml`, `infra/main.bicep` (Azure App Service Linux + Azure SQL) e `.github/workflows/deploy.yml`, com segredos injetados apenas em runtime/deploy.
3. **Funil GTM do Ebook & Gerador Nativo de QR Code SVG:**
   - Criar `GtmConversionLinkService` e `QrCodeSvgGenerator` em `Application` (implementação pura em C# para QR Code Byte mode com correção de erro Reed-Solomon, zero dependências JS/UI externas) e `RegisterTenantOwnerUseCase` + `ITenantRegistrationService` (implementado em `Infrastructure.Data.Identity`).
   - Criar o componente atômico `AppQrCode` em `Components/DesignSystem/AppQrCode/`, a landing pública `/ebook` (`EbookLanding.razor`) e o autocadastro `/Account/Register` (`Register.razor`) estilizados exclusivamente com `tokens.css`, além do catálogo de links e QR Codes em `docs/gtm/ebook-links-and-qrcodes.md`.

## Consequências
- **Benefícios:**
  - Links de conversão do Ebook fecham o funil completo (Ebook/QR -> `/ebook` com UTM -> `/Account/Register` -> `/onboarding`), com rastreamento de conversão no OpenTelemetry/Serilog.
  - Deploy seguro e repetível mantendo conformidade total com `tokens.css`, Clean Architecture e `.env`.
- **Custos:**
  - Em produção com múltiplas instâncias no App Service, `DATA_PROTECTION_KEYS_PATH` deve apontar para um volume compartilhado persistente (Azure Files mount).

