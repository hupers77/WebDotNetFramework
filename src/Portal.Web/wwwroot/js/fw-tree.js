// fw.tree: 트리 표준 컴포넌트 (설계서 12장). 부서·메뉴·코드처럼 상위 ID로 이어진 평평한 목록을 트리로 그린다.
// - WAI-ARIA 트리 패턴: role=tree/treeitem/group, aria-level·aria-expanded·aria-selected, 포커스는 한 항목만 Tab으로(roving tabindex).
// - 키보드: 위/아래 이동, 오른쪽 펼치기·첫 하위로, 왼쪽 접기·상위로, Home/End, Enter·Space 선택.
// - 글자는 textContent로만 넣는다(innerHTML 금지). 순서는 받은 목록 순서를 그대로 쓴다(정렬은 SP가 한다).
// 지연 로딩·체크박스·드래그 정렬은 그 기능이 필요한 화면 작업에서 추가한다.

/**
 * 트리.
 * @example
 * const tree = new Tree(ul, {
 *   label: '부서 트리',
 *   getId: (d) => d.deptId,
 *   getParentId: (d) => d.parentDeptId,
 *   render: (d) => ({ text: d.deptNm, meta: d.deptId, dimmed: d.useYn !== 'Y' }),
 *   onSelect: (d) => showDetail(d),
 * });
 * tree.setItems(rows);
 */
export class Tree {
    /**
     * @param {HTMLUListElement} container 트리를 그릴 목록 요소
     * @param {object} options
     * @param {string} options.label 트리 이름 (스크린리더용)
     * @param {(item: object) => string} options.getId
     * @param {(item: object) => string|null} options.getParentId 최상위면 null (목록에 없는 상위도 최상위로 본다)
     * @param {(item: object) => {text: string, meta?: string, dimmed?: boolean}} options.render
     * @param {(item: object) => void} [options.onSelect]
     */
    constructor(container, { label, getId, getParentId, render, onSelect }) {
        this.container = container;
        this.options = { getId, getParentId, render, onSelect };
        this.items = new Map();
        this.collapsed = new Set();
        this.selectedId = null;
        container.role = 'tree';
        container.classList.add('fw-tree');
        container.setAttribute('aria-label', label);
        container.addEventListener('click', (e) => this.#onClick(e));
        container.addEventListener('keydown', (e) => this.#onKeyDown(e));
    }

    /** 목록을 새로 그린다. 고른 항목이 아직 있으면 그대로 두고, 접은 상태도 유지한다. */
    setItems(items, selectedId = this.selectedId) {
        const { getId, getParentId } = this.options;
        this.items = new Map(items.map((item) => [getId(item), item]));
        this.selectedId = this.items.has(selectedId) ? selectedId : null;
        const children = new Map();
        for (const item of items) {
            const parent = getParentId(item);
            const key = parent != null && this.items.has(parent) ? parent : null;
            if (!children.has(key)) {
                children.set(key, []);
            }

            children.get(key).push(item);
        }

        this.container.replaceChildren(...(children.get(null) ?? []).map((item) => this.#renderItem(item, 1, children)));
        // 고른 항목이 접힌 상위 아래에 있으면 펼쳐서 보이게 한다
        if (this.selectedId) {
            this.#reveal(this.selectedId);
        }

        this.#resetTabStop();
    }

    /** 항목을 고른다 (onSelect는 부르지 않는다). */
    select(id) {
        this.selectedId = this.items.has(id) ? id : null;
        for (const li of this.container.querySelectorAll('[role="treeitem"]')) {
            li.setAttribute('aria-selected', li.dataset.id === this.selectedId ? 'true' : 'false');
        }

        if (this.selectedId) {
            this.#reveal(this.selectedId);
        }

        this.#resetTabStop();
    }

    /** id와 그 아래 모든 항목의 ID (상위 부서 선택지에서 자기와 하위를 빼는 데 쓴다). */
    descendantIds(id) {
        const result = new Set();
        const li = this.#find(id);
        if (li) {
            result.add(id);
            for (const child of li.querySelectorAll('[role="treeitem"]')) {
                result.add(child.dataset.id);
            }
        }

        return result;
    }

    /** 고른 항목에 포커스를 둔다. */
    focusSelected() {
        (this.#find(this.selectedId) ?? this.#visible()[0])?.focus();
    }

    #renderItem(item, level, children) {
        const { getId, render } = this.options;
        const id = getId(item);
        const view = render(item);
        const li = document.createElement('li');
        li.role = 'treeitem';
        li.dataset.id = id;
        li.tabIndex = -1;
        li.setAttribute('aria-level', String(level));
        li.setAttribute('aria-selected', id === this.selectedId ? 'true' : 'false');
        const row = document.createElement('div');
        row.className = 'fw-tree-row';
        row.style.paddingInlineStart = `${(level - 1) * 1.1}rem`;
        const toggle = document.createElement('span');
        toggle.className = 'fw-tree-toggle';
        toggle.setAttribute('aria-hidden', 'true');
        const text = document.createElement('span');
        text.className = 'fw-tree-text';
        text.textContent = view.text;
        row.append(toggle, text);
        if (view.meta) {
            const meta = document.createElement('span');
            meta.className = 'hint';
            meta.textContent = view.meta;
            row.append(meta);
        }

        li.classList.toggle('unused', !!view.dimmed);
        li.append(row);
        const kids = children.get(id);
        if (kids?.length) {
            const group = document.createElement('ul');
            group.role = 'group';
            group.append(...kids.map((k) => this.#renderItem(k, level + 1, children)));
            li.append(group);
            this.#setExpanded(li, !this.collapsed.has(id));
        }

        return li;
    }

    #setExpanded(li, expanded) {
        li.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        li.querySelector(':scope > ul').hidden = !expanded;
        li.querySelector(':scope > .fw-tree-row > .fw-tree-toggle').textContent = expanded ? '▾' : '▸';
        if (expanded) {
            this.collapsed.delete(li.dataset.id);
        } else {
            this.collapsed.add(li.dataset.id);
        }
    }

    #find(id) {
        return id == null ? null : this.container.querySelector(`[role="treeitem"][data-id="${CSS.escape(id)}"]`);
    }

    #reveal(id) {
        let li = this.#find(id)?.parentElement?.closest('[role="treeitem"]');
        while (li) {
            this.#setExpanded(li, true);
            li = li.parentElement.closest('[role="treeitem"]');
        }
    }

    /** 펼쳐서 보이는 항목들 (위에서 아래 순서). */
    #visible() {
        return [...this.container.querySelectorAll('[role="treeitem"]')].filter((li) => !li.parentElement.closest('ul[hidden]'));
    }

    #resetTabStop() {
        const items = this.container.querySelectorAll('[role="treeitem"]');
        for (const li of items) {
            li.tabIndex = -1;
        }

        const stop = this.#find(this.selectedId) ?? this.#visible()[0];
        if (stop) {
            stop.tabIndex = 0;
        }
    }

    #moveFocus(from, to) {
        if (!to) {
            return;
        }

        from.tabIndex = -1;
        to.tabIndex = 0;
        to.focus();
    }

    #choose(li) {
        this.select(li.dataset.id);
        this.options.onSelect?.(this.items.get(li.dataset.id));
        this.#find(li.dataset.id)?.focus();
    }

    #onClick(e) {
        const li = e.target.closest('[role="treeitem"]');
        if (!li) {
            return;
        }

        if (e.target.closest('.fw-tree-toggle') && li.hasAttribute('aria-expanded')) {
            this.#setExpanded(li, li.getAttribute('aria-expanded') !== 'true');
            return;
        }

        this.#choose(li);
    }

    #onKeyDown(e) {
        const li = e.target.closest('[role="treeitem"]');
        if (!li || e.target !== li) {
            return;
        }

        const visible = this.#visible();
        const index = visible.indexOf(li);
        const expandable = li.hasAttribute('aria-expanded');
        const expanded = li.getAttribute('aria-expanded') === 'true';
        switch (e.key) {
            case 'ArrowDown':
                this.#moveFocus(li, visible[index + 1]);
                break;
            case 'ArrowUp':
                this.#moveFocus(li, visible[index - 1]);
                break;
            case 'Home':
                this.#moveFocus(li, visible[0]);
                break;
            case 'End':
                this.#moveFocus(li, visible.at(-1));
                break;
            case 'ArrowRight':
                if (expandable && !expanded) {
                    this.#setExpanded(li, true);
                } else if (expandable) {
                    this.#moveFocus(li, li.querySelector(':scope > ul > [role="treeitem"]'));
                }

                break;
            case 'ArrowLeft':
                if (expandable && expanded) {
                    this.#setExpanded(li, false);
                } else {
                    this.#moveFocus(li, li.parentElement.closest('[role="treeitem"]'));
                }

                break;
            case 'Enter':
            case ' ':
                this.#choose(li);
                break;
            default:
                return;
        }

        e.preventDefault();
    }
}
