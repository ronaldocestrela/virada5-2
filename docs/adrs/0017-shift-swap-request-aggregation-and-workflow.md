# 17. Modelar Solicitações de Troca de Turno com ShiftSwapRequest e Validação CLT em Duas Etapas

## Status
Aceito

## Contexto
Na Subfase 3.4 do roadmap (`Gestão de Trocas de Turno`), o sistema precisa permitir que colaboradores solicitem trocas de turno (`RequestShiftSwapUseCase`) e que gestores aprovem essas trocas após validação integral das regras trabalhistas da CLT e de polivalência (`ApproveShiftSwapUseCase`). Além disso, a Fase 6 do roadmap prevê o aceite prévio pelo colega destinatário antes da notificação ao gestor (Subfase 6.2) e a aprovação em 1 clique pelo gestor (Subfase 6.3).

Três decisões estruturais precisam ser formalizadas em aderência ao `agents.md` e aos ADRs anteriores (`0005`, `0008`, `0011`, `0012` e `0016`):
1. **Fronteira de Agregação e Persistência:** Uma solicitação de troca possui ciclo de vida próprio (`PendingPeerApproval`, `PendingManagerApproval`, `Approved`, `Rejected`, `Cancelled`) que atravessa requisições HTTP/SignalR distintas entre solicitante, colega e gestor. Embuti-la dentro de `Roster` acoplaria consultas de pendências ao carregamento completo de escalas e turnos.
2. **Modalidades de Troca e Nível de Demanda por Turno:** Além da troca mútua entre dois turnos (`RequesterShiftId` <-> `TargetShiftId`), é comum em operações 5x2 que um colaborador com folga disponível (`< 5` dias trabalhados) assuma o turno de um colega sem ceder um turno em troca (cessão unilateral). Adicionalmente, dois turnos trocados podem ocorrer em faixas de demanda diferentes (ex.: segunda-feira `Idle` por sexta-feira `Peak`), exigindo avaliação individualizada de `DemandClassification` para aplicar corretamente a regra de proficiência (`Support` vs `Autonomous`).
3. **Concorrência Lógica e Fronteiras Temporais:** Entre a solicitação e a aprovação final pelo gestor, a escala pode ser editada, arquivada, ter outro turno trocado ou ter o colaborador desativado. Confiar apenas na validação inicial permitiria aprovar trocas obsoletas ou não conformes.

## Decisão
1. **Nova Raiz de Agregação `ShiftSwapRequest` e Repositório Específico:**
   - Criar `ShiftSwapRequest : AggregateRoot, ITenantEntity` em `Domain/Entities/` com o enum `ShiftSwapStatus` (`PendingPeerApproval`, `PendingManagerApproval`, `Approved`, `Rejected`, `Cancelled`).
   - Criar o contrato específico `IShiftSwapRequestRepository` em `Domain/Repositories/` e sua implementação `ShiftSwapRequestRepository` em `Infrastructure/Data/Repositories/`, preservando o padrão sem repositórios genéricos (ADR 0005).
   - Mapear a tabela `ShiftSwapRequests` via Fluent API (`ShiftSwapRequestConfiguration`) com Global Query Filter automático por `TenantId`, índices compostos iniciados por `TenantId` e todas as chaves estrangeiras externas (`Company`, `Roster`, `RequesterEmployee`, `TargetEmployee`, `RequesterShift`, `TargetShift`) com `DeleteBehavior.Restrict` para evitar *multiple cascade paths* no SQL Server (ADR 0011).
2. **Suporte a Troca Mútua e Cessão Unilateral com Validação Não Mutante no Domínio:**
   - Em `Roster`, extrair `ValidateSwapShiftEmployees` (não mutante) e adicionar sobrecarga de `SwapShiftEmployees` aceitando `DemandClassification` distinto para cada turno, preservando a assinatura anterior para compatibilidade.
   - Em `Roster`, adicionar `ValidateReassignShiftEmployee` e `ReassignShiftEmployee` para suportar cessão unilateral (`TargetShiftId == null`) mantendo as mesmas invariantes CLT (11h de interjornada, máximo de 5 dias trabalhados no ciclo 5x2, revezamento dominical e elegibilidade por polivalência).
3. **Fluxo de Estados e Validação CLT em Duas Etapas:**
   - `RequestShiftSwapCommand` aceita `RequirePeerApproval = true` por padrão (iniciando em `PendingPeerApproval` para confirmação via `RespondShiftSwapPeerUseCase`) ou `false` para seguir diretamente para `PendingManagerApproval`.
   - Tanto na solicitação/aceite do colega (pré-validação não mutante) quanto na aprovação do gestor (`ApproveShiftSwapUseCase`), o helper `ShiftSwapValidationHelper` revalida posse atual dos turnos, status ativo dos colaboradores, classificação de demanda de cada turno, `EmployeeWeekBoundaryContext` nas escalas adjacentes (`[-28, +28]` dias) e revezamento dominical multi-semana (`Roster.ValidateMultiWeekSundayRotation`, inclusive para semanas futuras já planejadas).
   - A aprovação executa a mutação no `Roster` e marca `ShiftSwapRequest` como `Approved` em transação única via `IUnitOfWork.SaveChangesAsync`.

## Consequências
- **Benefícios:**
  - Fluxo pronto para a Subfase 3.4 e diretamente reaproveitável nas telas da Fase 6 (Subfases 6.2 e 6.3) sem alterações de modelo ou schema.
  - Garantia atômica de conformidade CLT mesmo sob concorrência ou edições manuais intermediárias na escala.
  - Aderência integral às regras de arquitetura validadas por `Architecture.Tests`.
- **Custos:**
  - Introduz uma nova migration incremental (`AddShiftSwapRequests`) no SQL Server e exige que futuras telas de edição manual ou arquivamento de escala considerem solicitações pendentes (mitigado pela verificação explícita de posse de turno e status do `Roster` no momento da aprovação).

