// PE-GOL SaaS — Log de Auditoría (HU-005 § UI)
// Coherencia del rango de fechas del filtro (desde ≤ hasta) antes de enviar el form GET.
$(function () {
    'use strict';

    var form = document.getElementById('formFiltros');
    if (!form) {
        return;
    }

    form.addEventListener('submit', function (event) {
        var desde = document.getElementById('desde').value;
        var hasta = document.getElementById('hasta').value;

        if (desde && hasta && desde > hasta) {
            event.preventDefault();
            var hastaCampo = document.getElementById('hasta');
            hastaCampo.classList.remove('form-pe-input-error');
            void hastaCampo.offsetWidth; // reiniciar animación si existe
            hastaCampo.classList.add('form-pe-input-error');

            var aviso = document.getElementById('errorFiltros');
            if (!aviso) {
                aviso = document.createElement('span');
                aviso.id = 'errorFiltros';
                aviso.className = 'form-pe-error d-block mt-1';
                hastaCampo.parentElement.appendChild(aviso);
            }
            aviso.textContent = 'La fecha/hora "Hasta" debe ser posterior o igual a "Desde".';
            hastaCampo.focus();
        }
    });
});