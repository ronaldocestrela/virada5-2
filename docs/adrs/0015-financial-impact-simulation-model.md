# 15. Adotar Simulador Estateless de Impacto Financeiro (6x1 vs. 5x2) na Camada Application

## Status
Aceito

## Contexto
Na Subfase 3.2 do roadmap (`Simulador de Viabilidade Financeira`), o sistema precisa disponibilizar o caso de uso `SimulateFinancialImpactUseCase` para apresentar o comparativo matemático mensal e anual entre a escala atual **6x1** (folha com encargos, horas extras, turnover e passivo trabalhista) e a escala **5x2 otimizada**.

Três requisitos arquiteturais e de produto orientam esta decisão:
1. **Ordem do fluxo de onboarding (Roadmap Fases 5.3 e 5.4):** A Tela 3 (`Simulador de Impacto Financeiro`) antecede a Tela 4 (`Gestão da Equipe & Polivalência`). Portanto, o simulador pode ser executado antes mesmo de existirem colaboradores cadastrados no `IEmployeeRepository`, mas também deve aproveitar automaticamente a equipe ativa cadastrada quando já disponível.
2. **Projeção interativa sem persistência adicional (`Architecture.Tests`):** O simulador opera em modo "what-if" a partir de inputs salariais/horas extras e premissas parametrizáveis. Criar uma nova `AggregateRoot` exigiria repositório dedicado e nova tabela no `AppDbContext` apenas para armazenar simulações transitórias.
3. **Integração não destrutiva com a matriz de demanda (Subfase 3.1):** Caso o estabelecimento já possua `DemandSlot`s importados, o simulador deve aproveitar a taxa de ociosidade aberta para contextualizar a exposição da folha em horários ociosos/baixos, sem mutar ou persistir alterações nos slots e sem gerar dupla contagem na economia projetada.

## Decisão
1. **Caso de Uso Stateless na Camada `Application`:**
   - Implementar `SimulateFinancialImpactUseCase`, `SimulateFinancialImpactCommand` e os contratos imutáveis em `src/Core/Application/UseCases/Financial/` e `src/Core/Application/DTOs/Financial/`, consumindo exclusivamente `ICompanyRepository`, `IEmployeeRepository`, `IDemandSlotRepository` e `ICurrentUserService`.
   - Retornar `Result<FinancialImpactSimulationDto>` sem lançar exceções de fluxo de negócio (ADR 0004) e sem novas dependências NuGet.
2. **Modelo Matemático Explícito e Auditável (6x1 vs. 5x2):**
   - Decompor cada cenário em: (a) folha base e encargos patronais CLT (padrão `68%`, divisor `220h`), (b) horas extras com adicional padrão de `50%`, reflexo DSR padrão de `20%` e encargos, (c) custo mensalizado de turnover (`65%/ano` no 6x1 vs. `25%/ano` no 5x2, com custo de reposição de `1,5` salário base), e (d) provisão de passivo trabalhista (`8%` sobre folha base no 6x1 vs. `1%` no 5x2), além de redução padrão de `70%` nas horas extras na transição 5x2.
   - Todas as premissas possuem defaults documentados e podem ser sobrescritas de forma validada pelo comando, sendo devolvidas no `FinancialSimulationAssumptionsDto`.
3. **Resolução Flexível de Equipe (`Headcount`) e Ociosidade Somente-Leitura:**
   - Se `Headcount` for informado no comando, utiliza-o; caso contrário, consulta `IEmployeeRepository.GetActiveByCompanyAsync` (retornando erro de validação `FinancialSimulation.HeadcountRequired` se não houver colaboradores ativos).
   - Quando existirem `DemandSlot`s, calcula em memória a ociosidade dos slots dentro de `Company.IsOpenAt` e a métrica informativa `EstimatedIdlePayrollExposure6x1`, separada da soma de economia direta (`MonthlySavings`).
4. **Reuso da Resolução de Empresa:**
   - Extrair `CompanyResolutionHelper` em `Application/UseCases/Common/` para compartilhar a resolução fail-closed de tenant e `Company` entre os casos de uso de `Demand` e `Financial`.

## Consequências
- **Benefícios:**
  - Suporta tanto simulações imediatas no onboarding quanto recálculos baseados na equipe real cadastrada, sem migrations adicionais no SQL Server.
  - Mantém total transparência dos cálculos para exibição no componente `FinancialComparisonCard` (Fase 4) e na Tela 3 (Fase 5).
- **Custos:**
  - Simulações executadas não são persistidas em histórico próprio no banco; caso seja necessário histórico de cenários salvos pós-MVP, um novo ADR definirá a entidade e repositório correspondentes.

