// 공지 팝업 관리 (SYS_POPUP_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽 목록에서 고르면 오른쪽에서 공개 대상·기간·사용 여부와 언어별 제목·내용을 고친다. 기본 언어는 반드시 입력해야 하고,
// 다른 언어를 비워 두면 그 언어 사용자에게는 기본 언어 문구가 보인다. 내용은 글자만 보여 주므로 HTML을 쓰지 않는다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#popup-screen');
const langs = JSON.parse(root.dataset.langs);
const urls = { list: root.dataset.urlList, create: root.dataset.urlCreate, save: root.dataset.urlSave, delete: root.dataset.urlDelete };
const buttons = { add: $('#popup-new'), create: $('#popup-create'), save: $('#popup-save'), delete: $('#popup-delete') };
const list = $('#popup-list');
const detail = $('#popup-detail');
const inputs = { scope: $('#popup-scope'), start: $('#popup-start'), end: $('#popup-end'), use: $('#popup-use') };
const scopeText = { ALL: fw.t('SYS.POPUP.SCOPE_ALL'), EMP: fw.t('SYS.POPUP.SCOPE_EMP'), PARTNER: fw.t('SYS.POPUP.SCOPE_PARTNER') };

// 언어별 입력 칸을 한 번만 만든다
const langInputs = new Map();
for (const lang of langs) {
    const fieldset = document.createElement('fieldset');
    const legend = document.createElement('legend');
    legend.textContent = lang.langNm;
    const titleLabel = document.createElement('label');
    titleLabel.htmlFor = `popup-title-${lang.langCd}`;
    titleLabel.textContent = fw.t('SYS.POPUP.TITLE');
    const title = document.createElement('input');
    title.id = `popup-title-${lang.langCd}`;
    title.maxLength = 100;
    title.autocomplete = 'off';
    const contentLabel = document.createElement('label');
    contentLabel.htmlFor = `popup-content-${lang.langCd}`;
    contentLabel.textContent = fw.t('SYS.POPUP.CONTENT');
    const content = document.createElement('textarea');
    content.id = `popup-content-${lang.langCd}`;
    content.rows = 5;
    content.maxLength = 2000;
    fieldset.append(legend, titleLabel, title, contentLabel, content);
    $('#popup-langs').append(fieldset);
    langInputs.set(lang.langCd, { title, content, isDefault: lang.isDefault });
}

let page = { items: [], pageNo: 1, totalPages: 0 };
let current = null; // 고른 팝업. 신규 입력 중이면 { isNew: true }

function renderList() {
    const items = page.items.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.popupId = r.popupId;
        li.classList.toggle('unused', r.useYn !== 'Y');
        li.setAttribute('aria-selected', current?.popupId === r.popupId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.title;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = [`${r.startDt} ~ ${r.endDt}`, scopeText[r.openScopeCd], r.useYn === 'Y' ? '' : fw.t('LBL.NOT_IN_USE')].filter(Boolean).join(' · ');
        li.append(name, meta);
        return li;
    });
    list.replaceChildren(...items);
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }

    $('#popup-pager').hidden = page.totalPages <= 1;
    $('#popup-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#popup-prev').disabled = page.pageNo <= 1;
    $('#popup-next').disabled = page.pageNo >= page.totalPages;
}

function applyLocks() {
    const isNew = !!current?.isNew;
    const canWrite = isNew ? !!buttons.create : !!buttons.save;
    for (const input of Object.values(inputs)) {
        input.disabled = !canWrite;
    }

    for (const { title, content } of langInputs.values()) {
        title.disabled = content.disabled = !canWrite;
    }

    if (buttons.create) {
        buttons.create.hidden = !isNew;
    }

    if (buttons.save) {
        buttons.save.hidden = isNew;
    }

    if (buttons.delete) {
        buttons.delete.hidden = isNew;
    }
}

function showDetail(row) {
    current = row;
    renderList();
    $('#popup-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    inputs.scope.value = row.openScopeCd ?? 'ALL';
    inputs.start.value = row.startDt ?? '';
    inputs.end.value = row.endDt ?? '';
    inputs.use.checked = (row.useYn ?? 'Y') === 'Y';
    for (const [langCd, { title, content }] of langInputs) {
        const text = (row.langs ?? []).find((l) => l.langCd === langCd);
        title.value = text?.title ?? '';
        content.value = text?.content ?? '';
    }

    applyLocks();
}

async function load(pageNo = 1, keepId = null) {
    const query = new URLSearchParams({ keyword: $('#popup-keyword').value.trim(), useYn: $('#popup-use-filter').value, pageNo });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    page = result.data;
    $('#popup-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    showDetail(page.items.find((r) => r.popupId === keepId) ?? null);
}

function startNew() {
    const today = new Date().toISOString().slice(0, 10);
    showDetail({ isNew: true, openScopeCd: 'ALL', startDt: today, endDt: today, useYn: 'Y', langs: [] });
    langInputs.values().next().value.title.focus();
}

function invalid(input) {
    fw.notify(fw.t('MSG.INVALID_INPUT'));
    input.focus();
}

async function save() {
    const isNew = !!current.isNew;
    if (!inputs.start.value || !inputs.end.value || inputs.start.value > inputs.end.value) {
        invalid(inputs.start.value && inputs.end.value ? inputs.end : inputs.start);
        return;
    }

    const texts = [];
    for (const [langCd, { title, content, isDefault }] of langInputs) {
        const t = title.value.trim();
        const c = content.value.trim();
        if (!t && !c && !isDefault) {
            continue;
        }

        if (!t || !c) {
            invalid(t ? content : title);
            return;
        }

        texts.push({ langCd, title: t, content: c });
    }

    const body = {
        popupId: isNew ? null : current.popupId,
        rowVer: isNew ? null : current.rowVer,
        openScopeCd: inputs.scope.value,
        startDt: inputs.start.value,
        endDt: inputs.end.value,
        useYn: inputs.use.checked ? 'Y' : 'N',
        langs: texts,
    };
    const result = await fw.ajax(isNew ? urls.create : urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    if (isNew) {
        $('#popup-keyword').value = '';
        $('#popup-use-filter').value = '';
    }

    await load(isNew ? 1 : page.pageNo, result.data.popupId);
}

async function remove() {
    if (!(await fw.dialog.confirm(fw.t('CNF.DELETE')))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { popupId: current.popupId, rowVer: current.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load(page.pageNo);
}

$('#popup-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load(1);
});

$('#popup-form').addEventListener('submit', (e) => {
    e.preventDefault();
    (current?.isNew ? buttons.create : buttons.save)?.click();
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-popup-id]');
    if (li) {
        showDetail(page.items.find((r) => String(r.popupId) === li.dataset.popupId));
    }
});

list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-popup-id]');
    if (!li) {
        return;
    }

    const next = e.key === 'ArrowDown' ? li.nextElementSibling : e.key === 'ArrowUp' ? li.previousElementSibling : null;
    if (next) {
        e.preventDefault();
        li.tabIndex = -1;
        next.tabIndex = 0;
        next.focus();
    } else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        showDetail(page.items.find((r) => String(r.popupId) === li.dataset.popupId));
        list.querySelector(`[data-popup-id="${CSS.escape(li.dataset.popupId)}"]`)?.focus();
    }
});

$('#popup-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#popup-next').addEventListener('click', () => load(page.pageNo + 1));
buttons.add?.addEventListener('click', startNew);
buttons.create?.addEventListener('click', save);
buttons.save?.addEventListener('click', save);
buttons.delete?.addEventListener('click', remove);

await load();
