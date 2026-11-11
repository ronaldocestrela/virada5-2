# 14. Adotar Parsing Nativo de CSV/XLSX e Estratégia de Diagnóstico de Demanda

## Status
Aceito

## Contexto
Na Subfase 3.1 do roadmap (`Importação & Diagnóstico de Demanda`), o sistema precisa importar históricos de vendas de PDV nos formatos CSV e XLSX — além de suportar entrada manual da matriz semanal — para popular e atualizar a agregação `DemandSlot`, seguida pelo diagnóstico semanal de picos, vales e índice de ociosidade (`GenerateDemandDiagnosisUseCase`).

Dois requisitos arquiteturais precisam ser equilibrados:
1. **Pureza da camada `Application` (Regra 1.3 do `agents.md`):** `Application` deve depender apenas de `Domain`, evitando acoplamento com infraestrutura, UI ou bibliotecas pesadas de planilhas que ampliem a superfície de manutenção e de dependências transitivas no núcleo.
2. **Consistência da matriz semanal e da política `AsNoTracking()` (ADRs 0006, 0011 e 0012):**
   - Históricos transacionais de PDV frequentemente cobrem múltiplas semanas e podem omitir horas abertas sem movimentação.
   - `DemandSlotRepository.GetByCompanyAsync` retorna entidades com `AsNoTracking()`, exigindo invocação explícita de `Update` antes de `SaveChangesAsync`.

## Decisão
1. **Parsing CSV e XLSX com BCL Nativa (.NET 10):**
   - Implementar `CsvSalesHistoryParser` e `XlsxSalesHistoryParser` em `Application/UseCases/Demand/Parsing/` utilizando exclusivamente recursos nativos da BCL (`System.IO.Compression.ZipArchive` e `System.Xml.Linq` para OOXML SpreadsheetML em `.xlsx`; parser RFC-4180 para `.csv` com autodetecção de delimitador `,`/`;`, UTF-8 com/sem BOM e suporte a formatação numérica `pt-BR` e invariante).
   - Não suportar o formato legado binário `.xls` (BIFF8); arquivos incompatíveis ou corrompidos retornam `Result.Failure` sem lançamento de exceções de negócio.
2. **Normalização e Agregação Multi-Semana:**
   - Suportar dois layouts tabulares: **Transacional (PDV)** (`DataHora` ou `Data` + `Hora` e coluna de volume) e **Matriz Semanal Direta** (`DiaSemana`, `Hora`, `Volume`), além da **Entrada Manual**.
   - No layout transacional, os volumes são somados por `(DateOnly, Hour)` e agregados pela **média por ocorrência distinta do `DayOfWeek`** no período importado, representando uma semana típica do estabelecimento.
   - Por padrão (`FillMissingOperatingHoursWithZero = true`), horas pertencentes à janela de operação da `Company` (`Company.IsOpenAt`) ausentes no histórico importado são preenchidas com volume `0` para evidenciar vales ociosos.
3. **Sincronização Incremental e Diagnóstico Operacional:**
   - `ParseSalesHistoryUseCase` sincroniza `DemandSlot`s existentes via `UpdateVolume` + `IDemandSlotRepository.Update`, adiciona novos via `AddRangeAsync` e remove obsoletos via `RemoveRange`, respeitando o índice único `(TenantId, CompanyId, DayOfWeek, Hour)`.
   - `GenerateDemandDiagnosisUseCase` calcula o pico global a partir dos slots em horário aberto (`Company.IsOpenAt`), recalcula `RelativeVolume` e `Classification` via `DemandSlot.EvaluateRelativeDemand`, invoca `IDemandSlotRepository.Update` (ADR 0012) e `IUnitOfWork.SaveChangesAsync`, e entrega picos, vales abertos, índice de ociosidade e recomendação dos 2 dias de menor demanda para a transição 5x2. Registros fora da janela aberta são preservados, porém sinalizados nos avisos e excluídos da lista de vales abertos.

## Consequências
- **Benefícios:**
  - Zero pacotes NuGet adicionados ao projeto `Application`, preservando o isolamento da Clean Architecture e a velocidade de compilação/testes.
  - Consistência garantida entre o diagnóstico calculado, a matriz persistida no SQL Server e os futuros motores financeiro (3.2) e de escala 5x2 (3.3).
- **Custos:**
  - Suporte restrito a planilhas `.xlsx` padrão OOXML (Excel 2007+) e arquivos `.csv`/texto delimitado, exigindo que fórmulas em `.xlsx` contenham valor em cache (`<v>`).

