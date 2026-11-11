# 29. Testes Determinísticos de Stress e Orçamentos de Performance do Motor 5x2 na Subfase 7.2

## Status
Aceito

## Contexto
Na Subfase 7.2 do roadmap, o motor heurístico de geração de escalas (`Roster5x2OptimizationEngine` e `GenerateRoster5x2UseCase`) precisa ser validado sob carga realista de operações entre 10 e 50 colaboradores com restrições combinadas complexas (operação noturna após meia-noite, múltiplas praças, polivalência autônoma e de apoio, fronteira de descanso interjornada de 11h da semana anterior, limite de domingos consecutivos e picos/vales de demanda).

## Decisão
1. Construir um gerador determinístico com seed fixa (`RosterEngineStressProfileBuilder`) em `tests/Application.UnitTests/Fakes/` para produzir cenários reproduzíveis de 10, 25 e 50 colaboradores sem dependência de aleatoriedade externa.
2. Implementar `RosterEngineStressTests` em `tests/Application.UnitTests/UseCases/Rosters/` executando múltiplas iterações após aquecimento JIT e verificando:
   - Ausência de violações CLT duras (`IsCltCompliant == true`, descanso >= 11h inclusive na fronteira semanal, máximo de 5 dias trabalhados e 2 folgas semanais).
   - Determinismo absoluto entre execuções idênticas.
   - Orçamentos máximos de latência compatíveis com ambientes compartilhados de CI (`<= 500ms` para 10, `<= 1000ms` para 25 e `<= 2500ms` para 50 colaboradores em média pós-warmup), evitando dependência de pacotes externos não cacheados ou flakiness de infraestrutura.

## Consequências
- **Benefícios:**
  - Validação contínua de regressões algorítmicas de complexidade (O(N²) / explosão combinatória) e integridade CLT diretamente em `dotnet test` offline e no CI.
- **Custos:**
  - Orçamentos de latência devem manter margem conservadora para máquinas virtuais compartilhadas de CI (GitHub Actions), enquanto medições de microbenchmark finas podem ser realizadas localmente quando necessário.

