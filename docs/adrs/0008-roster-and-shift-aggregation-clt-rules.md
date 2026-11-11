# 8. Modelar Roster e Shift como Agregação de Escala com Regras CLT

## Status
Aceito

## Contexto
Na subfase 1.4 do roadmap (`Roster` e `Shift`), o sistema precisa representar escalas semanais de trabalho no modelo 5x2 e garantir, diretamente no domínio, a integridade legal da jornada segundo a CLT: intervalo interjornada mínimo de 11 horas consecutivas, garantia estrita de ao menos 2 folgas por ciclo semanal de 7 dias e revezamento dominical periódico. Além disso, o setor de bares e restaurantes opera frequentemente com turnos que cruzam a meia-noite (*overnight*, conforme ADR 0006) e realiza substituições/trocas de turno entre colaboradores elegíveis por função principal ou polivalência (conforme ADR 0007).

Como `Roster` e `Shift` possuem invariantes conjuntas por colaborador e por semana, modelar `Shift` fora da fronteira transacional de `Roster` impediria o bloqueio atômico de sobreposições, violações de interjornada e excesso de dias trabalhados na semana. Por outro lado, manter `Roster` acoplado dentro de `Company` ou `Employee` criaria agregações excessivamente grandes e inviabilizaria `IRosterRepository`.

## Decisão
1. Modelar `Roster` como raiz de agregação independente (`AggregateRoot`) implementando `ITenantEntity`, vinculada ao estabelecimento por identidade explícita (`CompanyId`) e representando um ciclo semanal fechado de 7 dias (`WeekStartDate` até `WeekEndDate = WeekStartDate.AddDays(6)`).
2. Modelar `Shift` como entidade filha (`BaseEntity`) encapsulada dentro da agregação `Roster` (`_shifts` exposto como `IReadOnlyCollection<Shift>`), implementando também `ITenantEntity` com `TenantId` propagado pelo `Roster` para assegurar isolamento lógico uniforme via Global Query Filters no EF Core.
3. Suportar turnos *overnight* (`EndTime < StartTime`, encerrando em `Date.AddDays(1)`), calculando continuamente `StartDateTime` e `EndDateTime`, exigindo `StartTime != EndTime` e duração máxima de 12 horas por turno único.
4. Centralizar as regras de integridade CLT no domínio retornando `Result` / `Result<T>` sem exceções de negócio:
   - **Interjornada mínima de 11h e não sobreposição:** verificadas continuamente entre todos os turnos do mesmo colaborador no `Roster` e contra fronteiras opcionais da semana anterior/posterior (`EmployeeWeekBoundaryContext`).
   - **Garantia estrita de 2 folgas no ciclo 5x2:** máximo de 5 dias distintos trabalhados pelo colaborador dentro do ciclo de 7 dias (`WorkedDays <= 5`), tanto na alocação (`AssignShift`) e troca (`SwapShiftEmployees`) quanto na publicação (`Publish`).
   - **Revezamento dominical periódico:** parametrizado pelo Value Object `SundayRotationPolicy` (suportando ciclo geral de 3 semanas / máximo de 2 domingos consecutivos conforme Lei 10.101/2000 Art. 6º e ciclo quinzenal / máximo de 1 domingo consecutivo conforme CLT Art. 386), além de validação multi-semana via `Roster.ValidateMultiWeekSundayRotation`.
5. Integrar a alocação e a troca de turnos diretamente com `Employee.EvaluateCoverageEligibility` para registrar a origem de cobertura (`PrimaryRole` ou `SecondarySkill`) e impedir alocação de colaboradores inativos, indisponíveis ou sem proficiência para o nível de demanda.

## Consequências
- **Benefícios:**
  - Fronteira transacional coesa em `Roster`, garantindo invariantes CLT atômicas em criação, alocação, troca de turnos e publicação.
  - Domínio 100% puro e testável sem acesso a banco ou repositórios, recebendo contexto histórico semanal via Value Object `EmployeeWeekBoundaryContext`.
  - Base pronta para `IRosterRepository` (subfase 1.5), Motor Otimizador 5x2 (Fase 3.3) e Trocas de Turno (Fases 3.4 e 6).
- **Custos:**
  - Os casos de uso da camada `Application` (Fase 3) serão responsáveis por carregar os turnos limítrofes/histórico dominical de semanas adjacentes para construir `EmployeeWeekBoundaryContext` ao alocar ou publicar escalas.

