// PE-GOL SaaS — Entregables Adjuntos (HU-022)
// Dropzone, subida múltiple, listado, descarga y eliminación de adjuntos.
// Usa fetch nativo con async/await. Token JWT en sesión (SesionService).
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
    const $modalEliminar = $('#modalEliminarAdjunto');
    const $modalTexto = $('#modalEliminarAdjuntoTexto');
    const $btnConfirmarEliminar = $('#btn-confirmar-eliminar');
    const $toast = $('#toast-notificacion');
    const $toastMensaje = $('#toast-mensaje');

    // ── Inicialización ───────────────────────────────────────────────────────
    function inicializar() {
        if (!$form.length) return;

        accionId = $form.data('accion-id');
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

            // TODO [hotfix-HU-022] fetch a /api/v1/... resuelve contra el origen del MVC (Defecto E, ADR-016).
            // Fix previsto: proxy /AccionPlan/EntregablesDatos con ApiClient (JWT en sesión).
            const token = obtenerToken();
            const response = await fetch(`/api/v1/acciones/${accionId}/entregables`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${token}`
                },
                body: formData
            });

            const data = await response.json();

            if (response.ok && data.success) {
                const cantidad = data.data?.length || archivosSeleccionados.length;
                mostrarToast(`Se agregaron ${cantidad} archivo(s) correctamente.`, 'success');

                // Agregar las nuevas filas a la tabla
                if (data.data && data.data.length > 0) {
                    data.data.forEach(adjunto => agregarFilaTabla(adjunto));
                }

                // Limpiar selección
                archivosSeleccionados = [];
                renderizarListaSeleccionados();
                actualizarBotones();

                // Ocultar empty state si estaba visible
                $('.empty-state').closest('.card-pe').addClass('d-none');
            } else {
                const mensaje = data.message || 'Error al subir los archivos.';
                mostrarToast(mensaje, 'danger');
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
        const $fila = $('<tr>').attr('data-adjunto-id', adjjunto.id);

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

                // Eliminar la fila de la tabla
                $(`tr[data-adjunto-id="${adjuntoIdEliminar}"]`).remove();

                // Si no quedan filas, mostrar empty state
                if ($tbodyAdjuntos.children().length === 0) {
                    location.reload();
                }
            } catch (error) {
                console.error('Error al eliminar:', error);
                mostrarToast('Error al eliminar el adjunto.', 'danger');
            } finally {
                $(this).prop('disabled', false).text('Sí, eliminar');
            }
        });
    }

    async function eliminarAdjunto(adjuntoId) {
        // TODO [hotfix-HU-022] fetch a /api/v1/... resuelve contra el origen del MVC (Defecto E, ADR-016).
        // Fix previsto: proxy /AccionPlan/EntregablesDatos con ApiClient (JWT en sesión).
        const token = obtenerToken();
        const response = await fetch(`/api/v1/acciones/${accionId}/entregables/${adjuntoId}`, {
            method: 'DELETE',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        const data = await response.json();

        if (response.ok && data.success) {
            mostrarToast('Adjunto eliminado correctamente.', 'success');
        } else {
            const mensaje = data.message || 'Error al eliminar el adjunto.';
            throw new Error(mensaje);
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
            // TODO [hotfix-HU-022] fetch a /api/v1/... resuelve contra el origen del MVC (Defecto E, ADR-016).
            // Fix previsto: proxy /AccionPlan/EntregablesDatos con ApiClient (JWT en sesión).
            const token = obtenerToken();
            const response = await fetch(`/api/v1/acciones/${accionId}/entregables/${adjuntoId}/descarga`, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${token}`
                }
            });

            const data = await response.json();

            if (response.ok && data.success && data.data?.url) {
                // Abrir la URL firmada en una pestaña nueva
                window.open(data.data.url, '_blank', 'noopener');
            } else {
                const mensaje = data.message || 'No se pudo generar el enlace de descarga.';
                mostrarToast(mensaje, 'danger');
            }
        } catch (error) {
            console.error('Error al descargar:', error);
            mostrarToast('Error de conexión. Inténtalo de nuevo.', 'danger');
        }
    }

    // ── Utilidades ───────────────────────────────────────────────────────────
    function obtenerToken() {
        // El token JWT se inyecta desde el servidor en un meta tag (HU-022)
        return $('meta[name="access-token"]').attr('content') || '';
    }

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
