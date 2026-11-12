# Guia GTM: Links de Conversão & QR Codes Integrados ao Ebook

Este catálogo define os links oficiais de conversão com parâmetros UTM e geração de QR Codes SVG via `GtmConversionLinkService`, `GetEbookGtmKitUseCase` e `QrCodeSvgGenerator` (Subfase 7.4).

## 1. Fluxo de Conversão do Leitor do Ebook

1. O leitor escaneia o QR Code impresso no Ebook ou clica no link do capítulo.
2. A aplicação abre a rota pública `/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=...&utm_content=...` (`EbookLanding.razor`).
3. A página registra o evento de telemetria `ebook_landing_view` no OpenTelemetry (`virada5x2.gtm.conversions`, incluindo a tag `utm_content`) e no Serilog com os parâmetros UTM.
4. Ao clicar em **"Criar Conta da Minha Casa"**, os parâmetros UTM são preservados até `/Account/Register`, onde `RegisterTenantOwnerUseCase` cria um novo `TenantId` isolado com papel `Owner`, registra `tenant_owner_registered` (com `utm_content`) e redireciona para `/onboarding`.

## 2. Links Canônicos por Ponto de Contato no Ebook

| Ponto no Ebook | Canal (`utm_medium`) | Campanha (`utm_campaign`) | Conteúdo (`utm_content`) | URL de Destino |
| :--- | :--- | :--- | :--- | :--- |
| **Capa / Prefácio** | `qrcode` | `transicao_5x2` | `prefacio_qr` | `/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=transicao_5x2&utm_content=prefacio_qr` |
| **Cap. 2: Mapa de Demanda** | `qrcode` | `transicao_5x2` | `cap2_demanda` | `/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=transicao_5x2&utm_content=cap2_demanda` |
| **Cap. 4: Simulador 6x1 vs. 5x2** | `qrcode` | `transicao_5x2` | `cap4_simulador` | `/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=transicao_5x2&utm_content=cap4_simulador` |
| **Cap. 6: Botão PDF Interativo** | `pdf_link` | `transicao_5x2` | `cap6_cta_pdf` | `/ebook?utm_source=ebook&utm_medium=pdf_link&utm_campaign=transicao_5x2&utm_content=cap6_cta_pdf` |
| **Contracapa / Checklist Final** | `qrcode` | `transicao_5x2` | `contracapa_qr` | `/ebook?utm_source=ebook&utm_medium=qrcode&utm_campaign=transicao_5x2&utm_content=contracapa_qr` |

## 3. Painel de Exportação & Geração Programática de QR Codes SVG

- **Painel Web (`/gtm/ebook-kit`):** Usuários autenticados com perfil `Owner` ou `Manager` podem visualizar os 5 pontos de contato sincronizados com `PUBLIC_APP_BASE_URL` (`.env`), copiar as URLs rastreadas e baixar os arquivos `.svg` prontos para diagramação (`virada5x2-prefacio_qr.svg`, `virada5x2-cap2_demanda.svg`, etc.).
- **Via Caso de Uso (`GetEbookGtmKitUseCase`):**
  ```csharp
  var kitResult = getEbookGtmKitUseCase.Execute(
      new GetEbookGtmKitQuery(
          Environment.GetEnvironmentVariable("PUBLIC_APP_BASE_URL") ?? "https://app.virada5x2.com.br"));
  ```

O SVG gerado (`QrCodeSvg`) utiliza `shape-rendering="crispEdges"`, zona de silêncio padrão ISO (4 módulos) e `fill="currentColor"` para estilização vetorial de alta resolução em impressão ou web.
