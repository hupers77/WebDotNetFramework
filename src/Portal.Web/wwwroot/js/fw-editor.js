// fw-editor: 글 편집기 표준 부품 (게시판 글쓰기 등). 외부 편집기 라이브러리 없이 contenteditable과 브라우저 편집 명령을 쓴다.
// - 문단 형식(본문·제목·인용·코드), 글자 크기, 굵게·기울임·밑줄·취소선, 글자 색·배경 색, 정렬, 목록, 링크, 사진, 표, 구분선, 서식 지우기.
// - CSP가 style 속성을 막으므로 서식은 정해진 클래스(.fs-*, .fc-*, .bg-*, .ta-*, .img-*)로만 저장한다. 서버 HTML 필터(HtmlSanitizer)도 같은 이름만 남긴다.
//   편집하는 동안은 브라우저가 만드는 <font size|color|face>를 그대로 두고(CSS가 같은 모양으로 보여 준다) getHtml()이 클래스로 바꾼다.
//   크기는 fontSize, 글자 색은 foreColor, 배경 색은 fontName(글꼴은 쓰지 않으므로)으로 서로 섞이지 않게 나눈다.
// - 사진은 첨부 공통 API(/File/Upload)로 본문 사진 묶음에 올리고 /File/Image/{id}로 넣는다. 버튼, 끌어 놓기, 붙여넣기(캡처 화면) 모두 된다.
//   큰 사진은 올리기 전에 가로 MAX_IMAGE_WIDTH로 줄인다. 저장 요청에 getImageGroupId()를 담으면 저장 SP가 묶음을 확정한다.
// - 붙여넣은 HTML(엑셀 표, 워드 문서 등)은 허용 태그만 남기고 속성을 지운다(서버가 다시 거른다).
import { fw } from './fw.js';

const MAX_IMAGE_WIDTH = 1920;
const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif'];

// 글자 크기: <font size> 값 → 클래스 (3은 보통)
const SIZES = [
    { value: '1', cls: 'fs-xs', key: 'ED.SIZE_XS' },
    { value: '2', cls: 'fs-sm', key: 'ED.SIZE_SM' },
    { value: '3', cls: '', key: 'ED.SIZE_MD' },
    { value: '4', cls: 'fs-lg', key: 'ED.SIZE_LG' },
    { value: '5', cls: 'fs-xl', key: 'ED.SIZE_XL' },
    { value: '6', cls: 'fs-xxl', key: 'ED.SIZE_XXL' },
];

// 글자 색: foreColor 값 → 클래스 (portal.css의 .fc-* 와 같은 색)
const TEXT_COLORS = [
    { hex: '#dc2626', cls: 'fc-red', key: 'ED.COLOR_RED' },
    { hex: '#ea580c', cls: 'fc-orange', key: 'ED.COLOR_ORANGE' },
    { hex: '#16a34a', cls: 'fc-green', key: 'ED.COLOR_GREEN' },
    { hex: '#2563eb', cls: 'fc-blue', key: 'ED.COLOR_BLUE' },
    { hex: '#7c3aed', cls: 'fc-purple', key: 'ED.COLOR_PURPLE' },
    { hex: '#6b7280', cls: 'fc-gray', key: 'ED.COLOR_GRAY' },
];

// 배경 색: fontName 표시값(fw-bg-*) → 클래스
const BACK_COLORS = [
    { hex: '#fef08a', cls: 'bg-yellow', key: 'ED.COLOR_YELLOW' },
    { hex: '#bbf7d0', cls: 'bg-green', key: 'ED.COLOR_GREEN' },
    { hex: '#bfdbfe', cls: 'bg-blue', key: 'ED.COLOR_BLUE' },
    { hex: '#fbcfe8', cls: 'bg-pink', key: 'ED.COLOR_PINK' },
    { hex: '#e5e7eb', cls: 'bg-gray', key: 'ED.COLOR_GRAY' },
];

const BLOCKS = [
    { tag: 'p', key: 'ED.BLOCK_P' },
    { tag: 'h2', key: 'ED.BLOCK_H2' },
    { tag: 'h3', key: 'ED.BLOCK_H3' },
    { tag: 'blockquote', key: 'ED.BLOCK_QUOTE' },
    { tag: 'pre', key: 'ED.BLOCK_PRE' },
];

const ALIGNS = [
    { cls: 'ta-left', icon: 'align-left', key: 'ED.ALIGN_LEFT' },
    { cls: 'ta-center', icon: 'align-center', key: 'ED.ALIGN_CENTER' },
    { cls: 'ta-right', icon: 'align-right', key: 'ED.ALIGN_RIGHT' },
    { cls: 'ta-justify', icon: 'align-justify', key: 'ED.ALIGN_JUSTIFY' },
];

const IMAGE_SIZES = [
    { cls: 'img-sm', key: 'ED.IMG_SM' },
    { cls: 'img-md', key: 'ED.IMG_MD' },
    { cls: 'img-lg', key: 'ED.IMG_LG' },
    { cls: 'img-full', key: 'ED.IMG_FULL' },
    { cls: '', key: 'ED.IMG_ORIGINAL' },
];

const BLOCK_SELECTOR = 'p,h1,h2,h3,h4,li,blockquote,pre,div,td,th';
const CLASS_GROUPS = ['fs-', 'fc-', 'bg-', 'ta-', 'img-'];
const NESTED_BLOCKS = 'ul,ol,table,h1,h2,h3,h4,blockquote,pre,p,hr';

/**
 * 브라우저 편집 명령이 남기는 잘못된 구조를 바로잡는다.
 * - 빈 줄에서 목록을 시작하면 Chromium이 &lt;p&gt; 안에 &lt;ul&gt;을 넣는다: 문단을 풀어 목록을 꺼낸다.
 * - 처음 친 글자는 문단 없이 편집 영역에 바로 붙는다: 문단으로 감싼다.
 * 글자 노드 자체는 그대로 옮기므로 커서(노드·위치)를 다시 놓을 수 있다. 바꾼 것이 있으면 true.
 */
function tidy(root) {
    let changed = false;
    for (const p of [...root.querySelectorAll('p')].reverse()) {
        if ([...p.children].some((c) => c.matches(NESTED_BLOCKS))) {
            const parts = [];
            let loose = null;
            for (const node of [...p.childNodes]) {
                if (node.nodeType === Node.ELEMENT_NODE && node.matches(NESTED_BLOCKS)) {
                    loose = null;
                    parts.push(node);
                } else if (node.nodeType === Node.TEXT_NODE && !node.data.trim() && !loose) {
                    node.remove();
                } else {
                    if (!loose) {
                        loose = document.createElement('p');
                        if (p.className) {
                            loose.className = p.className;
                        }

                        parts.push(loose);
                    }

                    loose.append(node);
                }
            }

            p.replaceWith(...parts);
            changed = true;
        }
    }

    let loose = null;
    for (const node of [...root.childNodes]) {
        const inline = node.nodeType === Node.TEXT_NODE ? node.data.length > 0
            : node.nodeType === Node.ELEMENT_NODE && !node.matches(`${NESTED_BLOCKS},div`);
        if (!inline) {
            loose = null;
            continue;
        }

        if (!loose) {
            loose = document.createElement('p');
            node.before(loose);
        }

        loose.append(node);
        changed = true;
    }

    return changed;
}

// 붙여넣기에서 남길 태그 (서버 HtmlSanitizer 허용 목록 안에서)
const PASTE_KEEP = new Set(['P', 'BR', 'B', 'STRONG', 'I', 'EM', 'U', 'S', 'STRIKE', 'SUB', 'SUP', 'UL', 'OL', 'LI', 'H1', 'H2', 'H3', 'H4',
    'BLOCKQUOTE', 'PRE', 'CODE', 'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TH', 'TD', 'CAPTION', 'A', 'HR']);
const PASTE_DROP = new Set(['SCRIPT', 'STYLE', 'META', 'LINK', 'TITLE', 'HEAD', 'IFRAME', 'OBJECT', 'EMBED', 'SVG', 'MATH', 'TEMPLATE', 'NOSCRIPT', 'IMG', 'XML']);

const safeHref = (url) => /^(https?:|mailto:)/i.test(url) || (url.startsWith('/') && !url.startsWith('//')) ? url : `https://${url}`;

/**
 * 글 편집기.
 * @example
 * const editor = new RichEditor(document.getElementById('brd-content'), {
 *   programId: 'BRD_NOTICE', imageGroupId: post.imageGroupId,
 *   onOtherFiles: (files) => attachments.add(files),   // 사진이 아닌 파일을 끌어 놓았을 때 (선택)
 * });
 * save({ content: editor.getHtml(), imageGroupId: editor.getImageGroupId() });
 */
export class RichEditor {
    /**
     * @param {HTMLElement} area contenteditable 편집 영역 (class="editor")
     * @param {object} options
     * @param {string} options.programId 사진 권한 기준 프로그램
     * @param {number|null} [options.imageGroupId] 기존 본문 사진 묶음
     * @param {boolean} [options.images=true] false면 사진 기능을 숨긴다
     * @param {(files: File[]) => void} [options.onOtherFiles] 사진이 아닌 파일을 끌어 놓거나 붙여 넣었을 때
     */
    constructor(area, { programId, imageGroupId = null, images = true, onOtherFiles } = {}) {
        this.area = area;
        this.programId = programId;
        this.imageGroupId = imageGroupId;
        this.images = images && !!programId;
        this.onOtherFiles = onOtherFiles;
        this.maxMb = Number(fw.meta('fw-file-max-mb')) || 0;
        this.maxCount = Number(fw.meta('fw-file-max-count')) || 0;
        this.savedRange = null;
        this.selectedImage = null;
        this.build();
        this.bind();
    }

    /** 저장할 HTML. 편집용 표시(<font>)를 서식 클래스로 바꾸고 빈 span·편집 표시 클래스를 없앤다. 원본은 바꾸지 않는다. */
    getHtml() {
        const root = this.area.cloneNode(true);
        tidy(root);
        root.querySelectorAll('.ed-selected').forEach((el) => el.classList.remove('ed-selected'));
        root.querySelectorAll('font').forEach((font) => {
            const span = document.createElement('span');
            const size = SIZES.find((s) => s.value === font.getAttribute('size'))?.cls;
            const color = TEXT_COLORS.find((c) => c.hex === (font.getAttribute('color') ?? '').toLowerCase())?.cls;
            const back = BACK_COLORS.find((c) => `fw-${c.cls}` === font.getAttribute('face'))?.cls;
            [size, color, back].filter(Boolean).forEach((cls) => span.classList.add(cls));
            span.append(...font.childNodes);
            font.replaceWith(span);
        });
        root.querySelectorAll('[style]').forEach((el) => el.removeAttribute('style'));
        root.querySelectorAll('span').forEach((span) => {
            if (!span.className) {
                span.replaceWith(...span.childNodes);
            }
        });
        root.querySelectorAll('[class=""]').forEach((el) => el.removeAttribute('class'));
        return root.innerHTML.trim();
    }

    /** 저장 요청에 담을 본문 사진 묶음 ID. 사진을 올린 적이 없으면 null. */
    getImageGroupId() {
        return this.imageGroupId;
    }

    /** 글자도 사진도 없으면 true. */
    isEmpty() {
        return !this.area.textContent.trim() && !this.area.querySelector('img,hr,table');
    }

    focus() {
        this.area.focus();
    }

    // ---------- 화면 ----------
    build() {
        const wrap = document.createElement('div');
        wrap.className = 'fw-editor-wrap';
        const box = document.createElement('div');
        box.className = 'fw-editor';
        this.area.before(wrap);
        wrap.append(box);

        const bar = document.createElement('div');
        bar.className = 'editor-toolbar';
        bar.setAttribute('role', 'toolbar');
        bar.setAttribute('aria-label', fw.t('ED.TOOLBAR'));
        if (this.area.id) {
            bar.setAttribute('aria-controls', this.area.id);
        }

        this.buttons = {};
        const sep = () => {
            const s = document.createElement('span');
            s.className = 'ed-sep';
            s.setAttribute('aria-hidden', 'true');
            return s;
        };

        bar.append(
            this.button('undo', 'ED.UNDO', () => this.exec('undo')),
            this.button('redo', 'ED.REDO', () => this.exec('redo')),
            sep(),
            this.blockSelect(),
            this.sizeSelect(),
            sep(),
            this.button('bold', 'ED.BOLD', () => this.exec('bold'), true),
            this.button('italic', 'ED.ITALIC', () => this.exec('italic'), true),
            this.button('underline', 'ED.UNDERLINE', () => this.exec('underline'), true),
            this.button('strike', 'ED.STRIKE', () => this.exec('strikeThrough'), true),
            this.colorPicker('text-color', 'ED.COLOR', TEXT_COLORS, (c) => this.exec('foreColor', c ? c.hex : '#000001')),
            this.colorPicker('highlight', 'ED.HIGHLIGHT', BACK_COLORS, (c) => this.exec('fontName', c ? `fw-${c.cls}` : 'fw-bg-none')),
            sep(),
            ...ALIGNS.map((a) => this.button(a.icon, a.key, () => this.align(a.cls), true)),
            sep(),
            this.button('list', 'ED.UL', () => this.exec('insertUnorderedList'), true),
            this.button('list-ol', 'ED.OL', () => this.exec('insertOrderedList'), true),
            sep(),
            this.button('chain', 'ED.LINK', () => this.link()),
        );
        if (this.images) {
            this.fileInput = document.createElement('input');
            this.fileInput.type = 'file';
            this.fileInput.accept = IMAGE_TYPES.join(',');
            this.fileInput.multiple = true;
            this.fileInput.className = 'sr-only';
            this.fileInput.tabIndex = -1;
            this.fileInput.addEventListener('change', async () => {
                await this.insertImages([...this.fileInput.files]);
                this.fileInput.value = '';
            });
            bar.append(this.button('image', 'ED.IMAGE', () => {
                this.saveRange();
                this.fileInput.click();
            }), this.fileInput);
        }

        bar.append(
            this.button('table', 'ED.TABLE', () => this.insertTable()),
            this.button('hr', 'ED.HR', () => this.exec('insertHorizontalRule')),
            this.button('eraser', 'ED.CLEAR', () => this.clearFormat()),
        );

        this.tableTools = document.createElement('span');
        this.tableTools.className = 'ed-table-tools';
        this.tableTools.hidden = true;
        this.tableTools.append(sep(),
            this.textButton('ED.ROW_ADD', () => this.tableEdit('row-add')),
            this.textButton('ED.COL_ADD', () => this.tableEdit('col-add')),
            this.textButton('ED.ROW_DEL', () => this.tableEdit('row-del')),
            this.textButton('ED.COL_DEL', () => this.tableEdit('col-del')),
            this.textButton('ED.TABLE_DEL', () => this.tableEdit('table-del')));
        bar.append(this.tableTools);

        this.area.classList.add('editor');
        this.area.dataset.placeholder = fw.t('ED.PLACEHOLDER');
        this.status = document.createElement('p');
        this.status.className = 'ed-status';
        this.status.setAttribute('role', 'status');
        this.status.textContent = this.images ? fw.t('ED.IMAGE_HINT', this.maxCount || '-') : '';
        this.status.hidden = !this.images;
        box.append(bar, this.area, this.status);

        this.imageTools = document.createElement('div');
        this.imageTools.className = 'ed-image-tools';
        this.imageTools.hidden = true;
        this.imageTools.append(
            ...IMAGE_SIZES.map((s) => this.textButton(s.key, () => this.resizeImage(s.cls))),
            this.textButton('ED.IMG_DEL', () => this.removeImage()));
        wrap.append(this.imageTools);
        this.wrap = wrap;
        this.bar = bar;
    }

    button(icon, key, action, toggle = false) {
        const b = document.createElement('button');
        b.type = 'button';
        b.title = fw.t(key);
        b.setAttribute('aria-label', fw.t(key));
        if (toggle) {
            b.setAttribute('aria-pressed', 'false');
        }

        b.append(fw.icon(icon));
        b.addEventListener('mousedown', (e) => e.preventDefault());   // 편집 영역의 선택을 잃지 않게
        b.addEventListener('click', () => {
            action();
            this.refreshState();
        });
        this.buttons[key] = b;
        return b;
    }

    textButton(key, action) {
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = fw.t(key);
        b.addEventListener('mousedown', (e) => e.preventDefault());
        b.addEventListener('click', () => {
            action();
            this.refreshState();
        });
        return b;
    }

    blockSelect() {
        const select = document.createElement('select');
        select.setAttribute('aria-label', fw.t('ED.BLOCK'));
        select.title = fw.t('ED.BLOCK');
        for (const b of BLOCKS) {
            select.append(new Option(fw.t(b.key), b.tag));
        }

        select.addEventListener('change', () => {
            this.restoreRange();
            this.exec('formatBlock', select.value);
        });
        select.addEventListener('mousedown', () => this.saveRange());
        this.blockSelectEl = select;
        return select;
    }

    sizeSelect() {
        const select = document.createElement('select');
        select.setAttribute('aria-label', fw.t('ED.SIZE'));
        select.title = fw.t('ED.SIZE');
        for (const s of SIZES) {
            select.append(new Option(fw.t(s.key), s.value));
        }

        select.value = '3';
        select.addEventListener('change', () => {
            this.restoreRange();
            this.exec('fontSize', select.value);
        });
        select.addEventListener('mousedown', () => this.saveRange());
        this.sizeSelectEl = select;
        return select;
    }

    colorPicker(icon, key, colors, apply) {
        const holder = document.createElement('span');
        holder.className = 'ed-color';
        const trigger = this.button(icon, key, () => {
            palette.hidden = !palette.hidden;
            trigger.setAttribute('aria-expanded', palette.hidden ? 'false' : 'true');
            if (!palette.hidden) {
                palette.querySelector('button')?.focus();
            }
        });
        trigger.setAttribute('aria-haspopup', 'true');
        trigger.setAttribute('aria-expanded', 'false');
        const bar = document.createElement('span');
        bar.className = `ed-swatch-bar ${colors[0].cls}`;
        bar.setAttribute('aria-hidden', 'true');
        trigger.append(bar);

        const palette = document.createElement('div');
        palette.className = 'ed-palette';
        palette.hidden = true;
        const pick = (color) => {
            palette.hidden = true;
            trigger.setAttribute('aria-expanded', 'false');
            this.area.focus();
            this.restoreRange();
            apply(color);
            if (color) {
                bar.className = `ed-swatch-bar ${color.cls}`;
            }
        };
        for (const color of colors) {
            const swatch = document.createElement('button');
            swatch.type = 'button';
            swatch.className = color.cls;
            swatch.title = fw.t(color.key);
            swatch.setAttribute('aria-label', fw.t(color.key));
            swatch.append(fw.icon(icon));
            swatch.addEventListener('mousedown', (e) => e.preventDefault());
            swatch.addEventListener('click', () => pick(color));
            palette.append(swatch);
        }

        const none = document.createElement('button');
        none.type = 'button';
        none.className = 'ed-none';
        none.textContent = fw.t('ED.COLOR_NONE');
        none.addEventListener('mousedown', (e) => e.preventDefault());
        none.addEventListener('click', () => pick(null));
        palette.append(none);
        palette.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                palette.hidden = true;
                trigger.setAttribute('aria-expanded', 'false');
                trigger.focus();
            }
        });
        document.addEventListener('click', (e) => {
            if (!holder.contains(e.target)) {
                palette.hidden = true;
                trigger.setAttribute('aria-expanded', 'false');
            }
        });
        holder.append(trigger, palette);
        return holder;
    }

    // ---------- 편집 명령 ----------
    exec(command, value) {
        this.area.focus();
        document.execCommand('styleWithCSS', false, false);
        document.execCommand(command, false, value);
        this.tidy();
        this.refreshState();
    }

    /** {@link tidy}를 편집 영역에 적용하고 커서를 제자리에 둔다. */
    tidy() {
        const range = this.currentRange();
        const mark = range && [range.startContainer, range.startOffset, range.endContainer, range.endOffset];
        // 바꾼 것이 없으면 커서를 건드리지 않는다 (다시 놓으면 골라 둔 글자 크기 등 입력 모양이 풀린다)
        if (tidy(this.area) && mark && this.area.contains(mark[0]) && this.area.contains(mark[2])) {
            try {
                const r = document.createRange();
                r.setStart(mark[0], mark[1]);
                r.setEnd(mark[2], mark[3]);
                const sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(r);
            } catch {
                // 노드가 줄어 위치가 사라졌으면 브라우저가 둔 커서를 쓴다
            }
        }
    }

    saveRange() {
        const sel = window.getSelection();
        if (sel.rangeCount > 0 && this.area.contains(sel.getRangeAt(0).commonAncestorContainer)) {
            this.savedRange = sel.getRangeAt(0).cloneRange();
        }
    }

    restoreRange() {
        if (!this.savedRange) {
            return;
        }

        this.area.focus();
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(this.savedRange);
    }

    currentRange() {
        const sel = window.getSelection();
        return sel.rangeCount > 0 && this.area.contains(sel.getRangeAt(0).commonAncestorContainer) ? sel.getRangeAt(0) : null;
    }

    /** 선택 영역이 걸친 가장 안쪽 문단들. 문단 없이 글자만 있으면 먼저 문단으로 감싼다. */
    selectedBlocks() {
        let range = this.currentRange();
        if (!range) {
            return [];
        }

        const start = range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement;
        if (!start.closest(BLOCK_SELECTOR) || !this.area.contains(start.closest(BLOCK_SELECTOR))) {
            this.exec('formatBlock', 'p');
            range = this.currentRange();
            if (!range) {
                return [];
            }
        }

        const blocks = [...this.area.querySelectorAll(BLOCK_SELECTOR)].filter((b) => range.intersectsNode(b));
        return blocks.filter((b) => !blocks.some((other) => other !== b && b.contains(other)));
    }

    align(cls) {
        const blocks = this.selectedBlocks();
        const already = blocks.length > 0 && blocks.every((b) => b.classList.contains(cls));
        for (const block of blocks) {
            ALIGNS.forEach((a) => block.classList.remove(a.cls));
            if (!already && cls !== 'ta-left') {
                block.classList.add(cls);
            }

            if (!block.className) {
                block.removeAttribute('class');
            }
        }
    }

    clearFormat() {
        this.exec('removeFormat');
        const range = this.currentRange();
        if (!range) {
            return;
        }

        for (const el of this.area.querySelectorAll('span[class], font')) {
            if (range.intersectsNode(el)) {
                el.replaceWith(...el.childNodes);
            }
        }

        for (const block of this.selectedBlocks()) {
            ALIGNS.forEach((a) => block.classList.remove(a.cls));
            if (!block.className) {
                block.removeAttribute('class');
            }
        }
    }

    async link() {
        this.saveRange();
        const url = await fw.dialog.prompt(fw.t('ED.LINK_PROMPT'), { label: 'URL', maxLength: 500 });
        this.restoreRange();
        if (!url) {
            return;
        }

        const href = safeHref(url.trim());
        const range = this.currentRange();
        if (range && range.collapsed) {
            const a = document.createElement('a');
            a.href = href;
            a.textContent = url.trim();
            range.insertNode(a);
            range.setStartAfter(a);
            range.collapse(true);
        } else {
            this.exec('createLink', href);
        }
    }

    /** 3칸 표(머리글 1줄 + 2줄)를 지금 문단 다음에 넣는다. 브라우저 insertHTML은 thead를 버려서 직접 만든다. */
    insertTable() {
        const row = (tag) => {
            const tr = document.createElement('tr');
            for (let i = 0; i < 3; i++) {
                const cell = document.createElement(tag);
                cell.append(document.createElement('br'));
                tr.append(cell);
            }

            return tr;
        };
        const table = document.createElement('table');
        table.createTHead().append(row('th'));
        table.createTBody().append(row('td'), row('td'));
        const after = document.createElement('p');
        after.append(document.createElement('br'));

        const range = this.currentRange();
        let block = range?.startContainer;
        while (block && block.parentNode !== this.area) {
            block = block.parentNode;
        }

        if (block && block.nodeType === Node.ELEMENT_NODE && !block.textContent.trim() && !block.querySelector('img,table,hr')) {
            block.replaceWith(table, after);
        } else if (block) {
            block.after(table, after);
        } else {
            this.area.append(table, after);
        }

        const caret = document.createRange();
        caret.setStart(table.rows[0].cells[0], 0);
        caret.collapse(true);
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(caret);
        this.area.dispatchEvent(new Event('input', { bubbles: true }));
        this.refreshState();
    }

    tableEdit(action) {
        const range = this.currentRange();
        const cell = range && (range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement).closest('td,th');
        const table = cell?.closest('table');
        if (!cell || !this.area.contains(table)) {
            return;
        }

        const row = cell.parentElement;
        const index = [...row.children].indexOf(cell);
        const rows = [...table.rows];
        const blank = (tag) => {
            const c = document.createElement(tag);
            c.append(document.createElement('br'));
            return c;
        };
        switch (action) {
            case 'row-add': {
                const tr = document.createElement('tr');
                [...row.children].forEach(() => tr.append(blank('td')));
                if (row.parentElement.tagName === 'THEAD') {
                    (table.tBodies[0] ?? table.createTBody()).prepend(tr);
                } else {
                    row.after(tr);
                }

                break;
            }
            case 'col-add':
                rows.forEach((r) => {
                    const ref = r.children[index];
                    const c = blank(r.parentElement.tagName === 'THEAD' ? 'th' : 'td');
                    if (ref) {
                        ref.after(c);
                    } else {
                        r.append(c);
                    }
                });
                break;
            case 'row-del':
                row.remove();
                break;
            case 'col-del':
                rows.forEach((r) => r.children[index]?.remove());
                break;
            case 'table-del':
                table.remove();
                break;
            default:
                break;
        }

        if (table.isConnected && (table.rows.length === 0 || table.rows[0].children.length === 0)) {
            table.remove();
        }
    }

    // ---------- 사진 ----------
    async insertImages(files) {
        const images = files.filter((f) => IMAGE_TYPES.includes(f.type));
        const others = files.filter((f) => !f.type.startsWith('image/'));
        if (files.some((f) => f.type.startsWith('image/') && !IMAGE_TYPES.includes(f.type))) {
            this.say(fw.t('ED.IMAGE_TYPE'));
        }

        if (others.length > 0 && this.onOtherFiles) {
            this.onOtherFiles(others);
            this.say(fw.t('ED.FILE_TO_ATTACH'));
        }

        for (const original of images) {
            const file = await shrink(original);
            if (this.maxMb && file.size > this.maxMb * 1024 * 1024) {
                this.say(`${file.name}: ${fw.t('MSG.FILE_TOO_LARGE', this.maxMb)}`);
                continue;
            }

            this.say(fw.t('ED.IMAGE_UPLOADING', file.name));
            const query = new URLSearchParams({ programId: this.programId });
            if (this.imageGroupId) {
                query.set('fileGroupId', this.imageGroupId);
            }

            const result = await fw.upload(`/File/Upload?${query}`, file);
            if (!result.success) {
                this.say(`${file.name}: ${result.error?.message ?? fw.t('MSG.SYSTEM_ERROR')}`);
                continue;
            }

            this.imageGroupId = result.data.fileGroupId;
            const img = document.createElement('img');
            img.src = `/File/Image/${result.data.fileId}`;
            img.alt = file.name.replace(/\.[^.]+$/, '');
            this.restoreRange();
            const range = this.currentRange();
            if (range) {
                range.deleteContents();
                range.insertNode(img);
                range.setStartAfter(img);
                range.collapse(true);
                const sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
                this.savedRange = range.cloneRange();
            } else {
                const p = document.createElement('p');
                p.append(img);
                this.area.append(p);
            }

            this.say(fw.t('MSG.FILE_UPLOADED', file.name));
        }
    }

    selectImage(img) {
        this.area.querySelectorAll('img.ed-selected').forEach((i) => i.classList.remove('ed-selected'));
        this.selectedImage = img;
        this.imageTools.hidden = !img;
        if (!img) {
            return;
        }

        img.classList.add('ed-selected');
        const wrapBox = this.wrap.getBoundingClientRect();
        const box = img.getBoundingClientRect();
        this.imageTools.style.left = `${Math.max(0, box.left - wrapBox.left)}px`;
        this.imageTools.style.top = `${box.bottom - wrapBox.top + 6}px`;
    }

    resizeImage(cls) {
        const img = this.selectedImage;
        if (!img) {
            return;
        }

        IMAGE_SIZES.forEach((s) => s.cls && img.classList.remove(s.cls));
        if (cls) {
            img.classList.add(cls);
        }

        this.selectImage(img);
    }

    removeImage() {
        this.selectedImage?.remove();
        this.selectImage(null);
        this.area.focus();
    }

    say(message) {
        this.status.hidden = false;
        this.status.textContent = message;
    }

    // ---------- 상태·이벤트 ----------
    refreshState() {
        const range = this.currentRange();
        if (!range) {
            return;
        }

        for (const [key, command] of [['ED.BOLD', 'bold'], ['ED.ITALIC', 'italic'], ['ED.UNDERLINE', 'underline'], ['ED.STRIKE', 'strikeThrough'],
            ['ED.UL', 'insertUnorderedList'], ['ED.OL', 'insertOrderedList']]) {
            this.buttons[key]?.setAttribute('aria-pressed', document.queryCommandState(command) ? 'true' : 'false');
        }

        const node = range.startContainer.nodeType === Node.ELEMENT_NODE ? range.startContainer : range.startContainer.parentElement;
        const block = node.closest(BLOCK_SELECTOR);
        const inArea = block && this.area.contains(block) ? block : null;
        const tag = inArea?.closest('h2,h3,blockquote,pre')?.tagName.toLowerCase() ?? 'p';
        this.blockSelectEl.value = BLOCKS.some((b) => b.tag === tag) ? tag : 'p';
        const size = node.closest('font[size]');
        this.sizeSelectEl.value = size && this.area.contains(size) ? size.getAttribute('size') : '3';
        for (const a of ALIGNS) {
            const on = inArea ? inArea.classList.contains(a.cls) || (a.cls === 'ta-left' && !ALIGNS.some((x) => inArea.classList.contains(x.cls))) : a.cls === 'ta-left';
            this.buttons[a.key]?.setAttribute('aria-pressed', on ? 'true' : 'false');
        }

        this.tableTools.hidden = !node.closest('td,th') || !this.area.contains(node.closest('td,th'));
    }

    bind() {
        const area = this.area;
        document.addEventListener('selectionchange', () => {
            if (document.activeElement === area) {
                this.saveRange();
                this.refreshState();
            }
        });
        area.addEventListener('focus', () => {
            document.execCommand('defaultParagraphSeparator', false, 'p');
        });
        // 한글 등 조합 중에는 글자 노드를 옮기면 입력이 끊기므로 조합이 끝난 뒤에 정리한다
        area.addEventListener('input', (e) => {
            if (!e.isComposing) {
                this.tidy();
            }

            this.saveRange();
        });
        // selectionchange는 늦게 오므로 키를 뗄 때도 커서를 저장한다 (바로 이어 고르는 글 모양이 엉뚱한 곳에 걸리지 않게)
        area.addEventListener('keyup', () => this.saveRange());
        area.addEventListener('compositionend', () => this.tidy());
        area.addEventListener('click', (e) => {
            this.selectImage(e.target instanceof HTMLImageElement ? e.target : null);
        });
        area.addEventListener('keydown', (e) => {
            if (this.selectedImage && (e.key === 'Delete' || e.key === 'Backspace')) {
                e.preventDefault();
                this.removeImage();
            } else if (this.selectedImage) {
                this.selectImage(null);
            }
        });
        area.addEventListener('paste', (e) => {
            const data = e.clipboardData;
            if (!data) {
                return;
            }

            const files = [...data.files];
            if (files.length > 0) {
                e.preventDefault();
                if (this.images) {
                    this.saveRange();
                    this.insertImages(files);
                }

                return;
            }

            e.preventDefault();
            const html = data.getData('text/html');
            if (html) {
                this.exec('insertHTML', cleanPaste(html));
            } else {
                const text = data.getData('text/plain');
                this.exec('insertText', text);
            }
        });
        area.addEventListener('dragover', (e) => {
            if ([...(e.dataTransfer?.types ?? [])].includes('Files')) {
                e.preventDefault();
                area.classList.add('drop-over');
            }
        });
        area.addEventListener('dragleave', () => area.classList.remove('drop-over'));
        area.addEventListener('drop', (e) => {
            area.classList.remove('drop-over');
            const files = [...(e.dataTransfer?.files ?? [])];
            if (files.length === 0) {
                return;
            }

            e.preventDefault();
            const point = document.caretRangeFromPoint?.(e.clientX, e.clientY);
            if (point && area.contains(point.startContainer)) {
                this.savedRange = point;
            }

            if (this.images) {
                this.insertImages(files);
            } else {
                this.onOtherFiles?.(files);
            }
        });
    }
}

/** 붙여넣은 HTML에서 허용 태그만 남기고 속성은 링크 주소와 표 칸 합치기만 둔다. */
function cleanPaste(html) {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const walk = (node) => {
        for (const child of [...node.childNodes]) {
            if (child.nodeType === Node.COMMENT_NODE) {
                child.remove();
                continue;
            }

            if (child.nodeType !== Node.ELEMENT_NODE) {
                continue;
            }

            walk(child);
            if (PASTE_DROP.has(child.tagName)) {
                child.remove();
            } else if (!PASTE_KEEP.has(child.tagName)) {
                const block = /^(DIV|SECTION|ARTICLE|HEADER|FOOTER)$/.test(child.tagName);
                if (block) {
                    const p = doc.createElement('p');
                    p.append(...child.childNodes);
                    child.replaceWith(p);
                } else {
                    child.replaceWith(...child.childNodes);
                }
            } else {
                for (const attr of [...child.attributes]) {
                    const keep = (child.tagName === 'A' && attr.name === 'href') || ((child.tagName === 'TD' || child.tagName === 'TH') && /^(colspan|rowspan)$/.test(attr.name));
                    if (!keep) {
                        child.removeAttribute(attr.name);
                    }
                }

                if (child.tagName === 'A') {
                    const href = child.getAttribute('href') ?? '';
                    if (/^(https?:|mailto:)/i.test(href)) {
                        child.setAttribute('href', href);
                    } else {
                        child.removeAttribute('href');
                    }
                }
            }
        }
    };
    walk(doc.body);
    return doc.body.innerHTML;
}

/** 가로가 MAX_IMAGE_WIDTH보다 큰 jpg·png를 줄인다. gif(움직이는 그림)와 작은 사진은 그대로 둔다. */
async function shrink(file) {
    const name = /\.(png|jpe?g|gif)$/i.test(file.name) ? file.name : `image-${Date.now()}.${file.type === 'image/jpeg' ? 'jpg' : file.type.split('/')[1]}`;
    if (file.type === 'image/gif' || typeof createImageBitmap !== 'function') {
        return name === file.name ? file : new File([file], name, { type: file.type });
    }

    try {
        const bitmap = await createImageBitmap(file);
        if (bitmap.width <= MAX_IMAGE_WIDTH) {
            bitmap.close();
            return name === file.name ? file : new File([file], name, { type: file.type });
        }

        const canvas = document.createElement('canvas');
        canvas.width = MAX_IMAGE_WIDTH;
        canvas.height = Math.round(bitmap.height * (MAX_IMAGE_WIDTH / bitmap.width));
        canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
        bitmap.close();
        const blob = await new Promise((resolve) => canvas.toBlob(resolve, file.type, 0.86));
        return blob ? new File([blob], name, { type: file.type }) : file;
    } catch {
        return file;
    }
}
