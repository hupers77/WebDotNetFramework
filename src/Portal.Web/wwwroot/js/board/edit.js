// 게시글 쓰기·수정·답글 (BRD_*): 경량 편집기(contenteditable)와 첨부. 편집기의 HTML은 서버가 화이트리스트로 다시 거른다.
// 붙여넣기는 글자만 받는다(서식·스크립트가 섞여 들어오지 않게). 저장하면 글 보기로 이동한다.
import { fw } from '../fw.js';
import { FileBox } from '../fw-file.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#brd-edit');
const d = root.dataset;
const editor = $('#brd-content');
const error = $('#brd-error');

let files = null;
if (d.attach === 'Y') {
    files = new FileBox($('#brd-files'), { programId: d.program, fileGroupId: d.fileGroupId ? Number(d.fileGroupId) : null, editable: true });
    await files.ready;
}

document.querySelectorAll('.editor-toolbar button[data-cmd]').forEach((button) => {
    button.addEventListener('click', async () => {
        editor.focus();
        const cmd = button.dataset.cmd;
        if (cmd === 'link') {
            const url = await fw.dialog.prompt(fw.t('BRD.ED_LINK_PROMPT'), { label: 'URL', maxLength: 500 });
            if (url) {
                document.execCommand('createLink', false, url);
            }

            return;
        }

        document.execCommand(cmd, false);
    });
});

editor.addEventListener('paste', (e) => {
    e.preventDefault();
    const text = e.clipboardData?.getData('text/plain') ?? '';
    document.execCommand('insertText', false, text);
});

function showError(message) {
    error.textContent = message;
    error.hidden = !message;
}

const checked = (selector) => $(selector)?.checked ? 'Y' : 'N';

$('#brd-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    showError('');
    const title = $('#brd-title').value.trim();
    const content = editor.innerHTML;
    if (!title || !editor.textContent.trim()) {
        showError(fw.t('MSG.INVALID_INPUT'));
        (title ? editor : $('#brd-title')).focus();
        return;
    }

    const body = {
        postId: d.postId ? Number(d.postId) : null,
        rowVer: d.rowVer || null,
        parentPostId: d.parentId ? Number(d.parentId) : null,
        title,
        content,
        secretYn: checked('#brd-secret'),
        pinYn: checked('#brd-pin'),
        startDt: $('#brd-start')?.value || null,
        endDt: $('#brd-end')?.value || null,
        fileGroupId: files?.getFileGroupId() ?? null,
        targets: $('#brd-targets') ? [...document.querySelectorAll('#brd-targets input[name="target"]:checked')].map((c) => c.value) : null,
    };
    const result = await fw.ajax(d.urlSave, { method: 'POST', body });
    if (!result.success) {
        showError(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    window.location.assign(`${d.urlView}${result.data}`);
});
