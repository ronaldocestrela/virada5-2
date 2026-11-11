# 12. Adotar Estratégias de Tracking, Split Queries e Filtro de Arquivamento nos Repositórios EF Core

## Status
Aceito

## Contexto
Na subfase 2.3 do roadmap (`Implementação dos Repositórios Específicos`), é necessário implementar os quatro contratos definidos no projeto `Domain` (`ICompanyRepository`, `IDemandSlotRepository`, `IEmployeeRepository` e `IRosterRepository`) na camada `Infrastructure.Data`, consumindo o `AppDbContext` configurado nas subfases 2.1 e 2.2 (ADRs 0010 e 0011).

Quatro aspectos técnicos precisam ser padronizados nas implementações para evitar bugs sutis de persistência, degradação de consulta e vazamento entre estados:
1. **Tracking vs. `AsNoTracking()`:** Consultas que carregam uma agregação para mutação no domínio precisam do *Change Tracker* ativo para que alterações em propriedades e coleções filhas sejam detectadas em `IUnitOfWork.SaveChangesAsync()`, enquanto consultas de leitura em lote podem dispensar *snapshots* de rastreamento.
2. **Explosão cartesiana em `Roster -> Shifts`:** O carregamento conjunto de `Roster` e seus `Shifts` via `JOIN` único duplica colunas do cabeçalho da escala por cada turno.
3. **Chaves `ValueGeneratedNever()` no método `Update`:** Como `BaseEntity` gera `Guid` no construtor e o mapeamento Fluent API declara `ValueGeneratedNever()`, invocar `DbSet.Update(entity)` sobre uma agregação já rastreada faria o EF Core classificar novos `Shift`s recém-adicionados como `EntityState.Modified`, provocando `DbUpdateConcurrencyException`.
4. **Escalas arquivadas (`RosterStatus.Archived`):** Conforme a ADR 0011, o índice `IX_Rosters_TenantId_CompanyId_WeekStartDate` é não-único para permitir recriação de uma escala semanal após arquivamento da versão anterior.

## Decisão
1. **Repositórios Concretos Isolados (Zero Classe Base Genérica):** Implementar `CompanyRepository`, `DemandSlotRepository`, `EmployeeRepository` e `RosterRepository` como classes `public sealed` independentes em `src/Infrastructure/Data/Repositories/`, sem herança de classe base genérica e sem uso de `.IgnoreQueryFilters()`.
2. **Política de Rastreamento por Tipo de Consulta:**
   - **Rastreadas (Tracked):** `GetByIdAsync`, `GetCurrentAsync`, `GetByCompanyDayAndHourAsync`, `GetByIdWithShiftsAsync` e `GetByCompanyAndWeekStartAsync`.
   - **Somente Leitura (`AsNoTracking()`):** `DemandSlotRepository.GetByCompanyAsync`, `EmployeeRepository.GetByCompanyAsync`, `EmployeeRepository.GetActiveByCompanyAsync`, `EmployeeRepository.GetByIdsAsync` e `RosterRepository.GetByWeekRangeAsync`.
3. **Split Queries para `Roster` com `Shifts`:** Todas as consultas de `RosterRepository` que incluem `Shifts` (`GetByIdWithShiftsAsync`, `GetByCompanyAndWeekStartAsync` e `GetByWeekRangeAsync`) utilizam `.AsSplitQuery()`.
4. **Guardião de `EntityState.Detached` em `Update`:** Em todos os repositórios, `Update(entity)` verifica `_dbContext.Entry(entity).State == EntityState.Detached` antes de chamar `DbSet.Update(entity)`, preservando a detecção automática de `Added`/`Modified`/`Deleted` em agregações já rastreadas.
5. **Exclusão de Escalas Arquivadas nas Consultas Semanais:** `RosterRepository.GetByCompanyAndWeekStartAsync` e `GetByWeekRangeAsync` filtram `roster.Status != RosterStatus.Archived`, enquanto `GetByIdWithShiftsAsync` continua localizando qualquer escala por ID (inclusive arquivadas) para consulta e auditoria.
6. **Ordenação Enum-Safe em `DemandSlotRepository.GetByCompanyAsync`:** Como `DayOfWeek` é persistido como `string` no SQL Server (ADR 0011), a ordenação por `DayOfWeek` (`Sunday`..`Saturday`) e `Hour` (`0`..`23`) é aplicada em memória após a materialização dos slots do estabelecimento (máximo de 168 registros), evitando ordenação alfabética dos nomes dos dias.

## Consequências
- **Benefícios:**
  - Mutação segura de agregações rastreadas (incluindo inserção e troca de `Shift`s em `Roster`) sem erros de concorrência por chave pré-gerada.
  - Menor consumo de memória e ausência de explosão cartesiana nas listagens e consultas históricas de escala.
  - Compatibilidade total entre recriação de escalas semanais arquivadas e validação de fronteiras semanais/dominicais.
- **Custos:**
  - Casos de uso que obtiverem entidades via métodos `AsNoTracking()` (como `DemandSlotRepository.GetByCompanyAsync`) e desejarem persistir alterações sobre esses objetos deverão invocar explicitamente `Update(slot)` antes de `SaveChangesAsync()`.

