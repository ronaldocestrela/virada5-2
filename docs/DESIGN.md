# Virada 5x2 · Design e referência do projeto

Tudo o que foi usado para criar a Virada 5x2: identidade, cores, letras, componentes, telas, apresentação, textos, dados e fontes. Serve de guia para qualquer peça nova (site, slides, post, folder) sair com a mesma cara.

Última atualização: 12/11/2026.

---

## 1. O que é a Virada 5x2

| Item | Definição |
|---|---|
| Nome | Virada 5x2 (nome de trabalho; consultar Junta Comercial e INPI antes de registrar) |
| O que faz | Consultoria que leva empresas da escala 6x1 para a 5x2 sem perder performance |
| Frase de posicionamento | Redesenhamos a escala função por função: cumpre a lei com o menor custo e sem deixar ninguém descoberto |
| Chamada curta | A virada é agora. |
| Origem | Projeto em equipe do Empretec, Feira de Santana, novembro de 2026 |
| Cliente | Empresas de 5 a 300 pessoas em 6x1: varejo, supermercados, alimentação, hospedagem, atacado, logística, clínicas, vigilância e limpeza |
| Base | Feira de Santana (BA) |
| Site (guia gratuito) | https://h3rcs.github.io/virada_5x2/ |
| Repositório | https://github.com/H3rcs/virada_5x2 |
| WhatsApp da equipe | (75) 99101-9893 (no código: `5575991019893`) |

### Conceito visual: "a virada do dia"

A marca conta uma passagem: da semana de 6x1 para a de 5x2, como a noite que vira amanhecer.

- Tema claro = amanhecer (céu pêssego no topo, papel azulado embaixo).
- Tema escuro = noite azul-marinho com brilho laranja nos cartões e ícones.
- O laranja é o sol nascendo; o amarelo é a luz (prazo, destaque); o azul-marinho é a base sólida.
- Sem estrelas e sem partículas no fundo (retiradas a pedido; o fundo é só gradiente e brilhos suaves).

---

## 2. Marca

### Símbolo (sol nascendo no horizonte)

Quadrado arredondado azul-marinho, meio sol laranja sobre uma linha amarela, três raios amarelos.

```html
<svg viewBox="0 0 32 32" aria-hidden="true">
  <rect width="32" height="32" rx="9" fill="#10243E"/>
  <path d="M5 22h22" stroke="#FFD43B" stroke-width="2.4" stroke-linecap="round"/>
  <path d="M9 22a7 7 0 0 1 14 0" fill="#E8590C"/>
  <path d="M16 8v3M8.5 11.5l2 2M23.5 11.5l-2 2" stroke="#FFD43B" stroke-width="2.2" stroke-linecap="round"/>
</svg>
```

### Logotipo

- "Virada" na cor do texto + "5x2" em laranja (`#E8590C` no claro, `#FF7A2E` no escuro).
- Letra: Bricolage Grotesque, peso 800, largura condensada (`font-stretch: 88%`), 23px no topo do site.
- Símbolo à esquerda, 30×30px, 8px de espaço até o texto.

### Créditos e parcerias

- Linha de crédito usada: "Projeto criado por participantes do Empretec · Feira de Santana · 2026".
- Não usar logo do Sebrae nem do Empretec: daria a entender patrocínio que não existe. Citar só em texto.

---

## 3. Cores

### 3.1 Site, tema claro

| Token | Hex | Uso |
|---|---|---|
| `--tinta` | `#10243E` | Texto principal, títulos, símbolo |
| `--suave` | `#56637A` | Texto secundário, legendas |
| `--papel` | `#F3F5FA` | Fundo da página |
| `--cartao` | `#FFFFFF` | Fundo de cartões |
| `--linha` | `#D6DCE7` | Bordas e divisórias |
| `--laranja` | `#E8590C` | Cor da marca, botões, passo atual |
| `--verde` | `#0B7A53` | Resultado positivo, "dá conta" |
| `--azul` | `#1C4FA1` | Rodízio, dias de trabalho |
| `--amarelo` | `#FFD43B` | Prazo, anel de contagem, destaque no herói |
| `--alerta` | `#C4281C` | Resultado negativo, função descoberta |
| `--campo` | `#FFFFFF` | Fundo de campos e chips |
| `--borda` | `#8A95A8` | Borda de campos |
| `--ok-bg` | `#D7F0E5` | Fundo de aviso positivo |
| `--fraco` | `#7A8AA3` | Itens desativados |

### 3.2 Site, tema escuro

| Token | Hex |
|---|---|
| `--tinta` | `#E9EEF7` |
| `--suave` | `#A7B4CA` |
| `--papel` | `#0B1524` |
| `--cartao` | `#152238` |
| `--linha` | `#2B3C58` |
| `--laranja` | `#FF7A2E` |
| `--verde` | `#3FCB98` |
| `--azul` | `#78A6F5` |
| `--amarelo` | `#FFD43B` |
| `--alerta` | `#FF7466` |
| `--campo` | `#0F1B2E` |
| `--borda` | `#5F7191` |
| `--ok-bg` | `#12382C` |

O tema segue o sistema do aparelho e tem botão "Tema claro / Tema escuro" no topo (`data-theme` no `<html>`, guardado no aparelho).

### 3.3 Cores dos quatro caminhos

Cada estratégia tem cor e ícone fixos, em todas as peças.

| Caminho | Cor (claro) | Degradê do ícone | Ícone |
|---|---|---|---|
| Rodízio de folgas | `#1C4FA1` | `#1C4FA1 → #4D86E0` | setas em ciclo |
| Fechar um dia | `#E8590C` | `#E8590C → #F2A03C` | cadeado |
| 1 hora a menos | `#0B7A53` | `#0B7A53 → #35C08E` | relógio |
| Escala 12x36 | `#7A3E9D` | `#5B2E91 → #9A6AD6` | lua |

### 3.4 Cores dos grupos de folga (escala por função)

Claro: `#1C4FA1`, `#0B7A53`, `#7A3E9D`, `#B45309`, `#0E7490`, `#9D174D`, `#4D7C0F`.
Escuro: `#78A6F5`, `#3FCB98`, `#C39BE8`, `#F2A65A`, `#56C7DE`, `#F08BB4`, `#A5D36A`.

### 3.5 Degradês e efeitos

| Nome | Claro | Escuro |
|---|---|---|
| Céu (fundo) | `linear-gradient(180deg, #FFE3C4 0, #FFF1E2 180px, #F3F5FA 520px)` | `linear-gradient(180deg, #070B1A 0, #0B1524 420px)` |
| Brilho laranja (canto superior direito) | `rgba(232,89,12,.16)` | `rgba(255,122,46,.20)` |
| Brilho azul (lateral esquerda) | `rgba(28,79,161,.12)` | `rgba(120,166,245,.20)` |
| Herói | `linear-gradient(155deg, #10243E 0, #27306B 46%, #B8431A 82%, #F08A3C 100%)` | `linear-gradient(155deg, #0A1022 0, #1B1F52 46%, #7A2E12 84%, #E8590C 100%)` |
| Botão principal | `linear-gradient(180deg, #FF7A2E, #E8590C)` | igual |
| Vidro (topo e barra de baixo) | `rgba(255,255,255,.86)` + desfoque 14px | `rgba(13,22,40,.82)` + desfoque 14px |
| Neon (só no escuro) | nenhum | `0 0 22px -4px` na cor do elemento |

### 3.6 Apresentação (slides)

| Papel | Hex |
|---|---|
| Fundo escuro (capa, evidência, guia, conta, fechamento) | `#14213D` |
| Fundo claro | `#FBF7F1` |
| Segundo fundo claro | `#F3ECE1` |
| Slide de destaque (a solução) | `#E8590C` com texto `#14213D` |
| Laranja de texto no claro | `#C2410C` |
| Laranja de texto no escuro | `#FF8A3D` |
| Amarelo de destaque | `#F5C542` |
| Corpo no claro / no escuro | `#4A5568` / `#C9D3E3` |
| Legenda no claro / no escuro | `#5F6B7A` / `#9AA8BF` |
| Cartão claro / cartão escuro | `#FFFDF9` / `#1D2B4A` |
| Linha clara / linha escura | `#E7DED0` / `#2E3E63` |
| Azul / verde / roxo de apoio | `#2F5BD3` / `#1E7F52` / `#6D4AB8` |

### 3.7 Plano de negócio (PDF)

Azul-marinho `#10243E`, laranja `#E8590C`, verde `#0B7A53`, azul `#1C4FA1`, amarelo `#FFD43B`, texto suave `#566377`, linha `#D5DBE5`, fundo `#F3F5F9`. Letra Inter.

### Regras de cor

- Texto comum com contraste mínimo de 4,5:1; títulos grandes, 3:1.
- Sobre amarelo, sempre texto escuro.
- Cor nunca fala sozinha: positivo/negativo também aparece no texto ("Melhor conta", "− R$ 1.846", "Falta gente").
- Para diferenciar, preferir azul × laranja a vermelho × verde.

---

## 4. Tipografia

| Papel | Família | Pesos | Observações |
|---|---|---|---|
| Títulos, números, botões, logotipo | **Bricolage Grotesque** | 400 a 800 | Eixo de largura usado entre 85% e 92% (mais condensada) |
| Texto | **Onest** | 400 a 700 | Corpo, rótulos, campos |
| Plano em PDF | Inter | 400 a 800 | Com `font-feature-settings: "calt" 0` nas fontes, para "6x1" não virar "6×1" em links |

Link do Google Fonts usado no site:

```html
<link href="https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:opsz,wdth,wght@12..96,75..100,400..800&family=Onest:wght@400..700&display=swap" rel="stylesheet">
```

Pilhas de reserva: `'Bricolage Grotesque','Arial Narrow',Arial,sans-serif` e `'Onest',system-ui,-apple-system,'Segoe UI',sans-serif`.

### Escala no site (celular)

| Elemento | Tamanho | Peso |
|---|---|---|
| Número do anel de contagem | 40px | 800, `letter-spacing:-.03em` |
| Título de tela (h2) | 29px | 800, `letter-spacing:-.025em`, largura 88% |
| Logotipo | 23px | 800 |
| Botão | 17px | 800 |
| Texto do herói | 15,5px | 600 |
| Texto comum | 16px | 400 |
| Chips | 15px | 600 |
| Rótulos em caixa alta (sobretítulo) | 11 a 12px | 700, espaçamento .1em a .14em |
| Passos do topo | 10,5px | 700, caixa alta |

No computador (a partir de 1000px): título da tela inicial 40px, título lateral 34px, texto da abertura 17,5px.

### Escala nos slides (tela 1920×1080)

200 (nome na capa) · 120 (frase final) · 96 (números grandes e frase da solução) · 72 (títulos) · 44 / 40 (subtítulos e nomes de cartões) · 32 · 28 · 26 · 24 (mínimo, rodapés e fontes).

### Regras de texto

- Português do Brasil, frases curtas e diretas.
- Sobretítulos em caixa alta, laranja, espaçados.
- Aspas tipográficas: “ ”.
- Números com vírgula decimal e ponto de milhar: R$ 2.429,41; 14,8 mi; 596,5 mil.
- Datas: 16/12/2026.

---

## 5. Forma, espaço e profundidade

| Item | Valor |
|---|---|
| Raio de cartões | 18px (celular) · 24px nos slides |
| Raio de botões | 14px |
| Raio de chips e abas | pílula (`99px`) |
| Raio do herói | 24px (celular) · 30px (computador) |
| Raio de ícones | 15px (48px) · 12px (38px) |
| Espaço entre blocos | 16px no celular · 20px no computador · 36px entre topo e conteúdo |
| Margem lateral | 16px no celular · 40px no computador |
| Largura máxima no computador | 1240px |
| Sombra (claro) | `0 1px 0 rgba(255,255,255,.9) inset, 0 10px 28px -12px rgba(16,36,62,.28)` |
| Sombra (escuro) | `0 1px 0 rgba(255,255,255,.06) inset, 0 14px 34px -14px rgba(0,0,0,.8)` |
| Sombra do botão | `0 10px 22px -10px #E8590C, 0 1px 0 rgba(255,255,255,.35) inset` |
| Faixa de cor dos indicadores | 6px no topo, em degradê |
| Faixa de cor dos caminhos | 6px na lateral esquerda |

---

## 6. Componentes

**Topo (cabeçalho).** Fixo, efeito vidro, linha embaixo. Símbolo + logotipo à esquerda, botão de tema à direita. Do passo 1 ao 4 mostra a barra de passos. No computador, os passos ficam no meio do topo.

**Barra de passos.** Cinco bolinhas numeradas: Empresa, Horário, Equipe, Dinheiro, Plano. Feito = laranja cheio com ✓; atual = contorno laranja com halo; futuro = cinza. Linha laranja liga os passos concluídos.

**Herói (abertura).** Cartão com o degradê do herói, anel de contagem amarelo (dias até 16/12/2026) e a faixa "A semana de quem trabalha" com sete dias S T Q Q S S D em azul. A segunda-feira gira e vira folga laranja listrada, mostrando "6x1 → 5x2" (animação de 5,5s em loop).

**Título com ícone.** Ícone quadrado arredondado de 48px em degradê laranja (ou na cor do caminho) + sobretítulo + título.

**Ícones.** Traço de 2px, pontas arredondadas, 24×24. Conjunto: rodízio, fechar, hora, 12x36, loja, calendário, equipe, cifrão, lei, plano, enviar, grade, rumo, sol.

**Botões.**
- Principal: degradê laranja, texto branco em Bricolage. Ao tocar, afunda 2px.
- Secundário: cartão com borda.
- Fixos numa barra de vidro embaixo (Voltar / Continuar). No computador, ficam à direita com no mínimo 210px de largura.

**Chips.** Pílulas para escolhas rápidas (setor, sim/não/não sei, dias). Selecionado = preenchido com a cor forte.

**Cartões de função.** Nome da função, "Pessoas" e "Mínimo por dia" com botões − e +. No computador, em duas colunas a partir de 1200px.

**Indicadores (tiles).** Quatro números no resultado: Prazo, Equipe, Pessoas a mais, Resultado no mês. Faixa de cor no topo: laranja (prazo), azul-marinho (equipe), verde (positivo), vermelho → laranja (negativo).

**Cartões de caminho.** Ícone, nome, selo "Melhor conta" em verde, descrição e três números (pessoas a mais, custo no mês, vendas perdidas), além do "Resultado no mês". O escolhido ganha borda e brilho laranja. Caminho que não serve fica apagado.

**Controles deslizantes.** "Aumento de preço", "Clientes que você perde com o aumento", "Vendas do período fechado que voltam em outro dia". Trilho laranja.

**Abas do resultado.** Caminhos, Escala, Custo, Lei, Plano, Enviar. No celular ficam numa faixa fixa que rola de lado; no computador, numa grade 2×3 no painel lateral fixo.

**Checklist do plano.** Tarefas com data; ao marcar, fica riscada e guardada no aparelho.

**Enviar.** Botão "Falar com a equipe Virada 5x2 no WhatsApp", que abre `https://wa.me/5575991019893?text=` com o resumo do plano, e "Copiar resumo".

---

## 7. Telas e layout

### Fluxo do guia

| Tela | Conteúdo |
|---|---|
| 0 · Início | Herói com contagem e semana animada, "Compare os caminhos e monte o plano da sua empresa", 4 itens numerados, aviso de privacidade, crédito |
| 1 · Empresa | Nome (opcional) e setor (presets: loja/comércio, supermercado, restaurante/bar, hotel/pousada, atacado/CD, clínica, outro) |
| 2 · Horário | Dias abertos, movimento por dia (fraco/médio/forte), horas por dia, escala atual e horas semanais |
| 3 · Equipe | Funções com pessoas e mínimo por dia |
| 4 · Dinheiro | Acordo com o sindicato (sim/não/não sei), salário médio (padrão R$ 2.429,41), se pode contratar, se atende o público, faturamento e margem (opcionais) |
| 5 · Resultado | Estratégia escolhida, indicadores, comparação dos caminhos, escala por função, custo, lei, plano com datas, enviar, "Como a conta é feita" e fontes |

### Celular

Coluna única, um passo por tela, barra fixa embaixo com Voltar e Continuar. Sem rolagem lateral.

### Computador (a partir de 1000px)

- Topo em largura total com os passos no centro.
- Início em duas colunas: herói à esquerda e texto com o botão "Começar o diagnóstico" à direita.
- Passos 1 a 4: título e explicação fixos à esquerda (340px; 280px entre 1000 e 1199px) e perguntas à direita (até 820px).
- Resultado: painel lateral fixo (380px) com título, indicadores e abas em grade; conteúdo à direita; caminhos em grade 2×2.

### Movimento

- Entrada dos blocos ao trocar de tela: sobe e aparece, em cascata (0,04s a 0,24s).
- A semana do herói vira de 6x1 para 5x2 em loop.
- Botões e chips encolhem levemente ao toque.
- Com "reduzir movimento" ligado no aparelho, tudo para.

---

## 8. Apresentação (Empretec, 13/11/2026)

Deck de 19 slides em 1920×1080, Bricolage Grotesque + Onest, com falas nas anotações do apresentador.

1. Capa: "Virada 5x2", anel amarelo com "33 dias até 16/12/2026"
2. O problema: 14,8 mi · 596,5 mil · até +7,8%
3. A regra e o prazo: linha do tempo de 16/10/2026 a 16/12/2027
4. Três pressões: prazo, custo, risco
5. Quem sente mais: barras por setor e o que planejam os pequenos negócios
6. A evidência: Gurumê, Savegnago, C&A, piloto FGV
7. A solução (slide laranja)
8. O método em 5 passos
9. Os 4 caminhos
10. O guia no celular (prints)
11. O guia no computador (print)
12. Concorrência
13. Modelo de negócio e canais
14. A conta do cliente de 30 pessoas
15. Projeção de 12 meses (barras mês a mês)
16. Os próximos 90 dias e metas
17. Riscos
18. As 10 CCEs do Empretec
19. Fechamento: "A virada é agora", QR code do site e WhatsApp

Padrão dos slides: margem de 128px, sobretítulo laranja em caixa alta, título de 72px no topo, rodapé de 24px com fonte à esquerda e número da página à direita.

---

## 9. Voz e conteúdo

- Fala com o dono do negócio: direta, prática, sem juridiquês.
- Não promete o que não dá para provar. Os números das empresas são relatos delas próprias, e isso é dito no texto.
- Toda estimativa da equipe é marcada como premissa.
- O guia não é assessoria jurídica: "Este guia orienta o planejamento e não substitui advogado nem contador."
- Privacidade: "Suas respostas ficam só neste aparelho."
- Palavras da casa: caminhos, virada, folga, escala por função, ninguém descoberto, melhor conta.

---

## 10. Como o guia faz a conta

- **Jornada nova:** 42 horas com acordo coletivo; sem acordo (ou "não sei"), 40 horas e 8 horas por dia.
- **Rodízio:**
  - Folgas fixas nos dias fechados (até 2); o resto das folgas vai para os dias de menor movimento.
  - Para cada função, procura o menor número de contratações e de grupos de folga que mantenha o mínimo de pessoas no pior dia.
  - Mínimo de grupos: 2 com uma folga a distribuir, 3 com duas.
- **Fechar um dia:**
  - Fecha o dia mais fraco e todos folgam juntos.
  - Vendas perdidas = faturamento do dia × (1 − parte que volta em outro dia).
  - Aceita aumento de preço e perda de clientes.
- **1 hora a menos:** corta a hora mais fraca e mantém os dias.
- **12x36:**
  - Duas equipes, cada uma com mínimo × ⌈horas abertas ÷ 12⌉ pessoas.
  - Média de cerca de 42 horas por semana, acima do teto de 40 horas em 2027.
- **Custo:** pessoas a mais × salário. Resultado = ganho com preço − custo de pessoal − margem perdida com vendas perdidas.
- **Peso do movimento:** fraco 0,6 · médio 1 · forte 1,5.
- **Ordem:** os caminhos que servem vêm primeiro, ordenados pelo melhor resultado no mês. O usuário pode escolher outro.

---

## 11. Dados usados

### A lei

| Dado | Valor |
|---|---|
| Emenda Constitucional 141 | Promulgada em 16/10/2026 (Senado: 75 votos a 1) |
| Duas folgas por semana | Obrigatórias a partir de 16/12/2026 |
| Teto na transição | 42 horas, seguro só com convenção ou acordo coletivo; sem isso, 40 horas e 8 horas por dia |
| Teto definitivo | 40 horas a partir de 16/12/2027 |
| Decreto regulamentador | Previsto para 9/11/2026 e adiado |
| STF | A CNC pediu mais prazo de transição (relator: Flávio Dino) |
| PEC paralela | Em preparo no Senado para categorias com jornadas especiais |
| Folga não concedida | Paga em dobro; hora acima do limite custa no mínimo 50% a mais |

### Mercado

| Dado | Valor |
|---|---|
| Empregos em 6x1 no Brasil | 14,8 milhões (29,9% dos vínculos formais, dez/2025) |
| Em médias e grandes empresas / em micro e pequenas | 7,9 mi / cerca de 6,9 mi |
| Bahia | 596.501 trabalhadores em 6x1 (32,52%) |
| Setores (% em 6x1) | Transporte aéreo 53,2% · Alojamento 52,0% · Alimentação 47,1% · Comércio 42,2% · Média 33,2% |
| Empresas com até 9 empregados | Cerca de 88% dos vínculos com 44 horas |
| Feira de Santana | 2º maior saldo de empregos formais da Bahia em 2025 (6.208 vagas) |
| Rotatividade no comércio paulista | 63,3% em 12 meses (FecomercioSP) |
| Salário médio de admissão | R$ 2.429,41 (Caged, set/2026) |

### Impacto na folha

| Estimativa | Valor |
|---|---|
| Ministério do Trabalho | +4,7% em média (1,6% a 10,5% por setor) |
| CNI | +7% em média |
| Ipea (44h → 40h em 5x2) | +7,84% |
| CNI, até 9 funcionários, só com hora extra | até +13% |
| CNI | Hora extra custa 50% mais do que contratar, nas pequenas |

### Pequenos negócios (Sebrae, Pulso)

51% dizem que não terão impacto. Dos que esperam impacto: 29% vão subir preços, 24% reorganizar turnos, 21% cortar pessoal e 22% não sabem o que fazer.

### Casos

| Empresa | Resultado |
|---|---|
| Gurumê (restaurantes) | −30% de rotatividade, menos faltas, jornada média de cerca de 38h |
| Grupo Savegnago (77 supermercados) | +120% de procura por vagas; 44h em 5 dias; rotatividade estável (4,8% ao mês) |
| C&A (60 lojas em teste) | Faltas caíram; custos pressionados; o domingo é o maior desafio |
| RD Saúde e DPSP | Eliminaram a 6x1 nas lojas; números não divulgados |
| Piloto 4 Day Week Brasil (FGV, 19 empresas) | 71,5% relataram mais produtividade e 60,3%, mais engajamento |

### Concorrência

Escala Sebrae (simulador gratuito, desde 21/10/2026), calculadoras contábeis, sistemas de RH e ponto, advogados e contadores. A Virada 5x2 entra no desenho da operação por função, na implantação e na medição.

---

## 12. Modelo de negócio (premissas da equipe)

| Pacote | Porte | Preço | Entrega |
|---|---|---|---|
| Express | até 15 pessoas | R$ 890 | Uma visita, escala pronta, vagas e roteiro legal em 3 dias |
| Completa | 16 a 60 | R$ 2.900 | 5 passos, caminhos comparados, piloto e painel em 30 dias |
| Multiturno | 61 a 300 | R$ 7.900 | Vários turnos, plano de contratação e 90 dias de medição |
| Acompanhamento | depois da virada | R$ 490/mês | Indicadores, ajustes e preparação para as 40h de 2027 |

- **Método em 5 passos:**
  - Mapear (2 a 3 dias)
  - Simular (2 dias)
  - Negociar (1 semana)
  - Implantar (1 a 2 semanas)
  - Medir (90 dias)
- **Indicadores:** cobertura mínima, horas extras, faltas, rotatividade e produção por hora.
- **Conta do cliente de 30 pessoas:**
  - Folha de R$ 72.882.
  - Impacto de R$ 3.425 a R$ 5.102 por mês.
  - A hora extra no teto de 42h custa R$ 17,35. Duas horas extras por semana para cada um dão R$ 4.508 por mês.
  - O pacote Completo (R$ 2.900) se paga em menos de um mês.
- **Projeção de 12 meses:**
  - Receita: R$ 134.070 (R$ 105.160 de projetos e R$ 28.910 de acompanhamento).
  - Impostos e custos: R$ 37.321. Resultado: R$ 96.749.
  - 46 projetos (24 Express, 18 Completos, 4 Multiturno) e 881 horas, ou R$ 110 por hora.
- **Receita por mês (R$):** nov 4.680 · dez 21.050 · jan 21.140 · fev 10.430 · mar 17.440 · abr 7.620 · mai 6.730 · jun 14.630 · jul 7.220 · ago 7.710 · set 7.710 · out 7.710.
- **Custos:**
  - Simples Nacional, Anexo V, 15,5% (Anexo III a 6% com pró-labore acima de 28%).
  - Contador R$ 350 por mês, ferramentas R$ 150 por mês, divulgação R$ 400 por mês.
  - Investimento inicial de cerca de R$ 2.500.
- **Formalização:** CNAE 7020-4/00 (não pode ser MEI), microempresa no Simples Nacional, sociedade limitada.
- **Canais:** guia gratuito → WhatsApp, contadores e advogados, CDL e sindicatos patronais, Sebrae e a rede do Empretec.
- **Metas:**
  - 10 entrevistas e 2 pilotos até 20/11/2026.
  - 12 projetos e R$ 25 mil até 31/12/2026.
  - 2 clientes em acompanhamento até 31/01/2027.
  - 4 casos documentados até 28/02/2027.
  - 46 projetos e R$ 134 mil até 31/10/2027.

---

## 13. Arquivos do projeto

| Arquivo | O que é |
|---|---|
| `index.html` | Guia Virada 5x2 (arquivo único, HTML + CSS + JavaScript, sem servidor) |
| `plano/plano.pdf` | Plano de negócio, A4, 10 páginas |
| `plano/plano.html` | Fonte do PDF |
| `DESIGN.md` | Este guia |
| Apresentação | Deck "Virada 5x2 · Apresentação Empretec" no Claude |

Detalhes técnicos do guia: estado guardado no aparelho (`virada5x2-app-v1`), tema guardado em `virada5x2-tema`, prazo fixo em 16/12/2026.

---

## 14. Fontes

1. Agência Brasil, 16/10/2026. Congresso promulga emenda do fim da 6x1.
2. Senado Notícias, out/2026. Senado aprova PEC que acaba com a escala 6x1.
3. ConJur, 25/10/2026. Fim da escala 6x1: o que já vale e o que ainda vai parar na Justiça.
4. Folhapress em Mais Goiás, 16/10/2026. 3 em cada 10 empregos formais têm escala 6x1 (4intelligence, dados do MTE).
5. CUT, 2026, com dados do Dieese e do MTE. Setores mais afetados pela escala 6x1.
6. Bahia Notícias, mai/2026. Mais de 596,5 mil trabalhadores baianos (MTE).
7. Folhapress em Jornal de Brasília, 24/04/2026. Estudo do Ipea com a Rais 2023.
8. Vermelho, 12/03/2026. Relatório do MTE sobre viabilidade e impactos da redução da jornada.
9. Conta Azul, 2026. Calculadora da escala 6x1, com estimativas da CNI de fev/2026.
10. Sebrae, Pulso dos Pequenos Negócios, 12ª edição, em Contracs, 18/08/2026.
11. Só Notícia Boa, 07/11/2026. Teste da escala 5x2 na C&A, farmácias e supermercados.
12. Jornal de Alagoas, 17/04/2026. Empresário adota escala 5x2 em restaurantes.
13. Times Brasil, 16/10/2026. Comércio paulista vê rotatividade subir a 63,3% (FecomercioSP).
14. Agência Gov, 04/11/2026. Novo Caged de setembro de 2026.
15. Folhapress em ICL Notícias, 09/11/2026. Decreto que regulamenta fim da 6x1 deve sair após viagem de Lula.
16. Exame, out/2026. Senado prepara PEC para deixar categorias fora das regras.
17. Contábeis, out/2026. Sebrae cria simulador para reorganizar escala de trabalho.
18. CSB, 09/11/2026. O que fazer se a empresa descumprir a nova jornada.
19. Contabilidade Zen, 2026. CNAE para consultoria empresarial.
20. Agência Brasil, 04/11/2026. Governo prevê decreto do fim da escala 6x1.
21. FGV EAESP e 4 Day Week Brazil, 2024, em Site Contábil.
22. VLV Advogados, out/2026. O que fazer se a empresa não der as duas folgas.
23. Jornal Grande Bahia, 2026. Feira de Santana registra saldo de 6,2 mil empregos formais em 2025.
24. Canal Rural, nov/2026. Governo adia decreto sobre transição do fim da escala 6x1.
25. Folha de Pernambuco, nov/2026. Flávio Dino é sorteado relator da ação da CNC que busca adiar o fim da escala 6x1.

Preços, metas, custos e projeções são premissas da equipe. Este material não substitui orientação jurídica ou contábil.
