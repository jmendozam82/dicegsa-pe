using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses.Objetivos;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Spec HU-022, D-A · Entregables adjuntos de una acción (bucket privado <c>entregables</c>).
/// <para>
/// <b>Los ids de tenant, usuario, rol y área NUNCA vienen del cliente</b> (SEC-06): los resuelve
/// el controller desde el <c>TenantContext</c> (claims del JWT) y los pasa por parámetro. La
/// única excepción es <c>SubirAsync</c>, que los agrupa en <see cref="EntregableSubidaRequest"/>,
/// un tipo de <c>PE-GOL.DTO</c> (D-D) porque lo construyen las DOS capas.
/// </para>
/// <para>
/// <b>Retornos y códigos HTTP</b> (la traducción a <c>ApiResponse&lt;T&gt;</c> es del controller,
/// ARCH-07 y ADR-010 D-1: la BLL nunca conoce el wrapper):
/// <list type="bullet">
///   <item>Listado: <b>200</b> con la lista, <b>posiblemente vacía</b> (D-C: la acción existe
///   aunque no tenga adjuntos; nunca 404). <c>404</c> si la acción no existe, <c>403</c> si el rol
///   o el área no permiten verla.</item>
///   <item>Subida: <b>201</b> con los adjuntos creados, <c>422</c> si se viola una regla de
///   <c>EntregableAdjuntoReglasNegocio</c>, <c>404</c>/<c>403</c> por acción, <c>500</c> por
///   fallo de Storage (todo o nada por lote, D-H).</item>
///   <item>Descarga: <b>200</b> con la URL firmada de 24 h. Si el Storage falla, degrada a
///   <c>Url = null</c> con <b>200</b> y <c>ExpiraEn</c> relleno (RNF-014, D-G): nunca 500.</item>
///   <item>Borrado: <see cref="int"/> con las <b>filas afectadas</b> (1 = eliminada; 0 = carrera,
///   en cuyo caso la BLL lanza <c>NotFoundException</c> → 404, D-M/bloqueo 2).</item>
/// </list>
/// </para>
/// <para>
/// <b>Orden de los parámetros NO intercambiable</b> (corrección 2 del spec): los dos repositorios
/// de lectura mantienen órdenes opuestos y el compilador no los avisa —
/// <c>IAccionPlanRepository.ObtenerPorIdAsync(accionId, tenantId, ct)</c> (acción primero) frente
/// a <c>ICicloRepository.ObtenerPorIdAsync(tenantId, cicloId, ct)</c> (tenant primero).
/// </para>
/// </summary>
public interface IEntregableService
{
    /// <summary>
    /// Adjuntos de una acción con nombre, tamaño, fecha y usuario que los subió (CA #3), más el
    /// campo derivado <c>PuedeEliminar</c> (CA #5, DB-04). Lista vacía → lista vacía (D-C, UX-05).
    /// </summary>
    Task<IEnumerable<EntregableAdjuntoResponse>> ListarAsync(
        Guid accionId, Guid tenantId, Guid userId, string rol, Guid? areaId,
        CancellationToken ct = default);

    /// <summary>
    /// Sube 1..5 archivos a la acción (multipart ya volcado a memoria por el controller). Es
    /// <b>todo o nada por lote</b> (D-H): o se escriben todas las filas con su auditoría en una
    /// única transacción, o no se escribe ninguna y se borra del Storage lo ya subido.
    /// </summary>
    Task<IEnumerable<EntregableAdjuntoResponse>> SubirAsync(
        EntregableSubidaRequest request, CancellationToken ct = default);

    /// <summary>
    /// URL firmada de acceso temporal (CA #4, ARCH-06: 24 h). El navegador descarga DIRECTO desde
    /// Supabase Storage: la API nunca hace proxy del binario (RNF-001). Esta operación es una
    /// LECTURA y no audita (ADR-003: en esta HU solo se auditan CREATE y DELETE).
    /// </summary>
    Task<EntregableDescargaResponse> ObtenerDescargaAsync(
        Guid accionId, Guid entregableId, Guid tenantId, string rol, Guid? areaId,
        CancellationToken ct = default);

    /// <summary>
    /// Elimina el adjunto: <b>solo quien lo subió o el Gerente</b> (CA #5). El borrado en BD y
    /// su auditoría van en la MISMA transacción; el objeto de Storage se borra después, en
    /// <b>best-effort</b>, para que un fallo de Storage no pueda impedir un borrado ya confirmado
    /// (D-I, RNF-014).
    /// </summary>
    /// <returns>Filas afectadas: 1 (el controller mapea a <c>ApiResponse&lt;bool&gt;</c>); 0 nunca sale, porque la BLL lanza 404.</returns>
    Task<int> EliminarAsync(
        Guid accionId, Guid entregableId, Guid tenantId, Guid userId, string rol, Guid? areaId,
        CancellationToken ct = default);
}
