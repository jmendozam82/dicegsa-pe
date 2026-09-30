// PE-GOL SaaS — Formulario de OKR (HU-024 — Crear/Editar)
// Validación cliente con jQuery Validate (UX-04). La validación del servidor (FluentValidation +
// BLL) es la fuente de verdad. Patrón verbatim de pilares.js (Crear.cshtml) y objetivoscg.js.
$(function () {
    'use strict';

    var form = document.getElementById('formOkr');
    if (!form) {
        return; // La vista no incluye el formulario.
    }

    // Contador de caracteres del textarea (UX helper, sin alert).
    var textarea = document.getElementById('descripcion');
    if (textarea && textarea.maxLength > 0) {
        // El navegador ya limita al maxlength (500) por atributo HTML; este listener sólo
        // informa visualmente al usuario cerca del límite para evitar perder el texto al pegar.
        textarea.addEventListener('input', function () {
            if (textarea.value.length >= textarea.maxLength) {
                textarea.classList.add('is-invalid');
            } else {
                textarea.classList.remove('is-invalid');
            }
        });
    }

    $(form).validate({
        rules: {
            descripcion: { required: true, maxlength: 500 },
            pilarId: { required: true }
        },
        messages: {
            descripcion: {
                required: 'La descripción del objetivo es requerida.',
                maxlength: 'La descripción no puede exceder 500 caracteres.'
            },
            pilarId: { required: 'Seleccione un pilar estratégico.' }
        },
        errorElement: 'span',
        errorClass: 'form-pe-error',
        errorPlacement: function (error, element) {
            error.insertAfter(element);
        }
    });
});