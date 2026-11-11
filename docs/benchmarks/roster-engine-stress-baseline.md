# Baseline de Stress e Performance: Motor de Escala 5x2 (Subfase 7.2)

Este documento registra as medições reproduzíveis obtidas localmente via `RosterEngineStressTests` (`Stopwatch` + `GC.GetAllocatedBytesForCurrentThread()` após aquecimento JIT) antes e depois da introdução da indexação em memória (`RosterWorkIndex` e lookup pré-computado em `RosterComplianceEvaluator`, conforme **ADR 0032**).

## 1. Comparativo na Suíte Expandida (Antes vs. Depois)

| Cenário | Equipe | Antes (Debug) Latência / Alocação | Depois (Debug) Latência / Alocação | Depois (Release) Latência / Alocação | Orçamento CI (Latência / Alocação) |
| :--- | :---: | :---: | :---: | :---: | :---: |
| `Solvable / RotatingDaysOff` | 10 | `2.40 ms` / `849.9 KB` | `1.57 ms` / `421.6 KB` | `1.34 ms` / `420.7 KB` | `<= 120 ms` / `<= 1400 KB` |
| `Solvable / RotatingDaysOff` | 25 | `9.29 ms` / `2290.1 KB` | `4.06 ms` / `1009.4 KB` | `3.54 ms` / `1008.6 KB` | `<= 200 ms` / `<= 3500 KB` |
| `Solvable / RotatingDaysOff` | 50 | `58.00 ms` / `8754.5 KB` | `21.06 ms` / `3417.7 KB` | `17.85 ms` / `3416.9 KB` | `<= 300 ms` / `<= 6500 KB` |
| `Solvable / FixedClosure` | 50 | `24.34 ms` / `7445.7 KB` | `17.00 ms` / `2856.2 KB` | `13.25 ms` / `2855.3 KB` | `<= 300 ms` / `<= 6500 KB` |
| `3Shifts-24h` (`06:00–02:00`, fronteira dupla) | 50 | `87.54 ms` / `17505.0 KB` | `66.21 ms` / `8621.6 KB` | `49.59 ms` / `8620.7 KB` | `<= 350 ms` / `<= 11000 KB` |
| `4-Week-Chain` (4 semanas sequenciais) | 50 | — | `47.37 ms` total | `45.29 ms` total | `<= 1200 ms` |
| `Oversubscribed / RotatingDaysOff` | 50 | `65.14 ms` / `12147.1 KB` | `26.41 ms` / `3809.9 KB` | `21.44 ms` / `3809.0 KB` | `<= 400 ms` / `<= 8500 KB` |
| `Oversubscribed / FixedClosure` | 50 | `45.44 ms` / `10735.2 KB` | `21.51 ms` / `3684.9 KB` | `24.53 ms` / `3684.1 KB` | `<= 400 ms` / `<= 8500 KB` |
| `ValidateRosterCompliance + SwapSearch` | 50 | `3.36 ms` + `47.81 ms` | `2.41 ms` + `51.61 ms` | `1.66 ms` + `49.52 ms` | `<= 300 ms` + `<= 400 ms` |

> **Nota sobre o baseline legado (`b86a4ac`):** No perfil único anterior de 50 colaboradores em `Debug`, o motor consumia `223.66 ms` / `28039.7 KB` (`RotatingDaysOff`) e `203.62 ms` / `26277.6 KB` (`FixedClosure`). A eliminação de varreduras lineares sobre `roster.Shifts` e reconstruções repetidas de dicionários reduziu as alocações em **~51% a ~68%** nos cenários de 50 colaboradores.

## 2. Como Reproduzir Localmente

```bash
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj --no-restore --configuration Release --filter RosterEngineStressTests --logger "console;verbosity=detailed"
```

