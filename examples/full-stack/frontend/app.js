const API_BASE = 'http://127.0.0.1:8000';

const STATUS_ORDER = ['новая', 'в_работе', 'готово'];
const STATUS_LABELS = { 'новая': 'Новые', 'в_работе': 'В работе', 'готово': 'Готово' };
const COLUMN_MAP = { 'новая': 'col-new', 'в_работе': 'col-progress', 'готово': 'col-done' };
const COUNT_MAP = { 'новая': 'count-new', 'в_работе': 'count-progress', 'готово': 'count-done' };
const PRIORITY_LABELS = { 'высокий': 'Высокий', 'средний': 'Средний', 'низкий': 'Низкий' };

async function api(fn, args = []) {
    const res = await fetch(`${API_BASE}/call`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ function: fn, args, reset_state: false }),
    });
    const json = await res.json();
    if (!json.ok) throw new Error(json.error?.message || 'API error');
    return json.data.result;
}

function $(id) { return document.getElementById(id); }

function showToast(message, type = 'info') {
    const container = $('toasts');
    const el = document.createElement('div');
    el.className = `toast toast--${type}`;
    el.textContent = message;
    container.appendChild(el);
    setTimeout(() => el.remove(), 3000);
}

function createCard(task) {
    const card = document.createElement('div');
    card.className = 'card';
    card.dataset.id = task.id;

    const statusIdx = STATUS_ORDER.indexOf(task['статус']);
    const canMoveLeft = statusIdx > 0;
    const canMoveRight = statusIdx < STATUS_ORDER.length - 1;

    card.innerHTML = `
        <div class="card__top">
            <span class="card__title">${escapeHtml(task['название'])}</span>
            <span class="card__id">#${task.id}</span>
        </div>
        ${task['описание'] ? `<div class="card__desc">${escapeHtml(task['описание'])}</div>` : ''}
        <div class="card__footer">
            <span class="priority-badge" data-priority="${task['приоритет']}">${PRIORITY_LABELS[task['приоритет']] || task['приоритет']}</span>
            <div class="card__actions">
                ${canMoveLeft ? `<button class="btn btn--icon" data-action="move-left" title="← ${STATUS_LABELS[STATUS_ORDER[statusIdx - 1]]}">◀</button>` : ''}
                ${canMoveRight ? `<button class="btn btn--icon" data-action="move-right" title="${STATUS_LABELS[STATUS_ORDER[statusIdx + 1]]} →">▶</button>` : ''}
                <button class="btn btn--icon" data-action="edit" title="Редактировать">✎</button>
                <button class="btn btn--icon" data-action="delete" title="Удалить" style="color:var(--danger)">✕</button>
            </div>
        </div>
    `;

    card.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        handleCardAction(task, btn.dataset.action);
    });

    return card;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

async function handleCardAction(task, action) {
    const id = task.id;
    const statusIdx = STATUS_ORDER.indexOf(task['статус']);

    if (action === 'move-left' && statusIdx > 0) {
        const newStatus = STATUS_ORDER[statusIdx - 1];
        await api('сменить_статус', [id, newStatus]);
        showToast(`Задача #${id} → ${STATUS_LABELS[newStatus]}`, 'success');
        await refresh();
        return;
    }

    if (action === 'move-right' && statusIdx < STATUS_ORDER.length - 1) {
        const newStatus = STATUS_ORDER[statusIdx + 1];
        await api('сменить_статус', [id, newStatus]);
        showToast(`Задача #${id} → ${STATUS_LABELS[newStatus]}`, 'success');
        await refresh();
        return;
    }

    if (action === 'edit') {
        openEditModal(task);
        return;
    }

    if (action === 'delete') {
        if (!confirm(`Удалить задачу #${id} "${task['название']}"?`)) return;
        await api('удалить_задачу', [id]);
        showToast(`Задача #${id} удалена`, 'success');
        await refresh();
    }
}

function openModal(title = 'Новая задача', submitLabel = 'Создать') {
    $('modal-title').textContent = title;
    $('btn-submit').textContent = submitLabel;
    $('modal-overlay').classList.add('active');
    setTimeout(() => $('input-title').focus(), 100);
}

function closeModal() {
    $('modal-overlay').classList.remove('active');
    $('task-form').reset();
    $('edit-id').value = '';
    $('input-priority').value = 'средний';
}

function openEditModal(task) {
    $('edit-id').value = task.id;
    $('input-title').value = task['название'];
    $('input-desc').value = task['описание'] || '';
    $('input-priority').value = task['приоритет'];
    openModal('Редактировать задачу', 'Сохранить');
}

async function handleSubmit(e) {
    e.preventDefault();
    const editId = $('edit-id').value;
    const title = $('input-title').value.trim();
    const desc = $('input-desc').value.trim();
    const priority = $('input-priority').value;

    if (!title) {
        showToast('Введите название задачи', 'error');
        return;
    }

    if (editId) {
        const res = await api('обновить_задачу', [editId, title, desc, priority]);
        if (res['ок'] !== '1') {
            showToast(res['ошибка'] || 'Ошибка обновления', 'error');
            return;
        }
        showToast(`Задача #${editId} обновлена`, 'success');
    } else {
        const res = await api('создать_задачу', [title, desc, priority]);
        if (res['ок'] !== '1') {
            showToast(res['ошибка'] || 'Ошибка создания', 'error');
            return;
        }
        showToast(`Задача #${res.id} создана`, 'success');
    }

    closeModal();
    await refresh();
}

function renderBoard(tasks) {
    const groups = { 'новая': [], 'в_работе': [], 'готово': [] };
    for (const task of tasks) {
        const status = task['статус'];
        if (groups[status]) groups[status].push(task);
    }

    for (const [status, items] of Object.entries(groups)) {
        const container = $(COLUMN_MAP[status]);
        container.innerHTML = '';
        for (const task of items) {
            container.appendChild(createCard(task));
        }
    }
}

function renderStats(stats) {
    const el = $('stats');
    el.innerHTML = `
        <div class="stat"><span>Всего:</span> <span class="stat__value">${stats['всего'] || 0}</span></div>
        <div class="stat"><span>Новые:</span> <span class="stat__value">${stats['новые'] || 0}</span></div>
        <div class="stat"><span>В работе:</span> <span class="stat__value">${stats['в_работе'] || 0}</span></div>
        <div class="stat"><span>Готово:</span> <span class="stat__value">${stats['готово'] || 0}</span></div>
    `;

    $(COUNT_MAP['новая']).textContent = stats['новые'] || 0;
    $(COUNT_MAP['в_работе']).textContent = stats['в_работе'] || 0;
    $(COUNT_MAP['готово']).textContent = stats['готово'] || 0;
}

function setConnectionStatus(online) {
    const el = $('connection-status');
    const dot = el.querySelector('.dot');
    const label = el.querySelector('span:last-child');
    dot.className = online ? 'dot dot--online' : 'dot dot--offline';
    label.textContent = online ? 'Подключено' : 'Нет связи';
}

async function refresh() {
    try {
        const [tasks, stats] = await Promise.all([
            api('все_задачи'),
            api('статистика'),
        ]);
        renderBoard(tasks);
        renderStats(stats);
        setConnectionStatus(true);
    } catch (err) {
        console.error('Refresh failed:', err);
        setConnectionStatus(false);
        showToast('Не удалось загрузить данные. Запущен ли сервер?', 'error');
    }
}

$('btn-add').addEventListener('click', () => openModal());
$('btn-modal-close').addEventListener('click', closeModal);
$('btn-cancel').addEventListener('click', closeModal);
$('task-form').addEventListener('submit', handleSubmit);
$('modal-overlay').addEventListener('click', (e) => {
    if (e.target === $('modal-overlay')) closeModal();
});
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') closeModal();
});

refresh();
