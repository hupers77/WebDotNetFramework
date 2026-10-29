// 게시글 보기 (BRD_*): 첨부 목록(받기만), 삭제, 조회자 목록, 댓글(등록·수정·삭제).
// 글 내용은 서버가 거른 HTML을 그대로 그린 것이고, 이 스크립트는 글자만 textContent로 넣는다.
import { fw } from '../fw.js';
import { FileBox } from '../fw-file.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#brd-view');
const d = root.dataset;
const postId = Number(d.postId);
const canComment = d.canComment === 'Y';

const time = root.querySelector('time[data-fw-datetime]');
if (time) {
    time.textContent = fw.format.dateTime(time.getAttribute('datetime')) || time.textContent;
}

if (d.fileGroupId) {
    const files = new FileBox($('#brd-files'), { programId: d.program, fileGroupId: Number(d.fileGroupId), editable: false });
    await files.ready;
}

$('#brd-delete')?.addEventListener('click', async () => {
    if (!(await fw.dialog.confirm(fw.t('CNF.DELETE')))) {
        return;
    }

    const result = await fw.ajax(d.urlDelete, { method: 'POST', body: { postId, rowVer: d.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    window.location.assign(d.urlList);
});

$('#brd-readers')?.addEventListener('click', async () => {
    const result = await fw.ajax(`${d.urlReaders}?id=${postId}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    const rows = result.data.map((r) => {
        const tr = document.createElement('tr');
        for (const text of [`${r.userNm} (${r.userId})`, r.companyCd, fw.format.dateTime(r.firstDt), r.readCnt]) {
            const td = document.createElement('td');
            td.textContent = text;
            tr.append(td);
        }

        return tr;
    });
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 4;
        td.className = 'grid-empty';
        td.textContent = fw.t('BRD.READERS_NONE');
        tr.append(td);
        rows.push(tr);
    }

    $('#brd-readers-body').replaceChildren(...rows);
    $('#brd-readers-panel').hidden = false;
    $('#brd-readers-heading').focus();
});

// ---- 댓글 ----
const list = $('#brd-comment-list');
let editing = null; // 고치는 중인 댓글

function commentItem(c) {
    const li = document.createElement('li');
    const meta = document.createElement('div');
    meta.className = 'comment-meta';
    const who = document.createElement('strong');
    who.textContent = c.writerNm ?? fw.t('BRD.ANONYMOUS');
    const when = document.createElement('span');
    when.textContent = fw.format.dateTime(c.regDt);
    meta.append(who, when);
    if (canComment && c.mineYn === 'Y') {
        meta.append(actionButton(fw.t('BRD.EDIT'), () => startEdit(c)));
    }

    if (c.mineYn === 'Y' || d.canDeleteAny === 'Y') {
        meta.append(actionButton(fw.t('BTN.DELETE'), () => removeComment(c)));
    }

    const text = document.createElement('p');
    text.className = 'comment-text';
    text.textContent = c.content;
    const avatar = document.createElement('span');
    avatar.className = 'avatar';
    avatar.setAttribute('aria-hidden', 'true');
    avatar.textContent = (c.writerNm ?? '?').slice(0, 1);
    li.append(avatar, meta, text);
    return li;
}

function actionButton(label, handler) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'link-button';
    b.textContent = label;
    b.addEventListener('click', handler);
    return b;
}

async function loadComments() {
    if (!list) {
        return;
    }

    const result = await fw.ajax(`${d.urlComments}?id=${postId}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    list.replaceChildren(...result.data.map(commentItem));
}

function startEdit(c) {
    editing = c;
    const box = $('#brd-comment-text');
    box.value = c.content;
    box.focus();
}

async function removeComment(c) {
    if (!(await fw.dialog.confirm(fw.t('CNF.DELETE')))) {
        return;
    }

    const result = await fw.ajax(d.urlCommentDelete, { method: 'POST', body: { commentId: c.commentId, postId, rowVer: c.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    await loadComments();
}

$('#brd-comment-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const box = $('#brd-comment-text');
    const content = box.value.trim();
    if (!content) {
        box.focus();
        return;
    }

    const body = { postId, content, commentId: editing?.commentId ?? null, rowVer: editing?.rowVer ?? null };
    const result = await fw.ajax(d.urlCommentSave, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    editing = null;
    box.value = '';
    await loadComments();
});

await loadComments();
