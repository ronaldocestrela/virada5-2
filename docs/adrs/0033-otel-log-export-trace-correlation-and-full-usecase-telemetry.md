# 33. Exportação OTLP de Logs Serilog, Correlação de Traces e Telemetria Integral dos Casos de Uso

## Status
Aceito

## Contexto
O [ADR 0030](0030-structured-logging-opentelemetry-and-result-telemetry.md) estruturou a base de observabilidade com `ApplicationTelemetry`, `IResultTelemetry`, Serilog (Console/Arquivo) e OpenTelemetry (Traces/Metrics). Contudo, uma auditoria para o fechamento da Subfase 7.3 identificou lacunas operacionais:
1. Apenas 7 dos 32 casos de uso registrados em `ApplicationServiceCollectionExtensions` emitiam telemetria de execução e falhas `Result<T>`.
2. Em fluxos com resolução tardia de tenant (`RegisterTenantOwnerUseCase` e casos de uso compartilhados via token), o `TenantId` era lido antes da execução e permanecia `null` na telemetria; exceções infraestruturais imprevistas também não registravam `ActivityStatusCode.Error` nem métricas de falha antes de propagar.
3. Os logs do Serilog não carregavam `TraceId`/`SpanId` da `Activity.Current` e não eram exportados para o coletor OTLP; além disso, `OTEL_EXPORTER_OTLP_HEADERS` (declarada em `.env.example`) não era aplicada aos exportadores.

## Decisão
1. **Pacote `Serilog.Sinks.OpenTelemetry` restrito à camada `Presentation`:**
   - Adicionar `Serilog.Sinks.OpenTelemetry` exclusivamente em `src/Presentation/WebApp.Blazor/WebApp.Blazor.csproj`, mantendo `Domain` e `Application` livres de quaisquer pacotes de logging ou telemetria externos (validado por `Architecture.Tests`).
   - Quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver configurado, exportar também os logs estruturados do Serilog via OTLP, compartilhando o parser `ParseOtlpHeaders` para `OTEL_EXPORTER_OTLP_HEADERS` entre Traces, Metrics e Logs.
2. **Correlação `TraceId`/`SpanId` e Contexto HTTP/SignalR:**
   - Implementar `ActivityTraceEnricher` (`ILogEventEnricher`) para anexar `TraceId` e `SpanId` da `Activity.Current` em todo evento Serilog.
   - Enriquecer `UseViradaRequestLogging` com `UserId` e `TraceId`, além de `TenantId`, `IsAuthenticated` e `RequestHost`.
3. **Captura dinâmica de `TenantId`, tratamento de exceções e cobertura total de casos de uso:**
   - Introduzir `TelemetryExecutionContext` em `ApplicationTelemetry` com sobrecargas `Observe` / `ObserveAsync` preservando compatibilidade com assinaturas existentes.
   - Registrar `ActivityStatusCode.Error` em resultados de categoria `Failure` e capturar exceções inesperadas no `Observe`/`ObserveAsync` para telemetria antes de relançar (`throw;`).
   - Instrumentar todos os 32 casos de uso de `Application.UseCases` com `IResultTelemetry? telemetry = null` opcional e garantir essa convenção via `LayerDependencyTests`.

## Consequências
- **Benefícios:**
  - Correlação ponta a ponta (Traces, Metrics e Logs) por `TraceId` e `TenantId`, inclusive em circuitos Blazor Server e links compartilhados via token.
  - Cobertura de 100% dos casos de uso para auditoria de erros de validação/conflito retornados por `Result<T>`.
  - Regra explícita nos testes arquiteturais impedindo regressão ao criar novos casos de uso sem telemetria.
- **Custos:**
  - Adição de um pacote NuGet adicional na borda de apresentação (`WebApp.Blazor`) e pequeno acréscimo de parâmetros opcionais nos construtores de casos de uso.

