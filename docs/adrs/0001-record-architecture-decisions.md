# 1. Registrar Decisões Arquiteturais com ADRs

## Status
Aceito

## Contexto
O desenvolvimento do SaaS de Transição 5x2 exige governança técnica clara e rastreabilidade sobre decisões estruturantes, escolhas de biblioteca, isolamento de camadas e padrões de domínio e interface.

## Decisão
Adotar Architecture Decision Records (ADRs) vivos armazenados em `docs/adrs/` numerados sequencialmente (`000X-titulo-curto.md`), seguindo o formato padrão estabelecido no `agents.md` (Status, Contexto, Decisão, Consequências).

## Consequências
- **Benefícios:** Histórico claro do racional técnico para desenvolvedores e agentes de IA, evitando regressões arquiteturais.
- **Custos:** Necessidade de atualizar ou criar um novo ADR sempre que uma decisão estruturante for tomada ou alterada.

