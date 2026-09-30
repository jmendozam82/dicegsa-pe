// PE-GOL SaaS — OKRs del área (HU-024)
// Modal de confirmación eliminar (patrón establecido: data-* en el botón disparador + action dinámico,
// form POST, navegación MVC full page load — mismo patrón que pilares.js/acciones.js/objetivoscg.js).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalEliminarOkr');
    if (!modalEl) {
        return; // La vista no incluye el modal.
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var okrId = boton.getAttribute('data-okr-id');
        var okrCodigo = boton.getAttribute('data-okr-codigo');
        var okrDescripcion = boton.getAttribute('data-okr-descripcion');

        var texto = document.getElementById('modalEliminarOkrTexto');
        var formulario = document.getElementById('formEliminarOkr');

        texto.textContent = "¿Eliminar el OKR '" + okrCodigo + ' — ' + okrDescripcion +
            "'? Esta acción no se puede deshacer. Si el OKR tiene KRs con valores reales registrados, el sistema lo impedirá (CA #3 / RN-028).";
        formulario.setAttribute('action', '/Okr/Eliminar/' + okrId);
    });
});