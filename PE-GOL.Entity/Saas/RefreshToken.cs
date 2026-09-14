namespace PE_GOL.Entity.Saas;

/// <summary>
/// Entidad que mapea la tabla refresh_token (Spec HU-004 § Queries DAL: DAL-A2 a DAL-A6).
/// DDL: 06_MODELO_DATOS.md líneas 100-108. La tabla NO está bajo RLS (verificado L458-477).
/// D2: la columna token persiste el HASH SHA-256 hex del refresh token (no el token en claro);
/// el UNIQUE del DDL aplica al hash (único por construcción).
/// </summary>
public class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public string Token { get; set; } = string.Empty;   // hash SHA-256 hex (D2)
    public DateTimeOffset ExpiraEn { get; set; }
    public bool Revocado { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}