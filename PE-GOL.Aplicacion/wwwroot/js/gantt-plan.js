// PE-GOL SaaS — Gantt del Plan de Acción (HU-021)
// ─────────────────────────────────────────────────────────────────────────────
// Lee el payload que el controller MVC ya embebió en
// <script type="application/json" id="gantt-plan-data"> y lo convierte al formato
// de DHTMLX Gantt **en memoria** (D-E/D-H del spec HU-021): este módulo NO
// vuelve a llamar a la API ni al endpoint de acciones.
//
// Reglas duras que este archivo cumple:
//   · UX-01 / DS § 12.7 — CERO colores. Solo se asignan clases
//     (gantt-bar--verde|amarillo|rojo|gris|cg); el color vive en wwwroot/css/gantt.css.
//   · SEC-05 — todo dato interpolado en el tooltip pasa por esc() antes de inyectarse
//     como HTML (descripcion, responsable, codigo, area son entrada de usuario).
//   · D-N / F9 — SOLO LECTURA: drag_move/drag_resize/drag_progress/drag_links/order_branch
//     en false, columnas sin `editor` y ningún handler de escritura. La edición de
//     fechas/peso/responsable sigue siendo el formulario de HU-019.
//   · Fuera de alcance — `links` SIEMPRE vacío (accion_plan no tiene dependencias) y
//     ninguna función PRO (gantt.ext.groups, recursos, auto-scheduling, export…).
(function () {
    'use strict';

    // Escala de 12 meses (CA #1) — abreviaturas de mes de AS-IS L125.
    var MESES_ABREV = ['ENE', 'FEB', 'MAR', 'ABR', 'MAY', 'JUN', 'JUL', 'AGO', 'SEP', 'OCT', 'NOV', 'DIC'];

    // CA #2 · DS § 12.2 — status de accion_plan → clase de barra. Solo el NOMBRE de la
    // clase; el color lo resuelve gantt.css con los tokens --gantt-bar-*.
    var CLASE_POR_STATUS = {
        Terminado: 'verde',
        EnProgreso: 'amarillo',
        Atrasado: 'rojo',
        NoIniciado: 'gris'
    };

    // Selectores de los filtros (ids fijados por el spec § UI punto 9).
    var ID_CONTENEDOR = 'gantt-pe';
    var ID_DATOS = 'gantt-plan-data';
    var ID_FILTRO_CG = 'filtroObjetivoCg';
    var ID_FILTRO_STATUS = 'filtroStatus';
    var ID_FILTRO_CLASIFICACION = 'filtroClasificacion';
    var ID_FILTRO_AREA = 'filtroArea';
    var ID_CARD_FILTROS = 'ganttFiltrosCard';
    var ID_SIN_COINCIDENCIAS = 'ganttSinCoincidencias';

    var datos = null;      // payload crudo del endpoint (DTOs del dominio)
    var esGerente = false; // decide si el filtro de área aplica (F6)

    // ── Utilidades ────────────────────────────────────────────

    // SEC-05: escape de HTML para todo valor interpolado en el tooltip.
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

    // Lee una longitud del Design System (px) para no duplicar el valor en JS.
    // Los tokens --gantt-row-height / --gantt-scale-height son la única fuente.
    function tokenPx(nombre, porDefecto) {
        var declarado = window.getComputedStyle(document.documentElement).getPropertyValue(nombre);
        var px = parseInt(declared, 10);
        return isNaN(px) ? porDefecto : px;
    }

    function fecha(d) {
        if (!(d instanceof Date) || isNaN(d.getTime())) {
            return '—';
        }
        var dia = ('0' + d.getDate()).slice(-2);
        var mes = ('0' + (d.getMonth() + 1)).slice(-2);
        return dia + '/' + mes + '/' + d.getFullYear();
    }

    // OJO — dos escalas distintas de progreso en el dominio (hallazgo 16 del spec):
    //   · accion_plan.progreso  es 0..100  → se muestra tal cual.
    //   · objetivo_cg.progreso  es 0..1    → se multiplica por 100.
    // Mezclarlas mostraría "0.35%" donde debe decir "35%".
    function progresoAccion(valor) {
        var n = Number(valor);
        return isNaN(n) ? '—' : String(n);
    }

    function progresoCg(valor) {
        var n = Number(valor);
        return isNaN(n) ? '—' : String(Math.round(n * 100));
    }

    function valorFiltro(id) {
        var el = document.getElementById(id);
        return el && el.value ? el.value : '';
    }

    // ── Construcción del dataset de DHTMLX (CA #2 + CA #3) ───

    // Los padres (type:"project") se incluyen SOLO si al menos una hija sobrevive
    // al filtro (spec § UI punto 9). `links` siempre vacío (fuera de alcance).
    function construirTareas(filtros) {
        var grupos = (datos.grupos || []);
        var acciones = (datos.acciones || []);
        var tareas = [];

        for (var i = 0; i < grupos.length; i++) {
            var g = grupos[i];

            if (filtros.objetivoCgId && g.objetivoCgId !== filtros.objetivoCgId) {
                continue;
            }
            if (filtros.areaId && g.areaId !== filtros.areaId) {
                continue;
            }

            var delGrupo = [];
            for (var j = 0; j < acciones.length; j++) {
                var a = acciones[j];
                if (a.objetivoCgId !== g.objetivoCgId) {
                    continue;
                }
                if (filtros.status && a.status !== filtros.status) {
                    continue;
                }
                if (filtros.clasificacion && a.clasificacion !== filtros.clasificacion) {
                    continue;
                }
                if (filtros.areaId && a.areaId !== filtros.areaId) {
                    continue;
                }
                delGrupo.push({
                    // id con prefijo en el padre para no colisionar con el GUID de la acción.
                    id: 'cg-' + g.objetivoCgId,
                    text: g.codigo + ' · ' + g.descripcion,
                    type: 'project',
                    parent: 0,
                    open: true,
                    progresoCg: g.progreso,
                    semaforoCg: g.semaforo,
                    areaCodigo: g.areaCodigo
                });
                delGrupo.push({
                    id: a.id,
                    parent: 'cg-' + g.objetivoCgId,
                    text: a.codigo + ' · ' + a.descripcion,
                    // DHTMLX espera fecha fin INCLUSIVA: 01/03 → 30/06 se representa
                    // con start_date 2026-03-01 y end_date 2026-06-30 (no 01/07).
                    start_date: a.fechaInicio,
                    end_date: a.fechaVencimiento,
                    tipo: 'task',
                    progreso: a.progreso,
                    status: a.status,
                    clasificacion: a.clasificacion,
                    tipoPresupuesto: a.tipoPresupuesto,
                    responsable: a.responsableNombre,
                    peso: a.peso,
                    areaId: a.areaId
                });
            }

            // Padre sin hijas que sobrevivan → no se dibuja (evita grupos vacíos).
            for (var k = 0; k < delGrupo.length; k++) {
                tareas.push(delGrupo[k]);
            }
        }

        return tareas;
    }

    function contarAcciones(tareas) {
        var n = 0;
        for (var i = 0; i < tareas.length; i++) {
            if (tareas[i].tipo === 'task') {
                n++;
            }
        }
        return n;
    }

    // ── Filtros: poblar opciones (CA #5 + CA #5 bis / F6) ────

    function agregarOpcion(select, value, label) {
        var option = document.createElement('option');
        option.value = value;
        option.textContent = label;
        select.appendChild(option);
    }

    // Opciones de CG — se construyen desde `grupos` del payload (el modelo MVC no
    // expone los grupos; el JSON sí). Con rol Gerente el código de área va delante.
    function poblarFiltroObjetivoCg(grupos) {
        var select = document.getElementById(ID_FILTRO_CG);
        if (!select) {
            return;
        }
        for (var i = 0; i < grupos.length; i++) {
            var g = grupos[i];
            var etiqueta = esGerente
                ? g.areaCodigo + ' · ' + g.codigo + ' — ' + g.descripcion
                : g.codigo + ' — ' + g.descripcion;
            agregarOpcion(select, g.objetivoCgId, etiqueta);
        }
    }

    // F6 — CUARTO filtro, solo Gerente. Opciones deduplicadas por areaId conservando
    // el orden de aparición en `grupos`; etiqueta exacta areaCodigo + ' — ' + areaNombre.
    function poblarFiltroArea(grupos) {
        if (!esGerente) {
            return [];
        }
        var select = document.getElementById(ID_FILTRO_AREA);
        if (!select) {
            return [];
        }

        var vistos = {};
        var opciones = [];
        for (var i = 0; i < grupos.length; i++) {
            var g = grupos[i];
            if (vistos[g.areaId]) {
                continue;
            }
            vistos[g.areaId] = true;
            var opcion = { value: g.areaId, label: g.areaCodigo + ' — ' + g.areaNombre };
            opciones.push(opcion);
            agregarOpcion(select, opcion.value, opcion.label);
        }
        return opciones;
    }

    function filtrosActuales() {
        return {
            objetivoCgId: valorFiltro(ID_FILTRO_CG),
            status: valorFiltro(ID_FILTRO_STATUS),
            clasificacion: valorFiltro(ID_FILTRO_CLASIFICACION),
            areaId: valorFiltro(ID_FILTRO_AREA)
        };
    }

    function limpiarFiltros() {
        [ID_FILTRO_CG, ID_FILTRO_STATUS, ID_FILTRO_CLASIFICACION, ID_FILTRO_AREA].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) {
                el.value = '';
            }
        });
    }

    // ── Aplicar filtros → gantt.parse (nunca gantt.init de nuevo) ──
    function aplicarFiltros() {
        var contenedor = document.getElementById(ID_CONTENEDOR);
        var vacio = document.getElementById(ID_SIN_COINCIDENCIAS);
        if (!contenedor) {
            return;
        }

        var tareas = construirTareas(filtrosActuales());

        // UX-05: 0 coincidencias → .empty-state dentro de la card, no un Gantt vacío.
        if (contarAcciones(tareas) === 0) {
            if (vacio) {
                vacio.classList.remove('d-none');
            }
            contenedor.classList.add('d-none');
            return;
        }

        if (vacio) {
            vacio.classList.add('d-none');
        }
        contenedor.classList.remove('d-none');

        // links SIEMPRE vacío: accion_plan no tiene columna de dependencia (fuera de alcance).
        gantt.parse({ data: tareas, links: [] });
    }

    // ── Templates (CA #2, CA #4) ─────────────────────────────

    function templateTaskClass(start, end, task) {
        // La barra resumen del CG (type "project") NO tiene status → clase propia,
        // nunca el gris de "No iniciado" (DS § 12.2).
        if (task.type === 'project') {
            return 'gantt-bar gantt-bar--cg';
        }
        return 'gantt-bar gantt-bar--' + (CLASE_POR_STATUS[task.status] || 'gris');
    }

    function templateTooltipText(start, end, task) {
        if (task.type === 'project') {
            return '<div class="gantt-tooltip gantt-tooltip--cg">' +
                '<div class="gantt-tooltip__cg">' + esc(task.text) + '</div>' +
                '<div><strong>Área:</strong> ' + esc(task.areaCodigo) + '</div>' +
                '<div><strong>Progreso CG:</strong> ' + esc(progresoCg(task.progresoCg)) + '%</div>' +
                '<div><strong>Semáforo:</strong> ' + esc(task.semaforoCg) + '</div>' +
                '</div>';
        }

        return '<div class="gantt-tooltip">' +
            '<div class="gantt-tooltip__title">' + esc(task.text) + '</div>' +
            '<div><strong>Inicio:</strong> ' + esc(fecha(task.start_date)) +
            ' · <strong>Vencimiento:</strong> ' + esc(fecha(task.end_date)) + '</div>' +
            '<div><strong>Progreso:</strong> ' + esc(progresoAccion(task.progreso)) + '%</div>' +
            '<div><strong>Responsable:</strong> ' + esc(task.responsable || '—') + '</div>' +
            '<div><strong>Clasificación:</strong> ' + esc(task.clasificacion) + '</div>' +
            '</div>';
    }

    // ── Arranque ──────────────────────────────────────────────

    function iniciar() {
        var nodo = document.getElementById(ID_DATOS);
        var contenedor = document.getElementById(ID_CONTENEDOR);
        if (!nodo || !contenedor) {
            return;
        }

        // El contenedor solo se renderiza cuando hay acciones (UX-05); si no existe,
        // la vista ya mostró su .empty-state y no hay nada que dibujar.
        try {
            datos = JSON.parse(nodo.textContent);
        } catch (e) {
            return;
        }
        if (!datos) {
            return;
        }

        var cardFiltros = document.getElementById(ID_CARD_FILTROS);
        esGerente = !!cardFiltros && cardFiltros.getAttribute('data-es-gerente') === 'true';

        var grupos = datos.grupos || [];
        poblarFiltroObjetivoCg(grupos);
        poblarFiltroArea(grupos);

        [ID_FILTRO_CG, ID_FILTRO_STATUS, ID_FILTRO_CLASIFICACION, ID_FILTRO_AREA].forEach(function (id) {
            var el = document.getElementById(id);
            if (el) {
                el.addEventListener('change', aplicarFiltros);
            }
        });

        Array.prototype.forEach.call(document.querySelectorAll('[data-limpiar-filtros]'), function (boton) {
            boton.addEventListener('click', function () {
                limpiarFiltros();
                aplicarFiltros();
            });
        });

        // ── Orden obligatorio: plugin → config → init → parse.
        // El plugin tooltip DEBE activarse ANTES de gantt.init(): sin él, el
        // gantt.templates.tooltip_text se ignora en silencio y el CA #4 no se cumple.
        gantt.plugins({ tooltip: true });

        gantt.config.date_format = '%d/%m/%Y';
        gantt.config.read_date_format = '%Y-%m-%d';

        // CA #1 — escala de exactamente 12 meses, desde ciclo.mes_inicio.
        // Con mes_inicio ≠ 1 la escala cruza de año (fila superior: 2026, 2027).
        gantt.config.scales = [
            { unit: 'year', step: 1, format: function (d) { return d.getFullYear(); } },
            { unit: 'month', step: 1, format: function (d) { return MESES_ABREV[d.getMonth()]; } }
        ];
        gantt.config.min_date = new Date(datos.fechaInicioCiclo);
        gantt.config.max_date = new Date(datos.fechaFinCiclo);
        gantt.config.row_height = tokenPx('--gantt-row-height', 34);
        gantt.config.scale_height = tokenPx('--gantt-scale-height', 60);

        // Solo lectura (D-N) — sin arrastre, sin reordenamiento, sin edición inline.
        gantt.config.drag_move = false;
        gantt.config.drag_resize = false;
        gantt.config.drag_progress = false;
        gantt.config.drag_links = false;
        gantt.config.order_branch = false;
        gantt.config.show_links = false;
        gantt.config.open_tree_initially = true;

        // Grilla sin `editor` en ninguna columna → no hay edición inline.
        gantt.config.columns = [
            { name: 'text', label: 'Acción', tree: true, width: '*' },
            { name: 'start_date', label: 'Inicio', align: 'left', width: 90 },
            { name: 'end_date', label: 'Vencimiento', align: 'left', width: 100 }
        ];
        gantt.config.grid_width = 420;

        gantt.templates.task_class = templateTaskClass;
        gantt.templates.tooltip_text = templateTooltipText;

        gantt.init(contenedor.id);
        gantt.parse({ data: construirTareas(filtrosActuales()), links: [] });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        iniciar();
    }
})();
