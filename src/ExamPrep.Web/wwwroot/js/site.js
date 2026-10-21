// 作答頁：顯示已作答題數，離開頁面前若有未送出的作答則提醒
(function () {
    const form = document.getElementById('take-form');
    if (!form) return;
    const counter = document.getElementById('answered-count');
    const total = Number(form.dataset.total || 0);
    let dirty = false;

    function update() {
        const names = new Set();
        form.querySelectorAll('input[type=radio]:checked, input[type=checkbox]:checked').forEach(i => names.add(i.name));
        if (counter) counter.textContent = `${names.size} / ${total}`;
    }

    form.addEventListener('change', () => { dirty = true; update(); });
    form.addEventListener('submit', (e) => {
        const answered = new Set([...form.querySelectorAll('input:checked')].map(i => i.name)).size;
        if (answered < total && !confirm(`還有 ${total - answered} 題未作答，確定要交卷嗎？未作答視為答錯。`)) {
            e.preventDefault();
            return;
        }
        dirty = false;
    });
    window.addEventListener('beforeunload', (e) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } });
    update();
})();
