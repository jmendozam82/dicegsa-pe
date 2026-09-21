// PE-GOL SaaS — Objetivos CG del Jefe de Área (HU-017)
// 1) Modal de confirmación eliminar (D-3: form POST, navegación MVC full page load).
// 2) Panel de referencia contextual (CA #3): ObjetivoQ1..Q4 del pilar seleccionado (HU-014),
//    expuestos vía data-q1..data-q4 en el <option> — sin AJAX.
$(function () {
    'use strict';

    // ── Modal eliminar CG ──
    var modalEl = document.getElementById('modalEliminarCg');
    if (modalEl) {
        var modal = new bootstrap.Modal(modalEl);

        modalEl.addEventListener('show.bs.modal', function (event) {
            var boton = event.relatedTarget;
            if (!boton) {
                return;
            }

            var cgId = boton.getAttribute('data-cg-id');
            var cgCodigo = boton.getAttribute('data-cg-codigo');
            var cgDescripcion = boton.getAttribute('data-cg-descripcion');

            var texto = document.getElementById('modalEliminarCgTexto');
            var formulario = document.getElementById('formEliminarCg');

            texto.textContent = "¿Eliminar el objetivo '" + cgCodigo + ' — ' + cgDescripcion +
                "'? Esta acción no se puede deshacer. Si el objetivo tiene acciones en el plan, el sistema lo impedirá.";
            formulario.setAttribute('action', '/ObjetivoCg/Eliminar/' + cgId);
        });
    }

    // ── Panel de referencia contextual (CA #3 HU-017) ──
    var selectPilar = document.getElementById('pilarId');
    var panel = document.getElementById('referenciaPilar');
    if (selectPilar && panel) {
        var refs = { Q1: 'refQ1', Q2: 'refQ2', Q3: 'refQ3', Q4: 'refQ4' };

        function actualizarReferencia() {
            var opcion = selectPilar.options[selectPilar.selectedIndex];
            if (!opcion || !opcion.value) {
                panel.classList.add('d-none');
                return;
            }

            var hayContenido = false;
            ['Q1', 'Q2', 'Q3', 'Q4'].forEach(function (q) {
                var valor = opcion.getAttribute('data-' + q.toLowerCase());
                var texto = valor && valor.trim() ? valor : '—';
                document.getElementById(refs[q]).textContent = texto;
                if (valor && valor.trim()) {
                    hayContenido = true;
                }
            });
            panel.classList.toggle('d-none', !hayContenido);
        }

        selectPilar.addEventListener('change', actualizarReferencia);
        actualizarReferencia(); // Inicializa en Editar (pilar precargado).
    }
});