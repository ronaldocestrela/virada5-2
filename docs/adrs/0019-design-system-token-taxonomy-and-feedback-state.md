# 19. Padronizar Taxonomia de Design Tokens CSS e Serviço de Feedback Toast

## Status
Aceito

## Contexto
O ADR 0003 definiu Blazor Server puro com `wwwroot/css/tokens.css` e `Components/DesignSystem/`. Ao iniciar a implementação da Fase 4, surgem três decisões estruturantes para garantir consistência entre Design System, testes arquiteturais e integração com as Fases 5 e 6:
1. O `tokens.css` base do `agents.md` define as cores principais, espaçamentos, tipografia, raios e sombras, mas os componentes complexos da Fase 4 (`DemandHeatmap`, `FinancialComparisonCard`, `RosterGrid`) exigem tokens semânticos adicionais para níveis de demanda (`Idle`, `Low`, `Moderate`, `Peak`, fechado), status de conformidade CLT, alocação por polivalência, folga 5x2, camadas de `z-index` e transições.
2. Os testes arquiteturais precisam verificar automaticamente a aderência ao Design System (zero cores literais, zero libs visuais externas, uso exclusivo de variáveis declaradas em `tokens.css`).
3. O componente `AppToast` (Subfase 4.2) precisa exibir feedbacks assíncronos acionados por qualquer tela ou caso de uso retornando `Result` / `Result<T>` sem lançar exceções de fluxo.

## Decisão
1. **Taxonomia de Tokens (`wwwroot/css/tokens.css`):**
   - Preservar todos os tokens obrigatórios do `agents.md` e expandir `:root` com grupos semânticos padronizados: `--color-primary-*`, `--color-surface-*`, `--color-text-*`, `--color-border-*`, `--color-danger-*`, `--color-warning-*`, `--color-success-*`, `--color-info-*`, `--color-clt-*`, `--color-shift-*`, `--color-day-off-*`, `--color-heatmap-*`, `--space-*`, `--font-*`, `--line-height-*`, `--border-width-*`, `--radius-*`, `--shadow-*`, `--transition-*` e `--z-*`.
   - Normalizar o stylesheet base em `wwwroot/css/app.css` (substituindo o `wwwroot/app.css` padrão do template) e carregá-lo após `css/tokens.css` em `Components/App.razor`.
2. **Convenção Estrutural 1:1 no Design System:**
   - Cada componente atômico ou de domínio reside em `Components/DesignSystem/{NomeDoComponente}/` com `{NomeDoComponente}.razor` e `{NomeDoComponente}.razor.css`, garantindo verificação determinística por `Architecture.Tests`.
3. **Serviço Scoped de Notificações (`IToastService` / `ToastService`):**
   - Criar `IToastService` e `ToastService` em `WebApp.Blazor/Services/` (escopo por circuito Blazor Server) com métodos `Show(...)`, `ShowFromResult(Result, string? successTitle, string? successMessage)` e `Dismiss(Guid id)`.
   - Integrar `<AppToast />` no `MainLayout.razor` para renderização global de feedbacks oriundos de `Result` / `Error`.

## Consequências
- **Benefícios:**
  - 100% das cores, espaçamentos, fontes e elevações auditáveis automaticamente em `Architecture.Tests`.
  - Integração direta entre casos de uso (`Result<T>`) e feedback visual (`AppToast`) nas Fases 5 e 6 sem duplicação de estado.
- **Custos:**
  - Qualquer novo token visual necessário nas Fases 5 e 6 deve ser declarado primeiro em `wwwroot/css/tokens.css` antes de ser referenciado em arquivos `.razor.css`.

