using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Dtos;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Interfaces;

/// <summary>
/// Spec HU-022 (DAL) · acceso a <c>entregable_adjunto</c> (06_MODELO_DATOS.md L314-325).
/// <para>
/// <b>Aislamiento multi-tenant en DOS capas (ARCH-04).</b> Todas las queries de esta interfaz
/// filtran por <c>tenant_id = @TenantId</c> con PARÁMETRO NOMBRADO (SEC-05/SEC-06, DB-03): el
/// <c>@TenantId</c> lo resuelve la BLL desde el <c>TenantContext</c> (claims del JWT) y NUNCA
/// viaja en el body ni en el query string. La segunda capa es la RLS de PostgreSQL.
/// </para>
/// <para>
/// <b>SEC-07 NO aparece aquí.</b> <c>entregable_adjunto</c> no tiene columna <c>area_id</c>: el
/// aislamiento por área se aplica en la BLL contra <c>accion_plan.area_id</c> (que ya devuelve
/// <c>IAccionPlanRepository.ObtenerPorIdAsync</c> filtrado por tenant) antes de tocar esta tabla.
/// No se añade un filtro inexistente ni se asume que la RLS cubre el área.
/// </para>
/// <para>
/// <b>Escrituras transaccionales.</b> <see cref="InsertarAsync"/>, <see cref="EliminarAsync"/> y
/// <see cref="InsertLogAsync"/> reciben la transacción abierta por
/// <see cref="BeginTransactionAsync"/> para que la fila y su auditoría en <c>log_auditoria</c>
/// sean atómicas (ADR-003). El <c>tx</c> es ANULABLE para que los métodos sirvan también fuera
/// de un lote; la BLL siempre les pasa la transacción. El <c>tx</c> anulable NO es opcional en
/// <see cref="EliminarAsync"/>: sin él el borrado y su auditoría no podrían ir en la misma
/// transacción.
/// </para>
/// <para>
/// <b>Sin <c>ILogger</c></b> en el constructor del repositorio (los 9 repositorios del repo
/// reciben solo <c>IDbConnectionFactory</c>): el logging estructurado vive en BLL/Services
/// (STACK-11) y un fallo de Dapper se propaga a la BLL, que lo traduce a
/// <c>InfraestructuraException</c>.
/// </para>
/// </summary>
public interface IEntregableAdjuntoRepository
{
    /// <summary>
    /// Adjuntos de una acción con el nombre del usuario que los subió (CA #3) y ordenados de
    /// forma determinista (más antiguo primero; desempate por nombre).
    /// Lista vacía = la acción existe pero no tiene adjuntos: <b>NO</b> es un error.
    /// </summary>
    Task<IEnumerable<EntregableAdjuntoEntity>> ListarPorAccionAsync(
        Guid accionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Nº de adjuntos de la acción, para el límite de 5 archivos (CA #1). El conteo lo calcula
    /// y valida la BLL (DB-04); aquí solo el COUNT.
    /// </summary>
    Task<int> ContarPorAccionAsync(
        Guid accionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Adjunto por PK <b>exigiendo tenant Y acción</b>: un anexo válido de otra acción devuelve
    /// <c>null</c> (→ 404, nunca 403, ni siquiera al firmar o borrar con un <c>accionId</c> falso
    /// en la ruta).
    /// </summary>
    Task<EntregableAdjuntoEntity?> ObtenerPorIdAsync(
        Guid entregableId, Guid accionId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Abre la transacción del lote. La cierra la BLL (<c>Commit</c>/<c>Rollback</c>): el
    /// repositorio NO hace commit implícito. Precedente: <c>CicloRepository</c>,
    /// <c>UsuarioRepository</c>, <c>AuthRepository</c>, <c>PlanRepository</c>,
    /// <c>TenantRepository</c>.
    /// </summary>
    Task<IDbTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Alta de un adjunto. El <c>id</c> y el <c>file_path</c> los genera la BLL (<c>Guid.NewGuid()</c>),
    /// no la DB ni el cliente. Se escribe con la transacción del lote.
    /// </summary>
    Task InsertarAsync(
        EntregableAdjuntoEntity entity, IDbTransaction? tx, CancellationToken ct = default);

    /// <summary>
    /// Borrado FÍSICO acotado por tenant + acción + id. Devuelve las <b>filas afectadas</b>:
    /// <c>0</c> = la fila ya no existe (carrera) → la BLL lanza <c>NoEncontradoException</c>
    /// (→ 404) y hace <c>Rollback</c>, de modo que no se audita un borrado que no ocurrió;
    /// <c>1</c> = eliminada. No hay borrado lógico: el DDL no tiene <c>deleted_at</c>.
    /// </summary>
    Task<int> EliminarAsync(
        Guid entregableId, Guid accionId, Guid tenantId, IDbTransaction? tx, CancellationToken ct = default);

    /// <summary>
    /// Auditoría de dominio (ADR-003) en la MISMA transacción que el <c>INSERT</c>/<c>DELETE</c>.
    /// Los 7 campos son los de <see cref="LogAuditoriaInsert"/>; la marca de tiempo la fija el
    /// <c>DEFAULT now()</c> del DDL. <c>log_auditoria</c> es append-only: aquí no hay ni
    /// <c>ActualizarLog</c> ni lectura (HU-005/CA #3).
    /// </summary>
    Task InsertLogAsync(
        LogAuditoriaInsert dto, IDbTransaction? tx, CancellationToken ct = default);
}
