namespace PE_GOL.Entity.Saas;

/// <summary>
/// Entidad que mapea la tabla usuario (Spec HU-003 § Queries DAL: DAL-U1 a DAL-U10).
/// DDL: 06_MODELO_DATOS.md líneas 81-98. La tabla usuario NO está bajo RLS (verificado:
/// no aparece en ENABLE ROW LEVEL SECURITY, L458-477) — el aislamiento se garantiza por
/// [Authorize(Roles = "SuperAdmin")] + BLL (decisión Jorge / D1, HANDOFF L156).
///
/// ⚠️ CONTRATO DE AUDITORÍA (ADR-003): PasswordHash se incluye porque la entidad mapea el
/// DDL completo, PERO el servicio JAMÁS puede serializar esta entidad completa en
/// log_auditoria.valor_anterior/valor_nuevo. El JSON de auditoría se construye con la forma
/// del UsuarioResponse SIN password (spec § Lógica BLL paso 9 y paso 5 de reset —
/// "nunca el hash literal"). Los tests @QA casos #11 y #30 lo verifican.
/// </summary>
public class UsuarioEntity
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }                 // NULL ⇔ SuperAdmin (no cuenta plaza, glosario Jorge)
    public string Nombre { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;  // BCrypt cost ≥ 12 (SEC-02) — nunca en responses/auditoría
    public string Rol { get; set; } = string.Empty;           // 'SuperAdmin' | 'AdminTenant' | 'Gerente' | 'JefeArea'
    public Guid? AreaId { get; set; }                        // obligatorio SOLO si rol = JefeArea (RN-011)
    public string Estado { get; set; } = string.Empty;       // 'Activo' | 'Inactivo' | 'Bloqueado'
    public int IntentosFallidos { get; set; }                // gestión de bloqueo → HU-004
    public DateTimeOffset? BloqueadoHasta { get; set; }      // bloqueo de cuenta → HU-004
    public DateTimeOffset? UltimoLogin { get; set; }
    public bool RequiereCambioPwd { get; set; }              // D5: siempre TRUE en creación y reset
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}