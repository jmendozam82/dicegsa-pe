// PE-GOL SaaS — Key Results de un OKR (HU-025)
// Modal de confirmación eliminar + modal de ajuste de pesos (patrón okrs.js).
// El JS es un asset estático: nunca lleva Razor dentro. El contexto (okrId) y los KRs se
// leen del DOM con data-*, igual que okrs.js y el resto de vistas (ADR-016).
$(function () {
    'use strict';

    var contenedor = document.getElementById('tablaKrs');
    var okrId = contenedor ? contenedor.getAttribute('data-okr-id') : null;

    // ─── Modal de eliminación ────────────────────────────────────────────────
    var modalEliminarEl = document.getElementById('modalEliminarKr');
    if (modalEliminarEl && okrId) {
        var modalEliminar = new bootstrap.Modal(modalEliminarEl);

        modalEliminarEl.addEventListener('show.bs.modal', function (event) {
            var boton = event.relatedTarget;
            if (!boton) {
                return;
            }

            var krId = boton.getAttribute('data-kr-id');
            var krCodigo = boton.getAttribute('data-kr-codigo');
            var krDescripcion = boton.getAttribute('data-kr-descripcion');

            var texto = document.getElementById('modalEliminarKrTexto');
            var formulario = document.getElementById('formEliminarKr');

            texto.textContent = "¿Eliminar el Key Result '" + krCodigo + ' — ' + krDescripcion +
                "'? Esta acción no se puede deshacer. Si el KR tiene valores reales registrados, el sistema lo impedirá (CA #4).";
            formulario.setAttribute('action', '/KeyResult/Eliminar/' + okrId + '/' + krId);
        });
    }

    // ─── Modal de ajuste de pesos ─────────────────────────────────────────────
    var modalPesosEl = document.getElementById('modalPesos');
    if (modalPesosEl && okrId) {
        var pesosContainer = document.getElementById('pesosContainer');
        var sumaPesosEl = document.getElementById('sumaPesos');
        var btnGuardarPesos = document.getElementById('btnGuardarPesos');
        var btnRepartirEquitativo = document.getElementById('btnRepartirEquitativo');

        // Datos de los KRs leídos de las filas que el MVC ya renderizó.
        var krs = Array.prototype.map.call(
            contenedor.querySelectorAll('tbody tr[data-kr-id]'),
            function (fila) {
                return {
                    Id: fila.getAttribute('data-kr-id'),
                    Codigo: fila.getAttribute('data-kr-codigo'),
                    Descripcion: fila.getAttribute('data-kr-descripcion'),
                    Peso: parseFloat(fila.getAttribute('data-kr-peso')) || 0
                };
            });

        modalPesosEl.addEventListener('show.bs.modal', function () {
            renderPesos();
        });

        function renderPesos() {
            pesosContainer.innerHTML = '';
            krs.forEach(function (kr, index) {
                var row = document.createElement('div');
                row.className = 'row g-2 mb-2 align-items-center';
                row.innerHTML = `
                    <div class="col-md-2">
                        <span class="cell-codigo">${kr.Codigo}</span>
                    </div>
                    <div class="col-md-6">
                        <span class="body-sm" title="${kr.Descripcion}">${kr.Descripcion}</span>
                    </div>
                    <div class="col-md-4">
                        <input type="number" class="form-pe-input input-peso" 
                               step="0.001" min="0.001" max="1" 
                               value="${kr.Peso.toFixed(3)}" 
                               data-kr-id="${kr.Id}" data-index="${index}" />
                    </div>
                `;
                pesosContainer.appendChild(row);
            });

            // Event listeners para recalcular Σ
            document.querySelectorAll('.input-peso').forEach(function (input) {
                input.addEventListener('input', actualizarSuma);
            });

            actualizarSuma();
        }

        function actualizarSuma() {
            var suma = 0;
            document.querySelectorAll('.input-peso').forEach(function (input) {
                var val = parseFloat(input.value) || 0;
                suma += val;
            });
            suma = Math.round(suma * 1000) / 1000;
            sumaPesosEl.textContent = suma.toFixed(3);
            sumaPesosEl.className = 'fw-bold ' + (suma === 1.000 ? 'text-success' : 'text-danger');
            btnGuardarPesos.disabled = suma !== 1.000;
        }

        btnRepartirEquitativo.addEventListener('click', function () {
            var n = krs.length;
            if (n === 0) return;
            var pesoEquitativo = Math.round((1 / n) * 1000) / 1000;
            document.querySelectorAll('.input-peso').forEach(function (input) {
                input.value = pesoEquitativo.toFixed(3);
            });
            actualizarSuma();
        });

        btnGuardarPesos.addEventListener('click', function () {
            // Form-encoded (no JSON): la acción MVC declara el request como parámetro complejo
            // sin [FromBody], así que el binder solo lo enlaza desde el cuerpo de un formulario.
            // Con application/json el vector llegaba vacío y la BLL lo rechazaba.
            var cuerpo = new URLSearchParams();
            document.querySelectorAll('.input-peso').forEach(function (input, index) {
                cuerpo.append('pesos[' + index + '].id', input.getAttribute('data-kr-id'));
                cuerpo.append('pesos[' + index + '].peso', parseFloat(input.value).toFixed(3));
            });

            btnGuardarPesos.disabled = true;
            fetch('/KeyResult/ActualizarPesos/' + okrId, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8'
                },
                body: cuerpo.toString()
            }).then(function (response) {
                if (response.ok) {
                    window.location.reload();
                    return null;
                }
                return response.json()
                    .catch(function () { return null; })
                    .then(function (data) {
                        alert(data && data.message ? data.message : 'Error al guardar los pesos.');
                    });
            }).catch(function () {
                alert('Error de conexión al guardar los pesos.');
            }).then(function () {
                btnGuardarPesos.disabled = false;
                actualizarSuma();
            });
        });
    }
});
