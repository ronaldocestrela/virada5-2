# 4. Implementar Padrão Result Nativo Compartilhado entre Domain e Application

## Status
Aceito

## Contexto
As diretrizes do projeto (`agents.md`) determinam duas regras inegociáveis complementares:
1. **Regra 1.3 (Clean Architecture):** `Domain` não depende de nenhum outro projeto; `Application` depende de `Domain`.
2. **Regra 1.6 (Padrão `Result<T>` Nativo):** É proibido lançar exceções para controle de fluxo de negócio (`throw new BusinessException(...)`). Todo serviço de aplicação e método de regra de domínio que possa falhar deve retornar a estrutura de dados imutável `Result` ou `Result<T>`.

Para que tanto as entidades/regras de negócio em `Domain` quanto os casos de uso em `Application` retornem `Result`, `Result<T>` e `Error` sem violar o sentido de dependência da Clean Architecture (e mantendo compatibilidade com o namespace `Application.Common` exemplificado no `agents.md`), a localização física e o compartilhamento desses tipos precisam ser formalizados.

## Decisão
1. Implementar `Error`, `Result` e `Result<TValue>` de forma nativa (sem bibliotecas de terceiros), imutável e fortemente tipada.
2. Definir os tipos fisicamente em `src/Core/Domain/Common/` (`namespace Domain.Common`) para que `Domain` permaneça 100% livre de dependências externas e possa retornar `Result` e `Result<TValue>` em todas as suas regras de negócio.
3. Expor `global using` (type aliases públicos/globais) em `src/Core/Application/Common/` para que o projeto `Application` e seus consumidores possam referenciar `Error`, `Result` e `Result<TValue>` transparentemente tanto via `Domain.Common` quanto `Application.Common`.

## Consequências
- **Benefícios:**
  - Cumprimento simultâneo das Regras 1.3 e 1.6 do `agents.md`.
  - Zero alocação de stack trace para erros de validação, conflito ou não encontrado.
  - Fluxo de erro explícito na assinatura dos métodos do domínio e da aplicação.
- **Custos:**
  - Necessidade de disciplina da equipe/agentes para verificar `IsSuccess`/`IsFailure` antes de acessar `Result<TValue>.Value`.

