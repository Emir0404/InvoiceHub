// Invoice form: add/remove lines and show a live total preview.
// The server recalculates everything, this is only for the user's convenience.
(function () {
    var body = document.getElementById('lines-body');
    var template = document.getElementById('line-template');
    if (!body || !template) return;

    function round2(value) {
        // half away from zero, like the server
        return Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100;
    }

    function money(value) {
        return value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function rows() {
        return Array.prototype.slice.call(body.querySelectorAll('.line-row'));
    }

    function num(row, field) {
        var input = row.querySelector('[data-field="' + field + '"]');
        var value = parseFloat(input ? input.value : '');
        return isNaN(value) ? 0 : value;
    }

    // After adding/removing, names must be Lines[0], Lines[1], ... without gaps so the server binds them all.
    function renumber() {
        rows().forEach(function (row, index) {
            row.querySelectorAll('input[name^="Lines["]').forEach(function (input) {
                input.name = input.name.replace(/^Lines\[\d+\]/, 'Lines[' + index + ']');
            });
        });
    }

    function recalc() {
        var byRate = {};
        rows().forEach(function (row) {
            var net = round2(num(row, 'qty') * num(row, 'price'));
            row.querySelector('[data-role="net"]').textContent = money(net);
            var rate = num(row, 'vat');
            byRate[rate] = (byRate[rate] || 0) + net;
        });

        var totalNet = 0, totalVat = 0;
        var vatHtml = '';
        Object.keys(byRate).map(Number).sort(function (a, b) { return a - b; }).forEach(function (rate) {
            var net = round2(byRate[rate]);
            var vat = round2(net * rate / 100);
            totalNet += net;
            totalVat += vat;
            if (rate > 0) {
                vatHtml += '<div class="row"><div class="col text-muted">VAT ' + rate + '%</div><div class="col-auto num">' + money(vat) + '</div></div>';
            }
        });

        document.getElementById('vat-lines').innerHTML = vatHtml;
        document.querySelector('[data-role="total-net"]').textContent = money(totalNet);
        document.querySelector('[data-role="total-gross"]').textContent = money(totalNet + totalVat);
    }

    document.getElementById('add-line').addEventListener('click', function () {
        if (rows().length >= 50) return;
        var html = template.innerHTML.replace(/__i__/g, String(rows().length));
        body.insertAdjacentHTML('beforeend', html);
        recalc();
        var last = rows().pop();
        last.querySelector('input').focus();
    });

    body.addEventListener('click', function (event) {
        var button = event.target.closest('[data-role="remove"]');
        if (!button) return;
        if (rows().length === 1) {
            // never leave the invoice without a line: just clear it
            rows()[0].querySelectorAll('input').forEach(function (input) {
                input.value = input.dataset.field === 'qty' ? '1' : (input.dataset.field ? '0' : '');
            });
        } else {
            button.closest('.line-row').remove();
            renumber();
        }
        recalc();
    });

    body.addEventListener('input', recalc);
    recalc();
})();
