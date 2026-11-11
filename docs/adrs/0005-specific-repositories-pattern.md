# 5. Adotar Repositórios Específicos por Agregação no Domínio

## Status
Aceito

## Contexto
As diretrizes do projeto (`agents.md`, Princípio 5) proíbem expressamente o uso de repositórios genéricos (`IRepository<T>`) e exigem contratos especializados por raiz de agregação no projeto `Domain`, com implementações concretas na camada `Infrastructure.Data`.

Nas subfases anteriores (ADRs 0006, 0007 e 0008), foram definidas quatro raízes de agregação independentes (`Company`, `DemandSlot`, `Employee` e `Roster`, que encapsula a entidade filha `Shift`), todas implementando `ITenantEntity` sob isolamento lógico por Global Query Filters (ADR 0002). Na subfase 1.5, é necessário formalizar os contratos dessas agregações para sustentar os casos de uso de diagnóstico de demanda, simulador financeiro, motor de escala 5x2 e trocas de turno (Fases 2, 3, 5 e 6).

## Decisão
1. Definir contratos de repositório específicos no namespace `Domain.Repositories` (`src/Core/Domain/Repositories/`), estritamente um por raiz de agregação:
   - `ICompanyRepository` para `Company`;
   - `IDemandSlotRepository` para `DemandSlot`;
   - `IEmployeeRepository` para `Employee`;
   - `IRosterRepository` para `Roster` (incluindo o carregamento conjunto de seus turnos `Shift`).
2. **Proibir repositório independente para entidades filhas:** `Shift` não possui `IShiftRepository`, devendo ser carregado e persistido exclusivamente através da fronteira transacional de `IRosterRepository`.
3. **Alinhamento com Multi-tenancy Lógico (ADR 0002):** nenhum método de contrato recebe `Guid tenantId` manualmente, pois o isolamento por tenant é garantido pelos Global Query Filters do `AppDbContext`. Agregações pertencentes a um estabelecimento (`DemandSlot`, `Employee`, `Roster`) recebem `Guid companyId` nas consultas por estabelecimento para impedir mistura de dados entre estabelecimentos de um mesmo tenant.
4. **Assinaturas assíncronas e unidade de trabalho:** métodos de consulta são assíncronos, aceitam `CancellationToken ct = default` e retornam `Task<T?>` ou `Task<IReadOnlyList<T>>`. Operações de escrita (`AddAsync`, `AddRangeAsync`, `Update`, `RemoveRange`) apenas registram alterações no contexto, delegando a confirmação transacional (`SaveChangesAsync` / `IUnitOfWork`) à camada de aplicação/infraestrutura.

## Consequências
- **Benefícios:**
  - Eliminação de abstrações genéricas vazadas e exposição apenas de consultas aderentes às regras de negócio de cada agregação.
  - Preservação da fronteira transacional de `Roster` e `Shift` para validação atômica das regras CLT.
  - Desacoplamento total entre `Domain`/`Application` e EF Core/SQL Server, viabilizando mocks limpos nos testes unitários da Fase 3.
- **Custos:**
  - Necessidade de manter os contratos sincronizados com novas necessidades de consulta que surjam nos casos de uso da Fase 3 e nas telas das Fases 5 e 6, sempre respeitando o limite de agregação.

