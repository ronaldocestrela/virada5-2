# 36. Implementação da Identidade Visual e Regras de Design (DESIGN.md)

## Status
Aceito

## Contexto
O arquivo `docs/DESIGN.md` foi consolidado como a especificação canônica da identidade de marca, regras visuais, paleta de cores, tipografia, componentes e conceito da Virada 5x2 ("a virada do dia").
Anteriormente, o produto utilizava tokens genéricos baseados em verde esmeralda (ADR 0019) sem suporte nativo aos temas Claro (Amanhecer) e Escuro (Noite azul-marinho), sem a tipografia oficial (**Bricolage Grotesque** e **Onest**), sem o símbolo SVG do sol nascente sobre o horizonte e sem os componentes característicos definidos no guia (anel de contagem até 16/12/2026, animação da semana 6x1 &rarr; 5x2, faixas indicadoras superiores e laterais dos 4 caminhos).

## Decisão
1. **Unificação dos Tokens Canônicos em `wwwroot/css/tokens.css`:**
   - Adicionar os tokens oficiais do `DESIGN.md`: `--tinta`, `--suave`, `--papel`, `--cartao`, `--linha`, `--laranja`, `--verde`, `--azul`, `--amarelo`, `--alerta`, `--campo`, `--borda`, `--ok-bg`, `--fraco`, as cores dos 4 caminhos (`--caminho-*`) e dos grupos de folga (`--grupo-folga-*`).
   - Mapear os tokens legados (`--color-primary`, `--color-surface-bg`, etc.) como referências diretas a esses tokens para preservar 100% de compatibilidade retroativa.
   - Declarar o tema escuro em `[data-theme="dark"]` e na media query `@media (prefers-color-scheme: dark)`.

2. **Tipografia Oficial:**
   - Injetar Google Fonts para **Bricolage Grotesque** (pesos 400 a 800, eixo de condensação entre 85% e 92%) e **Onest** (pesos 400 a 700) em `Components/App.razor`.
   - Adicionar script inline pré-renderização para evitar FOUC na alternância do tema via `localStorage['virada5x2-tema']`.

3. **Novos Componentes no Design System (`Components/DesignSystem`):**
   - `BrandLogo`: Símbolo SVG oficial (sol nascente sobre retângulo azul-marinho com raios amarelos) e logotipo "Virada 5x2" em Bricolage Grotesque.
   - `ThemeToggle`: Alternador acessível de tema claro/escuro com ícones sol/lua e persistência no cliente.
   - `HeroBanner`: Cartão com gradiente do herói, anel de contagem regressiva até 16/12/2026 e animação da semana 6x1 virando 5x2 com suporte a `prefers-reduced-motion`.
   - `StepProgressBar`: Barra com os 5 passos oficiais (Empresa, Horário, Equipe, Dinheiro, Plano) e estados concluído/atual/futuro.
   - `MetricTile`: Card de indicador com faixa colorida superior de 6px em degradê (laranja, azul-marinho, verde, alerta).

4. **Atualizações de Layout e Páginas:**
   - Shell com efeito de vidro (`backdrop-filter: blur(14px)`), topo fixo com logo e alternador de tema.
   - Página inicial (`Home.razor`) integrando o `HeroBanner`, métricas em `MetricTile` e rodapé institucional com menção ao Empretec Feira de Santana 2026 e canal WhatsApp (`5575991019893`).
   - Simulador financeiro e layouts mobile atualizados com os botões e cores de caminhos.

5. **Testes Arquiteturais e Unitários:**
   - Atualizar `tests/Architecture.Tests/DesignSystemArchitectureTests.cs` e `tests/WebApp.Blazor.UnitTests/DesignSystem/DesignTokensTests.cs` para validar a presença dos novos tokens oficiais.
   - Criar `BrandLogoAndWidgetsTests.cs` com testes bUnit para os novos componentes.

## Consequências
- **Benefícios:**
  - O produto agora reflete 100% da identidade visual oficial estabelecida no `DESIGN.md`.
  - Suporte completo a temas Claro e Escuro sem cintilação.
  - Testes arquiteturais garantem ausência de cores hexadecimais literais e estilos inline em todos os arquivos CSS isolados.
- **Custos:**
  - Carregamento de fontes externas via Google Fonts requer conectividade de rede no primeiro carregamento do navegador do cliente (com fallbacks locais especificados nas pilhas de fontes CSS).

