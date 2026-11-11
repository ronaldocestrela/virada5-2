# 7. Modelar Employee e Polivalência como Agregação Independente

## Status
Aceito

## Contexto
Na subfase 1.3 do roadmap (`Employee`), o estabelecimento gerencia sua equipe operacional, funções principais por praça, matriz de competências secundárias (polivalência), preferências de folga e restrições individuais de indisponibilidade. Manter a coleção de colaboradores dentro da raiz de agregação `Company` aumentaria o tamanho das fronteiras transacionais e inviabilizaria a evolução e consulta isolada da equipe via `IEmployeeRepository`. Além disso, o motor de escalas (Fase 3) e o fluxo de trocas (Fase 6) exigem regras explícitas no domínio para validar se um colaborador pode cobrir determinada praça em função do nível de demanda (`DemandClassification`).

## Decisão
1. Modelar `Employee` como raiz de agregação independente (`AggregateRoot`) implementando `ITenantEntity`, vinculada ao estabelecimento por identidade explícita (`CompanyId`), seguindo o mesmo padrão da ADR 0006.
2. Definir enums tipados no domínio para representar praças e polivalência:
   - `StationRole` (`Kitchen`, `KitchenAssistant`, `Waiter`, `Bar`, `Cashier`, `Cleaning`, `Expedition`, `Management`).
   - `SkillProficiency` (`Support` para apoio em demandas até `Moderate`; `Autonomous` para cobertura autônoma inclusive em `Peak`).
   - `CoverageOrigin` (`PrimaryRole`, `SecondarySkill`) para indicar a origem da elegibilidade retornada por `Result<CoverageOrigin>`.
3. Modelar cada competência secundária como Value Object imutável `SecondarySkill` (`readonly record struct`) com fábrica `Create(...)` retornando `Result<SecondarySkill>`, garantindo que `SecondarySkills` nunca contenha duplicidades nem repita a `PrimaryRole`.
4. Separar explicitamente restrições rígidas (`UnavailableDays`, bloqueiam elegibilidade no dia e não podem cobrir todos os 7 dias) de preferências flexíveis de folga (`PreferredDaysOff`, sem sobreposição com `UnavailableDays`, preservando elegibilidade para ponderação no motor de otimização).
5. Centralizar a validação de elegibilidade de cobertura em `Employee.EvaluateCoverageEligibility(targetRole, demandLevel, dayOfWeek?)` retornando `Result<CoverageOrigin>` sem exceções de negócio.

## Consequências
- **Benefícios:**
  - Fronteira transacional enxuta para gestão de equipe e aderência direta ao contrato futuro `IEmployeeRepository`.
  - Regra de elegibilidade auditável e reutilizável tanto pelo motor de escalas 5x2 (Fase 3.3) quanto pela seleção de pares compatíveis em trocas de turno (Fase 6.2).
  - Integração direta entre `SkillProficiency` e `DemandClassification` definida na subfase 1.2.
- **Custos:**
  - Caso estabelecimentos precisem de praças totalmente customizadas além das categorias padrão de `StationRole` no pós-MVP, será necessário evoluir o modelo para catálogo por tenant.

