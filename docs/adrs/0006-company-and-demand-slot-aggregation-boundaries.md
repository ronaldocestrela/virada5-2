# 6. Modelar Company e DemandSlot como Agregações Independentes

## Status
Aceito

## Contexto
Na subfase 1.2 do roadmap (`Company` e `DemandSlot`), um estabelecimento possui uma matriz de demanda semanal que pode conter até 168 slots (7 dias × 24 horas). Manter todos os `DemandSlot` dentro da raiz de agregação `Company` obrigaria a carregar e persistir toda a curva de demanda sempre que apenas dados cadastrais, horários ou modelo de escala do estabelecimento fossem alterados. Além disso, a estrutura de repositórios prevista no `agents.md` define `ICompanyRepository` e `IDemandSlotRepository` como contratos separados.

## Decisão
1. Modelar `Company` e `DemandSlot` como raízes de agregação distintas (`AggregateRoot`) que implementam `ITenantEntity`.
2. Vincular `DemandSlot` ao estabelecimento por referência explícita de identidade (`CompanyId`), sem coleção navegacional de `DemandSlot` dentro de `Company`.
3. Concentrar em `Company` as invariantes de identidade do estabelecimento, tipo de negócio (`BusinessType`), horário padrão de funcionamento (`OpeningTime`, `ClosingTime`, com suporte a fechamento após a meia-noite) e estratégia de folga (`ScheduleModel`: `FixedClosure` ou `RotatingDaysOff`, além de `ClosedDays`).
4. Concentrar em `DemandSlot` as invariantes da célula dia/hora (`DayOfWeek`, `Hour` de 0 a 23), volume esperado (`ExpectedVolume >= 0`), cálculo proporcional de volume relativo (`RelativeVolume` em `[0, 1]`, tratando pico zero sem divisão por zero) e classificação de ociosidade (`DemandClassification`: `Idle`, `Low`, `Moderate`, `Peak`).

## Consequências
- **Benefícios:**
  - Fronteiras de transação menores e aderência direta ao padrão de repositórios específicos (`ICompanyRepository` e `IDemandSlotRepository`).
  - Evolução independente da curva de demanda (importação de PDV/matriz) sem contention na entidade `Company`.
- **Custos:**
  - O cálculo do volume relativo de um conjunto de `DemandSlot` a partir do pico global da semana exige coordenação no caso de uso da camada `Application` (ex.: `GenerateDemandDiagnosisUseCase`), passando o `peakVolume` para `DemandSlot.EvaluateRelativeDemand(peakVolume)`.

