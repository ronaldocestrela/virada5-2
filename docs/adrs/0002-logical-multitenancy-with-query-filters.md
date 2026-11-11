# 2. Adotar Multi-tenancy Lógico com Global Query Filters

## Status
Aceito

## Contexto
O SaaS de Transição 5x2 atende múltiplos estabelecimentos (clientes/tenants) compartilhando a mesma aplicação e banco de dados relacional (Microsoft SQL Server). É indispensável garantir isolamento rigoroso de dados entre estabelecimentos sem incorrer no custo operacional de provisionar bancos físicos distintos por cliente no MVP.

## Decisão
1. Adotar **multi-tenancy lógico** com coluna discriminadora `TenantId` (`Guid`) em todas as entidades pertencentes a um tenant.
2. Definir a interface de marcação `ITenantEntity` em `src/Core/Domain/Common/ITenantEntity.cs` (`Guid TenantId { get; set; }`).
3. Toda entidade de negócio vinculada a um estabelecimento — incluindo a própria raiz `Company` e entidades associadas como `DemandSlot`, `Employee`, `Roster` e `Shift` — deve implementar `ITenantEntity` e validar no domínio que `TenantId` não seja `Guid.Empty`.
4. Na camada `Infrastructure.Data`, o `AppDbContext` aplicará **Global Query Filters** (`HasQueryFilter`) para toda entidade que implemente `ITenantEntity`, bem como a injeção automática de `TenantId` via `ICurrentUserService` durante `SaveChangesAsync`.

## Consequências
- **Benefícios:**
  - Baixo custo de infraestrutura e simplicidade de migrations no SQL Server.
  - Prevenção sistêmica de vazamento de dados entre tenants por esquecimento de cláusula `Where` em consultas.
  - Rastreabilidade explícita no domínio de que toda entidade tenant-specific exige contexto de tenant válido.
- **Custos:**
  - Exige atenção redobrada em fluxos administrativos ou testes de integração para garantir contexto de tenant consistente e cobertura dedicada de isolamento entre tenants.

