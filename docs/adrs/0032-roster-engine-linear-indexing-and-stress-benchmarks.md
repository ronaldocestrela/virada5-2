# 32. Indexação em Memória no Motor 5x2 e Suíte Expandida de Stress & Benchmark na Subfase 7.2

## Status
Aceito

## Contexto
A medição inicial da suíte criada no ADR 0029 (`RosterEngineStressTests`) revelou que, embora os testes passem dentro da margem folgada de CI (`<= 2500ms`), o tempo médio e a alocação de memória crescem de forma superlinear entre 25 e 50 colaboradores (`26.93ms / 5.86MB` para 25 vs. `223.66ms / 28.04MB` para 50 em `Debug`, um salto de ~8,3x para 2x mais colaboradores). Além disso, foram identificadas lacunas de cobertura na Subfase 7.2:
1. Varreduras lineares repetidas sobre `roster.Shifts` e reconstrução de `context.Requirements.ToDictionary(...)` em `Roster5x2OptimizationEngine` e `RosterComplianceEvaluator`.
2. O perfil de stress original gerava demanda supersaturada (`PeakCoverage ~50%`) sem distinguir um cenário complexo **factível** de um cenário **supersaturado**, além de não exercitar `Employee.UnavailableDays`, 3 turnos em operação estendida/24h, fronteiras de `NextWeekFirstShiftStart` (`LookaheadDays`), teto de domingos consecutivos em 4 semanas encadeadas e casos de uso acoplados (`ValidateRosterComplianceUseCase` e `GetCompatibleSwapOptionsUseCase`).
3. Ausência de asserções explícitas de orçamento de alocação de memória (`GC.GetAllocatedBytesForCurrentThread()`).

## Decisão
1. **Registrar primeiro os novos cenários e orçamentos em ciclo TDD (`Red`):**
   - Expandir `RosterEngineStressProfileBuilder` com cenários determinísticos cobrindo: (a) operação complexa factível (10, 25 e 50 colaboradores com `UnavailableDays`, `PreferredDaysOff`, `FixedClosure` e `RotatingDaysOff`); (b) operação 24h em 3 turnos com fronteira de 11h na entrada e saída da semana e streak de 2 domingos anteriores; (c) cadeia sequencial de 4 semanas para 50 colaboradores; (d) cenário supersaturado com degradação graciosa sem violações CLT; (e) stress de validação de conformidade e busca de pares compatíveis de troca com 50 colaboradores.
   - Impor orçamentos estritos de latência e de alocação por execução em `RosterEngineStressTests` mantendo pureza zero-dependência externa conforme o ADR 0029.
2. **Otimizar `Roster5x2OptimizationEngine` e `RosterComplianceEvaluator` mantendo autoridade do Domínio (`Green` / `Refactor`):**
   - Pré-computar o lookup de `RosterRequirementSlot` uma única vez por execução de `Optimize` e de `Evaluate`.
   - Introduzir índice interno de trabalho por execução (`RosterWorkIndex`) no `Roster5x2OptimizationEngine` (contagem de dias trabalhados e conjunto `(EmployeeId, Date)` atualizados em $O(1)$ e buffer reutilizável de candidatos) preservando integralmente todas as mutações e validações CLT via `Roster.AssignShift`, `Roster.RemoveShift` e `Roster.SwapShiftEmployees`.
3. **Documentar baseline comparativo:**
   - Registrar os números medidos antes/depois (Debug e Release) em `docs/benchmarks/roster-engine-stress-baseline.md`.

## Consequências
- **Benefícios:**
  - Redução expressiva da complexidade efetiva e da pressão no Garbage Collector para equipes de até 50 colaboradores.
  - Cobertura de stress realista sobre todas as restrições de horário, fronteiras inter-semanas, rotação dominical e trocas de turno.
- **Custos:**
  - Estruturas auxiliares internas (`RosterWorkIndex`) exigem sincronização rigorosa após cada operação no agregado `Roster`, mitigada por testes de determinismo e invariantes CLT.

