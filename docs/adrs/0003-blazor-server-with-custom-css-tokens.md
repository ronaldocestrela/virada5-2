# 3. Adotar Blazor Server Puro com Design Tokens CSS Customizados

## Status
Aceito

## Contexto
O SaaS de Transição 5x2 exige uma interface altamente interativa para edição de escalas semanais, visualização de mapa de calor de demanda, simulador financeiro e tela mobile leve para os colaboradores. No MVP, é necessário evitar a sobrecarga de download e hidratação de WebAssembly (WASM) e impedir a dependência de bibliotecas de componentes pesadas (como MudBlazor, Radzen ou AntDesign), que dificultam a padronização visual fina e aumentam o acoplamento com terceiros.

## Decisão
1. Adotar **Blazor Server** (`.NET 10`, modo interativo `InteractiveServer` via SignalR) no projeto `WebApp.Blazor`, sem misturar WebAssembly no MVP.
2. Construir o **Design System atômico do zero** sob `Components/DesignSystem/{Componente}/`, com arquivos `.razor` pequenos, desacoplados e estilização encapsulada por **CSS Isolation** (`.razor.css`).
3. Centralizar toda a identidade visual (cores, escalas de espaçamento, tipografia, raios de borda e sombras) em **CSS Custom Properties** declaradas em `wwwroot/css/tokens.css`, sendo proibido o uso de cores ou medidas hardcoded no markup ou nos arquivos `.razor.css`.

## Consequências
- **Benefícios:**
  - Carregamento inicial rápido e execução direta das regras no servidor, com integração natural ao ASP.NET Core Identity e à camada `Application`.
  - Controle total sobre acessibilidade, responsividade mobile e consistência visual sem peso de bibliotecas externas.
- **Custos:**
  - Maior esforço inicial na Fase 4 para construção dos componentes atômicos (`AppButton`, `AppCard`, `AppModal`, `DemandHeatmap`, `RosterGrid`).
  - Necessidade de disciplina contínua e revisão de CSS para evitar valores literais fora de `tokens.css` e de gerenciar conexões SignalR em produção.

