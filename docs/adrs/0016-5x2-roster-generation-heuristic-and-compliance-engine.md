# 16. Adotar Heurística Determinística Nativa e Validador de Conformidade Compartilhado para o Motor de Escalas 5x2

## Status
Aceito

## Contexto
Na Subfase 3.3 do roadmap (`Motor Gerador de Escalas 5x2 (Core Algorithm)`), o sistema precisa gerar automaticamente escalas semanais no modelo **5x2** a partir dos dados de estabelecimento (`Company`), equipe e polivalência (`Employee`), matriz de demanda (`DemandSlot`) e histórico de escalas adjacentes (`Roster`), além de disponibilizar um validador de conformidade CLT e integrador de alertas de inconsistência.

Quatro desafios arquiteturais precisam ser endereçados em consonância com o `agents.md` e os ADRs anteriores (`0008`, `0012`, `0014` e `0015`):
1. **Pureza da camada `Application` (Regra 1.3 do `agents.md`):** Assim como no parsing de planilhas (ADR 0014) e no simulador financeiro (ADR 0015), a camada `Application` deve permanecer livre de pacotes externos pesados (ex.: solvers nativos C++/OR-Tools) que aumentam a complexidade operacional e de build.
2. **Modelagem de turnos e metas por praça:** A entidade `Company` define a janela diária única (`OpeningTime`..`ClosingTime`, inclusive *overnight*) e `DemandSlot` registra o volume esperado por `(DayOfWeek, Hour)`, sem discriminar quantitativos por `StationRole`. O motor precisa aceitar templates de turno e metas por praça explícitos quando configurados, sem inviabilizar a geração imediata quando omitidos.
3. **Garantia atômica de regras CLT entre semanas:** Conforme o ADR 0008, a agregação `Roster` valida interjornada mínima de 11h, limite de 5 dias trabalhados (2 folgas), elegibilidade por polivalência e revezamento dominical (`SundayRotationPolicy`), porém depende da camada `Application` para construir os `EmployeeWeekBoundaryContext` a partir das escalas anteriores e posteriores.
4. **Reuso da validação na edição manual (Fase 5.5):** O painel interativo permitirá ajustes manuais após a geração automática, exigindo que o mesmo motor de diagnóstico de conformidade e alertas possa ser executado sob demanda sobre qualquer `Roster` persistida.

## Decisão
1. **Heurística Determinística em Duas Etapas em C# Puro (`Application/UseCases/Rosters/Engine/`):**
   - Implementar `Roster5x2OptimizationEngine` sem dependências NuGet externas, utilizando exclusivamente tipos de `Domain` e da BCL .NET 10.
   - **Etapa 1 (Construção Gulosa Ponderada + MRV):** Ordenar vagas requeridas por prioridade de demanda (`Peak` -> `Moderate` -> `Low` -> `Idle`), desempate por menor número de candidatos elegíveis (*Minimum Remaining Values*) e alocação via `Roster.AssignShift` (garantindo 100% das *Hard Constraints* CLT diretamente no domínio). A pontuação pondera: prioridade absoluta para `PrimaryRole` sobre `SecondarySkill`, preservação de folgas nos dias de vale de demanda (menor volume aberto) e em `Employee.PreferredDaysOff`, equilíbrio semanal de dias trabalhados e menor sequência dominical acumulada.
   - **Etapa 2 (Busca Local / Reparo):** Caso alguma vaga `Peak` fique descoberta por saturação dos 5 dias em turnos de menor demanda, executar tentativa de liberação atômica (remoção de turno `Idle`/`Low` ou `Moderate` excedente e realocação no `Peak`, com rollback determinístico caso viole 11h de interjornada) e refinamento por troca (`SwapShiftEmployees`) para melhorar preferências de folga e alinhamento aos vales.
2. **Resolução Flexível de Templates de Turno e Metas por Praça (`RosterDemandAndTemplateResolver`):**
   - `GenerateRoster5x2Command` aceita `ShiftTemplates` e `StaffingRules` opcionais.
   - Quando omitidos, o motor infere automaticamente os templates pela janela `Company.OpeningTime..ClosingTime` (1 turno único se `<= 10h`; 2 turnos sobrepostos de até `9h`–`10h` respeitando o limite de `12h` do `Shift` quando `> 10h`) e infere as metas por praça a partir da distribuição de `PrimaryRole` dos colaboradores ativos.
   - A matriz de `DemandSlot` é normalizada em memória sem mutar os registros do banco (padrão ADR 0015).
3. **Semântica de Persistência em `Draft` e Substituição Segura:**
   - `GenerateRoster5x2UseCase` persiste a nova escala com `RosterStatus.Draft`.
   - Caso já exista escala não arquivada na semana (`GetByCompanyAndWeekStartAsync`), retorna `Result.Failure` (`Conflict`) por padrão. Se o registro existente for `Draft` e `ReplaceExistingDraft = true`, arquiva o rascunho anterior via `Archive()` e persiste o novo na mesma unidade de trabalho (`IUnitOfWork`), aproveitando o índice não único `IX_Rosters_TenantId_CompanyId_WeekStartDate` (ADRs 0011 e 0012). Escalas `Published` nunca são sobrescritas automaticamente.
4. **Validador de Conformidade Compartilhado (`RosterComplianceEvaluator` & `ValidateRosterComplianceUseCase`):**
   - Extrair `RosterBoundaryContextBuilder` (janela `[-28, +13]` dias via `GetByWeekRangeAsync`) e `RosterComplianceEvaluator` para produzir `RosterComplianceReportDto` com métricas de cobertura (`PeakCoveragePercentage`, `OverallCoveragePercentage`, uso de polivalência, preferências de folga) e `ComplianceAlertDto`s estruturados por severidade (`Error`, `Warning`, `Info`) e categoria (`CltHardConstraint`, `PeakCoverage`, `StationStaffing`, `SoftConstraint`, `PolyvalenceUsage`, `BoundaryContext`).
   - Expor `ValidateRosterComplianceUseCase` em modo somente leitura para reavaliação de escalas geradas ou editadas manualmente.

## Consequências
- **Benefícios:**
  - Zero pacotes externos na camada `Application`, mantendo compilação rápida, determinismo total e conformidade integral com `Architecture.Tests`.
  - Garantia estrutural de que nenhuma escala gerada viole regras CLT de domínio, pois toda alocação e reparo passa por `Roster.AssignShift` e `Roster.SwapShiftEmployees`.
  - Base pronta para os casos de uso de trocas de turno (Subfase 3.4) e para o Painel de Escala Interativo (Subfase 5.5).
- **Custos:**
  - Como heurística gulosa com reparo local (e não programação inteira exata), cenários extremos de restrições fortemente acopladas podem exigir ajustes manuais sinalizados via `ComplianceAlertDto`; a arquitetura isolada em `Engine/` permite evoluir ou adicionar passes de busca local pós-MVP sem alterar os contratos públicos.

