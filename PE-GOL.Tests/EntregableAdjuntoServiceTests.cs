using System.Data;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.Ciclo;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Storage;

namespace PE_GOL.Tests;

/// <summary>
/// Tests TDD (FASE ROJA) de HU-022 — Gestión de Entregables Adjuntos.
/// Spec HU-022 <b>v3 (2026-09-27)</b> § "Tests requeridos", casos <b>1-51 y 66</b> — <b>53
/// métodos</b> (el caso 1 se desdobla en el 1 y el 66 por el bloqueo 1 de la v3, y el caso 42
/// declara DOS nombres: inline y attachment). Escritos ANTES de la implementación (TEST-01):
/// los miembros de producción de HU-022 NO existen todavía → <b>el rojo legítimo de esta fase
/// es el FALLO DE COMPILACIÓN del proyecto de tests (CS0246 / CS0234 / CS0117 / CS1061)</b>, con el que
/// @Orquestador cierra el ciclo Tests → Implement (LOOP-01, LOOP-02, LOOP-03).
/// Patrón: xUnit + Moq (TEST-03), Arrange/Act/Assert, nombre [Metodo]_[Escenario]_[Resultado]
/// (TEST-05), clase propia por servicio (TEST-04). Mock puro, sin AutoMoq.
/// NOTA DE CONVENCION (misma que AccionPlanGanttServiceTests HU-021, PilarServiceTests HU-013,
/// AreaServiceTests HU-009): @QA NO crea stubs ni réplicas de los miembros de producción —
/// un stub invalidaría la especificación ejecutable (obligaría a borrarlo después) y
/// modificaría código de producción, que es territorio de @BackendDev/@Arquitecto.
///
/// ─────────────────────────── CONTRATO QUE @BackendDev DEBE IMPLEMENTAR ───────────────────────
/// Sin estos miembros, este archivo NO compila. Las firmas son EXACTAS (sin adivinar) y son las
/// de la v3, que resolvió los 9 bloqueos que esta fase roja encontró:
///
/// (1) PE-GOL.DAL/Interfaces/IEntregableAdjuntoRepository.cs → namespace PE_GOL.DAL.Interfaces
///     (PLANO — corrección 3.a de la v3: las interfaces del repo no van en subcarpetas):
///       Task&lt;IEnumerable&lt;EntregableAdjuntoEntity&gt;&gt; ListarPorAccionAsync(Guid accionId, Guid tenantId, CancellationToken ct = default);
///       Task&lt;int&gt;                            ContarPorAccionAsync(Guid accionId, Guid tenantId, CancellationToken ct = default);
///       Task&lt;EntregableAdjuntoEntity?&gt;       ObtenerPorIdAsync(Guid entregableId, Guid accionId, Guid tenantId, CancellationToken ct = default);
///       Task&lt;IDbTransaction&gt;                  BeginTransactionAsync(CancellationToken ct = default);
///       Task                                  InsertarAsync(EntregableAdjuntoEntity entity, IDbTransaction? tx, CancellationToken ct = default);
///       Task&lt;int&gt;                            EliminarAsync(Guid entregableId, Guid accionId, Guid tenantId, IDbTransaction? tx, CancellationToken ct = default);  // ← bloqueo 2
///       Task                                  InsertLogAsync(LogAuditoriaInsert dto, IDbTransaction? tx, CancellationToken ct = default);
///     ⚠ BLOQUEO 2: <c>EliminarAsync</c> devuelve <c>Task&lt;int&gt;</c> (filas afectadas), NO <c>Task</c>.
///       0 = carrera → la BLL lanza NoEncontradoException → 404; 1 = eliminada.
///
/// (2) PE-GOL.BLL/Interfaces/IEntregableService.cs → namespace PE_GOL.BLL.Interfaces (plano):
///       Task&lt;IEnumerable&lt;EntregableAdjuntoResponse&gt;&gt; ListarAsync(Guid accionId, Guid tenantId, Guid userId, string rol, Guid? areaId, CancellationToken ct = default);
///       Task&lt;IEnumerable&lt;EntregableAdjuntoResponse&gt;&gt; SubirAsync(EntregableSubidaRequest request, CancellationToken ct = default);
///       Task&lt;EntregableDescargaResponse&gt;         ObtenerDescargaAsync(Guid accionId, Guid entregableId, Guid tenantId, string rol, Guid? areaId, CancellationToken ct = default);
///       Task&lt;int&gt;                               EliminarAsync(Guid accionId, Guid entregableId, Guid tenantId, Guid userId, string rol, Guid? areaId, CancellationToken ct = default);  // ← bloqueo 2
///     → El DTO es PLANO: la BLL nunca conoce ApiResponse&lt;T&gt; (ADR-010 D-1, caso 35/40).
///
/// (3) PE-GOL.BLL/Services/EntregableAdjuntoService.cs → namespace PE_GOL.BLL.Services (plano).
///     Ctor de 7 args, SIN TenantContext (el controller resuelve el contexto y lo pasa por
///     parámetro — SEC-06; es lo que fijan las 4 firmas de (2)):
///       public EntregableAdjuntoService(
///           IEntregableAdjuntoRepository entregables,
///           IAccionPlanRepository acciones,
///           ICicloRepository ciclos,
///           IStorageHelper storage,
///           EntregableAdjuntoReglasNegocio reglas,
///           ILogger&lt;EntregableAdjuntoService&gt; logger,
///           TimeProvider timeProvider);
///     ⚠ ORDEN DE ARGUMENTOS DISTINTOS Y NO INTERCAMBIABLES (corrección 2 del spec):
///       · _acciones.ObtenerPorIdAsync(accionId, tenantId, ct)   → accionId PRIMERO
///       · _ciclos.ObtenerPorIdAsync(tenantId, cicloId, ct)     → tenantId PRIMERO
///
/// (4) PE-GOL.Entity/PlanOperativo/EntregableAdjuntoEntity.cs → PE_GOL.Entity.PlanOperativo
///     (junto a AccionPlanEntity; el entregable es hijo de accion_plan). SIN TamanoLegible
///     (campo DERIVADO, DB-04/corrección 4), CON SubidoPorNombre (string?, del LEFT JOIN).
///
/// (5) PE-GOL.DTO/Responses/Objetivos/EntregableAdjuntoResponse.cs y EntregableDescargaResponse.cs
///     → PE_GOL.DTO.Responses.Objetivos (la carpeta SÍ existe). SIN TenantId y SIN FilePath
///     (SEC-06 / D-H) — verificado por reflexión en los casos 35 y 40.
///
/// (6) ⚠ BLOQUEO 9: PE-GOL.DTO/Requests/EntregableSubidaRequest.cs → <b>namespace PE_GOL.DTO.Requests</b>
///     (raíz de Requests/, el mismo que PilarCreateRequest y CicloCreateRequest). YA NO es
///     PE-GOL.BLL/Models/Objetivos: ese namespace NO EXISTE en el repositorio (PE-GOL.BLL solo
///     tiene Interfaces/, Limites/ y Services/). Contiene EntregableSubidaRequest + EntregableArchivo
///     (el junction D-D: la BLL nunca ve IFormFile).
///
/// (7) PE-GOL.BLL/Limites/EntregableAdjuntoReglasNegocio.cs → PE_GOL.BLL.Limites.
///     Ctor: (TimeProvider time). Métodos: ValidarConteo(int,int) · ValidarTamano(long,string) ·
///     ValidarTipo(string,string,string) · ValidarCicloCerrado(string). Sin FluentValidation (corrección 5).
///     En los tests se usa la INSTANCIA REAL (el spec lo autoriza: "o la instancia real: es
///     purista") — no un Mock — para que las reglas ejercitadas sean las de producción.
///
/// (8) PE-GOL.Utility/Helpers/EntregableAdjuntoReglas.cs → PE_GOL.Utility.Helpers (constantes +
///     FormatearTamano + SanearNombreArchivo). Y ⚠ BLOQUEO 4 / D-R:
///     <b>PE-GOL.Utility/Files/TipoArchivoHelper.cs → namespace PE_GOL.Utility.Files</b> (subcarpeta
///     NUEVA, no Helpers/), con el enum CERRADO:
///       public enum TipoArchivo { Pdf, Docx, Xlsx, Png, Jpg, Desconocido }
///       public static TipoArchivo DetectarTipo(ReadOnlySpan&lt;byte&gt; header);
///       public static string     ContentDisposition(TipoArchivo tipo);   // ← 1 ARGUMENTO (tipo), no (nombre, mime)
///       public static string     ExtensionCanonica(string extensionNombre, string contentType);
///       public static string     MimeCanonica(TipoArchivo tipo);
///
/// (9) PE-GOL.Utility/Storage/IStorageHelper.cs → los 3 genéricos de ADR-013, con la ENMIENDA 2
///     de la v3 (el 4.º parámetro es el TipoArchivo canónico, no un string de content-disposition):
///       Task&lt;string?&gt; SubirArchivoAsync(string bucket, string path, byte[] bytes, <b>TipoArchivo tipo</b>, CancellationToken ct = default);
///       Task&lt;string?&gt; ObtenerUrlFirmadaAsync(string bucket, string path, int horas, CancellationToken ct = default);   // SIN contentDisposition (bloqueo 8)
///       Task&lt;bool&gt;    EliminarArchivoAsync(string bucket, string path, CancellationToken ct = default);
///
/// ───────────────────────────── BLOQUEOS DE LA v3 QUE ESTOS TESFIJAN ─────────────────────────
/// B-2 · <c>EliminarAsync</c> devuelve <c>int</c> en DAL <b>y</b> en BLL: los casos 43, 44, 46 y
///      47 afirman el VALOR (1) y la carrera (0 → 404 + Rollback + SIN auditoría).
/// B-4 · <c>TipoArchivoHelper.DetectarTipo</c> devuelve el ENUM <c>TipoArchivo</c> y
///      <c>ContentDisposition</c> recibe el ENUM (no un par nombre/mime): el caso 42 se reescribió.
/// B-8 · El <c>content-disposition</c> viaja SOLO en la subida
///      (<c>FileUploadOptions.ContentDisposition = TipoArchivoHelper.ContentDisposition(tipo)</c>):
///      <c>ObtenerUrlFirmadaAsync(bucket, path, horas, ct)</c> <b>no</b> lleva ese parámetro, porque
///      la URL firmada no puede reescribir el <c>content-disposition</c> de un objeto ya subido.
/// B-9 · <c>EntregableSubidaRequest</c> sale de <c>PE_GOL.BLL/Models/Objetivos</c> (namespace
///      inexistente) y entra en <c>PE-GOL.DTO/Requests/</c> (bloqueo 2 de la v3 + ARCH-02).
/// B-1 · El caso 1 se desdobla: el 1 afirma el alta y sus metadatos; el 66 afirma, por separado, que
///      la auditoría CREATE va en la MISMA transacción (comparación por REFERENCIA de la <c>tx</c>).
///
/// Lógica BLL bajo test (spec § "Lógica BLL"): SubirAsync = valida forma+verdad con
/// EntregableAdjuntoReglasNegocio (422) → acción (404) → área/rol (403) → ciclo Cerrado/Borrador
/// (422, F3/RC-12) → conteo existentes+nuevos ≤ 5 (422) → rutas {tenant}/{ciclo}/{accion}/{uuid}.{ext}
/// → UNA transacción para todo el lote → por archivo: leer bytes, FIRMA DE BYTES (422), subir,
/// INSERT + auditoría CREATE en la MISMA tx → fallo ⇒ rollback + compensación de Storage con
/// CancellationToken.None + InfraestructuraException (todo o nada, D-H/RNF-014) → re-lectura de
/// las filas (CreatedAt del DEFAULT now() y SubidoPorNombre del LEFT JOIN) → proyección.
/// Listar/ObtenerDescarga = 404/403/200 con degradación elegante (D-G, RNF-014).
/// Eliminar = 404/403 (solo autor o Gerente, CA #5) → 422 si el ciclo no es Activo → BD y
/// auditoría DELETE en la MISMA tx → Commit → SOLO ENTONCES borrado best-effort en Storage (D-I).
/// </summary>
public class EntregableAdjuntoServiceTests
{
    // ─── Guids del fixture (deterministas: el log y las rutas deben ser comparables) ───────────
    private static readonly Guid TenantId       = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId         = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid OtroUserId    = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid AreaId         = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OtroAreaId    = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid AccionId       = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid CicloId        = Guid.Parse("00000000-0000-0000-0000-000000000030");
    private static readonly Guid EntregableId   = Guid.Parse("00000000-0000-0000-0000-000000000040");
    private static readonly DateTimeOffset Ahora = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private const string Bucket = EntregableAdjuntoReglas.BucketEntregables;   // "entregables" (ARCH-06)
    private const string RolJefeArea = "JefeArea";
    private const string RolGerente   = "Gerente";

    private readonly Mock<IEntregableAdjuntoRepository> _entregables = new();
    private readonly Mock<IAccionPlanRepository> _acciones = new();
    private readonly Mock<ICicloRepository> _ciclos = new();
    private readonly Mock<IStorageHelper> _storage = new();
    private readonly Mock<ILogger<EntregableAdjuntoService>> _logger = new();
    private readonly Mock<TimeProvider> _time = new();
    private readonly Mock<IDbTransaction> _tx = new();

    private readonly EntregableAdjuntoReglasNegocio _reglas;
    private readonly IEntregableService _sut;

    /// <summary>Filas que el SUT ha insertado (las re-lecturas del paso 13 se sirven de aquí).</summary>
    private readonly List<EntregableAdjuntoEntity> _insertadas = new();

    /// <summary>Logs capturados del SUT: (nivel, mensaje formateado). Para RNF-009/RNF-011/STACK-11.</summary>
    private readonly List<(LogLevel Nivel, string Mensaje)> _logs = new();

    /// <summary>Transacciones que el SUT ha abierto vía BeginTransactionAsync (comparación por referencia).</summary>
    private readonly List<IDbTransaction> _txAbiertas = new();

    public EntregableAdjuntoServiceTests()
    {
        _time.Setup(t => t.GetUtcNow()).Returns(Ahora);
        _reglas = new EntregableAdjuntoReglasNegocio(_time.Object);

        // Auditoría de los logs estructurados (STACK-11). El patrón It.IsAnyType + ToString del
        // estado es el estándar para asertar sobre ILogger con Moq (FormattedLogValues.ToString()
        // devuelve el mensaje YA formateado con sus pares clave-valor).
        _logger
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(i =>
                _logs.Add(((LogLevel)i.Arguments[0], i.Arguments[2]?.ToString() ?? string.Empty))));

        // Defaults seguros: sin setup explícito Moq devuelve null y la BLL explotaría con NRE.
        _entregables
            .Setup(r => r.ListarPorAccionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>());
        _entregables
            .Setup(r => r.ContarPorAccionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _entregables
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { _txAbiertas.Add(_tx.Object); return _tx.Object; });

        _sut = new EntregableAdjuntoService(
            _entregables.Object,
            _acciones.Object,
            _ciclos.Object,
            _storage.Object,
            _reglas,
            _logger.Object,
            _time.Object);
    }

    // ══════════════════════════════ HELPERS DEL FIXTURE ══════════════════════════════════════

    /// <summary>Acción del tenant, de su área, con su ciclo Activo (camino feliz).</summary>
    private void ConfigurarEscenarioValido(Guid? areaIdDeLaAccion = null, string estadoCiclo = "Activo")
    {
        _acciones
            .Setup(a => a.ObtenerPorIdAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccionPlanEntity
            {
                Id = AccionId,
                TenantId = TenantId,
                CicloId = CicloId,
                AreaId = areaIdDeLaAccion ?? AreaId,
                Codigo = "ACC-001",
                Status = "EnProgreso"
            });

        _ciclos
            .Setup(c => c.ObtenerPorIdAsync(TenantId, CicloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CicloEntity
            {
                Id = CicloId,
                TenantId = TenantId,
                Nombre = "PE 2026",
                AñoFiscal = 2026,
                MesInicio = 1,
                Estado = estadoCiclo
            });

        // Storage OK: devuelve la MISMA ruta que se le pasó (ADR-013: el path persistido).
        _storage
            .Setup(s => s.SubirArchivoAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()))
            .Returns((string bucket, string path, byte[] bytes, TipoArchivo tipo, CancellationToken ct)
                => Task.FromResult<string?>(path));

        _storage
            .Setup(s => s.EliminarArchivoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Contexto de subida VÁLIDO: <b>JefeArea con su área asignada</b> (la de la acción que devuelve
    /// <see cref="ConfigurarEscenarioValido"/>). Es el caso feliz de SEC-07 y el que usan todos los tests
    /// que esperan 201/422/500 por una regla de negocio que NO sea de área.
    /// <para>
    /// OJO — no reintroducir un <c>areaId = null</c> por defecto: <c>ValidarAreaDelActor</c> rechaza con 403
    /// a un JefeArea sin <c>area_id</c> en el JWT, y ese default rompería los ~20 tests de éxito que usan
    /// este helper. Para el 403 explícito está <see cref="CrearRequestSinArea"/>.
    /// </para>
    /// </summary>
    private EntregableSubidaRequest CrearRequestValido(int cantidad = 1)
        => ConstruirRequest(cantidad, RolJefeArea, AreaId);

    /// <summary>
    /// Contexto de subida SIN área en el JWT (<c>rol=JefeArea, areaId=null</c>). El caso 16 espera 403:
    /// «el token no tiene área asignada» nunca degenera en «todas las áreas» (SEC-07).
    /// </summary>
    private EntregableSubidaRequest CrearRequestSinArea(int cantidad = 1)
        => ConstruirRequest(cantidad, RolJefeArea, null);

    /// <summary>
    /// Contexto del <b>Gerente</b>, que no lleva filtro de área (RN-006): puede actuar sobre la acción
    /// sea cual sea su <c>area_id</c>. El caso 2 lo sube contra una acción de <c>OtroAreaId</c>.
    /// </summary>
    private EntregableSubidaRequest CrearRequestGerente(int cantidad = 1)
        => ConstruirRequest(cantidad, RolGerente, null);

    /// <summary>Primitiva: N archivos PDF válidos (firmas de bytes correctas) con el contexto dado.</summary>
    private EntregableSubidaRequest ConstruirRequest(int cantidad, string rol, Guid? areaId)
    {
        var archivos = new List<EntregableArchivo>();
        for (var i = 0; i < cantidad; i++)
        {
            var nombre = $"informe-{i + 1}.pdf";
            archivos.Add(CrearArchivo(nombre, "application/pdf", BytesPdf(), 12_345 + i));
        }

        return new EntregableSubidaRequest
        {
            AccionId = AccionId,
            TenantId = TenantId,
            UserId = UserId,
            Rol = rol,
            AreaId = areaId,
            Archivos = archivos
        };
    }

    /// <summary>Un archivo del junction D-D (sin IFormFile: la BLL no ve ASP.NET Core).</summary>
    private static EntregableArchivo CrearArchivo(
        string nombreOriginal, string contentType, byte[] contenido, long tamanoDeclarado)
        => new()
        {
            NombreOriginal = nombreOriginal,
            ContentType = contentType,
            ExtensionDeNombre = Path.GetExtension(nombreOriginal).TrimStart('.').ToLowerInvariant(),
            TamanoBytes = tamanoDeclarado,
            LeerContenidoAsync = _ => Task.FromResult(contenido)
        };

    /// <summary>Cabecera PDF válida (%PDF-1.7 + salto) — RNF-009: la firma de bytes es la mitigación real.</summary>
    private static byte[] BytesPdf()
        => new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25, 0xE2, 0xE3 };

    private static byte[] BytesPng()
        => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

    private static byte[] BytesJpg()
        => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

    /// <summary>ZIP con word/ → es un DOCX renombrado a .xlsx (RNF-009: el directorio central lo delata).</summary>
    private static byte[] BytesDocxComoXlsx()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var contenido = zip.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(contenido.Open())) w.Write("<Types/>");
            var documento = zip.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(documento.Open())) w.Write("<w:document/>");
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Captura el INSERT (tx incluida) y sirve las re-lecturas del paso 13 con los valores que
    /// pone la BD: created_at (DEFAULT now()) y subido_por_nombre (LEFT JOIN usuario.nombre).
    /// </summary>
    private IDbTransaction? _txDeLaInsercion;

    private void ConfigurarInsercionYRelectura(string nombreUsuario = "Jefe de Área")
    {
        _entregables
            .Setup(r => r.InsertarAsync(
                It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<EntregableAdjuntoEntity, IDbTransaction?, CancellationToken>((entidad, tx, ct) =>
            {
                _txDeLaInsercion = tx;
                entidad.CreatedAt = Ahora.UtcDateTime;         // lo fija el DEFAULT now() del DDL
                entidad.SubidoPorNombre = nombreUsuario;       // lo trae el LEFT JOIN usuario.nombre
                _insertadas.Add(entidad);
            })
            .Returns(Task.CompletedTask);

        _entregables
            .Setup(r => r.ObtenerPorIdAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, Guid accionId, Guid tenantId, CancellationToken ct)
                => Task.FromResult<EntregableAdjuntoEntity?>(
                    _insertadas.FirstOrDefault(e => e.Id == id && e.AccionId == accionId && e.TenantId == tenantId)));
    }

    private EntregableAdjuntoEntity CrearAdjunto(
        Guid? id = null, Guid? subidoPor = null, long tamano = 12_345,
        string nombre = "informe.pdf", string? nombreUsuario = "Jefe de Área",
        DateTime? createdAt = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TenantId = TenantId,
            AccionId = AccionId,
            NombreArchivo = nombre,
            FilePath = $"{TenantId}/{CicloId}/{AccionId}/{Guid.NewGuid()}.pdf",
            FileSizeBytes = tamano,
            TipoMime = "application/pdf",
            SubidoPor = subidoPor ?? UserId,
            CreatedAt = createdAt ?? Ahora.UtcDateTime,
            SubidoPorNombre = nombreUsuario
        };

    private void ConfigurarAdjunto(EntregableAdjuntoEntity adjunto)
    {
        _entregables
            .Setup(r => r.ObtenerPorIdAsync(adjunto.Id, AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adjunto);
    }

    private void ConfigurarAuditoria()
    {
        _entregables
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private bool HayLogDeError => _logs.Any(l => l.Nivel == LogLevel.Error);

    // ═══════════════════════════════════ SubirAsync (1-25 + 66) ═══════════════════════════════

    // 1 · v3: el caso 1 ya NO afirma nada de la auditoría (eso es el 66) — un test que afirma dos
    // comportamientos distintos falla sin decir cuál, y la auditoría en la MISMA transacción es la
    // afirmación estructural más importante de la HU (D-M). Por eso el nombre cambió a
    // SubirAsync_ArchivoValido_InsertaYRetornaMetadatos.
    [Fact]
    public async Task SubirAsync_ArchivoValido_InsertaYRetornaMetadatos()
    {
        // Arrange
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: 1 adjunto con metadatos proyectados (Id, TamanoLegible, PuedeEliminar del autor)
        var adjunto = Assert.Single(resultado);
        Assert.NotEqual(Guid.Empty, adjunto.Id);
        Assert.Equal("informe-1.pdf", adjunto.NombreArchivo);
        // 12.345 B con base 1024 → 12,06 KB → "12,1 KB" (coma decimal, como el resto de la app)
        Assert.Equal("12,1 KB", adjunto.TamanoLegible);
        Assert.True(adjunto.PuedeEliminar);
        Assert.Equal(Ahora.UtcDateTime, adjunto.CreatedAt);
        Assert.Equal("Jefe de Área", adjunto.SubidoPorNombre);

        // Bucket entregables y ruta {tenant}/{ciclo}/{accion}/{uuid}.pdf (ARCH-06 + D-F, sin barra inicial)
        _storage.Verify(s => s.SubirArchivoAsync(
            Bucket,
            It.Is<string>(p => p.StartsWith($"{TenantId}/{CicloId}/{AccionId}/")
                            && p.EndsWith(".pdf")
                            && p.Length == $"{TenantId}/{CicloId}/{AccionId}/".Length + 36 + 4),
            It.IsAny<byte[]>(), It.Is<TipoArchivo>(t => t == TipoArchivo.Pdf), It.IsAny<CancellationToken>()),
            Times.Once);

        // UNA transacción para el lote y el INSERT recibe ESA MISMA tx (corrección 1 / D-M)
        _entregables.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(_tx.Object, _txDeLaInsercion);
        _tx.Verify(t => t.Commit(), Times.Once);
        _tx.Verify(t => t.Rollback(), Times.Never);
    }

    // 66 · v3 (bloqueo 1): la mitad que el caso 1 soltó — la auditoría CREATE en la MISMA tx.
    // Numerado 66 y NO 2 para no renumerar 2-65 (numeración estable de la fase roja).
    [Fact]
    public async Task SubirAsync_ArchivoValido_AuditoriaEnMismaTransaccion()
    {
        // Arrange: se capturan POR REFERENCIA la tx que recibe el INSERT y la que recibe el log
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        IDbTransaction? txDelInsert = null;
        IDbTransaction? txDelLog = null;
        _entregables
            .Setup(r => r.InsertarAsync(
                It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<EntregableAdjuntoEntity, IDbTransaction?, CancellationToken>((entidad, tx, ct) =>
            {
                txDelInsert = tx;
                entidad.CreatedAt = Ahora.UtcDateTime;
                entidad.SubidoPorNombre = "Jefe de Área";
                _insertadas.Add(entidad);
            })
            .Returns(Task.CompletedTask);
        _entregables
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<LogAuditoriaInsert, IDbTransaction?, CancellationToken>((log, tx, ct) => txDelLog = tx)
            .Returns(Task.CompletedTask);
        var request = CrearRequestValido(1);

        // Act
        await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: EXACTAMENTE 1 auditoría CREATE, con los 7 campos de LogAuditoriaInsert y el
        // snapshot acotado a 4 campos (sin TamanoLegible → DB-04, sin bytes, sin filePath → RNF-009)
        var idAdjunto = Assert.Single(_insertadas).Id;
        _entregables.Verify(r => r.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "CREATE"
                                       && l.Entidad == "EntregableAdjunto"
                                       && l.EntidadId == idAdjunto.ToString()
                                       && l.UsuarioId == UserId
                                       && l.TenantId == TenantId
                                       && l.ValorAnterior == null
                                       && l.ValorNuevo!.Contains("\"fileSizeBytes\":12345")
                                       && l.ValorNuevo!.Contains("\"tipoMime\":\"application/pdf\"")
                                       && !l.ValorNuevo!.Contains("TamanoLegible")
                                       && !l.ValorNuevo!.Contains("filePath")),
            It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Once);

        // La prueba estructural de la corrección 1: la MISMA INSTANCIA de IDbTransaction en el
        // INSERT y en su auditoría (comparación por referencia, NO It.IsAny) → o se escriben
        // ambos o ninguno.
        Assert.NotNull(txDelInsert);
        Assert.Same(txDelInsert, txDelLog);
        _entregables.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.Commit(), Times.Once);
    }

    // 2
    [Fact]
    public async Task SubirAsync_GerenteSinArea_RetornaCreatedYNoFalla()
    {
        // Arrange: Gerente sin areaId → el filtro de área NO aplica (RN-006)
        ConfigurarEscenarioValido(areaIdDeLaAccion: OtroAreaId);
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestGerente(1);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(resultado);
        _storage.Verify(s => s.SubirArchivoAsync(
            Bucket, It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 3
    [Fact]
    public async Task SubirAsync_CincoArchivosValidos_Retorna201YPersisteCinco()
    {
        // Arrange: 5 archivos, ninguno existente (borde superior del CA #1)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(5);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(5, resultado.Count());
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));

        // 5 rutas DISTINTAS (UUID) bajo el mismo prefijo → ninguna colisión de nombre (D-E)
        var rutas = _insertadas.Select(e => e.FilePath).ToList();
        Assert.Equal(5, rutas.Distinct().Count());
        Assert.All(rutas, r => Assert.StartsWith($"{TenantId}/{CicloId}/{AccionId}/", r));
        Assert.Equal(5, rutas.Distinct(StringComparer.Ordinal).Count());
    }

    // 4
    [Fact]
    public async Task SubirAsync_SeisArchivos_Retorna422YNoSube()
    {
        // Arrange: 6 archivos en el request (CA #1: máximo 5)
        ConfigurarEscenarioValido();
        var request = CrearRequestValido(6);

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert: 422 antes de tocar Storage o la BD
        Assert.NotNull(ex);
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 5
    [Fact]
    public async Task SubirAsync_AccionYaTieneCincoYSeMandaUno_Retorna422YNoSube()
    {
        // Arrange: la acción ya tiene 5 adjuntos y llega 1 más
        ConfigurarEscenarioValido();
        _entregables.Setup(r => r.ContarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(5);
        var request = CrearRequestValido(1);

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert: el mensaje ACCIONABLE menciona el total (spec L408), no un genérico
        Assert.NotNull(ex);
        Assert.Contains("5", ex.Message, StringComparison.Ordinal);
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 6
    [Fact]
    public async Task SubirAsync_AccionTieneCuatroYSeMandaUno_Retorna201()
    {
        // Arrange: borde inferior del que aún cabe (4 + 1 = 5 exactos)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        _entregables.Setup(r => r.ContarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(4);
        var request = CrearRequestValido(1);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(resultado);
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 7
    [Fact]
    public async Task SubirAsync_ArchivoMayorDe20MB_Retorna422YNoSube()
    {
        // Arrange: 20 MB + 1 byte (RN-020)
        ConfigurarEscenarioValido();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo(
            "informe.pdf", "application/pdf", BytesPdf(), EntregableAdjuntoReglas.TamanoMaximoBytes + 1);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 8
    [Fact]
    public async Task SubirAsync_ArchivoDe20MBExactos_Retorna201()
    {
        // Arrange: exactamente 20 MB (borde permitido)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo(
            "informe.pdf", "application/pdf", BytesPdf(), EntregableAdjuntoReglas.TamanoMaximoBytes);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(resultado);
        Assert.Equal(EntregableAdjuntoReglas.TamanoMaximoBytes, Assert.Single(resultado).TamanoBytes);
        Assert.Equal("20,0 MB", Assert.Single(resultado).TamanoLegible);
    }

    // 9
    [Fact]
    public async Task SubirAsync_ExtensionNoPermitida_Retorna422()
    {
        // Arrange: .exe no está en la allowlist del CA #2
        ConfigurarEscenarioValido();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo("virus.exe", "application/octet-stream", new byte[] { 0x4D, 0x5A }, 100);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 10
    [Fact]
    public async Task SubirAsync_ContentTypeOctetStreamConExtensionValida_AceptaYNormalizaMime()
    {
        // Arrange: D-C tolera application/octet-stream si la extensión es válida
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo("informe.pdf", "application/octet-stream", BytesPdf(), 12_345);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: se persiste el MIME CANÓNICO de la allowlist, nunca el crudo del navegador
        Assert.Single(resultado);
        Assert.Equal("application/pdf", Assert.Single(resultado).TipoMime);
        Assert.Equal("application/pdf", Assert.Single(_insertadas).TipoMime);
    }

    // 11
    [Fact]
    public async Task SubirAsync_PdfConMagicBytesInvalidos_Retorna422YNoSube()
    {
        // Arrange: extensión .pdf spoofeada; el contenido es HTML con <script> (RNF-009)
        var html = Encoding.UTF8.GetBytes("<html><script>alert('xss')</script></html>");
        ConfigurarEscenarioValido();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo("informe.pdf", "application/pdf", html, html.Length);

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert: se rechaza ANTES de subir (rollback vacío ⇒ nada que compensar) y nada se inserta
        Assert.NotNull(ex);
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 12
    [Fact]
    public async Task SubirAsync_DocxRenombradoAXlsx_Retorna422()
    {
        // Arrange: .xlsx cuyo ZIP contiene word/ → en realidad es un DOCX (D-C: directorio central)
        ConfigurarEscenarioValido();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo(
            "presupuesto.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BytesDocxComoXlsx(), 4_096);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 13
    [Fact]
    public async Task SubirAsync_PngYJpgValidos_Retorna201()
    {
        // Arrange
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);
        request.Archivos = new List<EntregableArchivo>
        {
            CrearArchivo("captura.png", "image/png", BytesPng(), 2_048),
            CrearArchivo("foto.jpg", "image/jpeg", BytesJpg(), 3_072)
        };

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: ambos con su MIME canónico. El esperado va en el MISMO orden que produce el
        // StringComparer.Ordinal del assert: "image/jpeg" < "image/png" ('j' 0x6A < 'p' 0x70).
        Assert.Equal(2, resultado.Count());
        Assert.Equal(new[] { "image/jpeg", "image/png" },
            _insertadas.Select(e => e.TipoMime).OrderBy(m => m, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "jpg", "png" },
            resultado.Select(r => r.Extension).OrderBy(e => e, StringComparer.Ordinal).ToArray());
    }

    // 14
    [Fact]
    public async Task SubirAsync_AccionNoExiste_Retorna404()
    {
        // Arrange
        ConfigurarEscenarioValido();
        _acciones.Setup(a => a.ObtenerPorIdAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((AccionPlanEntity?)null);
        var request = CrearRequestValido(1);

        // Act
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 15
    [Fact]
    public async Task SubirAsync_JefeAreaDeOtraArea_Retorna403YNoSube()
    {
        // Arrange: la acción es de OtraArea, el contexto es de AreaId (SEC-07)
        ConfigurarEscenarioValido(areaIdDeLaAccion: OtroAreaId);
        var request = CrearRequestValido(1);

        // Act
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 16
    [Fact]
    public async Task SubirAsync_JefeAreaSinAreaIdEnContexto_Retorna403()
    {
        // Arrange: rol JefeArea sin area_id en el JWT → 403, nunca "todas las áreas" (SEC-07)
        ConfigurarEscenarioValido();
        var request = CrearRequestSinArea(1);

        // Act
        var ex = await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        Assert.NotNull(ex);
        Assert.Contains("rea", ex.Message, StringComparison.OrdinalIgnoreCase);
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 17
    [Fact]
    public async Task SubirAsync_CicloCerrado_Retorna422()
    {
        // Arrange: F3/RC-12 — en Cerrado no se adjunta nada
        ConfigurarEscenarioValido(estadoCiclo: "Cerrado");
        var request = CrearRequestValido(1);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 18
    [Fact]
    public async Task SubirAsync_CicloBorrador_Retorna422()
    {
        // Arrange: solo el ciclo Activo admite escritura
        ConfigurarEscenarioValido(estadoCiclo: "Borrador");
        var request = CrearRequestValido(1);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.SubirAsync(request, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.SubirArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 19
    [Fact]
    public async Task SubirAsync_StorageDevuelveNull_CompensaYNoDejaFilas()
    {
        // Arrange: el 1er archivo sube bien; el 2º devuelve null (fallo de Storage, RNF-014)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var llamada = 0;
        _storage
            .Setup(s => s.SubirArchivoAsync(
                It.IsAny<string>(), It.IsAny<string>(),                 It.IsAny<byte[]>(),
                It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()))
            .Returns((string bucket, string path, byte[] bytes, TipoArchivo tipo, CancellationToken ct) =>
            {
                llamada++;
                return Task.FromResult<string?>(llamada == 1 ? path : null);
            });
        var request = CrearRequestValido(2);

        // Act
        var ex = await Assert.ThrowsAsync<InfraestructuraException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert: 500 con mensaje accionable
        Assert.NotNull(ex);

        // El 1er (ya subido) se borra de Storage → cero huérfanos
        var primeraRuta = _insertadas.Single().FilePath;
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, primeraRuta, It.IsAny<CancellationToken>()), Times.Once);

        // Cero filas huérfanas: solo 1 INSERT (el del 1er) y la tx recibe Rollback (D-H)
        _entregables.Verify(r => r.InsertarAsync(
            It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tx.Verify(t => t.Rollback(), Times.Once);
        _tx.Verify(t => t.Commit(), Times.Never);
    }

    // 20
    [Fact]
    public async Task SubirAsync_FallaInsercionEnBase_CompensaStorageYPropaga()
    {
        // Arrange: el 1er sube OK y la BD falla al insertar
        ConfigurarEscenarioValido();
        ConfigurarAuditoria();
        var rutaSubida = string.Empty;
        _storage
            .Setup(s => s.SubirArchivoAsync(
                It.IsAny<string>(), It.IsAny<string>(),                 It.IsAny<byte[]>(),
                It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()))
            .Returns((string bucket, string path, byte[] bytes, TipoArchivo tipo, CancellationToken ct) =>
            {
                rutaSubida = path;
                return Task.FromResult<string?>(path);
            });
        _entregables
            .Setup(r => r.InsertarAsync(
                It.IsAny<EntregableAdjuntoEntity>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("23505 duplicate key value (Dapper crudo)"));
        var request = CrearRequestValido(1);

        // Act
        var ex = await Assert.ThrowsAsync<InfraestructuraException>(
            () => _sut.SubirAsync(request, CancellationToken.None));

        // Assert: rollback + compensación del archivo ya subido; NO se propaga la excepción cruda
        Assert.NotNull(ex);
        Assert.DoesNotContain("23505", ex.ToString(), StringComparison.Ordinal);
        _tx.Verify(t => t.Rollback(), Times.Once);
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, rutaSubida, It.IsAny<CancellationToken>()), Times.Once);
    }

    // 21
    [Fact]
    public async Task SubirAsync_CompensacionUsaCancellationTokenNone_AunqueElClienteCancele()
    {
        // Arrange: el cliente se desconecta justo cuando falla el 2º archivo
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        using var cts = new CancellationTokenSource();
        var llamada = 0;
        _storage
            .Setup(s => s.SubirArchivoAsync(
                It.IsAny<string>(), It.IsAny<string>(),                 It.IsAny<byte[]>(),
                It.IsAny<TipoArchivo>(), It.IsAny<CancellationToken>()))
            .Returns((string bucket, string path, byte[] bytes, TipoArchivo tipo, CancellationToken ct) =>
            {
                llamada++;
                if (llamada == 2) cts.Cancel();   // el cliente se va en este punto
                return Task.FromResult<string?>(llamada == 1 ? path : null);
            });
        var request = CrearRequestValido(2);

        // Act
        await Assert.ThrowsAsync<InfraestructuraException>(() => _sut.SubirAsync(request, cts.Token));

        // Assert: la compensación se completa IGUAL, con CancellationToken.None (ADR-013 / D-M)
        var primeraRuta = _insertadas.Single().FilePath;
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, primeraRuta, CancellationToken.None), Times.Once);
    }

    // 22
    [Fact]
    public async Task SubirAsync_NombreConRutaOSeparadores_LoSaneaYUsaUuidEnLaRuta()
    {
        // Arrange: path traversal + XSS en el nombre (D-E, RNF-009)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo("..\\..\\evil<script>.pdf", "application/pdf", BytesPdf(), 12_345);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: nombre visible sin ruta ni caracteresreserved; la ruta lleva solo el UUID
        var nombrePersistido = Assert.Single(_insertadas).NombreArchivo;
        Assert.DoesNotContain("..", nombrePersistido, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", nombrePersistido, StringComparison.Ordinal);
        Assert.DoesNotContain("<", nombrePersistido, StringComparison.Ordinal);
        Assert.DoesNotContain(">", nombrePersistido, StringComparison.Ordinal);
        Assert.Equal(nombrePersistido, Assert.Single(resultado).NombreArchivo);

        var ruta = Assert.Single(_insertadas).FilePath;
        Assert.DoesNotContain("evil", ruta, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", ruta, StringComparison.Ordinal);
        // La clave de Storage son 4 segmentos {tenant}/{ciclo}/{accion}/{uuid}.{ext} (ARCH-06, D-F)
        Assert.Equal(4, ruta.Split('/').Length);
    }

    // 23
    [Fact]
    public async Task SubirAsync_NombreMasLargoDe255_LoRecortaPorLaExtension()
    {
        // Arrange: 400 caracteres con extensión válida (varchar(255) del DDL)
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var base_ = new string('a', 400);
        var request = CrearRequestValido(1);
        request.Archivos[0] = CrearArchivo($"{base_}.pdf", "application/pdf", BytesPdf(), 12_345);

        // Act
        var resultado = await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: recortado a ≤ 255 y CONSERVANDO la extensión (se recorta la base, no el sufijo)
        var nombrePersistido = Assert.Single(_insertadas).NombreArchivo;
        Assert.True(nombrePersistido.Length <= EntregableAdjuntoReglas.LongitudMaximaNombre,
            $"El nombre persistido mide {nombrePersistido.Length} caracteres");
        Assert.Equal("pdf", Path.GetExtension(nombrePersistido).TrimStart('.').ToLowerInvariant());
        Assert.Equal(nombrePersistido, Assert.Single(resultado).NombreArchivo);
    }

    // 24
    [Fact]
    public async Task SubirAsync_GrabaAuditoriaCreatePorCadaArchivo()
    {
        // Arrange: 2 archivos → 2 auditorías CREATE, cada una en la tx de SU fila
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(2);

        // Act
        await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: exactamente 2, con los 7 campos de LogAuditoriaInsert y el snapshot acotado
        _entregables.Verify(r => r.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "CREATE"
                                       && l.Entidad == "EntregableAdjunto"
                                       && l.EntidadId == _insertadas[0].Id.ToString()
                                       && l.UsuarioId == UserId
                                       && l.TenantId == TenantId
                                       && l.ValorAnterior == null
                                       && l.ValorNuevo!.Contains("\"fileSizeBytes\":12345")
                                       && !l.ValorNuevo!.Contains("TamanoLegible")
                                       && !l.ValorNuevo!.Contains("filePath")),
            It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Once);

        _entregables.Verify(r => r.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "CREATE"
                                       && l.EntidadId == _insertadas[1].Id.ToString()
                                       && l.ValorAnterior == null),
            It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Once);

        // Ambas auditorías comparten la MISMA transacción que el INSERT (corrección 1, D-M)
        Assert.Equal(2, _insertadas.Count);
        _entregables.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(_tx.Object, _txDeLaInsercion);
    }

    // 25
    [Fact]
    public async Task SubirAsync_RegistraLogSinBytesNiRutaSensible()
    {
        // Arrange
        ConfigurarEscenarioValido();
        ConfigurarInsercionYRelectura();
        ConfigurarAuditoria();
        var request = CrearRequestValido(1);

        // Act
        await _sut.SubirAsync(request, CancellationToken.None);

        // Assert: LogInformation presente, pero ni la ruta interna de Storage ni el contenido
        Assert.Contains(_logs, l => l.Nivel == LogLevel.Information);
        var ruta = Assert.Single(_insertadas).FilePath;
        Assert.All(_logs, l => Assert.DoesNotContain(ruta, l.Mensaje, StringComparison.Ordinal));
        Assert.All(_logs, l => Assert.DoesNotContain("%PDF", l.Mensaje, StringComparison.Ordinal));
    }

    // ═══════════════════════════════════ ListarAsync (26-35) ═══════════════════════════════════

    // 26
    [Fact]
    public async Task ListarAsync_DevuelveProyeccionConNombreTamanoYFecha()
    {
        // Arrange: los 4 campos del CA #3 + tamaño y MIME
        ConfigurarEscenarioValido();
        var created = new DateTime(2026, 9, 20, 8, 30, 0, DateTimeKind.Utc);
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>
            {
                CrearAdjunto(nombre: "evidencia.pdf", tamano: 12_345, nombreUsuario: "Jefe de Área", createdAt: created)
            });

        // Act
        var resultado = await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert
        var adjunto = Assert.Single(resultado);
        Assert.Equal("evidencia.pdf", adjunto.NombreArchivo);
        Assert.Equal("12,1 KB", adjunto.TamanoLegible);
        Assert.Equal(12_345, adjunto.TamanoBytes);
        Assert.Equal("application/pdf", adjunto.TipoMime);
        Assert.Equal("Jefe de Área", adjunto.SubidoPorNombre);
        Assert.Equal(created, adjunto.CreatedAt);
    }

    // 27
    [Fact]
    public async Task ListarAsync_SinAdjuntos_DevuelveListaVacia()
    {
        // Arrange: la acción existe pero no tiene adjuntos
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>());

        // Act
        var resultado = await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: lista vacía, NO excepción y NO 404 (D-C; la UI pinta .empty-state, UX-05)
        Assert.NotNull(resultado);
        Assert.Empty(resultado);
    }

    // 28
    [Fact]
    public async Task ListarAsync_FormateaTamanoABytesYMegabytes()
    {
        // Arrange: 812 B y 1,4 MB (1.468.006 B = 1,40000 MB exacto: inequívoco con cualquier redondeo)
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>
            {
                CrearAdjunto(id: EntregableId, tamano: 812, nombre: "a.pdf", nombreUsuario: "Jefe de Área"),
                CrearAdjunto(tamano: 1_468_006, nombre: "b.pdf", nombreUsuario: "Jefe de Área")
            });

        // Act
        var resultado = (await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None))
                            .ToList();

        // Assert: coma decimal, coherente con el resto de la app
        Assert.Equal("812 B", resultado[0].TamanoLegible);
        Assert.Equal("1,4 MB", resultado[1].TamanoLegible);
    }

    // 29
    [Fact]
    public async Task ListarAsync_OrdenaPorFechaYNombre()
    {
        // Arrange: 3 filas desordenadas, dos del mismo instante (desempate por nombre)
        ConfigurarEscenarioValido();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>
            {
                CrearAdjunto(nombre: "zeta.pdf", createdAt: t2),
                CrearAdjunto(nombre: "beta.pdf", createdAt: t1),
                CrearAdjunto(nombre: "alfa.pdf", createdAt: t1)
            });

        // Act
        var resultado = (await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None))
                            .ToList();

        // Assert: CreatedAt ASC y luego NombreArchivo ASC (determinista, testeable sin DAL)
        Assert.Equal(new[] { "alfa.pdf", "beta.pdf", "zeta.pdf" }, resultado.Select(r => r.NombreArchivo).ToArray());
    }

    // 30
    [Fact]
    public async Task ListarAsync_AutorYGerente_PuedenEliminarYLosDemasNo()
    {
        // Arrange: del usuario actual, de otro, del usuario actual
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>
            {
                CrearAdjunto(subidoPor: UserId, nombre: "mio-1.pdf"),
                CrearAdjunto(subidoPor: OtroUserId, nombre: "de-otro.pdf"),
                CrearAdjunto(subidoPor: UserId, nombre: "mio-2.pdf")
            });

        // Act
        var comoJefeArea = (await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None))
                                .ToList();
        var comoGerente = (await _sut.ListarAsync(AccionId, TenantId, UserId, RolGerente, null, CancellationToken.None))
                                .ToList();

        // Assert: la regla del CA #5 se materializa UNA vez, en el proyector BLL (DB-04).
        // La afirmación va POR NOMBRE, no por posición: ListarAsync ordena por CreatedAt y luego por
        // NombreArchivo (contrato que fija el caso 29) y el reloj del fixture está FIJO, así que el
        // desempate cae en el nombre. Indexar por posición daría un falso rojo si ese orden cambia.
        var permisosJefeArea = comoJefeArea.ToDictionary(r => r.NombreArchivo, r => r.PuedeEliminar);
        Assert.True(permisosJefeArea["mio-1.pdf"], "el autor puede eliminar su propio adjunto.");
        Assert.False(permisosJefeArea["de-otro.pdf"], "un JefeArea NO puede eliminar el adjunto de otro usuario.");
        Assert.True(permisosJefeArea["mio-2.pdf"], "el autor puede eliminar su propio adjunto.");

        // El Gerente puede eliminar los de cualquiera (RN-006), incluidos los que no son suyos.
        var permisosGerente = comoGerente.ToDictionary(r => r.NombreArchivo, r => r.PuedeEliminar);
        Assert.Equal(3, permisosGerente.Count);
        Assert.All(permisosGerente.Values, puede => Assert.True(puede));
    }

    // 31
    [Fact]
    public async Task ListarAsync_UsuarioBorrado_SubidoPorNombreNull()
    {
        // Arrange: LEFT JOIN sin coincidencia (el usuario fue borrado)
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity>
            {
                CrearAdjunto(nombreUsuario: null, nombre: "huerfano.pdf")
            });

        // Act
        var resultado = await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: la fila NO desaparece y el nombre queda null (la UI muestra "—")
        var adjunto = Assert.Single(resultado);
        Assert.Null(adjunto.SubidoPorNombre);
        Assert.Equal("huerfano.pdf", adjunto.NombreArchivo);
    }

    // 32
    [Fact]
    public async Task ListarAsync_AccionNoExiste_Retorna404()
    {
        // Arrange
        ConfigurarEscenarioValido();
        _acciones.Setup(a => a.ObtenerPorIdAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync((AccionPlanEntity?)null);

        // Act
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert
        _entregables.Verify(r => r.ListarPorAccionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 33
    [Fact]
    public async Task ListarAsync_JefeAreaDeOtraArea_Retorna403()
    {
        // Arrange: SEC-07 — la acción es de OtraArea
        ConfigurarEscenarioValido(areaIdDeLaAccion: OtroAreaId);

        // Act
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert: la tabla de adjuntos ni se toca
        _entregables.Verify(r => r.ListarPorAccionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 34
    [Fact]
    public async Task ListarAsync_RolDesconocido_Retorna403()
    {
        // Arrange: defensa en profundidad — la BLL no confía en el [Authorize] del controller
        ConfigurarEscenarioValido();

        // Act
        await Assert.ThrowsAsync<AccesoDenegadoException>(
            () => _sut.ListarAsync(AccionId, TenantId, UserId, "Analista", null, CancellationToken.None));

        // Assert
        _entregables.Verify(r => r.ListarPorAccionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 35
    [Fact]
    public async Task ListarAsync_NoExponeTenantIdNiFilePath()
    {
        // Arrange
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ListarPorAccionAsync(AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntregableAdjuntoEntity> { CrearAdjunto() });

        // Act
        await _sut.ListarAsync(AccionId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: SEC-06 (nunca TenantId) y D-H (la ruta interna nunca sale) — por reflexión
        var tipo = typeof(EntregableAdjuntoResponse);
        Assert.Null(tipo.GetProperty("TenantId"));
        Assert.Null(tipo.GetProperty("FilePath"));
    }

    // ═══════════════════════ ObtenerDescargaAsync (36-42, 42 con dos nombres) ═══════════════════

    // 36
    [Fact]
    public async Task ObtenerDescargaAsync_AdjuntoValido_RetornaUrlFirmadaYExpiracion24h()
    {
        // Arrange
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto();
        ConfigurarAdjunto(adjunto);
        const string urlFirmada = "https://proyecto.supabase.co/storage/v1/object/sign/entregables/firmado?token=abc";
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(urlFirmada);

        // Act
        var respuesta = await _sut.ObtenerDescargaAsync(
            AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert
        Assert.Equal(urlFirmada, respuesta.Url);
        Assert.Equal(Ahora.AddHours(24), respuesta.ExpiraEn);
        Assert.Equal(adjunto.NombreArchivo, respuesta.NombreArchivo);
        Assert.Equal(adjunto.FileSizeBytes, respuesta.TamanoBytes);

        // Se firma la ruta INTERNA de Storage con 24 h (ARCH-06) y con el bucket correcto
        _storage.Verify(s => s.ObtenerUrlFirmadaAsync(
            Bucket, adjunto.FilePath, 24, It.IsAny<CancellationToken>()), Times.Once);
    }

    // 37
    [Fact]
    public async Task ObtenerDescargaAsync_StorageDevuelveNull_DegradaAUrlNulaYNoLanza500()
    {
        // Arrange: el storage no devuelve URL (RNF-014 → degradación elegante, D-G)
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto();
        ConfigurarAdjunto(adjunto);
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var respuesta = await _sut.ObtenerDescargaAsync(
            AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: 200 con Url = null, ExpiraEn SIEMPRE relleno, LogError y NINGÚN 500
        Assert.Null(respuesta.Url);
        Assert.Equal(Ahora.AddHours(24), respuesta.ExpiraEn);
        Assert.True(HayLogDeError, "Se esperaba un LogError por el fallo de Storage (STACK-11).");
    }

    // 38
    [Fact]
    public async Task ObtenerDescargaAsync_AdjuntoDeOtraAccion_Retorna404()
    {
        // Arrange: el adjunto existe pero es de OTRA acción → la DAL exige los dos ids
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto();
        _entregables
            .Setup(r => r.ObtenerPorIdAsync(adjunto.Id, AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntregableAdjuntoEntity?)null);

        // Act
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ObtenerDescargaAsync(
            AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert: 404 (no 403) para no filtrar la existencia, y no se firma NADA
        _storage.Verify(s => s.ObtenerUrlFirmadaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 39
    [Fact]
    public async Task ObtenerDescargaAsync_JefeAreaDeOtraArea_Retorna403()
    {
        // Arrange
        ConfigurarEscenarioValido(areaIdDeLaAccion: OtroAreaId);
        var adjunto = CrearAdjunto();
        ConfigurarAdjunto(adjunto);

        // Act
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => _sut.ObtenerDescargaAsync(
            AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert
        _storage.Verify(s => s.ObtenerUrlFirmadaAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 40
    [Fact]
    public async Task ObtenerDescargaAsync_NoExponeLaRutaInterna_RetornaSoloUrl()
    {
        // Arrange
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto();
        ConfigurarAdjunto(adjunto);
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://x.supabase.co/firmada");

        // Act
        var respuesta = await _sut.ObtenerDescargaAsync(
            AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: D-H — la clave de Storage no existe en el DTO (por reflexión)
        var tipo = typeof(EntregableDescargaResponse);
        Assert.Null(tipo.GetProperty("FilePath"));
        Assert.Null(tipo.GetProperty("TenantId"));
        Assert.Equal("https://x.supabase.co/firmada", respuesta.Url);
    }

    // 41
    [Fact]
    public async Task ObtenerDescargaAsync_RegistraLogSinLaUrlCompleta()
    {
        // Arrange: la URL firmada es una CREDENCIAL temporal (RNF-008/RNF-009)
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto();
        ConfigurarAdjunto(adjunto);
        const string urlFirmada = "https://proyecto.supabase.co/storage/v1/object/sign/entregables/x?token=SECRETO";
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(urlFirmada);

        // Act
        await _sut.ObtenerDescargaAsync(AccionId, adjunto.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: LogInformation con TenantId/UserId/AccionId/AdjuntoId/ExpiraEn, sin la URL
        Assert.Contains(_logs, l => l.Nivel == LogLevel.Information);
        Assert.All(_logs, l => Assert.DoesNotContain("SECRETO", l.Mensaje, StringComparison.Ordinal));
        Assert.All(_logs, l => Assert.DoesNotContain(urlFirmada, l.Mensaje, StringComparison.Ordinal));
        Assert.All(_logs, l => Assert.DoesNotContain(adjunto.FilePath, l.Mensaje, StringComparison.Ordinal));
    }

    // 42a · v3 (bloqueo 8): el criterio pasó de «MIME canónico» a «TIPO CANÓNICO»: solo Pdf va
    // inline; Png y Jpg se descargan (attachment). La afirmación va sobre el HELPER ÚNICO
    // —ContentDisposition(TipoArchivo)— porque ObtenerUrlFirmadaAsync(bucket, path, horas, ct)
    // NO lleva contentDisposition: una URL firmada no puede reescribir el content-disposition de un
    // objeto ya subido, así que el helper es la única superficie verificable desde la BLL.
    [Fact]
    public async Task ObtenerDescargaAsync_ImagenUsaContentDispositionInline()
    {
        // Arrange: un PNG se DESCARGA (attachment en la v3) y un PDF se abre en pestaña (inline)
        ConfigurarEscenarioValido();
        var png = new EntregableAdjuntoEntity
        {
            Id = EntregableId, TenantId = TenantId, AccionId = AccionId,
            NombreArchivo = "captura.png",
            FilePath = $"{TenantId}/{CicloId}/{AccionId}/{Guid.NewGuid()}.png",
            FileSizeBytes = 2_048, TipoMime = "image/png", SubidoPor = UserId,
            CreatedAt = Ahora.UtcDateTime, SubidoPorNombre = "Jefe de Área"
        };
        ConfigurarAdjunto(png);
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://x.supabase.co/firmada.png");

        // Act
        var respuesta = await _sut.ObtenerDescargaAsync(
            AccionId, png.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert 1: la descarga funciona…
        Assert.Equal("https://x.supabase.co/firmada.png", respuesta.Url);

        // Assert 2: el helper decide POR EL TIPO CANÓNICO, sin filename= y sin tocar la firma
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Png));
        Assert.DoesNotContain("filename=", TipoArchivoHelper.ContentDisposition(TipoArchivo.Png), StringComparison.OrdinalIgnoreCase);
    }

    // 42b · v3 (bloqueo 8): el MISMO helper, en el otro sentido — Pdf es el único inline.
    // Además afirma que la BLL entrega al Storage el TIPO CANÓNICO ya detectado (no el nombre ni
    // el ContentType crudo del navegador), que es el valor que el helper convierte en
    // FileUploadOptions.ContentDisposition dentro de StorageHelper (caso 58).
    [Fact]
    public async Task ObtenerDescargaAsync_DocumentoUsaAttachment()
    {
        // Arrange
        ConfigurarEscenarioValido();
        var pdf = CrearAdjunto();
        ConfigurarAdjunto(pdf);
        _storage
            .Setup(s => s.ObtenerUrlFirmadaAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://x.supabase.co/firmada.pdf");

        // Act
        var respuesta = await _sut.ObtenerDescargaAsync(
            AccionId, pdf.Id, TenantId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert 1
        Assert.Equal("https://x.supabase.co/firmada.pdf", respuesta.Url);

        // Assert 2: el helper, y la subida que lo alimenta
        Assert.Equal("inline", TipoArchivoHelper.ContentDisposition(TipoArchivo.Pdf));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Docx));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Xlsx));
        Assert.Equal("attachment", TipoArchivoHelper.ContentDisposition(TipoArchivo.Desconocido));

        // Y en la SUBIDA (que es donde se fija el content-disposition) la BLL pasa el TipoArchivo
        // canónico detectado, no el nombre ni el ContentType crudo del navegador (D-C / RNF-009).
        ConfigurarInsercionYRelectura();
        var request = CrearRequestValido(1);
        await _sut.SubirAsync(request, CancellationToken.None);
        _storage.Verify(s => s.SubirArchivoAsync(
            Bucket, It.IsAny<string>(), It.IsAny<byte[]>(),
            TipoArchivo.Pdf, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═══════════════ EliminarAsync (43-51) · devuelve Task<int> (bloqueo 2) ═══════════════════

    // 43
    [Fact]
    public async Task EliminarAsync_AutorDelAdjunto_EliminaYBorraDeStorage()
    {
        // Arrange: el autor del adjunto
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: UserId);
        ConfigurarAdjunto(adjunto);
        ConfigurarAuditoria();
        IDbTransaction? txUsada = null;
        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, Guid, IDbTransaction?, CancellationToken>((id, accionId, tenantId, tx, ct) => txUsada = tx)
            .ReturnsAsync(1);

        // Act
        var resultado = await _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: BLOQUEO 2 — la BLL devuelve las FILAS AFECTADAS (int), no un bool
        Assert.Equal(1, resultado);

        // La fila se borra con los 3 ids Y la transacción (corrección 1)
        _entregables.Verify(r => r.EliminarAsync(
            adjunto.Id, AccionId, TenantId, It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(_tx.Object, txUsada);
        _tx.Verify(t => t.Commit(), Times.Once);

        // Y el objeto de Storage se borra con su ruta interna, en el bucket correcto
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, adjunto.FilePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    // 44
    [Fact]
    public async Task EliminarAsync_GerenteEliminaAdjuntoDeOtro_EliminaOk()
    {
        // Arrange: el Gerente puede borrar el adjunto de cualquiera (CA #5)
        ConfigurarEscenarioValido(areaIdDeLaAccion: OtroAreaId);
        var adjunto = CrearAdjunto(subidoPor: OtroUserId);
        ConfigurarAdjunto(adjunto);
        ConfigurarAuditoria();
        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var resultado = await _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolGerente, null, CancellationToken.None);

        // Assert: BLOQUEO 2 — el Gerente también obtiene 1 fila afectada
        Assert.Equal(1, resultado);
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, adjunto.FilePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    // 45
    [Fact]
    public async Task EliminarAsync_JefeAreaEliminaAdjuntoDeOtro_Retorna403()
    {
        // Arrange: mismo área, pero el adjunto lo subió OTRO JefeArea
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: OtroUserId);
        ConfigurarAdjunto(adjunto);

        // Act
        var ex = await Assert.ThrowsAsync<AccesoDenegadoException>(() => _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert: ni BD ni Storage tocados, y ni siquiera se abre transacción
        Assert.NotNull(ex);
        _entregables.Verify(r => r.EliminarAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.EliminarArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _entregables.Verify(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // 46
    [Fact]
    public async Task EliminarAsync_StorageFallaTrasBorrarEnBD_ReportaExitoYRegistraLog()
    {
        // Arrange: la fila y su auditoría YA están commiteadas; el storage revienta después
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: UserId);
        ConfigurarAdjunto(adjunto);
        ConfigurarAuditoria();
        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _storage
            .Setup(s => s.EliminarArchivoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Supabase Storage 500"));

        // Act
        var resultado = await _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: D-I — degradación elegante: 1 fila afectada + LogError, nunca 500
        Assert.Equal(1, resultado);
        Assert.True(HayLogDeError, "Se esperaba un LogError por el fallo de Storage (RNF-014).");
        _tx.Verify(t => t.Commit(), Times.Once);
        _tx.Verify(t => t.Rollback(), Times.Never);
    }

    // 47 · v3 (bloqueo 2): este caso cubre TAMBIÉN la CARRERA. Con `ObtenerPorIdAsync` → null es el
    // 404 de siempre; y si la fila se lee pero el DELETE devuelve 0 (otro la borró entre el paso 4
    // y el DELETE), la BLL lanza la MISMA NoEncontradoException DESDE DENTRO de la transacción →
    // Rollback, 404 y, sobre todo, SIN InsertLogAsync: no se audita un borrado que no ocurrió.
    [Fact]
    public async Task EliminarAsync_AdjuntoNoExiste_Retorna404()
    {
        // ── Fase 1 · el adjunto no existe ─────────────────────────────────────────────
        // Arrange
        ConfigurarEscenarioValido();
        _entregables
            .Setup(r => r.ObtenerPorIdAsync(EntregableId, AccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntregableAdjuntoEntity?)null);

        // Act
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.EliminarAsync(
            AccionId, EntregableId, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert
        _entregables.Verify(r => r.EliminarAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.EliminarArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // ── Fase 2 · la CARRERA: la fila se leyó, pero el DELETE afecta a 0 filas ──────
        // Arrange: otro actor borró la fila entre la lectura y el DELETE
        ConfigurarAuditoria();
        var adjunto = CrearAdjunto(subidoPor: UserId);
        ConfigurarAdjunto(adjunto);
        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert: el 0 de la DAL es la ÚNICA señal de la carrera (por eso Task<int>, no Task)
        Assert.NotNull(ex);
        _tx.Verify(t => t.Rollback(), Times.Once);
        _tx.Verify(t => t.Commit(), Times.Never);
        _entregables.Verify(r => r.InsertLogAsync(
            It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.EliminarArchivoAsync(
            Bucket, adjunto.FilePath, It.IsAny<CancellationToken>()), Times.Never);
    }

    // 48
    [Fact]
    public async Task EliminarAsync_CicloCerrado_Retorna422()
    {
        // Arrange: F3/RC-12 — un ciclo Cerrado es de solo lectura, también para los adjuntos
        ConfigurarEscenarioValido(estadoCiclo: "Cerrado");
        var adjunto = CrearAdjunto(subidoPor: UserId);
        ConfigurarAdjunto(adjunto);

        // Act
        await Assert.ThrowsAsync<ValidacionException>(() => _sut.EliminarAsync(
            AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert
        _entregables.Verify(r => r.EliminarAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.EliminarArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 49
    [Fact]
    public async Task EliminarAsync_GrabaAuditoriaDeleteConDatosAntes()
    {
        // Arrange
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: UserId, tamano: 12_345);
        ConfigurarAdjunto(adjunto);
        ConfigurarAuditoria();
        IDbTransaction? txDeLaFila = null;
        IDbTransaction? txDelLog = null;
        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, Guid, IDbTransaction?, CancellationToken>((id, a, t, tx, ct) => txDeLaFila = tx)
            .ReturnsAsync(1);
        _entregables
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback<LogAuditoriaInsert, IDbTransaction?, CancellationToken>((log, tx, ct) => txDelLog = tx)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.EliminarAsync(AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: DELETE con el snapshot en ValorAnterior, ValorNuevo = null, sin TamanoLegible
        _entregables.Verify(r => r.InsertLogAsync(
            It.Is<LogAuditoriaInsert>(l => l.Accion == "DELETE"
                                       && l.Entidad == "EntregableAdjunto"
                                       && l.EntidadId == adjunto.Id.ToString()
                                       && l.UsuarioId == UserId
                                       && l.TenantId == TenantId
                                       && l.ValorNuevo == null
                                       && l.ValorAnterior != null
                                       && l.ValorAnterior!.Contains("\"fileSizeBytes\":12345")
                                       && !l.ValorAnterior!.Contains("TamanoLegible")
                                       && !l.ValorAnterior!.Contains("filePath")),
            It.IsAny<IDbTransaction>(), It.IsAny<CancellationToken>()), Times.Once);

        // El MISMO objeto tx en el DELETE de la fila y en su auditoría (corrección 1, D-M)
        Assert.NotNull(txDeLaFila);
        Assert.Same(txDeLaFila, txDelLog);
    }

    // 50
    [Fact]
    public async Task EliminarAsync_BorraEnBdAntesQueEnStorage()
    {
        // Arrange: se registra el ORDEN real de las llamadas (BD → auditoría → commit → Storage)
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: UserId);
        ConfigurarAdjunto(adjunto);
        var orden = new List<string>();

        _entregables
            .Setup(r => r.EliminarAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback(() => orden.Add("bd"))
            .ReturnsAsync(1);
        _entregables
            .Setup(r => r.InsertLogAsync(It.IsAny<LogAuditoriaInsert>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()))
            .Callback(() => orden.Add("log"))
            .Returns(Task.CompletedTask);
        _tx.Setup(t => t.Commit()).Callback(() => orden.Add("commit"));
        _storage
            .Setup(s => s.EliminarArchivoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => orden.Add("storage"))
            .ReturnsAsync(true);

        // Act
        await _sut.EliminarAsync(AccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None);

        // Assert: D-I — BD primero; el Storage es best-effort y NO puede abortar la fila
        Assert.Equal(new[] { "bd", "log", "commit", "storage" }, orden);
        _tx.Verify(t => t.Rollback(), Times.Never);
    }

    // 51
    [Fact]
    public async Task EliminarAsync_UsurpacionDeAccion_Retorna404()
    {
        // Arrange: el adjunto existe en la BD pero NO en la acción de la URL
        ConfigurarEscenarioValido();
        var adjunto = CrearAdjunto(subidoPor: UserId);
        var otraAccionId = Guid.Parse("00000000-0000-0000-0000-000000000099");
        _acciones
            .Setup(a => a.ObtenerPorIdAsync(otraAccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccionPlanEntity { Id = otraAccionId, TenantId = TenantId, CicloId = CicloId, AreaId = AreaId });
        _entregables
            .Setup(r => r.ObtenerPorIdAsync(adjunto.Id, otraAccionId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntregableAdjuntoEntity?)null);

        // Act
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.EliminarAsync(
            otraAccionId, adjunto.Id, TenantId, UserId, RolJefeArea, AreaId, CancellationToken.None));

        // Assert: la DAL exige tenant + ACCIÓN + id; nada se borra
        _entregables.Verify(r => r.EliminarAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IDbTransaction?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _storage.Verify(s => s.EliminarArchivoAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
