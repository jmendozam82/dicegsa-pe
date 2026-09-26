// PE-GOL SaaS — Ciclos (HU-007)
// Modal de confirmación activar/cerrar (D-3: form POST, navegación MVC full page load).
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

        var cicloId = boton.getAttribute('data-ciclo-id');
        var cicloNombre = boton.getAttribute('data-ciclo-nombre');
        var accion = boton.getAttribute('data-accion'); // 'activar' | 'cerrar'
        var esActivar = accion === 'activar';

        var titulo = document.getElementById('modalConfirmarTitulo');
        var texto = document.getElementById('modalConfirmarTexto');
        var formulario = document.getElementById('formConfirmarAccion');
        var botonConfirmar = document.getElementById('modalConfirmarBoton');

        if (esActivar) {
            titulo.textContent = 'Activar ciclo';
            texto.textContent = "¿Activar el ciclo '" + cicloNombre + "'? Solo puede haber un ciclo activo a la vez (RN-004).";
            formulario.setAttribute('action', '/Ciclo/Activar/' + cicloId);
            botonConfirmar.textContent = 'Activar';
            botonConfirmar.classList.remove('btn-pe--danger');
            botonConfirmar.classList.add('btn-pe--primary');
        } else {
            titulo.textContent = 'Cerrar ciclo';
            texto.textContent = "¿Cerrar el ciclo '" + cicloNombre + "'? El cierre es irreversible y bloquea la edición de sus datos.";
            formulario.setAttribute('action', '/Ciclo/Cerrar/' + cicloId);
            botonConfirmar.textContent = 'Cerrar';
            botonConfirmar.classList.remove('btn-pe--primary');
            botonConfirmar.classList.add('btn-pe--danger');
        }
    });
});