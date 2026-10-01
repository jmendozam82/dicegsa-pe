// PE-GOL SaaS — Entregables Adjuntos (HU-022 + hotfix ADR-016)
// Dropzone, subida múltiple, listado, descarga y eliminación de adjuntos.
// Los fetch apuntan a proxies MVC (/AccionPlan/Entregables*); el JWT viaja server-side.
$(function () {
    'use strict';

    // ── Configuración ────────────────────────────────────────────────────────
    const MAX_ARCHIVOS = 5;
    const TAMANO_MAXIMO = 20 * 1024 * 1024; // 20 MB
    const EXTENSIONES_PERMITIDAS = ['pdf', 'docx', 'xlsx', 'png', 'jpg'];
    const MIME_PERMITIDOS = {
        'pdf': 'application/pdf',
        'docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        'xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'png': 'image/png',
        'jpg': 'image/jpeg'
    };

    // ── Estado ───────────────────────────────────────────────────────────────
    let archivosSeleccionados = [];
    let accionId = null;
    let cicloActivo = true;

    // ── Elementos DOM ────────────────────────────────────────────────────────
    const $form = $('#form-entregables');
    const $dropzone = $('#dropzone');
    const $inputArchivos = $('#input-archivos');
    const $listaSeleccionados = $('#lista-seleccionados');
    const $btnSubir = $('#btn-subir');
    const $btnLimpiar = $('#btn-limpiar');
    const $tbodyAdjuntos = $('#tbody-adjuntos');
    const $contenedorTabla = $('#contenedor-tabla-adjuntos');
    const $emptyState = $('#empty-state-adjuntos');
    const $modalEliminar = $('#modalEliminarAdjunto');
    const $modalTexto = $('#modalEliminarAdjuntoTexto');
    const $btnConfirmarEliminar = $('#btn-confirmar-eliminar');
    const $toast = $('#toast-notificacion');
    const $toastMensaje = $('#toast-mensaje');

    // ── Inicialización ───────────────────────────────────────────────────────
    function inicializar() {
        accionId = $form.length
            ? $form.data('accion-id')
            : $('#panel-adjuntos').data('accion-id');

        if (!$form.length) {
            // Vista read-only (Gerente): solo inicializar modal y botones de descarga/eliminar
            inicializarModalEliminar();
            inicializarBotonesAccion();
            return;
        }

        cicloActivo = $form.data('ciclo-activo') !== false;

        if (!cicloActivo) {
            $dropzone.addClass('dropzone-pe--disabled');
            $btnSubir.prop('disabled', true);
        }

        inicializarDropzone();
        inicializarEventos();
        inicializarModalEliminar();
        inicializarBotonesAccion();
    }

    // ── Dropzone ─────────────────────────────────────────────────────────────
    function inicializarDropzone() {
        if (!$dropzone.length) return;

        // Click para abrir el selector de archivos
        $dropzone.on('click', function (e) {
            if (e.target !== $inputArchivos[0]) {
                $inputArchivos.trigger('click');
            }
        });

        // Drag & drop
        $dropzone.on('dragover', function (e) {
            e.preventDefault();
            e.stopPropagation();
            if (cicloActivo) {
                $dropzone.addClass('dropzone-pe--active');
            }
        });

        $dropzone.on('dragleave', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $dropzone.removeClass('dropzone-pe--active');
        });

        $dropzone.on('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $dropzone.removeClass('dropzone-pe--active');

            if (!cicloActivo) {
                mostrarToast('El ciclo está cerrado. No se pueden subir archivos.', 'warning');
                return;
            }

            const files = e.originalEvent.dataTransfer.files;
            agregarArchivos(files);
        });

        // Input file change
        $inputArchivos.on('change', function () {
            agregarArchivos(this.files);
            // Limpiar el input para permitir seleccionar el mismo archivo de nuevo
            this.value = '';
        });
    }

    function agregarArchivos(files) {
        if (!files || files.length === 0) return;

        const archivosValidos = [];
        const errores = [];

        for (const file of files) {
            // Validar cantidad total
            if (archivosSeleccionados.length + archivosValidos.length >= MAX_ARCHIVOS) {
                errores.push(`Máximo ${MAX_ARCHIVOS} archivos por acción.`);
                break;
            }

            // Validar tamaño
            if (file.size > TAMANO_MAXIMO) {
                errores.push(`"${file.name}" supera los 20 MB.`);
                continue;
            }

            // Validar extensión
            const extension = file.name.split('.').pop()?.toLowerCase() || '';
            if (!EXTENSIONES_PERMITIDAS.includes(extension)) {
                errores.push(`"${file.name}" no es un tipo permitido (PDF, DOCX, XLSX, PNG, JPG).`);
                continue;
            }

            // Validar MIME (si está disponible)
            if (file.type && !esMimeValido(file.type, extension)) {
                errores.push(`"${file.name}" tiene un tipo MIME no válido.`);
                continue;
            }

            archivosValidos.push(file);
        }

        if (errores.length > 0) {
            mostrarToast(errores.join(' '), 'danger');
        }

        if (archivosValidos.length > 0) {
            archivosSeleccionados = archivosSeleccionados.concat(archivosValidos);
            renderizarListaSeleccionados();
            actualizarBotones();
        }
    }

    function esMimeValido(mime, extension) {
        // Permitir MIME vacío o genérico (algunos navegadores no lo detectan)
        if (!mime || mime === 'application/octet-stream') return true;
        return MIME_PERMITIDOS[extension] === mime;
    }

    function renderizarListaSeleccionados() {
        $listaSeleccionados.empty();

        archivosSeleccionados.forEach((file, index) => {
            const $li = $('<li>').addClass('d-flex align-items-center gap-2 mb-2 p-2 border rounded');
            const $icono = $('<i>').addClass('bi bi-file-earmark text-secondary');
            const $nombre = $('<span>').addClass('flex-grow-1').text(file.name);
            const $tamaño = $('<span>').addClass('text-muted small').text(formatearTamaño(file.size));
            const $btnQuitar = $('<button>').addClass('btn btn-sm btn-outline-danger')
                .html('<i class="bi bi-x-lg"></i>')
                .attr('aria-label', 'Quitar archivo')
                .on('click', function () {
                    archivosSeleccionados.splice(index, 1);
                    renderizarListaSeleccionados();
                    actualizarBotones();
                });

            $li.append($icono, $nombre, $tamaño, $btnQuitar);
            $listaSeleccionados.append($li);
        });
    }

    function formatearTamaño(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function actualizarBotones() {
        const hayArchivos = archivosSeleccionados.length > 0;
        $btnSubir.prop('disabled', !hayArchivos || !cicloActivo);
        $btnLimpiar.prop('disabled', !hayArchivos);
    }

    // ── Eventos del formulario ───────────────────────────────────────────────
    function inicializarEventos() {
        $form.on('submit', async function (e) {
            e.preventDefault();

            if (archivosSeleccionados.length === 0) {
                mostrarToast('Selecciona al menos un archivo.', 'warning');
                return;
            }

            if (archivosSeleccionados.length > MAX_ARCHIVOS) {
                mostrarToast(`Máximo ${MAX_ARCHIVOS} archivos por acción.`, 'danger');
                return;
            }

            await subirArchivos();
        });

        $btnLimpiar.on('click', function () {
            archivosSeleccionados = [];
            renderizarListaSeleccionados();
            actualizarBotones();
        });
    }

    async function subirArchivos() {
        $btnSubir.prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span> Subiendo...');

        try {
            const formData = new FormData();
            archivosSeleccionados.forEach(file => {
                formData.append('archivos', file, file.name);
            });

            // ADR-016: proxy MVC relativo, sin Authorization header (JWT server-side).
            const response = await fetch(`/AccionPlan/EntregablesSubir/${accionId}`, {
                method: 'POST',
                headers: {
                    'Accept': 'application/json'
                },
                body: formData
            });

            if (response.ok) {
                const data = await response.json();
                const creados = Array.isArray(data) ? data : [];
                mostrarToast(`Se agregaron ${creados.length} archivo(s) correctamente.`, 'success');

                // Agregar las nuevas filas a la tabla
                creados.forEach(adjunto => agregarFilaTabla(adjunto));
                actualizarEmptyState($tbodyAdjuntos.children().length);

                // Limpiar selección
                archivosSeleccionados = [];
                renderizarListaSeleccionados();
                actualizarBotones();
            } else {
                await mostrarErrorResponse(response);
            }
        } catch (error) {
            console.error('Error al subir archivos:', error);
            mostrarToast('Error de conexión. Inténtalo de nuevo.', 'danger');
        } finally {
            $btnSubir.prop('disabled', archivosSeleccionados.length === 0 || !cicloActivo)
                .html('<i class="bi bi-upload"></i> Subir adjuntos');
        }
    }

    // ── Tabla de adjuntos ────────────────────────────────────────────────────
    function agregarFilaTabla(adjunto) {
        const icono = iconoTipo(adjunto.extension);
        const $fila = $('<tr>').attr('data-adjunto-id', adjunto.id);

        const $celdaNombre = $('<td>').html(`
            <i class="bi bi-paperclip me-1 text-secondary"></i>
            <span class="cell-truncate d-inline-block" title="${escapeHtml(adjunto.nombreArchivo)}">${escapeHtml(adjunto.nombreArchivo)}</span>
        `);

        const $celdaTamaño = $('<td>').text(adjunto.tamanoLegible);
        const $celdaTipo = $('<td>').html(`
            <span class="badge bg-light text-dark border">
                <i class="bi ${icono} me-1"></i>${adjunto.extension.toUpperCase()}
            </span>
        `);
        const $celdaSubidoPor = $('<td>').text(adjunto.subidoPorNombre || '—');
        const $celdaFecha = $('<td>').text(formatearFecha(adjunto.createdAt));

        const $celdaAcciones = $('<td>').addClass('text-end').html(`
            <div class="d-inline-flex gap-1">
                <button type="button" class="btn-pe btn-pe--secondary btn-pe--sm btn-descargar"
                        data-adjunto-id="${adjunto.id}" title="Descargar">
                    <i class="bi bi-download"></i>
                </button>
                ${adjunto.puedeEliminar ? `
                <button type="button" class="btn-pe btn-pe--danger btn-pe--sm btn-eliminar"
                        data-adjunto-id="${adjunto.id}" data-adjunto-nombre="${escapeHtml(adjunto.nombreArchivo)}"
                        data-bs-toggle="modal" data-bs-target="#modalEliminarAdjunto" title="Eliminar">
                    <i class="bi bi-trash"></i>
                </button>` : ''}
            </div>
        `);

        $fila.append($celdaNombre, $celdaTamaño, $celdaTipo, $celdaSubidoPor, $celdaFecha, $celdaAcciones);
        $tbodyAdjuntos.append($fila);
    }

    function iconoTipo(extension) {
        const ext = (extension || '').toLowerCase();
        const iconos = {
            'pdf': 'bi-file-earmark-pdf',
            'docx': 'bi-file-earmark-word',
            'xlsx': 'bi-file-earmark-excel',
            'png': 'bi-file-earmark-image',
            'jpg': 'bi-file-earmark-image'
        };
        return iconos[ext] || 'bi-file-earmark';
    }

    function formatearFecha(fechaISO) {
        const fecha = new Date(fechaISO);
        return fecha.toLocaleString('es-ES', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    function escapeHtml(texto) {
        const div = document.createElement('div');
        div.textContent = texto;
        return div.innerHTML;
    }

    function actualizarEmptyState(conteo) {
        $emptyState.toggleClass('d-none', conteo > 0);
        $contenedorTabla.toggleClass('d-none', conteo === 0);
    }

    // ── Modal de eliminación ─────────────────────────────────────────────────
    function inicializarModalEliminar() {
        if (!$modalEliminar.length) return;

        let adjuntoIdEliminar = null;

        $modalEliminar.on('show.bs.modal', function (event) {
            const $boton = $(event.relatedTarget);
            adjuntoIdEliminar = $boton.data('adjunto-id');
            const nombre = $boton.data('adjunto-nombre');

            $modalTexto.text(`Se eliminará "${nombre}" de forma permanente. Esta acción no se puede deshacer.`);
        });

        $btnConfirmarEliminar.on('click', async function () {
            if (!adjuntoIdEliminar) return;

            $(this).prop('disabled', true).html('<span class="spinner-border spinner-border-sm me-1"></span> Eliminando...');

            try {
                await eliminarAdjunto(adjuntoIdEliminar);
                $modalEliminar.modal('hide');
                await cargarAdjuntos();
                mostrarToast('Adjunto eliminado correctamente.', 'success');
            } catch (error) {
                console.error('Error al eliminar:', error);
                mostrarToast(error.message || 'Error al eliminar el adjunto.', 'danger');
            } finally {
                $(this).prop('disabled', false).text('Sí, eliminar');
            }
        });
    }

    async function eliminarAdjunto(adjuntoId) {
        // ADR-016: proxy MVC relativo, sin Authorization header (JWT server-side).
        const response = await fetch(`/AccionPlan/EntregablesEliminar/${accionId}?entregableId=${adjuntoId}`, {
            method: 'POST',
            headers: {
                'Accept': 'application/json'
            }
        });

        if (!response.ok) {
            await mostrarErrorResponse(response, true);
        }
    }

    async function cargarAdjuntos() {
        try {
            const response = await fetch(`/AccionPlan/EntregablesDatos/${accionId}`, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json'
                }
            });

            if (!response.ok) {
                await mostrarErrorResponse(response);
                return;
            }

            const data = await response.json();
            const adjuntos = Array.isArray(data) ? data : [];
            $tbodyAdjuntos.empty();
            adjuntos.forEach(adjunto => agregarFilaTabla(adjunto));
            actualizarEmptyState(adjuntos.length);
        } catch (error) {
            console.error('Error al cargar adjuntos:', error);
        }
    }

    // ── Botones de acción (descargar/eliminar) ──────────────────────────────
    function inicializarBotonesAccion() {
        // Delegación de eventos para botones dinámicos
        $(document).on('click', '.btn-descargar', async function () {
            const adjuntoId = $(this).data('adjunto-id');
            await descargarAdjunto(adjuntoId);
        });
    }

    async function descargarAdjunto(adjuntoId) {
        try {
            // ADR-016: proxy MVC relativo, sin Authorization header (JWT server-side).
            const response = await fetch(`/AccionPlan/EntregablesDescarga/${accionId}?entregableId=${adjuntoId}`, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json'
                }
            });

            if (!response.ok) {
                await mostrarErrorResponse(response);
                return;
            }

            const data = await response.json();
            if (data.url) {
                window.open(data.url, '_blank', 'noopener');
            } else {
                mostrarToast('No se pudo generar el enlace de descarga.', 'warning');
            }
        } catch (error) {
            console.error('Error al descargar:', error);
            mostrarToast('Error de conexión. Inténtalo de nuevo.', 'danger');
        }
    }

    // ── Manejo de error común ────────────────────────────────────────────────
    async function mostrarErrorResponse(response, lanzar = false) {
        if (response.status === 401) {
            mostrarToast('La sesión expiró. Vuelva a iniciar sesión.', 'warning');
            window.location.href = '/Auth/Login';
            if (lanzar) throw new Error('Sesión expirada');
            return;
        }

        let errorData = null;
        try {
            errorData = await response.json();
        } catch (e) {
            // no JSON
        }

        const mensaje = errorData?.errors?.length
            ? errorData.errors.join(' ')
            : (errorData?.message || `Error ${response.status}`);

        mostrarToast(mensaje, 'danger');
        if (lanzar) throw new Error(mensaje);
    }

    // ── Utilidades ───────────────────────────────────────────────────────────
    function mostrarToast(mensaje, tipo = 'info') {
        if (!$toast.length) {
            // Fallback: usar alert nativa si no hay toast
            alert(mensaje);
            return;
        }

        $toastMensaje.text(mensaje);
        $toast.removeClass('text-bg-primary text-bg-success text-bg-danger text-bg-warning')
            .addClass(`text-bg-${tipo}`);

        const bsToast = new bootstrap.Toast($toast, { delay: 5000 });
        bsToast.show();
    }

    // ── Iniciar ──────────────────────────────────────────────────────────────
    inicializar();
});
