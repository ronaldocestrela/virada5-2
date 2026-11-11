# 11. Adotar Mapeamento Fluent API SQL Server e Migration Inicial com Design-Time Factory

## Status
Aceito

## Contexto
Na subfase 2.2 do roadmap (`Mapeamento EF Core & Migrations Iniciais`), o `AppDbContext` criado na subfase 2.1 (ADR 0010) precisa evoluir de configurações estruturais mínimas para um mapeamento relacional completo no **Microsoft SQL Server**, mantendo o projeto `Domain` livre de *Data Annotations* e gerando a migration inicial (`InitialCreate`).

Além disso, como o `AppDbContext` exige `ICurrentUserService` no construtor e ainda não há registro de DI na camada de apresentação até as subfases posteriores, o EF Core CLI (`dotnet ef migrations add` / `dotnet ef migrations script`) precisa de uma fábrica de design-time (`IDesignTimeDbContextFactory<AppDbContext>`) que respeite integralmente a regra de segurança de não armazenar credenciais ou connection strings no código-fonte (`.env` / variáveis de ambiente).

## Decisão
1. **Mapeamento Exclusivo via Fluent API (`IEntityTypeConfiguration<T>`):**
   - Todas as 5 entidades persistidas (`Company`, `DemandSlot`, `Employee`, `Roster`, `Shift`) são configuradas em `src/Infrastructure/Data/Context/Configurations/`, com nomes explícitos de tabelas no plural (`Companies`, `DemandSlots`, `Employees`, `Rosters`, `Shifts`) e chaves primárias `Guid` com `ValueGeneratedNever()` (geradas no domínio via `BaseEntity`).
   - O projeto `Domain` permanece proibido de referenciar ou utilizar atributos de `System.ComponentModel.DataAnnotations` / `System.ComponentModel.DataAnnotations.Schema`.
2. **Convenções de Tipos e Precisão no SQL Server:**
   - **Enums como `string` (`nvarchar`):** Todos os enums escalares (`BusinessType`, `ScheduleModel`, `DayOfWeek` em `DemandSlot`, `DemandClassification`, `StationRole`, `RosterStatus`, `CoverageOrigin`) são persistidos via `HasConversion<string>()` com `HasMaxLength` explícito (entre `20` e `50`), favorecendo legibilidade no banco e imunidade à reordenação numérica.
   - **Precisão Decimal Explícita:** Em `DemandSlot`, `ExpectedVolume` usa `HasPrecision(18, 2)` e `RelativeVolume` usa `HasPrecision(5, 4)` para preservar as 4 casas decimais calculadas em `EvaluateRelativeDemand`.
   - **Datas, Horários e Coleções JSON:** `DateOnly` e `TimeOnly` mapeados nativamente; `SundayRotationPolicy` persistido via `ValueConverter` inteiro; coleções encapsuladas (`ClosedDays`, `SecondarySkills`, `PreferredDaysOff`, `UnavailableDays`) persistidas em JSON via `CollectionMappingHelper` com `IsRequired()` e `HasMaxLength` explícito.
3. **Integridade Referencial sem Múltiplos Caminhos de Cascata:**
   - Relacionamentos entre raízes de agregação distintas (`DemandSlot -> Company`, `Employee -> Company`, `Roster -> Company`, `Shift -> Employee`) usam `DeleteBehavior.Restrict`, evitando o erro `1785 (multiple cascade paths)` do SQL Server e preservando fronteiras de agregação DDD.
   - O relacionamento intra-agregação `Roster -> Shifts` mantém `DeleteBehavior.Cascade`.
4. **Índices Compostos Multi-Tenant:**
   - Todo índice operacional tem `TenantId` como coluna líder para alinhar-se aos Global Query Filters:
     - `IX_Companies_TenantId` (único, relação 1:1 Tenant–Estabelecimento no MVP).
     - `IX_DemandSlots_TenantId_CompanyId_DayOfWeek_Hour` (único, garantindo integridade da matriz semanal).
     - `IX_Employees_TenantId_CompanyId_IsActive`.
     - `IX_Rosters_TenantId_CompanyId_WeekStartDate` (não-único, permitindo recriação de escala semanal após arquivamento de versão anterior).
     - `IX_Shifts_TenantId_RosterId_EmployeeId` e `IX_Shifts_TenantId_EmployeeId_Date`.
5. **Suporte Design-Time para Migrations (`EF Core Design` & `AppDbContextFactory`):**
   - Adicionar `Microsoft.EntityFrameworkCore.Design` (`10.0.14`, `PrivateAssets="all"`) em `Infrastructure.Data`.
   - Implementar `AppDbContextFactory` (`IDesignTimeDbContextFactory<AppDbContext>`) lendo `DB_CONNECTION_STRING` do ambiente ou de `.env` local (se presente) e utilizando `.UseSqlServer()` sem credenciais hardcoded quando executado offline para geração de migrations/scripts.

## Consequências
- **Benefícios:**
  - Schema SQL Server previsível, auditável e validado automaticamente contra *multiple cascade paths*, truncamento de `decimal` e regressões arquiteturais.
  - Geração de migrations e scripts DDL idempotentes 100% reproduzível em CI ou localmente sem exigir servidor SQL ativo nem expor segredos.
- **Custos:**
  - Alterações futuras em entidades ou Value Objects exigem atualização das respectivas `IEntityTypeConfiguration<T>` e geração de novas migrations incrementais.

