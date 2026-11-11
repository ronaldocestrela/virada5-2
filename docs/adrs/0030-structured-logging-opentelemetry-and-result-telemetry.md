# 30. Telemetria Estruturada com Serilog, OpenTelemetry e Rastreamento de `Result<T>` na Subfase 7.3

## Status
Aceito

## Contexto
A Subfase 7.3 exige logs estruturados com **Serilog**, instrumentação **OpenTelemetry** e rastreamento sistemático das falhas e erros de validação retornados pelo padrão `Result` / `Result<T>`. Entretanto, o `LayerDependencyTests` e o `agents.md` proíbem que `Domain` e `Application` dependam de `Infrastructure`, `Presentation`, `Microsoft.AspNetCore` ou pacotes de logging/telemetria externos.

## Decisão
1. **Camada `Application` livre de dependências externas:**
   - Expor `IResultTelemetry` em `Application/Common/Interfaces/IResultTelemetry.cs` e instrumentar métricas/traces na BCL pura (`System.Diagnostics.ActivitySource` `"Virada5x2.Application"` e `System.Diagnostics.Metrics.Meter` `"Virada5x2.Application"`) via `ApplicationTelemetry` e `ResultTelemetryExtensions`.
   - Instrumentar os casos de uso críticos (`GenerateRoster5x2UseCase`, `ApproveShiftSwapUseCase`, `RequestSharedShiftSwapUseCase`, `SaveCompanySettingsUseCase`, `ParseSalesHistoryUseCase`, `SimulateFinancialImpactUseCase`, `RegisterTenantOwnerUseCase`) aceitando `IResultTelemetry?` opcional (preservando compatibilidade com testes unitários existentes).
2. **Camada `Presentation` (`WebApp.Blazor`) com Serilog e OpenTelemetry:**
   - Adicionar os pacotes `Serilog.AspNetCore` (`10.0.0`), `Serilog.Formatting.Compact` (`3.0.0`), `Serilog.Sinks.Console` (`6.1.1`), `Serilog.Sinks.File` (`7.0.0`), `OpenTelemetry.Extensions.Hosting` (`1.15.3`), `OpenTelemetry.Exporter.OpenTelemetryProtocol` (`1.15.3`), `OpenTelemetry.Instrumentation.AspNetCore` (`1.15.2`), `OpenTelemetry.Instrumentation.Http` (`1.15.1`) e `OpenTelemetry.Instrumentation.Runtime` (`1.14.0`).
   - Implementar `StructuredResultTelemetry` e `ObservabilityServiceCollectionExtensions`, configurando logs JSON compactos em produção, enriquecimento de `TenantId`/`UserId`/`TraceId`, `UseSerilogRequestLogging()` e exportador OTLP ativado exclusivamente via variáveis `.env` (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`, `SERILOG_MINIMUM_LEVEL`, `SERILOG_LOG_FILE_PATH`).

## Consequências
- **Benefícios:**
  - Visibilidade completa de erros de validação/conflito (`Error.Code`, `UseCase`, `TenantId`, latência) sem quebrar o padrão `Result<T>` nem acoplar o núcleo da solução a frameworks de telemetria.
  - Zero segredos ou endpoints de telemetria hardcoded no código ou `appsettings.json`.
- **Custos:**
  - Aumento modesto no grafo de dependências de `WebApp.Blazor`, mantido estritamente na borda de apresentação.

