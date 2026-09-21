namespace PE_GOL.Aplicacion.Services;

/// <summary>
/// Cliente HTTP tipado hacia la API interna (04_ARQUITECTURA § 1: Aplicacion → HTTP interno → API).
/// Contrato fijado por @QA (Spec HU-045 § Cimiento de frontend) — no desviar las firmas.
/// Extensiones aditivas de Tanda B (HU-045, UI AdminTenant): PutAsync sin body (desactivar
/// área/responsable — HU-009/HU-010) y PostMultipartAsync (logo de empresa — HU-006).
/// Extensiones aditivas de Tanda C (HU-045, UI Gerente): DeleteAsync (eliminar pilar — HU-013).
/// Extensiones aditivas de Tanda D (HU-045, UI JefeArea): PatchAsync (actualizar progreso de
/// acción — HU-020).
/// </summary>
public interface IApiClient
{
    /// <summary>GET con query params opcionales. Desenvuelve ApiResponse&lt;T&gt;.Data.</summary>
    Task<T> GetAsync<T>(string path, IDictionary<string, string?>? query = null, CancellationToken ct = default);

    /// <summary>POST con body JSON. Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PostAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default);

    /// <summary>PUT con body JSON. Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PutAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default);

    /// <summary>POST sin body (activar/desactivar). Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PostAsync<TRes>(string path, CancellationToken ct = default);

    /// <summary>PUT sin body (desactivar área/responsable — HU-009/HU-010). Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PutAsync<TRes>(string path, CancellationToken ct = default);

    /// <summary>POST multipart/form-data (logo de empresa — HU-006, campo 'archivo'). Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PostMultipartAsync<TRes>(string path, IFormFile archivo, string campo, CancellationToken ct = default);

    /// <summary>DELETE sin body (eliminar pilar — HU-013). Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> DeleteAsync<TRes>(string path, CancellationToken ct = default);

    /// <summary>PATCH con body JSON (actualizar progreso de acción — HU-020). Desenvuelve ApiResponse&lt;TRes&gt;.Data.</summary>
    Task<TRes> PatchAsync<TReq, TRes>(string path, TReq body, CancellationToken ct = default);
}