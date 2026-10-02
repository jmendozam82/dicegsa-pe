// PE-GOL SaaS — Formulario de Key Result (HU-025 — Crear/Editar)
// Validación cliente con jQuery Validate (UX-04). La validación del servidor (FluentValidation +
// BLL) es la fuente de verdad. Patrón verbatim de okr-form.js.
$(function () {
    'use strict';

    var form = document.getElementById('formKeyResult');
    if (!form) {
        return; // La vista no incluye el formulario.
    }

    // Contador de caracteres del textarea (UX helper, sin alert).
    var textarea = document.getElementById('descripcion');
    if (textarea && textarea.maxLength > 0) {
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
            peso: { required: true, number: true, min: 0.001, max: 1 }
        },
        messages: {
            descripcion: {
                required: 'La descripción de la métrica es requerida.',
                maxlength: 'La descripción no puede exceder 500 caracteres.'
            },
            peso: {
                required: 'El peso ponderado es requerido.',
                number: 'Ingrese un valor numérico válido.',
                min: 'El peso debe ser mayor que 0.',
                max: 'El peso no puede ser mayor que 1.000.'
            }
        },
        errorElement: 'span',
        errorClass: 'form-pe-error',
        errorPlacement: function (error, element) {
            error.insertAfter(element);
        }
    });
});
