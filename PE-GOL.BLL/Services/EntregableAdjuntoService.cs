using System.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DTO.Dtos;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.PlanOperativo;
using PE_GOL.Utility.Exceptions;
using PE_GOL.Utility.Files;
using PE_GOL.Utility.Helpers;
using PE_GOL.Utility.Storage;

namespace PE_GOL.BLL.Services;

/// <summary>
/// Spec HU-022 · Entregables adjuntos de una acción (bucket privado <c>entregables</c>).
/// <para>
/// <b>Dónde vive cada decisión.</b> La BLL es la fuente de verdad de la lógica de negocio
/// (ARCH-02, DB-04, UX-04): aquí se calculan los campos derivados (<c>TamanoLegible</c>,
/// <c>Extension</c>, <c>PuedeEliminar</c>), se aplican las reglas de
/// <see cref="EntregableAdjuntoReglasNegocio"/> y se toma la decisión de aislamiento por área
/// (SEC-07). <b>No hay ni una línea de SQL en esta clase</b> y tampoco decisiones de autorización
/// duplicadas en el controller: el <c>[Authorize(Roles = …)]</c> es la primera barrera y esta BLL
/// la segunda (defensa en profundidad, la BLL no confía en él).
/// </para>
/// <para>
/// <b>Por qué el <c>TenantContext</c> NO se inyecta aquí</b> (SEC-06): el servicio lo recibe por
/// parámetro (<c>tenantId</c>, <c>userId</c>, <c>rol</c>, <c>areaId</c>), lo resuelve el controller
/// desde los claims del JWT y así el servicio es testeable sin contexto de ambientación. Ningún
/// endpoint acepta <c>tenant_id</c> ni <c>ciclo_id</c>: el <c>ciclo_id</c> sale de
/// <c>accion_plan</c> (paso 4 de <see cref="SubirAsync"/>).
/// </para>
/// <para>
/// <b>SEC-07 vive aquí y no en la DAL</b>, porque <c>entregable_adjunto</c> no tiene columna
/// <c>area_id</c>: el aislamiento se comprueba contra <c>accion_plan.area_id</c> —que la DAL de
/// acciones ya devuelve filtrado por tenant y por RLS— <b>antes de tocar la tabla de adjuntos</b>.
/// Un JefeArea sin <c>area_id</c> en el JWT es 403, nunca «todas las áreas».
/// </para>
/// <para>
/// <b>No se inyecta <c>IUsuarioRepository</c></b>: el «usuario que subió» del CA #3 llega por el
/// <c>LEFT JOIN usuario.nombre</c> de la DAL (la entidad tiene <c>SubidoPorNombre</c>), de modo que
/// la BLL no vuelve a consultar <c>usuario</c>. El nombre del usuario <i>actual</i> para la UI lo
/// da <c>SesionService</c>, no este servicio.
/// </para>
/// <para>
/// <b>Compensación (D-H / D-I, RNF-014).</b> Ningún camino deja archivos huérfanos ni filas
/// fantasma: <c>SubirAsync</c> es todo o nada por lote (una única transacción para las filas y sus
/// auditorías, y borrado best-effort de lo ya subido si algo falla) y <c>EliminarAsync</c> borra
/// primero en BD y después en Storage, para que un fallo de infraestructura no pueda impedir un
/// borrado que el usuario ya ve confirmado.
/// </para>
/// </summary>
public sealed class EntregableAdjuntoService : IEntregableService
{
    /// <summary>Entidad en <c>log_auditoria</c> (ADR-003). Mismo nombre que en el resto del repo.</summary>
    private const string EntidadAuditoria = "EntregableAdjunto";

    private const string RolJefeArea = "JefeArea";
    private const string RolGerente = "Gerente";

    /// <summary>
    /// Bytes leídos para la firma. El spec fija ~512: suficiente para las 4 firmas cortas y para
    /// las cabeceras locales del ZIP que desambiguan DOCX de XLSX.
    /// </summary>
    private const int BytesCabecera = 512;

    /// <summary>JSON legible en el log, sin escapes Unicode (ADR-003).</summary>
    private static readonly JsonSerializerOptions JsonOpcionesAuditoria = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IEntregableAdjuntoRepository _entregables;
    private readonly IAccionPlanRepository _acciones;
    private readonly ICicloRepository _ciclos;
    private readonly IStorageHelper _storage;
    private readonly EntregableAdjuntoReglasNegocio _reglas;
    private readonly ILogger<EntregableAdjuntoService> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Inyección por ctor únicamente, sin <c>IServiceProvider</c> (spec § Constructor).</summary>
    public EntregableAdjuntoService(
        IEntregableAdjuntoRepository entregables,
        IAccionPlanRepository acciones,
        ICicloRepository ciclos,
        IStorageHelper storage,
        EntregableAdjuntoReglasNegocio reglas,
        ILogger<EntregableAdjuntoService> logger,
        TimeProvider timeProvider)
    {
        _entregables = entregables;
        _acciones = acciones;
        _ciclos = ciclos;
        _storage = storage;
        _reglas = reglas;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ══════════════════════════════ Método 1 · ListarAsync (CA #3) ═══════════════════════════

    /// <inheritdoc cref="IEntregableService.ListarAsync"/>
    public async Task<IEnumerable<EntregableAdjuntoResponse>> ListarAsync(
        Guid accionId, Guid tenantId, Guid userId, string rol, Guid? areaId,
        CancellationToken ct = default)
    {
        if (accionId == Guid.Empty)
            throw new ValidacionException("El identificador de la acción es obligatorio.");

        // accionId PRIMERO: IAccionPlanRepository invierte el orden de ICicloRepository (corrección 2).
        var accion = await _acciones.ObtenerPorIdAsync(accionId, tenantId, ct)
            ?? throw new NotFoundException($"La acción {accionId} no existe para el tenant actual.");

        ValidarAccesoAAccion(accion.AreaId, rol, areaId);

        var entidades = await _entregables.ListarPorAccionAsync(accionId, tenantId, ct);

        // DB-04: el orden y los campos derivados se calculan AQUÍ, no en SQL ni en el controller.
        return entidades
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.NombreArchivo, StringComparer.Ordinal)
            .Select(e => Proyectar(e, userId, rol))
            .ToList();
    }

    // ══════════════════════════════ Método 2 · SubirAsync (CA #1, #2) ═════════════════════════

    /// <inheritdoc cref="IEntregableService.SubirAsync"/>
    public async Task<IEnumerable<EntregableAdjuntoResponse>> SubirAsync(
        EntregableSubidaRequest request, CancellationToken ct = default)
    {
        if (request.AccionId == Guid.Empty)
            throw new ValidacionException("El identificador de la acción es obligatorio.");

        var archivos = request.Archivos ?? [];

        // ── 1. FORMA y verdad, antes de tocar nada (UX-04: el servidor es la fuente de verdad) ──
        _reglas.ValidarConteo(0, archivos.Count);
        foreach (var archivo in archivos)
        {
            _reglas.ValidarTamano(archivo.TamanoBytes, archivo.NombreOriginal);
            _reglas.ValidarTipo(archivo.ExtensionDeNombre, archivo.ContentType, archivo.NombreOriginal);
        }

        // ── 2. La acción existe y es del tenant (404) ──
        var accion = await _acciones.ObtenerPorIdAsync(request.AccionId, request.TenantId, ct)
            ?? throw new NotFoundException("La acción no existe para el tenant actual.");

        // ── 3. Área y rol (403). El Gerente tiene lectura de todas las áreas (RN-006), por eso no
        //       se le exige area_id; el JefeArea sí (SEC-07). ──
        if (EsJefeArea(request.Rol))
            ValidarAreaDelActor(accion.AreaId, request.AreaId);

        // ── 4. Estado del ciclo (F3 / RC-12). tenantId PRIMERO: orden opuesto al de acciones. ──
        var ciclo = await _ciclos.ObtenerPorIdAsync(request.TenantId, accion.CicloId, ct)
            ?? throw new NotFoundException("El ciclo de la acción no existe.");
        _reglas.ValidarCicloCerrado(ciclo.Estado);

        // ── 5. Conteo TOTAL: la tabla no tiene contador ni trigger, se cuenta en BLL (DB-04) ──
        var existentes = await _entregables.ContarPorAccionAsync(request.AccionId, request.TenantId, ct);
        _reglas.ValidarConteo(existentes, archivos.Count);

        // ── 6-7. Rutas y saneo, ANTES de abrir la transacción: si algo es inválido, no hay nada
        //         que compensar todavía. El UUID garantiza que 5 archivos no colisionen (D-E). ──
        var pendientes = new List<ArchivoAPersistir>(archivos.Count);
        for (var i = 0; i < archivos.Count; i++)
        {
            var archivo = archivos[i];
            var uuid = Guid.NewGuid();
            var extension = TipoArchivoHelper.ExtensionCanonica(archivo.ExtensionDeNombre, archivo.ContentType);

            pendientes.Add(new ArchivoAPersistir(
                Archivo: archivo,
                Uuid: uuid,
                Path: $"{request.TenantId}/{accion.CicloId}/{request.AccionId}/{uuid}.{extension}",
                NombreSaneado: EntregableAdjuntoReglas.SanearNombreArchivo(archivo.NombreOriginal)));
        }

        // ── 8-11. UNA transacción para todo el lote (D-H): todo o nada ──
        var subidas = new List<(string Path, int Orden)>();
        var creadas = new List<Guid>();
        long totalBytes = 0;

        using var tx = await _entregables.BeginTransactionAsync(ct);
        try
        {
            for (var i = 0; i < pendientes.Count; i++)
            {
                var pendiente = pendientes[i];

                // 11.a · El contenido ya está en memoria (el controller volcó el IFormFile).
                var bytes = await pendiente.Archivo.LeerContenidoAsync(ct);

                // 11.b · FIRMA DE BYTES, antes de subir (RNF-009). Al ejecutarse aquí la lista de
                //         compensación está vacía para este archivo: no hay nada que compensar.
                var tipo = TipoArchivoHelper.DetectarTipo(
                    bytes.AsSpan(0, Math.Min(bytes.Length, BytesCabecera)));
                if (tipo == TipoArchivo.Desconocido || tipo != TipoDesdeExtension(pendiente.Archivo.ExtensionDeNombre))
                    throw new ValidacionException(
                        $"El contenido del archivo «{pendiente.NombreSaneado}» no corresponde a su extensión. Se rechazó antes de almacenarlo.");

                // 11.c · Subida al bucket privado con la ruta ya construida (ARCH-06).
                var rutaPersistida = await _storage.SubirArchivoAsync(
                    EntregableAdjuntoReglas.BucketEntregables, pendiente.Path, bytes, tipo, ct);
                if (string.IsNullOrEmpty(rutaPersistida))
                {
                    _logger.LogError(
                        "Supabase Storage no confirmó la subida de un entregable. Modulo=PlanOperativo TenantId={TenantId} UserId={UserId} AccionId={AccionId} Archivo={Archivo}",
                        request.TenantId, request.UserId, request.AccionId, pendiente.NombreSaneado);
                    throw new InfraestructuraException(
                        $"No se pudo almacenar el archivo «{pendiente.NombreSaneado}». Inténtalo de nuevo.");
                }

                // Se registra en la compensación en cuanto el objeto EXISTE en Storage, antes del
                // INSERT: si la BD falla, ese objeto es un huérfano y hay que borrarlo. Es lo que
                // hace que un fallo de base de datos no deje archivos sueltos (RNF-014).
                subidas.Add((rutaPersistida, i));

                // 11.d · INSERT + auditoría CREATE en la MISMA transacción (corrección 1, D-M).
                var entidad = new EntregableAdjuntoEntity
                {
                    Id = pendiente.Uuid,
                    TenantId = request.TenantId,
                    AccionId = request.AccionId,
                    NombreArchivo = pendiente.NombreSaneado,
                    FilePath = rutaPersistida,
                    FileSizeBytes = pendiente.Archivo.TamanoBytes,
                    // MIME CANÓNICO de la allowlist (D-C): nunca el ContentType crudo del navegador.
                    TipoMime = TipoArchivoHelper.MimeCanonica(tipo),
                    SubidoPor = request.UserId
                    // SubidoPorNombre y CreatedAt NO se envían: los fijan el LEFT JOIN y el DEFAULT now().
                };
                await _entregables.InsertarAsync(entidad, tx, ct);
                await _entregables.InsertLogAsync(new LogAuditoriaInsert
                {
                    TenantId = request.TenantId,
                    UsuarioId = request.UserId,
                    Accion = "CREATE",
                    Entidad = EntidadAuditoria,
                    EntidadId = entidad.Id.ToString(),
                    ValorAnterior = null,                                   // CREATE: siempre null
                    ValorNuevo = SerializarSnapshot(entidad, request.AccionId)
                }, tx, ct);

                creadas.Add(entidad.Id);
                totalBytes += entidad.FileSizeBytes;
            }

            tx.Commit();
        }
        catch (Exception ex) when (!EsFalloDeDominio(ex))
        {
            // Fallo NO previsto: Dapper/Postgres al escribir la fila o su auditoría, o la lectura
            // de los bytes del archivo. Único punto de rollback y único punto de compensación.
            await RevertirLoteAsync(tx, subidas);

            // Un fallo de base de datos NUNCA sale crudo: el mensaje técnico de Dapper/Postgres no
            // debe viajar al cliente (y el usuario ya tiene el archivo en el Storage, no en la BD).
            // Por eso se envuelve SIN inner exception, que es justamente lo que lo delataría.
            _logger.LogError(ex,
                "No se pudo registrar un entregable en la base de datos. Modulo=PlanOperativo TenantId={TenantId} UserId={UserId} AccionId={AccionId} ArchivosSubidos={ArchivosSubidos}",
                request.TenantId, request.UserId, request.AccionId, subidas.Count);
            throw new InfraestructuraException(
                "El archivo se almacenó, pero no pudo registrarse. No quedaron residuos en el sistema.");
        }
        catch (Exception ex) when (EsFalloDeDominio(ex))
        {
            // Excepciones de dominio (404 / 403 / 422) y el fallo de Storage (500): todas traen ya
            // su mensaje accionable y su LogError, así que se propagan TAL CUAL. Este es el fix de
            // producción: antes se reenvolvían en InfraestructuraException y un 422 de «el contenido
            // no corresponde a su extensión» llegaba al cliente como 500 «no pudo registrarse».
            //
            // OJO: rollback y compensación SÍ se ejecutan también aquí. En un lote de 5, la
            // ValidacionException de la firma de bytes (paso 11.b) se evalúa archivo por archivo: si
            // el 3º tiene la extensión spoofeada, los 2 primeros YA están en Storage y sin
            // compensación serían huérfanos (D-H, RNF-014). «No committeada» protege la base de
            // datos, no el bucket.
            await RevertirLoteAsync(tx, subidas);
            throw;
        }

        // ── 12. Log sin la ruta de Storage ni los bytes: la ruta es interna y el contenido es dato
        //        sensible. Filtrable por tenant, usuario y módulo (STACK-11). ──
        _logger.LogInformation(
            "Se registraron {Cantidad} entregable(s) adjunto(s) en una acción. Modulo=PlanOperativo TenantId={TenantId} UserId={UserId} AccionId={AccionId} TotalBytes={TotalBytes}",
            creadas.Count, request.TenantId, request.UserId, request.AccionId, totalBytes);

        // ── 13. Re-lectura de las filas creadas: el 201 lleva los valores REALES de CreatedAt (lo
        //        fija el DEFAULT now() del DDL) y SubidoPorNombre (el LEFT JOIN), no valores adivinados.
        //        Máximo 5 point-reads (RNF-001 aceptable). ──
        var releidas = await Task.WhenAll(creadas.Select(id =>
            _entregables.ObtenerPorIdAsync(id, request.AccionId, request.TenantId, ct)));

        return releidas
            .Where(e => e is not null)
            .Select(e => Proyectar(e!, request.UserId, request.Rol))
            .ToList();
    }

    // ═══════════════════════════ Método 3 · ObtenerDescargaAsync (CA #4) ═════════════════════

    /// <inheritdoc cref="IEntregableService.ObtenerDescargaAsync"/>
    public async Task<EntregableDescargaResponse> ObtenerDescargaAsync(
        Guid accionId, Guid entregableId, Guid tenantId, string rol, Guid? areaId,
        CancellationToken ct = default)
    {
        if (accionId == Guid.Empty)
            throw new ValidacionException("El identificador de la acción es obligatorio.");
        if (entregableId == Guid.Empty)
            throw new ValidacionException("El identificador del adjunto es obligatorio.");

        var accion = await _acciones.ObtenerPorIdAsync(accionId, tenantId, ct)
            ?? throw new NotFoundException("La acción no existe para el tenant actual.");

        ValidarAccesoAAccion(accion.AreaId, rol, areaId);

        // La DAL exige tenant + ACCIÓN + id: un anexo válido de otra acción es 404, no 403, para no
        // filtrar su existencia falseando el accionId de la ruta.
        var adjunto = await _entregables.ObtenerPorIdAsync(entregableId, accionId, tenantId, ct)
            ?? throw new NotFoundException("El adjunto no existe para esta acción.");

        // ARCH-06: URL firmada de 24 h sobre la ruta INTERNA. El navegador descarga directo desde
        // Storage; la API nunca hace proxy del binario (RNF-001).
        var url = await _storage.ObtenerUrlFirmadaAsync(
            EntregableAdjuntoReglas.BucketEntregables, adjunto.FilePath,
            EntregableAdjuntoReglas.ExpiracionHoras, ct);

        var respuesta = new EntregableDescargaResponse
        {
            Id = adjunto.Id,
            AccionId = adjunto.AccionId,
            NombreArchivo = adjunto.NombreArchivo,
            TamanoBytes = adjunto.FileSizeBytes,
            TipoMime = adjunto.TipoMime,
            Url = url,
            // SIEMPRE relleno, también en la degradación: la UI lo muestra y el test es determinista.
            ExpiraEn = _timeProvider.GetUtcNow().AddHours(EntregableAdjuntoReglas.ExpiracionHoras)
        };

        if (url is null)
        {
            // RNF-014 / D-G · degradación elegante: 200 con Url nula y aviso, nunca 500. La fila
            // sigue operativa y el usuario puede reintentar.
            _logger.LogError(
                "Supabase Storage no emitió la URL firmada de un entregable. Modulo=PlanOperativo TenantId={TenantId} AccionId={AccionId} EntregableId={EntregableId}",
                tenantId, accionId, entregableId);
        }
        else
        {
            // NUNCA la URL firmada completa ni la ruta interna en el log: son credenciales
            // temporales (RNF-008/RNF-009). El UserId no se registra porque la firma de
            // ObtenerDescargaAsync no lo recibe —el controller no lo necesita para una lectura y el
            // JWT no se propaga a la BLL en este método (SEC-06)—; el log queda filtrable por tenant
            // y módulo, que es lo que exige STACK-11.
            _logger.LogInformation(
                "Se emitió una URL firmada de entrega. Modulo=PlanOperativo TenantId={TenantId} AccionId={AccionId} EntregableId={EntregableId} ExpiraEn={ExpiraEn:O}",
                tenantId, accionId, entregableId, respuesta.ExpiraEn);
        }

        // Operación de LECTURA: no audita (ADR-003 — en esta HU solo CREATE y DELETE).
        return respuesta;
    }

    // ═════════════════════════════ Método 4 · EliminarAsync (CA #5) ═════════════════════════

    /// <inheritdoc cref="IEntregableService.EliminarAsync"/>
    public async Task<int> EliminarAsync(
        Guid accionId, Guid entregableId, Guid tenantId, Guid userId, string rol, Guid? areaId,
        CancellationToken ct = default)
    {
        if (accionId == Guid.Empty)
            throw new ValidacionException("El identificador de la acción es obligatorio.");
        if (entregableId == Guid.Empty)
            throw new ValidacionException("El identificador del adjunto es obligatorio.");

        var accion = await _acciones.ObtenerPorIdAsync(accionId, tenantId, ct)
            ?? throw new NotFoundException("La acción no existe para el tenant actual.");

        ValidarAccesoAAccion(accion.AreaId, rol, areaId);

        var adjunto = await _entregables.ObtenerPorIdAsync(entregableId, accionId, tenantId, ct)
            ?? throw new NotFoundException("El adjunto no existe para esta acción.");

        // CA #5 · la regla, en BLL y en un solo sitio (el proyector de ListarAsync la reutiliza
        // para PuedeEliminar). Un JefeArea NO puede borrar el adjunto de otro JefeArea aunque sea
        // de su área: el CA dice «quien lo subió o el GER».
        var esGerente = EsGerente(rol);
        var esAutor = adjunto.SubidoPor == userId;
        if (!esGerente && !esAutor)
            throw new AccesoDenegadoException("Solo puedes eliminar los adjuntos que subiste, o el Gerente puede eliminarlos.");

        // F3 / RC-12 · mismo criterio que la subida: un ciclo Cerrado o Borrador es de solo lectura.
        var ciclo = await _ciclos.ObtenerPorIdAsync(tenantId, accion.CicloId, ct)
            ?? throw new NotFoundException("El ciclo de la acción no existe.");
        _reglas.ValidarCicloCerrado(ciclo.Estado);

        // ── Orden deliberado BD → Storage (D-I) ──
        // Primero la fila y su auditoría, atómicas; el objeto de Storage se borra DESPUÉS, en
        // best-effort, para que un fallo de Storage no pueda dejar al usuario con un adjunto que
        // ya no ve pero que la BD aún lista. La alternativa (Storage → BD) deja un huérfano
        // invisible, que es peor.
        int filasAfectadas;
        using (var tx = await _entregables.BeginTransactionAsync(ct))
        {
            try
            {
                filasAfectadas = await _entregables.EliminarAsync(entregableId, accionId, tenantId, tx, ct);

                // Carrera: otro actor borró la fila entre la lectura y el DELETE. El 0 de la DAL es
                // la ÚNICA señal, y se lanza DESDE DENTRO de la transacción → Rollback, 404 y, sobre
                // todo, sin fila de auditoría: no se audita un borrado que no ocurrió.
                if (filasAfectadas == 0)
                    throw new NotFoundException("El adjunto no existe para esta acción.");

                await _entregables.InsertLogAsync(new LogAuditoriaInsert
                {
                    TenantId = tenantId,
                    UsuarioId = userId,
                    Accion = "DELETE",
                    Entidad = EntidadAuditoria,
                    EntidadId = entregableId.ToString(),
                    ValorAnterior = SerializarSnapshot(adjunto, accionId),  // el MISMO helper del CREATE
                    ValorNuevo = null                                        // DELETE: siempre null
                }, tx, ct);

                tx.Commit();
            }
            catch
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception exRollback)
                {
                    _logger.LogWarning(exRollback, "Rollback fallido en EliminarAsync. Modulo=PlanOperativo");
                }
                throw;
            }
        }

        _logger.LogInformation(
            "Se eliminó un entregable. Modulo=PlanOperativo TenantId={TenantId} UserId={UserId} AccionId={AccionId} EntregableId={EntregableId}",
            tenantId, userId, accionId, entregableId);

        // Fuera de la transacción y best-effort: si falla, la fila ya no se lista y la BD es la
        // fuente de verdad. Solo queda un objeto huérfano (D-I, RNF-014), nunca una fila viva
        // apuntando a un objeto que el usuario sí ve.
        try
        {
            await _storage.EliminarArchivoAsync(
                EntregableAdjuntoReglas.BucketEntregables, adjunto.FilePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "El entregable se eliminó de la base de datos, pero no de Supabase Storage. Modulo=PlanOperativo TenantId={TenantId} UserId={UserId} AccionId={AccionId} EntregableId={EntregableId}",
                tenantId, userId, accionId, entregableId);
        }

        return filasAfectadas;
    }

    // ═══════════════════════════════════ APOYO ═════════════════════════════════════════════

    /// <summary>
    /// Autorización de lectura sobre la acción: área primero (SEC-07) y después rol. Se usa
    /// después de haber leído la acción, para que ninguna consulta a <c>entregable_adjunto</c> se
    /// ejecute sin haber comprobado el aislamiento.
    /// </summary>
    private void ValidarAccesoAAccion(Guid areaIdDeLaAccion, string rol, Guid? areaId)
    {
        if (EsJefeArea(rol))
            ValidarAreaDelActor(areaIdDeLaAccion, areaId);

        // Defensa en profundidad: el [Authorize] del controller ya filtró, pero la BLL no confía en él.
        if (!EsJefeArea(rol) && !EsGerente(rol))
            throw new AccesoDenegadoException("No tienes permisos para consultar los entregables de la acción.");
    }

    /// <summary>SEC-07 — un JefeArea sin área en el JWT es 403, nunca «todas las áreas».</summary>
    private static void ValidarAreaDelActor(Guid areaIdDeLaAccion, Guid? areaId)
    {
        if (areaId is null || areaId == Guid.Empty)
            throw new AccesoDenegadoException("Tu sesión no tiene un área asignada. Contacta al administrador.");

        if (areaIdDeLaAccion != areaId.Value)
            throw new AccesoDenegadoException("La acción pertenece a otra área estratégica.");
    }

    /// <summary>
    /// Filtro discriminante del <c>catch</c> de <see cref="SubirAsync"/>. Son las excepciones que
    /// la BLL lanza con un mensaje pensado para el usuario, más el fallo de Storage: todas deben
    /// propagarse <b>sin reenvoltorio</b> para que el <c>ExceptionMiddleware</c> las traduzca a su
    /// código real (404 / 403 / 422 / 500) y no a un 500 genérico. Cualquier otra cosa —un
    /// <c>PostgresException</c> de Dapper, un <c>IOException</c>— es infraestructura y se traduce
    /// dentro de la propia capa. Se mantiene el filtro como método (y no un <c>catch (Exception ex)</c>
    /// con la variable sin usar) para no dejar ni un warning.
    /// </summary>
    private static bool EsFalloDeDominio(Exception ex) => ex is InfraestructuraException
        or ValidacionException
        or NotFoundException
        or AccesoDenegadoException;

    /// <summary>
    /// <b>Único</b> punto de reversión del lote de <see cref="SubirAsync"/>: <c>Rollback</c> de la
    /// transacción (tolerando que falle —el <c>using</c> y la conexión ya la cierran—) y
    /// compensación de Storage en orden inverso. Lo comparten el catch de fallo no previsto y el de
    /// excepción ya tipada, de modo que <b>ningún</b> camino sale del <c>try</c> dejando objetos en
    /// el bucket. El fallo de Storage reentrante no vuelve a revertir: el doble
    /// <c>Rollback</c> no aporta nada y ensucia el log.
    /// </summary>
    private async Task RevertirLoteAsync(IDbTransaction tx, List<(string Path, int Orden)> subidas)
    {
        try
        {
            tx.Rollback();
        }
        catch (Exception exRollback)
        {
            _logger.LogWarning(exRollback, "Rollback fallido en SubirAsync. Modulo=PlanOperativo");
        }

        await CompensarStorageAsync(subidas);
    }

    /// <summary>
    /// Compensación de Storage (D-H): borra en orden inverso al de subida lo que ya se subió en
    /// este lote. Va con <see cref="CancellationToken.None"/> <b>a propósito</b>: si el cliente se
    /// desconectó, la limpieza debe completarse igualmente, y el repositorio es la única capa que
    /// puede dejar objetos huérfanos.
    /// </summary>
    private async Task CompensarStorageAsync(List<(string Path, int Orden)> subidas)
    {
        foreach (var (path, _) in subidas.OrderByDescending(x => x.Orden))
        {
            try
            {
                await _storage.EliminarArchivoAsync(
                    EntregableAdjuntoReglas.BucketEntregables, path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // La ruta NO se registra: es interna (D-H) y no ayuda a diagnosticar el 500 que el
                // usuario ya va a recibir.
                _logger.LogError(ex, "No se pudo compensar un entregable de Supabase Storage. Modulo=PlanOperativo");
            }
        }
    }

    /// <summary>
    /// Proyector COMÚN de listar y subir, para que la lógica de presentación no se duplique
    /// (DB-04: los campos derivados se calculan aquí y en ningún otro sitio).
    /// </summary>
    private static EntregableAdjuntoResponse Proyectar(EntregableAdjuntoEntity e, Guid userId, string rol)
    {
        var extension = Path.GetExtension(e.NombreArchivo).TrimStart('.').ToLowerInvariant();

        return new EntregableAdjuntoResponse
        {
            Id = e.Id,
            AccionId = e.AccionId,
            NombreArchivo = e.NombreArchivo,
            TamanoBytes = e.FileSizeBytes,
            TamanoLegible = EntregableAdjuntoReglas.FormatearTamano(e.FileSizeBytes),
            TipoMime = e.TipoMime,
            // "bin" es solo un valor de visualización cuando el nombre no trae extensión: el MIME
            // del dominio ya viene canónico de la DAL.
            Extension = string.IsNullOrEmpty(extension) ? "bin" : extension,
            // Viene del LEFT JOIN de usuario.nombre; null si el usuario fue borrado (la vista
            // muestra "—"). La BLL no vuelve a consultar usuario.
            SubidoPorNombre = string.IsNullOrWhiteSpace(e.SubidoPorNombre) ? null : e.SubidoPorNombre,
            CreatedAt = e.CreatedAt,
            // MISMA regla que EliminarAsync, aplicada una sola vez (CA #5, DB-04).
            PuedeEliminar = EsGerente(rol) || e.SubidoPor == userId
        };
    }

    /// <summary>
    /// Snapshot de auditoría (ADR-003). <b>Sin <c>TamanoLegible</c></b> (campo derivado, DB-04),
    /// <b>sin <c>FilePath</c></b> (interno, D-H) y <b>sin bytes</b> (RNF-009). El mismo helper
    /// alimenta el <c>ValorNuevo</c> del CREATE y el <c>ValorAnterior</c> del DELETE.
    /// </summary>
    private static string SerializarSnapshot(EntregableAdjuntoEntity e, Guid accionId)
        => JsonSerializer.Serialize(new
        {
            nombreArchivo = e.NombreArchivo,
            fileSizeBytes = e.FileSizeBytes,   // long CRUDO, nunca formateado
            tipoMime = e.TipoMime,
            accionId = accionId
        }, JsonOpcionesAuditoria);

    /// <summary>Tipo canónico que corresponde a la extensión declarada (CA #2).</summary>
    private static TipoArchivo TipoDesdeExtension(string extension)
        => (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "pdf" => TipoArchivo.Pdf,
            "docx" => TipoArchivo.Docx,
            "xlsx" => TipoArchivo.Xlsx,
            "png" => TipoArchivo.Png,
            "jpg" or "jpeg" => TipoArchivo.Jpg,
            _ => TipoArchivo.Desconocido
        };

    private static bool EsGerente(string rol) => string.Equals(rol, RolGerente, StringComparison.Ordinal);

    private static bool EsJefeArea(string rol) => string.Equals(rol, RolJefeArea, StringComparison.Ordinal);

    /// <summary>Un archivo del lote con lo ya decidido que no depende de la red (ruta, UUID y nombre sano).</summary>
    private sealed record ArchivoAPersistir(EntregableArchivo Archivo, Guid Uuid, string Path, string NombreSaneado);
}
