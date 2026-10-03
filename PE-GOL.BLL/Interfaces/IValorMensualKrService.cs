using PE_GOL.DTO.Requests.Okr;
using PE_GOL.DTO.Responses.Okr;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de registro mensual de valores reales de KRs (Spec HU-026 § Lógica BLL).
/// Actor JefeArea (SEC-07); doble compuerta: el OKR padre debe pertenecer al área del JEF
/// y al ciclo activo. Los campos calculados (puntuaciones, semáforo) se calculan aquí (DB-04).
/// </summary>
public interface IValorMensualKrService
{
    /// <summary>GET · Grilla completa del OKR: 12 celdas por KR + ventana editable + agregados.</summary>
    Task<ValorMensualKrGrillaResponse> ObtenerGrillaAsync(Guid okrId, CancellationToken ct = default);

    /// <summary>PUT · UPSERT batch de 1..12 meses + recálculo completo de la cadena (CA #4, CA #5).</summary>
    Task<ValorMensualKrGuardarResponse> GuardarValoresAsync(Guid okrId, Guid keyResultId, ValorMensualKrUpdateRequest request, CancellationToken ct = default);

    /// <summary>DELETE · Borra el valor de un mes + recálculo. Desbloquea HU-025 CA #4.</summary>
    Task<ValorMensualKrGuardarResponse> EliminarValorAsync(Guid okrId, Guid keyResultId, int mes, CancellationToken ct = default);

    /// <summary>GET exportar · Genera el XLSX del OKR con ClosedXML (ADR-014).</summary>
    Task<byte[]> ExportarAsync(Guid okrId, CancellationToken ct = default);
}
