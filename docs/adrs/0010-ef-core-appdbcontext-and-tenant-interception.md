# 10. Adotar EF Core 10 com AppDbContext, Query Filter Dinâmico e Interceptação Fail-Closed de Tenant

## Status
Aceito

## Contexto
Na subfase 2.1 do roadmap (`DbContext & Global Query Filters`), é necessário implementar o `AppDbContext` na camada `Infrastructure.Data` em conformidade com o multi-tenancy lógico definido na ADR 0002 e com o isolamento de dependências da Clean Architecture validado pela ADR 0009.

Dois riscos técnicos precisam ser mitigados desde a fundação do `AppDbContext`:
1. **Cache de modelo no EF Core:** O método `OnModelCreating` é executado apenas uma vez por tipo de `DbContext` e seu `IModel` resultante é armazenado em cache. Se um filtro global capturar diretamente o valor escalar do `TenantId` da primeira requisição, todas as requisições subsequentes ficariam presas ao tenant inicial, causando vazamento crítico entre clientes.
2. **Persistência sem contexto ou cross-tenant:** Caso `SaveChangesAsync` seja chamado sem um `TenantId` válido em `ICurrentUserService`, ou receba uma entidade com `TenantId` divergente do usuário atual, a persistência deve falhar de forma segura (*fail-closed*) em vez de gravar registros órfãos ou sobrescrever silenciosamente a propriedade de outro estabelecimento.

Além disso, a camada `Application` precisa declarar os contratos `ICurrentUserService` e `IUnitOfWork` sem referenciar `Microsoft.EntityFrameworkCore`, e o `IModel` do `AppDbContext` deve suportar os Value Objects (`SundayRotationPolicy`, `SecondarySkill`) e coleções encapsuladas do domínio (`Company`, `Employee`, `Roster`, `Shift`, `DemandSlot`).

## Decisão
1. **Pacotes NuGet de Persistência:** Adotar `Microsoft.EntityFrameworkCore` (`10.0.14`) e `Microsoft.EntityFrameworkCore.SqlServer` (`10.0.14`) exclusivamente no projeto `Infrastructure.Data`.
2. **Contratos na Camada Application:** Definir `ICurrentUserService` (`UserId`, `TenantId`, `IsAuthenticated`) e `IUnitOfWork` (`SaveChangesAsync(CancellationToken ct = default)`) em `src/Core/Application/Common/Interfaces/`. O `AppDbContext` implementa `IUnitOfWork` diretamente.
3. **Global Query Filters Dinâmicos e Fail-Closed:**
   - Em `OnModelCreating`, aplicar `HasQueryFilter` automaticamente para toda entidade raiz não-owned que implemente `ITenantEntity`.
   - A expressão do filtro referencia uma propriedade privada de instância no `AppDbContext` (`CurrentTenantId => _currentUserService.TenantId`), garantindo que o EF Core avalie dinamicamente o tenant da instância escopada do `AppDbContext` a cada execução de consulta:
     `entity => CurrentTenantId != Guid.Empty && entity.TenantId == CurrentTenantId`.
   - Quando `CurrentTenantId == Guid.Empty`, nenhuma linha `ITenantEntity` é retornada (*fail-closed*).
4. **Interceptação em `SaveChanges` e `SaveChangesAsync`:**
   - Ambos os métodos delegam para um helper único `ApplyTenantEnforcement()` antes de enviar comandos ao banco.
   - Se houver entradas `ITenantEntity` em estado `Added`, `Modified` ou `Deleted` e `ICurrentUserService.TenantId == Guid.Empty`, lança `InvalidOperationException`.
   - Em `EntityState.Added`: atribui `CurrentTenantId` quando `TenantId == Guid.Empty` e lança `InvalidOperationException` caso `TenantId` já esteja preenchido com valor diferente do contexto atual.
   - Em `EntityState.Modified` e `EntityState.Deleted`: valida que `TenantId == CurrentTenantId` e marca `TenantId` como `IsModified = false`.
5. **Provedor de Testes da Camada de Dados:** Criar a suíte `tests/Infrastructure.Data.UnitTests` com `Microsoft.EntityFrameworkCore.Sqlite` (`10.0.14`) em memória (`DataSource=:memory:`) para validar geração de SQL real dos Global Query Filters e interceptação transacional sem depender de `InMemory` nem de instância externa do SQL Server nos testes unitários.

## Consequências
- **Benefícios:**
  - Isolamento lógico multi-tenant garantido por padrão em 100% das consultas e mutações, independente de quem consome os repositórios.
  - Zero vazamento de dependência do EF Core para `Domain` ou `Application`.
  - Base pronta para a subfase 2.2 (Fluent API completo para SQL Server e migration inicial) e para a subfase 2.4 (`IdentityDbContext`).
- **Custos:**
  - Fluxos administrativos ou acessos públicos controlados (como links por token previstos na Fase 6) precisarão utilizar `.IgnoreQueryFilters()` de maneira explícita e auditada.
  - Exige configurações estruturais mínimas para Value Objects e backing fields já na subfase 2.1 para que o `IModel` do `AppDbContext` seja válido em tempo de teste.
