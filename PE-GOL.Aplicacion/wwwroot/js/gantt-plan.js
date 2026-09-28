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
        var valorToken = window.getComputedStyle(document.documentElement).getPropertyValue(nombre);
        var px = parseInt(valorToken, 10);
        return isNaN(px) ? porDefecto : px;
    }

    // Convierte una fecha del payload a Date, o null si no es utilizable.
    // El payload llega en ISO 8601 CON hora ("2026-03-01T00:00:00"); la forma de solo
    // fecha ("2026-03-01") se ancla a medianoche LOCAL, porque new Date("2026-03-01")
    // la interpreta como UTC y en huso negativo la desplazaría un día atrás.
    function aFecha(valor) {
        if (valor === null || valor === undefined || valor === '') {
            return null;
        }
        if (valor instanceof Date) {
            return isNaN(valor.getTime()) ? null : valor;
        }
        if (typeof valor === 'string') {
            var soloFecha = /^(\d{4})-(\d{2})-(\d{2})$/.exec(valor);
            if (soloFecha) {
                return new Date(Number(soloFecha[1]), Number(soloFecha[2]) - 1, Number(soloFecha[3]));
            }
        }
        var d = new Date(valor);
        return isNaN(d.getTime()) ? null : d;
    }

    function fecha(d) {
        // DHTMLX entrega Date tras el parseo; se acepta también el string crudo por
        // si el tooltip llegara con el valor sin parsear del payload.
        var v = aFecha(d);
        if (v === null) {
            return '—';
        }
        var dia = ('0' + v.getDate()).slice(-2);
        var mes = ('0' + (v.getMonth() + 1)).slice(-2);
        return dia + '/' + mes + '/' + v.getFullYear();
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

            // 1) Hijas que sobreviven al filtro. Se calculan ANTES de emitir el padre
            //    porque el resumen del CG necesita el min(inicio)/max(vencimiento) de ellas.
            var hijas = [];
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
                hijas.push(a);
            }

            // Padre sin hijas que sobrevivan → no se dibuja (evita grupos vacíos).
            if (hijas.length === 0) {
                continue;
            }

            // 2) Fechas del padre = unión (min inicio / max vencimiento) de sus hijas.
            //    Un type:"project" SIN start_date válido hace que DHTMLX 10 lance en
            //    gantt.parse() "Invalid start_date argument for calculateEndDate method":
            //    su rollup interno deriva el fin de un inicio inexistente. Si alguna
            //    fecha viniera inválida se cae al rango del ciclo para no dejar al padre
            //    sin fecha (el MVC solo renderiza esta vista con ciclo activo).
            var inicio = null;
            var fin = null;
            for (var h = 0; h < hijas.length; h++) {
                var fInicio = aFecha(hijas[h].fechaInicio);
                var fFin = aFecha(hijas[h].fechaVencimiento);
                if (fInicio && (inicio === null || fInicio < inicio)) {
                    inicio = fInicio;
                }
                if (fFin && (fin === null || fFin > fin)) {
                    fin = fFin;
                }
            }
            if (inicio === null || fin === null || fin < inicio) {
                inicio = aFecha(datos.fechaInicioCiclo) || inicio;
                fin = aFecha(datos.fechaFinCiclo) || fin;
            }

            // 3) UN solo padre por CG — id con prefijo para no colisionar con el GUID de
            //    la acción — seguido de sus hijas.
            tareas.push({
                id: 'cg-' + g.objetivoCgId,
                text: g.codigo + ' · ' + g.descripcion,
                type: 'project',
                parent: 0,
                open: true,
                start_date: inicio,
                end_date: fin,
                progresoCg: g.progreso,
                semaforoCg: g.semaforo,
                areaCodigo: g.areaCodigo
            });

            for (var k = 0; k < hijas.length; k++) {
                var accion = hijas[k];
                tareas.push({
                    id: accion.id,
                    parent: 'cg-' + g.objetivoCgId,
                    text: accion.codigo + ' · ' + accion.descripcion,
                    // DHTMLX espera fecha fin INCLUSIVA: 01/03 → 30/06 se representa
                    // con start_date 2026-03-01 y end_date 2026-06-30 (no 01/07).
                    // Se pasa el payload tal cual: llega en ISO 8601 con hora
                    // ("2026-03-01T00:00:00") y DHTMLX >= 9.1.3 lo parsea solo.
                    start_date: accion.fechaInicio,
                    end_date: accion.fechaVencimiento,
                    tipo: 'task',
                    progreso: accion.progreso,
                    status: accion.status,
                    clasificacion: accion.clasificacion,
                    tipoPresupuesto: accion.tipoPresupuesto,
                    responsable: accion.responsableNombre,
                    peso: accion.peso,
                    areaId: accion.areaId
                });
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
        //
        // clearAll() ANTES de parse() — no es cosmético, es lo que hace que el filtro
        // FILTRE. En 10.0.3 ninguno de los dos parse resetea el store: el de gantt es
        //   parse:function(e){ … var t=this._parseInner(e); this._buildTree(t); this.filter(); … }
        // y el de datapull termina en _parseInner con
        //   this.pull.hasOwnProperty(t.id) || this.fullOrder.push(t.id), … this.pull[t.id]=t
        // es decir FUSIONA por id. Nuestros ids son estables (GUID de la acción y
        // 'cg-'+objetivoCgId), así que las acciones que el filtro deja fuera nunca se
        // borraban de `pull` y seguían dibujadas: el dataset solo CRECÍA en cada
        // cambio de filtro. Efecto secundario cubierto por este fix: cada parse
        // re-empujaba ids a `fullOrder`, que también inflaba la grilla — el "bajaba
        // infinitamente" que se reportó era en parte ESTE bug, no solo el `height:auto`
        // del contenedor (ya acotado por --gantt-altura).
        //
        // gantt.silent() (que sí existe en 10.0.3) envuelve el clearAll+parse para no
        // despachar los eventos ni los refresh intermedios. NO evita el `render()` final
        // de gantt.clearAll() — ese no consulta _skip_refresh — pero como ambas llamadas
        // son síncronas en el mismo task, el navegador no pinta el estado vacío en medio.
        gantt.silent(function () {
            gantt.clearAll();
            gantt.parse({ data: tareas, links: [] });
        });
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
        // El payload llega en ISO 8601 CON hora ("2026-03-01T00:00:00") y DHTMLX >= 9.1.3
        // lo detecta y parsea solo (docs: "Loading dates in ISO format"), así que no hace
        // falta ninguna config de lectura. Lo anterior (`read_date_format`) se eliminó de
        // la librería en la v8 y en 10.0.3 no existe: 0 referencias en el bundle, inerte.
        // Descartadas también las dos alternativas que parecen equivalentes pero NO lo son
        // (medido contra el bundle 10.0.3): `gantt.config.xml_date` está marcada como
        // REEMPLAZADA por `date_format` en el changelog, y tanto su forma de objeto
        // {parse_date, format_date} como su forma-cadena se IGNORAN en silencio ante una
        // fecha ISO (el parseo va por la autodetección, no por ese config). Para imponer un
        // parse propio la API vigente es gantt.templates.parse_date / format_date.

        // CA #1 — escala de exactamente 12 meses, desde ciclo.mes_inicio.
        // Con mes_inicio ≠ 1 la escala cruza de año (fila superior: 2026, 2027).
        gantt.config.scales = [
            { unit: 'year', step: 1, format: function (d) { return d.getFullYear(); } },
            { unit: 'month', step: 1, format: function (d) { return MESES_ABREV[d.getMonth()]; } }
        ];

        // Rango de la escala = ciclo activo. OJO: las claves son start_date / end_date,
        // NO min_date / max_date — esas pertenecen al estado interno de la librería y no
        // existen en su config (0 referencias en los defaults de 10.0.3), por lo que antes
        // no tenían efecto. Ambas deben fijarse juntas o DHTMLX las ignora, y su valor es
        // un Date (docs de gantt.config.start_date). DHTMLX las alinea a la unidad de la
        // escala, así que el rango cubre los 12 meses exactos del ciclo.
        var inicioCiclo = aFecha(datos.fechaInicioCiclo);
        var finCiclo = aFecha(datos.fechaFinCiclo);
        if (inicioCiclo && finCiclo) {
            gantt.config.start_date = inicioCiclo;
            gantt.config.end_date = finCiclo;
        }

        // El default de DHTMLX 10.0.3 para show_tasks_outside_timescale es FALSE y su filtro
        // interno descarta las tareas fuera de la escala; y AccionPlanCreateRequestValidator solo
        // valida NotEmpty en las fechas (una acción puede salirse del rango de su ciclo), por eso
        // se activa: que la acción se vea antes que ocultarla, con la escala anclada a los 12 meses
        // del ciclo (CA #1). Debe fijarse antes de gantt.init(): el filtro corre en el 1er render.
        gantt.config.show_tasks_outside_timescale = true;

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
