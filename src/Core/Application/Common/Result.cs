global using Domain.Common;

namespace Application.Common;

// Tipos Error, Result e Result<TValue> são definidos em Domain.Common (conforme ADR 0004)
// e reexportados globalmente na camada Application para preservar o isolamento da Clean Architecture.
public static class ResultPatternMarker;

