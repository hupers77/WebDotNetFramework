// fw.grid: 그리드 편집형 표준 컴포넌트 (설계서 12장). 인라인 편집과 행 상태(I/U/D) 추적, 화면 검증을 맡는다.
// - 표는 네이티브 <table>과 <input>으로 그려 키보드(Tab)와 스크린리더가 그대로 동작한다. 행 상태는 색과 함께 글자로도 표시한다.
// - 저장할 때는 getChanges()의 행(rowStat 포함)을 그대로 서버에 보낸다. 서버는 TVP 일괄 저장 SP로 처리한다.
// - 값은 textContent·input.value로만 넣는다(innerHTML 금지).
// 가상 스크롤·컬럼 고정·엑셀 출력은 대용량 조회 화면 작업에서 추가한다. 한 그리드에 수백 행까지를 기준으로 한다.
import { fw } from './fw.js';

const STATE_TEXT = { I: 'LBL.ROW_INSERTED', U: 'LBL.ROW_UPDATED', D: 'LBL.ROW_DELETED' };

/** "names.ko" 같은 경로로 값을 읽는다. */
function getPath(data, field) {
    return field.split('.').reduce((v, k) => (v == null ? undefined : v[k]), data);
}

function setPath(data, field, value) {
    const keys = field.split('.');
    const last = keys.pop();
    const target = keys.reduce((v, k) => (v[k] ??= {}), data);
    target[last] = value;
}

function isEmpty(value) {
    return value === undefined || value === null || (typeof value === 'string' && value.trim() === '');
}

/**
 * 편집 그리드.
 * @example
 * const grid = new Grid(el, {
 *   label: '공통코드',
 *   columns: [
 *     { field: 'code', header: fw.t('LBL.CODE'), required: true, maxLength: 30, readonly: (row, state) => state !== 'I' },
 *     { field: 'names.ko', header: '코드명(ko)', required: true, maxLength: 200 },
 *     { field: 'sortNo', header: fw.t('LBL.SORT_NO'), type: 'number' },
 *     { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn' },
 *   ],
 * });
 * grid.setRows(rows);
 */
export class Grid {
    /**
     * @param {HTMLElement} container 그리드를 그릴 요소
     * @param {object} options
     * @param {string} options.label 표 제목 (스크린리더용 caption)
     * @param {Array<object>} options.columns 컬럼: field, header, type(text|number|date|yn), required, maxLength,
     *   pattern(RegExp)+patternKey(메시지 키), readonly(row, state), width
     * @param {boolean} [options.editable=true] false면 읽기 전용
     */
    constructor(container, { label, columns, editable = true }) {
        this.container = container;
        this.label = label;
        this.columns = columns;
        this.editable = editable;
        this.rows = [];
        this.render();
    }

    /** 조회 결과로 바꾼다 (변경 상태는 모두 지운다). */
    setRows(items) {
        this.rows = items.map((data) => ({ data: structuredClone(data), state: '', modified: false }));
        this.render();
    }

    /** 새 행을 끝에 추가하고 첫 편집 칸으로 이동한다. */
    addRow(defaults = {}) {
        this.rows.push({ data: structuredClone(defaults), state: 'I', modified: true });
        this.render();
        this.body.lastElementChild?.querySelector('input:not(.row-select)')?.focus();
    }

    /** 선택한 행을 삭제 표시한다. 추가한 행은 바로 없애고, 이미 삭제 표시한 행은 되돌린다. 선택한 행이 없으면 false. */
    deleteSelected() {
        const selected = this.rows.filter((r) => r.selected);
        if (selected.length === 0) {
            return false;
        }

        this.rows = this.rows.filter((r) => !(r.selected && r.state === 'I'));
        for (const row of this.rows.filter((r) => r.selected)) {
            row.state = row.state === 'D' ? (row.modified ? 'U' : '') : 'D';
            row.selected = false;
        }

        this.render();
        return true;
    }

    /** 선택한 행의 상태 목록 ('' 조회한 그대로, I 추가, U 수정, D 삭제 표시). */
    selectedStates() {
        return this.rows.filter((r) => r.selected).map((r) => r.state);
    }

    /** 변경된 행 ({ ...값, rowStat }). */
    getChanges() {
        return this.rows.filter((r) => r.state).map((r) => ({ ...structuredClone(r.data), rowStat: r.state }));
    }

    /** 저장하지 않은 변경이 있는지. */
    hasChanges() {
        return this.rows.some((r) => r.state);
    }

    /** 현재 행 (데이터만). */
    getRows() {
        return this.rows.map((r) => r.data);
    }

    /**
     * 추가·수정 행을 컬럼 규칙으로 검사한다. 첫 오류 칸으로 이동하고 메시지를 돌려준다. 오류가 없으면 null.
     * 서버도 같은 규칙과 같은 메시지 키로 다시 검사한다.
     */
    validate() {
        this.body.querySelectorAll('[aria-invalid="true"]').forEach((el) => el.removeAttribute('aria-invalid'));
        for (const [index, row] of this.rows.entries()) {
            if (row.state !== 'I' && row.state !== 'U') {
                continue;
            }

            for (const col of this.columns) {
                if (this.isReadonly(col, row)) {
                    continue;
                }

                const value = getPath(row.data, col.field);
                let message = null;
                if (col.required && isEmpty(value)) {
                    message = fw.t('VAL.REQUIRED', col.header);
                } else if (col.maxLength && typeof value === 'string' && value.trim().length > col.maxLength) {
                    message = fw.t('VAL.MAX_LENGTH', col.header, col.maxLength);
                } else if (col.pattern && !isEmpty(value) && !col.pattern.test(value.trim())) {
                    message = fw.t(col.patternKey ?? 'MSG.INVALID_INPUT', col.header);
                } else if (col.check) {
                    message = col.check(row.data);
                }

                if (message) {
                    const input = this.body.rows[index]?.querySelector(`[data-field="${CSS.escape(col.field)}"]`);
                    input?.setAttribute('aria-invalid', 'true');
                    input?.focus();
                    return message;
                }
            }
        }

        return null;
    }

    isReadonly(col, row) {
        return !this.editable || row.state === 'D' || (col.readonly?.(row.data, row.state) ?? false);
    }

    render() {
        const table = document.createElement('table');
        table.className = 'fw-grid';
        const caption = table.createCaption();
        caption.className = 'sr-only';
        caption.textContent = this.label;

        const head = table.createTHead().insertRow();
        const th = (text, cls) => {
            const cell = document.createElement('th');
            cell.scope = 'col';
            cell.textContent = text;
            if (cls) {
                cell.className = cls;
            }

            head.append(cell);
            return cell;
        };
        th(fw.t('LBL.ROW_SELECT'), 'col-select');
        th(fw.t('LBL.ROW_STAT'), 'col-state');
        for (const col of this.columns) {
            const cell = th(col.header);
            if (col.width) {
                cell.style.width = col.width;
            }

            if (col.required) {
                cell.classList.add('required');
            }
        }

        this.body = table.createTBody();
        for (const row of this.rows) {
            this.body.append(this.renderRow(row));
        }

        if (this.rows.length === 0) {
            const empty = this.body.insertRow().insertCell();
            empty.colSpan = this.columns.length + 2;
            empty.className = 'grid-empty';
            empty.textContent = fw.t('MSG.NO_DATA');
        }

        this.container.replaceChildren(table);
    }

    renderRow(row) {
        const tr = document.createElement('tr');
        tr.className = row.state ? `row-${row.state}` : '';

        const select = document.createElement('input');
        select.type = 'checkbox';
        select.className = 'row-select';
        select.checked = !!row.selected;
        select.setAttribute('aria-label', fw.t('LBL.ROW_SELECT'));
        select.addEventListener('change', () => {
            row.selected = select.checked;
        });
        tr.insertCell().append(select);

        const state = tr.insertCell();
        state.className = 'cell-state';
        state.textContent = row.state ? fw.t(STATE_TEXT[row.state]) : '';

        for (const col of this.columns) {
            tr.insertCell().append(this.renderCell(col, row, tr, state));
        }

        return tr;
    }

    renderCell(col, row, tr, stateCell) {
        const value = getPath(row.data, col.field);
        if (this.isReadonly(col, row)) {
            const span = document.createElement('span');
            span.dataset.field = col.field;
            span.textContent = col.type === 'yn' ? (value === 'Y' ? '✓' : '') : (value ?? '');
            return span;
        }

        const input = document.createElement('input');
        input.dataset.field = col.field;
        input.setAttribute('aria-label', col.header);
        if (col.type === 'yn') {
            input.type = 'checkbox';
            input.checked = value === 'Y';
        } else {
            input.type = col.type === 'number' ? 'number' : col.type === 'date' ? 'date' : 'text';
            input.value = value ?? '';
            if (col.maxLength) {
                input.maxLength = col.maxLength;
            }
        }

        input.addEventListener('change', () => {
            const next = col.type === 'yn' ? (input.checked ? 'Y' : 'N')
                : col.type === 'number' ? (input.value === '' ? null : Number(input.value))
                : input.value === '' ? null : input.value;
            setPath(row.data, col.field, next);
            input.removeAttribute('aria-invalid');
            row.modified = true;
            if (!row.state) {
                row.state = 'U';
                tr.className = 'row-U';
                stateCell.textContent = fw.t(STATE_TEXT.U);
            }
        });
        return input;
    }
}

fw.Grid = Grid;
