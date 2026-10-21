// 作答頁：已作答計數、逐題即時對答、計時模擬考、離開頁面提醒
(function () {
    const form = document.getElementById('take-form');
    if (!form) return;
    const counter = document.getElementById('answered-count');
    const total = Number(form.dataset.total || 0);
    const instant = form.dataset.instant === '1';
    const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let dirty = false;

    function answeredCount() {
        const names = new Set();
        form.querySelectorAll('input:checked').forEach(i => names.add(i.name));
        return names.size;
    }
    function update() { if (counter) counter.textContent = `${answeredCount()} / ${total}`; }

    async function check(section) {
        const qid = section.dataset.qid;
        const picked = [...section.querySelectorAll('input:checked')].map(i => i.value).join('');
        if (!picked) return;
        const body = new URLSearchParams({ questionId: qid, selected: picked });
        if (token) body.append('__RequestVerificationToken', token);
        const verdict = section.querySelector('.js-verdict');
        try {
            const res = await fetch(form.dataset.checkUrl, { method: 'POST', body, headers: { 'Accept': 'application/json' } });
            if (!res.ok) throw new Error(res.status);
            const r = await res.json();
            section.querySelectorAll('.opt').forEach(li => {
                const l = li.dataset.letter;
                if (r.answer.includes(l)) li.classList.add('is-answer');
                else if (r.selected.includes(l)) li.classList.add('is-wrong');
            });
            section.querySelectorAll('input').forEach(i => i.disabled = true);
            section.querySelector('.js-check')?.remove();
            section.classList.add(r.correct ? 'result-correct' : 'result-wrong');
            verdict.textContent = r.correct ? '答對' : `答錯，正解 ${r.answer}`;
            verdict.style.color = r.correct ? 'var(--green)' : 'var(--red)';
            section.querySelector('.js-detail')?.classList.remove('d-none');
            if (r.finished) { dirty = false; verdict.textContent += '　全部作答完成，按下方按鈕看結果。'; }
        } catch {
            verdict.textContent = '對答失敗，請檢查網路後再試一次。';
        }
    }

    form.addEventListener('change', (e) => {
        dirty = true;
        update();
        if (instant) {
            const section = e.target.closest('section.q');
            if (section && section.dataset.multi === '0') check(section);
        }
    });
    form.addEventListener('click', (e) => {
        const btn = e.target.closest('.js-check');
        if (btn) check(btn.closest('section.q'));
    });

    let timeUp = false;
    form.addEventListener('submit', (e) => {
        const left = total - answeredCount();
        if (!timeUp && left > 0 && !confirm(`還有 ${left} 題未作答，確定要${instant ? '結束' : '交卷'}嗎？未作答視為答錯。`)) {
            e.preventDefault();
            return;
        }
        dirty = false;
    });
    window.addEventListener('beforeunload', (e) => { if (dirty && !instant) { e.preventDefault(); e.returnValue = ''; } });

    const deadline = form.dataset.deadline ? new Date(form.dataset.deadline) : null;
    const timer = document.getElementById('timer');
    if (deadline && timer) {
        const tick = () => {
            const ms = deadline - new Date();
            if (ms <= 0) {
                timer.textContent = '00:00';
                timeUp = true;
                dirty = false;
                form.requestSubmit();
                return;
            }
            const m = Math.floor(ms / 60000), s = Math.floor(ms % 60000 / 1000);
            timer.textContent = `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
            timer.style.color = ms < 5 * 60000 ? 'var(--red)' : '';
            setTimeout(tick, 1000);
        };
        tick();
    }
    update();
})();
