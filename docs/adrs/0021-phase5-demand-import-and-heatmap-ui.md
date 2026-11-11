# 21. Padronizar Leitura de Workspace de Demanda, Upload Drag-and-Drop e Fallback Manual na Fase 5

## Status
Aceito

## Contexto
Na **Subfase 5.2 (Tela 2: Importação e Mapa de Demanda)**, a interface Blazor Server precisa apresentar:
1. Upload de histórico de vendas (`.csv` e `.xlsx`) via drag-and-drop ou seleção de arquivo;
2. Fallback de preenchimento manual rápido da matriz semanal (~3 minutos) restrito à janela de funcionamento cadastrada na agregação `Company`;
3. Exibição reativa do componente `DemandHeatmap` após importação, salvamento manual ou ajuste pontual de slots.

Duas restrições técnicas e arquiteturais motivaram esta decisão:
- **Pureza de Apresentação e Leitura sem Mutações:** `GenerateDemandDiagnosisUseCase` recalcula e persiste alterações via `IUnitOfWork.SaveChangesAsync`. Invocá-lo no carregamento inicial (`OnInitializedAsync`) provocaria escritas desnecessárias no banco em requisições de leitura, além de falhar quando ainda não há slots importados. Além disso, a página Blazor precisa saber quais células `(DayOfWeek, Hour)` estão abertas conforme `Company.IsOpenAt` sem duplicar regras de domínio na UI.
- **Compatibilidade de Streams no Blazor Server:** `IBrowserFile.OpenReadStream` no Blazor Server (`BrowserFileStream`) rejeita leituras síncronas (`NotSupportedException`), enquanto `StreamReader.ReadToEnd` (`CsvSalesHistoryParser`) e `ZipArchive` (`XlsxSalesHistoryParser`) exigem leitura síncrona/seekable.

## Decisão
1. **Caso de Uso Somente Leitura `GetCurrentDemandWorkspaceUseCase` e Calculador Compartilhado:**
   - Extrair a lógica de avaliação de picos, vales, ociosidade, resumos diários e recomendação de 2 folgas para o helper interno `DemandDiagnosisCalculator` em `Application/UseCases/Demand/`, compartilhado entre `GenerateDemandDiagnosisUseCase` (mutação/persistência) e `GetCurrentDemandWorkspaceUseCase` (leitura pura).
   - `GetCurrentDemandWorkspaceUseCase` retorna `DemandWorkspaceDto` contendo `CompanySettingsDto`, a grade de operação (`OperatingHours` + `OperatingSlots` avaliados via `Company.IsOpenAt`) e `DemandDiagnosisDto?` (`null` quando ainda não há slots). Retorna `Error.NotFound("Company.NotFound", ...)` caso o onboarding não tenha sido concluído.
2. **Upload Drag-and-Drop Nativo com Buffer em `MemoryStream`:**
   - Expor a Tela 2 em `/demand` e `/demand/import` (`Components/Pages/Demand/DemandMapping.razor`) protegida por `[Authorize(Roles = "Owner,Manager")]`.
   - Utilizar `<InputFile accept=".csv,.xlsx" />` sobreposto em dropzone estilizada via `tokens.css` com `@ondragenter` / `@ondragleave` nativos (sem JS customizado), copiando o stream de forma assíncrona (limite de 5 MB) para `MemoryStream` antes de chamar `ParseSalesHistoryUseCase` seguido de `GenerateDemandDiagnosisUseCase`.
3. **Fallback Manual Rápido e Reatividade do Mapa de Calor:**
   - Exibir apenas células com `IsOpen == true` para edição numérica na grade semanal e fornecer aceleradores de produtividade (*Aplicar curva típica*, *Replicar dia* e *Zerar matriz*) com totalizadores diários reativos.
   - Integrar `<DemandHeatmap Interactive="true" />` com alternância entre `% Relativo` e `Volume Absoluto` (`ShowAbsoluteVolume`) e painel de ajuste rápido para o slot selecionado.

## Consequências
- **Benefícios:**
  - Zero duplicação da lógica de diagnóstico ou de `Company.IsOpenAt` entre `Application` e `WebApp.Blazor`.
  - Carregamento inicial idempotente sem escritas espúrias no SQL Server.
  - Upload drag-and-drop testável diretamente com `bUnit` e imune a `NotSupportedException` de stream síncrono no Blazor Server.
- **Custos:**
  - Limite explícito de 5 MB por arquivo em memória para importação no circuito SignalR.

