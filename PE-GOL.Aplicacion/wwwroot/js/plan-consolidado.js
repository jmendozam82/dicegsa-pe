// PE-GOL SaaS — Vista Consolidada del Plan (HU-023)
// ─────────────────────────────────────────────────────────────────────────────
// Módulo que gestiona la tabla consolidada del plan con filtros server-side,
// paginación y exportación a Excel. Consume la API interna /api/v1/planes/consolidado.
//
// Reglas duras:
//   · SEC-05 — todo dato interpolado en HTML pasa por esc() antes de inyectarse.
//   · SEC-06 — nunca se envía tenant_id ni ciclo_id en query string.
//   · UX-01 — solo clases del Design System; sin estilos inline ni clases custom.
//   · La validación del servidor (FluentValidation) es siempre la fuente de verdad.
(function () {
    'use strict';

    var API_BASE = '/api/v1/planes/consolidado';
    var TOKEN = '';

    // Selectores
    var ID_FORM_FILTROS = 'formFiltros';
    var ID_BTN_FILTRAR = 'btnFiltrar';
    var ID_BTN_LIMPIAR = 'btnLimpiar';
    var ID_BTN_LIMPIAR_SIN_COINCIDENCIAS = 'btnLimpiarSinCoincidencias';
    var ID_BTN_EXPORTAR = 'btnExportarExcel';
    var ID_TBODY = 'tbodyConsolidado';
    var ID_SIN_COINCIDENCIAS = 'sinCoincidencias';
    var ID_PAGINACION = 'paginationPe';
    var ID_TABLA = 'tablaConsolidado';

    // Estado
    var filtrosActuales = {};
    var paginaActual = 1;
    var totalPaginas = 1;

    // ── Utilidades ────────────────────────────────────────────

    // SEC-05: escape de HTML para todo valor interpolado.
    function esc(valor) {
        if (valor === null || valor === undefined) {
            return '';
        }
        return String(valor)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function formatearFecha(fecha) {
        if (!fecha) return '—';
        var d = new Date(fecha);
        if (isNaN(d.getTime())) return '—';
        var dia = ('0' + d.getDate()).slice(-2);
        var mes = ('0' + (d.getMonth() + 1)).slice(-2);
        return dia + '/' + mes + '/' + d.getFullYear();
    }

    function claseStatus(status) {
        switch (status) {
            case 'Terminado': return 'semaforo--verde';
            case 'EnProgreso': return 'semaforo--amarillo';
            case 'Atrasado': return 'semaforo--rojo';
            default: return 'semaforo--gris';
        }
    }

    function statusLabel(status) {
        switch (status) {
            case 'NoIniciado': return 'No iniciado';
            case 'EnProgreso': return 'En progreso';
            case 'Terminado': return 'Terminado';
            case 'Atrasado': return 'Atrasado';
            default: return status;
        }
    }

    // ── API ───────────────────────────────────────────────────

    async function obtenerConsolidado(page) {
        var params = new URLSearchParams();
        params.append('page', page.toString());
        params.append('pageSize', '25');

        // Aplicar filtros actuales
        Object.keys(filtrosActuales).forEach(function (key) {
            var valor = filtrosActuales[key];
            if (valor) {
                params.append(key, valor);
            }
        });

        var url = API_BASE + '?' + params.toString();
        var response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': 'Bearer ' + TOKEN,
                'Accept': 'application/json'
            }
        });

        if (!response.ok) {
            var errorData = null;
            try {
                errorData = await response.json();
            } catch (e) {
                // no JSON
            }
            throw new Error(errorData?.message || 'Error ' + response.status);
        }

        var data = await response.json();
        return data.data || data;
    }

    async function exportarExcel() {
        var params = new URLSearchParams();

        // Aplicar filtros actuales
        Object.keys(filtrosActuales).forEach(function (key) {
            var valor = filtrosActuales[key];
            if (valor) {
                params.append(key, valor);
            }
        });

        var url = API_BASE + '/exportar' + (params.toString() ? '?' + params.toString() : '');

        // Descargar el archivo
        var response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': 'Bearer ' + TOKEN,
                'Accept': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
            }
        });

        if (!response.ok) {
            var errorData = null;
            try {
                errorData = await response.json();
            } catch (e) {
                // no JSON
            }
            throw new Error(errorData?.message || 'Error ' + response.status);
        }

        var blob = await response.blob();
        var downloadUrl = window.URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = downloadUrl;
        a.download = 'PlanConsolidado.xlsx';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        window.URL.revokeObjectURL(downloadUrl);
    }

    // ── Render ────────────────────────────────────────────────

    function renderTabla(items) {
        var tbody = document.getElementById(ID_TBODY);
        if (!tbody) return;

        if (!items || items.length === 0) {
            tbody.innerHTML = '';
            return;
        }

        tbody.innerHTML = items.map(function (item) {
            return '<tr>' +
                '<td>' +
                    '<span class="cell-codigo">' + esc(item.areaCodigo) + '</span>' +
                    '<span class="d-block body-sm">' + esc(item.areaNombre) + '</span>' +
                '</td>' +
                '<td>' +
                    '<span class="cell-codigo">' + esc(item.objetivoCodigo) + '</span>' +
                    '<span class="d-block body-sm">' + esc(item.objetivoDescripcion) + '</span>' +
                '</td>' +
                '<td>' +
                    '<span class="cell-codigo">' + esc(item.accionCodigo) + '</span>' +
                    '<span class="d-block cell-truncate" title="' + esc(item.accionDescripcion) + '">' + esc(item.accionDescripcion) + '</span>' +
                '</td>' +
                '<td>' +
                    '<span class="semaforo ' + claseStatus(item.status) + '">' +
                        '<i class="bi bi-circle-fill" aria-hidden="true"></i> ' + statusLabel(item.status) +
                    '</span>' +
                '</td>' +
                '<td>' + esc(item.clasificacion) + '</td>' +
                '<td>' + esc(item.tipoPresupuesto) + '</td>' +
                '<td>' +
                    '<span class="body-sm d-block">' + formatearFecha(item.fechaInicio) + '</span>' +
                    '<span class="body-sm d-block">→ ' + formatearFecha(item.fechaVencimiento) + '</span>' +
                '</td>' +
                '<td>' + (item.responsableNombre ? esc(item.responsableNombre) : '—') + '</td>' +
            '</tr>';
        }).join('');
    }

    function renderResumen(resumen) {
        if (!resumen) return;

        var elNoIniciado = document.getElementById('resumenNoIniciado');
        var elEnProgreso = document.getElementById('resumenEnProgreso');
        var elTerminado = document.getElementById('resumenTerminado');
        var elAtrasado = document.getElementById('resumenAtrasado');
        var elTotal = document.getElementById('resumenTotal');

        if (elNoIniciado) elNoIniciado.textContent = resumen.noIniciado || 0;
        if (elEnProgreso) elEnProgreso.textContent = resumen.enProgreso || 0;
        if (elTerminado) elTerminado.textContent = resumen.terminado || 0;
        if (elAtrasado) elAtrasado.textContent = resumen.atrasado || 0;
        if (elTotal) elTotal.textContent = resumen.total || 0;
    }

    function renderPaginacion(paginacion) {
        var contenedor = document.getElementById(ID_PAGINACION);
        if (!contenedor) return;

        if (!paginacion || paginacion.totalPages <= 1) {
            contenedor.innerHTML = '';
            return;
        }

        paginaActual = paginacion.page;
        totalPaginas = paginacion.totalPages;

        var html = '';

        // Botón anterior
        html += '<li class="page-item ' + (paginaActual <= 1 ? 'disabled' : '') + '">' +
            '<a class="page-link" href="#" data-page="' + (paginaActual - 1) + '" aria-label="Anterior">' +
                '<i class="bi bi-chevron-left"></i>' +
            '</a></li>';

        // Números de página
        for (var i = 1; i <= totalPaginas; i++) {
            html += '<li class="page-item ' + (i === paginaActual ? 'active' : '') + '">' +
                '<a class="page-link" href="#" data-page="' + i + '">' + i + '</a></li>';
        }

        // Botón siguiente
        html += '<li class="page-item ' + (paginaActual >= totalPaginas ? 'disabled' : '') + '">' +
            '<a class="page-link" href="#" data-page="' + (paginaActual + 1) + '" aria-label="Siguiente">' +
                '<i class="bi bi-chevron-right"></i>' +
            '</a></li>';

        contenedor.innerHTML = html;
    }

    function mostrarSinCoincidencias(mostrar) {
        var el = document.getElementById(ID_SIN_COINCIDENCIAS);
        var tabla = document.getElementById(ID_TABLA);
        if (!el || !tabla) return;

        if (mostrar) {
            el.classList.remove('d-none');
            tabla.classList.add('d-none');
        } else {
            el.classList.add('d-none');
            tabla.classList.remove('d-none');
        }
    }

    // ── Eventos ───────────────────────────────────────────────

    function recogerFiltros() {
        var form = document.getElementById(ID_FORM_FILTROS);
        if (!form) return {};

        var formData = new FormData(form);
        var filtros = {};

        ['areaId', 'cgId', 'status', 'clasificacion', 'tipo', 'fechaDesde', 'fechaHasta'].forEach(function (key) {
            var valor = formData.get(key);
            if (valor) {
                filtros[key] = valor;
            }
        });

        return filtros;
    }

    function limpiarFiltros() {
        var form = document.getElementById(ID_FORM_FILTROS);
        if (form) {
            form.reset();
        }
        filtrosActuales = {};
    }

    async function aplicarFiltros(page) {
        var btnFiltrar = document.getElementById(ID_BTN_FILTRAR);
        if (btnFiltrar) {
            btnFiltrar.disabled = true;
            btnFiltrar.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Cargando...';
        }

        try {
            var data = await obtenerConsolidado(page || 1);

            renderTabla(data.items);
            renderResumen(data.resumen);
            renderPaginacion(data.paginacion);

            // Mostrar/ocultar estado vacío de filtros
            var sinCoincidencias = !data.items || data.items.length === 0;
            mostrarSinCoincidencias(sinCoincidencias);

            // Habilitar botón exportar
            var btnExportar = document.getElementById(ID_BTN_EXPORTAR);
            if (btnExportar) {
                btnExportar.disabled = false;
            }
        } catch (error) {
            console.error('Error al obtener consolidado:', error);
            alert('Error al cargar los datos: ' + error.message);
        } finally {
            if (btnFiltrar) {
                btnFiltrar.disabled = false;
                btnFiltrar.innerHTML = '<i class="bi bi-funnel" aria-hidden="true"></i> Filtrar';
            }
        }
    }

    // ── Inicialización ────────────────────────────────────────

    function iniciar() {
        // Obtener el token del modelo (se inyecta desde el servidor)
        var tokenEl = document.getElementById('accessToken');
        if (tokenEl) {
            TOKEN = tokenEl.value;
        }

        // Si no hay token, no podemos hacer llamadas
        if (!TOKEN) {
            console.warn('No hay token JWT disponible');
            return;
        }

        // Evento: submit del formulario de filtros
        var form = document.getElementById(ID_FORM_FILTROS);
        if (form) {
            form.addEventListener('submit', function (e) {
                e.preventDefault();
                filtrosActuales = recogerFiltros();
                aplicarFiltros(1);
            });
        }

        // Evento: limpiar filtros
        var btnLimpiar = document.getElementById(ID_BTN_LIMPIAR);
        if (btnLimpiar) {
            btnLimpiar.addEventListener('click', function () {
                limpiarFiltros();
                aplicarFiltros(1);
            });
        }

        // Evento: limpiar filtros desde sin coincidencias
        var btnLimpiarSinCoincidencias = document.getElementById(ID_BTN_LIMPIAR_SIN_COINCIDENCIAS);
        if (btnLimpiarSinCoincidencias) {
            btnLimpiarSinCoincidencias.addEventListener('click', function () {
                limpiarFiltros();
                aplicarFiltros(1);
            });
        }

        // Evento: exportar Excel
        var btnExportar = document.getElementById(ID_BTN_EXPORTAR);
        if (btnExportar) {
            btnExportar.addEventListener('click', async function () {
                btnExportar.disabled = true;
                btnExportar.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Exportando...';

                try {
                    await exportarExcel();
                } catch (error) {
                    console.error('Error al exportar:', error);
                    alert('Error al exportar: ' + error.message);
                } finally {
                    btnExportar.disabled = false;
                    btnExportar.innerHTML = '<i class="bi bi-file-earmark-excel" aria-hidden="true"></i> Exportar Excel';
                }
            });
        }

        // Evento: paginación (delegación)
        var paginacion = document.getElementById(ID_PAGINACION);
        if (paginacion) {
            paginacion.addEventListener('click', function (e) {
                var link = e.target.closest('a[data-page]');
                if (!link) return;

                e.preventDefault();
                var page = parseInt(link.getAttribute('data-page'), 10);
                if (page && page >= 1 && page <= totalPaginas && page !== paginaActual) {
                    aplicarFiltros(page);
                }
            });
        }

        // Cargar datos iniciales (página 1)
        aplicarFiltros(1);
    }

    // Iniciar cuando el DOM esté listo
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        iniciar();
    }
})();
