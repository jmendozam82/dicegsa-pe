using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PE_GOL.DTO.Requests;

/// <summary>
/// Petición de subida interna (Spec HU-022, D-D). Construido por el controller a partir de
/// <c>List&lt;IFormFile&gt;</c>; la BLL nunca ve <c>IFormFile</c> ni ASP.NET Core.
/// <para>
/// <b>Vive en <c>PE-GOL.DTO</c> y no en <c>PE-GOL.BLL/Models</c></b> (bloqueo 9 de la v3): el
/// namespace <c>PE_GOL.BLL.Models.Objetivos</c> no existe en el repositorio, y este tipo lo usan
/// las DOS capas — lo construye el controller de <c>PE-GOL.API</c> y lo consume la BLL — que es
/// exactamente el caso de uso de <c>PE-GOL.DTO</c> (ARCH-02). Namespace plano, el mismo que
/// <c>PilarCreateRequest</c> y <c>CicloCreateRequest</c>.
/// </para>
/// <para>
/// <b>NO contiene <c>IFormFile</c></b>: <c>PE-GOL.DTO</c> es una librería <c>net8.0</c> sin
/// <c>FrameworkReference</c> a ASP.NET Core, de modo que el tipo no existe ahí (D-D). El
/// <c>LeerContenidoAsync</c> es el sustituto: el controller decide cómo volcar el binario a
/// memoria y la BLL solo pide los bytes.
/// </para>
/// <para>
/// <b><c>TenantId</c>, <c>UserId</c>, <c>Rol</c> y <c>AreaId</c> no vienen del cliente</b>:
/// los rellena el controller desde el <c>TenantContext</c> (claims del JWT). <c>AccionId</c> viene
/// de la ruta. Ningún endpoint acepta <c>tenant_id</c> ni <c>ciclo_id</c> en el cuerpo (SEC-06).
/// </para>
/// </summary>
public class EntregableSubidaRequest
{
    /// <summary>UUID — acción destino, viene de la ruta <c>{accionId:guid}/entregables</c>.</summary>
    public Guid AccionId { get; set; }

    /// <summary>UUID — del <c>TenantContext</c> (SEC-06). Nunca del body ni del query string.</summary>
    public Guid TenantId { get; set; }

    /// <summary>UUID — del <c>TenantContext</c>: el actor, que también es quien audita y puede borrar.</summary>
    public Guid UserId { get; set; }

    /// <summary>Rol del actor (<c>JefeArea</c> / <c>Gerente</c>), del <c>TenantContext</c>.</summary>
    public string Rol { get; set; } = string.Empty;

    /// <summary>Área del actor, del <c>TenantContext</c>. <c>null</c> para el Gerente (RN-006).</summary>
    public Guid? AreaId { get; set; }

    /// <summary>Entre 1 y 5 archivos (CA #1). El controller lo proyecta desde el multipart.</summary>
    public List<EntregableArchivo> Archivos { get; set; } = new();
}

/// <summary>
/// Un archivo del junction D-D: metadatos + una fábrica de contenido. Sin <c>IFormFile</c> y sin
/// streams (D-D), de modo que <c>PE-GOL.DTO</c> no necesita referenciar ASP.NET Core.
/// </summary>
public class EntregableArchivo
{
    /// <summary>Nombre original tal cual lo envió el cliente. La BLL lo sanea antes de persistirlo (D-E).</summary>
    public string NombreOriginal { get; set; } = string.Empty;

    /// <summary><c>ContentType</c> del navegador. Nunca se persiste tal cual: lo normaliza la BLL (D-C).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Extensión del nombre, en minúsculas y sin punto (<c>".PDF"</c> → <c>"pdf"</c>).</summary>
    public string ExtensionDeNombre { get; set; } = string.Empty;

    /// <summary>Tamaño declarado (<c>IFormFile.Length</c>). Lo valida la BLL contra RN-020.</summary>
    public long TamanoBytes { get; set; }

    /// <summary>
    /// Contenido ya volcado a memoria (<c>byte[]</c>). El controller decide cómo leerlo
    /// (<c>MemoryStream</c> de un <c>IFormFile</c>, o <c>ReadOnlyMemory</c> desde otro origen).
    /// Coste: UNA lectura por archivo — el array se reutiliza para la firma de bytes y para el
    /// upload; no se lee dos veces.
    /// </summary>
    public Func<CancellationToken, Task<byte[]>> LeerContenidoAsync { get; set; } =
        _ => Task.FromResult(Array.Empty<byte>());
}
