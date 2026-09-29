using System;

namespace PE_GOL.Entity.PlanOperativo;

/// <summary>
/// Adjunto de una acción — Spec HU-022.
/// Mapeo 1:1 de la tabla <c>entregable_adjunto</c> (<c>06_MODELO_DATOS.md</c> L314-325) más UNA
/// columna de lectura del <c>LEFT JOIN</c> (<see cref="SubidoPorNombre"/>, CA #3 — corrección 3
/// de la revisión v3 del spec). SIN Navigation.
/// <para>
/// <b>NO tiene <c>TamanoLegible</c></b>: es un campo DERIVADO (DB-04 — corrección 4) que se
/// calcula en el proyector de la BLL con <c>EntregableAdjuntoReglas.FormatearTamano</c>, y no se
/// persiste ni se audita.
/// </para>
/// <para>
/// <b>NO tiene <c>CicloId</c> ni <c>AreaId</c></b>: no existen en la tabla. La BLL resuelve
/// <c>ciclo_id</c> leyendo <c>accion_plan</c> (para la ruta de Storage) y <c>area_id</c> para el
/// filtro SEC-07.
/// </para>
/// </summary>
public class EntregableAdjuntoEntity
{
    /// <summary>UUID — PK, generado en la BLL (<c>Guid.NewGuid()</c>), nunca por el cliente.</summary>
    public Guid Id { get; set; }

    /// <summary>UUID — tenant. SIEMPRE del <c>TenantContext</c> (SEC-06, DB-03).</summary>
    public Guid TenantId { get; set; }

    /// <summary>UUID — <c>accion_plan.id</c> (FK, <c>ON DELETE CASCADE</c> en el DDL).</summary>
    public Guid AccionId { get; set; }

    /// <summary>varchar(255) — nombre original saneado, sin ruta ni caracteres de control (XSS, RNF-009).</summary>
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>
    /// text — ruta interna en Supabase Storage: <c>{tenant_id}/{ciclo_id}/{accion_id}/{uuid}.{ext}</c>.
    /// NUNCA se expone en un DTO (D-H) y nunca se acepta del cliente (SEC-06).
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>int — <c>file_size_bytes</c>, ≤ 20 MB. Tipo C# <c>long</c> (D-J).</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>varchar(100) — MIME canónico de la allowlist, no el <c>ContentType</c> crudo del navegador.</summary>
    public string TipoMime { get; set; } = string.Empty;

    /// <summary>UUID — <c>usuario.id</c> que subió el archivo (FK usuario).</summary>
    public Guid SubidoPor { get; set; }

    /// <summary>TIMESTAMPTZ — <c>created_at</c>. Lo fija la BD (<c>DEFAULT now()</c>); la BLL no lo envía.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// NO es columna de <c>entregable_adjunto</c>: es <c>usuario.nombre</c> traída por el
    /// <c>LEFT JOIN usuario u ON u.id = e.subido_por AND u.tenant_id = e.tenant_id</c> de la DAL.
    /// Nullable porque el usuario pudo borrarse (la fila no desaparece: <c>LEFT JOIN</c>, no
    /// <c>JOIN</c>). Lo rellena el proyector BLL al construir el response (CA #3).
    /// El DDL tiene UNA sola columna de nombre (<c>nombre</c>); no existe <c>nombre_completo</c>.
    /// </summary>
    public string? SubidoPorNombre { get; set; }
}
