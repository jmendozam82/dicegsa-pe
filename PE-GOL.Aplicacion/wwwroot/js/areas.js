// PE-GOL SaaS — Áreas (HU-009)
// Modal de confirmación desactivar (D-3: form POST, navegación MVC full page load).
$(function () {
    'use strict';

    var modalEl = document.getElementById('modalConfirmarAccion');
    if (!modalEl) {
        return; // La vista no incluye el modal.
    }

    var modal = new bootstrap.Modal(modalEl);

    modalEl.addEventListener('show.bs.modal', function (event) {
        var boton = event.relatedTarget;
        if (!boton) {
            return;
        }

        var areaId = boton.getAttribute('data-area-id');
        var areaNombre = boton.getAttribute('data-area-nombre');
        var areaCodigo = boton.getAttribute('data-area-codigo');
        var cicloId = boton.getAttribute('data-ciclo-id');

        var titulo = document.getElementById('modalConfirmarTitulo');
        var texto = document.getElementById('modalConfirmarTexto');
        var formulario = document.getElementById('formConfirmarAccion');
        var botonConfirmar = document.getElementById('modalConfirmarBoton');

        titulo.textContent = 'Desactivar área';
        texto.textContent = "¿Desactivar el área '" + areaCodigo + ' — ' + areaNombre + "'? Quedará en solo lectura y su responsable quedará disponible para reasignación.";
        formulario.setAttribute('action', '/Areas/Desactivar/' + areaId + '?cicloId=' + encodeURIComponent(cicloId));
        botonConfirmar.textContent = 'Desactivar';
        botonConfirmar.classList.remove('btn-pe--primary');
        botonConfirmar.classList.add('btn-pe--danger');
    });
});