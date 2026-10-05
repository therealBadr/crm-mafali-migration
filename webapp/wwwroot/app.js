// Plain scroll-position listener — deliberately not using Blazor's built-in
// <Virtualize ItemsProvider> mechanism. That mechanism (IntersectionObserver
// + JS interop callback into an ItemsProvider delegate) was confirmed, via
// real-browser testing with no JS errors and no failed network requests, to
// simply never invoke its provider in this app — the query that would
// supply rows never even runs. Every other grid in this app uses
// <Virtualize Items="list">, which only needs a plain, static list and has
// none of that machinery — proven reliable everywhere it's used. This file
// is the standalone replacement for the on-demand-loading half of that
// mechanism: fire a callback when the user scrolls near the bottom of a
// container, nothing more.
window.mafaliScroll = {
    attach: function (element, dotNetRef) {
        if (!element) return;
        const handler = () => {
            const nearBottom = element.scrollTop + element.clientHeight >= element.scrollHeight - 400;
            if (nearBottom) {
                dotNetRef.invokeMethodAsync('OnScrolledNearBottom');
            }
        };
        element.addEventListener('scroll', handler, { passive: true });
        element._mafaliScrollHandler = handler;
    },
    detach: function (element) {
        if (element && element._mafaliScrollHandler) {
            element.removeEventListener('scroll', element._mafaliScrollHandler);
            delete element._mafaliScrollHandler;
        }
    }
};

// Blazor Server has no filesystem/download API of its own — a component can
// only push data to the browser over the SignalR circuit, not trigger a
// real file save. This is the standard workaround: the server streams bytes
// via a DotNetStreamReference, and this function turns that stream into a
// Blob + a synthetic <a download> click, exactly like the old React app's
// client-side export used to do directly (there, the data was already in
// browser memory; here it has to arrive over the circuit first).
window.mafaliFileDownload = {
    downloadFileFromStream: async function (fileName, contentType, streamRef) {
        const arrayBuffer = await streamRef.arrayBuffer();
        const blob = new Blob([arrayBuffer], { type: contentType });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    }
};

// Excel-style column resize for the .header-row/.cell grids used throughout
// this app. Deliberately does the drag itself entirely client-side (moving
// a guide line, not the real column) instead of calling back into Blazor on
// every mousemove — a Blazor Server round-trip per pixel of mouse movement
// would be visibly laggy over the SignalR connection. Only the final width,
// on mouseup, goes to .NET (one round-trip), which then re-renders every
// cell in that column — header and every currently-mounted row — from the
// updated width, same as any other server-side state change in this app.
window.mafaliColumnResize = {
    // resizeMethod/autoFitMethod default to the names Clients.razor uses —
    // only overridden by pages with more than one resizable grid per
    // component, where a single pair of [JSInvokable] method names can't
    // disambiguate which grid a callback is for (e.g. Filtres Prédéfinis'
    // client results grid + its historique grid, both on one component).
    attach: function (headerRow, dotNetRef, resizeMethod, autoFitMethod) {
        if (!headerRow) return;
        resizeMethod = resizeMethod || 'OnColumnResized';
        autoFitMethod = autoFitMethod || 'GetColumnAutoFitData';

        let drag = null;

        const widthAt = (clientX) => Math.max(40, Math.round(drag.startWidth + (clientX - drag.startX)));

        const onMouseDown = (e) => {
            const handle = e.target.closest('.col-resize-handle');
            if (!handle) return;
            e.preventDefault();
            e.stopPropagation();

            const gridHeight = headerRow.closest('.table-wrap')?.getBoundingClientRect().height
                ?? headerRow.getBoundingClientRect().height;

            const ghost = document.createElement('div');
            ghost.className = 'col-resize-ghost';
            ghost.style.left = e.clientX + 'px';
            ghost.style.top = headerRow.getBoundingClientRect().top + 'px';
            ghost.style.height = gridHeight + 'px';
            document.body.appendChild(ghost);

            drag = {
                key: handle.dataset.colKey,
                startX: e.clientX,
                startWidth: handle.closest('.cell').getBoundingClientRect().width,
                ghost: ghost,
            };
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        };

        const onMouseMove = (e) => {
            if (!drag) return;
            const newWidth = widthAt(e.clientX);
            drag.ghost.style.left = (drag.startX + (newWidth - drag.startWidth)) + 'px';
        };

        const onMouseUp = (e) => {
            if (!drag) return;
            const newWidth = widthAt(e.clientX);
            document.body.removeChild(drag.ghost);
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            const key = drag.key;
            drag = null;
            dotNetRef.invokeMethodAsync(resizeMethod, key, newWidth);
        };

        // Double-click a handle to auto-fit, same as Excel/Sheets. The data
        // itself never leaves the server as a client-side model in Blazor
        // Server, so this asks .NET for this one column's text (respecting
        // whatever search filter is currently applied — "longest content"
        // means longest of what's actually showing), then measures it here
        // with a canvas using the grid's real fonts, since only the browser
        // knows how wide a given string actually renders.
        const measureAutoFitWidth = (header, values, sampleCell, sampleHeaderCell) => {
            const canvas = measureAutoFitWidth._canvas || (measureAutoFitWidth._canvas = document.createElement('canvas'));
            const ctx = canvas.getContext('2d');

            const cellStyle = getComputedStyle(sampleCell);
            const headerStyle = getComputedStyle(sampleHeaderCell);
            const hPad = parseFloat(cellStyle.paddingLeft) + parseFloat(cellStyle.paddingRight);

            ctx.font = cellStyle.font;
            let maxContentWidth = 0;
            for (const v of values) {
                const w = ctx.measureText(v ?? '').width;
                if (w > maxContentWidth) maxContentWidth = w;
            }

            ctx.font = headerStyle.font;
            // Header text is uppercased via CSS (text-transform), not in the
            // underlying string — measure what's actually drawn.
            const headerWidth = ctx.measureText((header ?? '').toUpperCase()).width;

            return Math.max(40, Math.ceil(Math.max(maxContentWidth, headerWidth) + hPad + 4));
        };

        const onDblClick = async (e) => {
            const handle = e.target.closest('.col-resize-handle');
            if (!handle) return;
            e.preventDefault();
            e.stopPropagation();

            const key = handle.dataset.colKey;
            const headerCell = handle.closest('.cell');
            // Scoped to headerRow's own parent (the width-wrapper div every
            // grid in this app renders header-row + rows into), not
            // document-wide — pages with two resizable grids (Filtres
            // Prédéfinis) would otherwise measure against whichever grid's
            // row happened to be first in the DOM, using the wrong
            // font/padding for the other one. Not '.table-wrap': the
            // histo-pane grid on Filtres Prédéfinis isn't wrapped in one.
            const sampleBodyCell = headerRow.parentElement?.querySelector('.row .cell') || headerCell;

            const data = await dotNetRef.invokeMethodAsync(autoFitMethod, key);
            const newWidth = measureAutoFitWidth(data.header, data.values, sampleBodyCell, headerCell);
            dotNetRef.invokeMethodAsync(resizeMethod, key, newWidth);
        };

        headerRow.addEventListener('mousedown', onMouseDown);
        headerRow.addEventListener('dblclick', onDblClick);
        headerRow._mafaliResizeMouseDownHandler = onMouseDown;
        headerRow._mafaliResizeDblClickHandler = onDblClick;
    },
    detach: function (headerRow) {
        if (!headerRow) return;
        if (headerRow._mafaliResizeMouseDownHandler) {
            headerRow.removeEventListener('mousedown', headerRow._mafaliResizeMouseDownHandler);
            delete headerRow._mafaliResizeMouseDownHandler;
        }
        if (headerRow._mafaliResizeDblClickHandler) {
            headerRow.removeEventListener('dblclick', headerRow._mafaliResizeDblClickHandler);
            delete headerRow._mafaliResizeDblClickHandler;
        }
    }
};

// Lets every .table-wrap grid in the app (Clients, Rappels, Recherche
// Client, Filtres Prédéfinis, Parcours Client's history grid, etc. — same
// blanket reach as GridLayout.cs) grow taller by dragging a handle on the
// top border of its whole "group" — not just the grid itself, but every
// local toolbar/action-row/search-bar sitting directly above it too.
// Third design for this feature, all same day (2026-09-21) — Badr, after
// the second version only moved the grid's own box: "it is not working...
// this is the whole grid, not only from the top of the columns" (with a
// screenshot showing Recherche Client's Rechercher-Critères/count action
// row as part of what he considers "the grid"), then confirmed via
// AskUserQuestion that this applies everywhere, not just that one page —
// e.g. on Clients, the Nouveau/Modifier/Supprimer toolbar AND the search
// box both count as part of "the grid" too.
//
// What actually happens on drag: only the grid itself (`.table-wrap`)
// grows — it leaves flow (`position: fixed`) and floats, top climbing
// while its bottom stays anchored to its own standard position, exactly
// like the previous version (see that design's own reasoning: every
// page's root is a fixed-height `overflow: hidden` flex column, already
// claiming 100% of the leftover space by default, so growing in-flow has
// nowhere to go — confirmed live on the very first attempt at this
// feature). Everything ELSE in the group (toolbar, search-bar, action
// row) does NOT grow — it just visually SLIDES UP by the same distance
// via `transform: translateY()`, which repositions paint output without
// touching layout at all, so nothing below the grid shifts because of it
// either. The two together read as "the whole cluster got pulled up as
// one piece, and the grid underneath grew to fill the reclaimed space."
//
// Which elements belong to a grid's "group": by default, EVERY sibling
// above a `.table-wrap` back to the top of its parent (matches Clients/
// Familles/etc. — no dedicated form section to leave out, so the whole
// toolbar+search-bar+grid header area moves as one). A page can opt a
// narrower group by adding `.grid-group-start` to the specific element
// that should be the top of the group instead — e.g. Recherche Client's
// `.rc-actions` (the Rechercher Critères row), so the group stops there
// instead of continuing back through the big multi-field `.rc-body` form,
// which stays put and gets covered when the grid expands (see that page's
// own comment on `.rc-actions`). `collectGroup` also always stops at any
// OTHER `.table-wrap` it meets, so back-to-back grids on one page (Filtres
// Prédéfinis, Parcours Client's CA modal) never merge into each other's
// group.
//
// No per-page markup beyond that one opt-in class: a MutationObserver
// watches the whole document and, for every `.table-wrap` without a
// `data-grid-id` yet, assigns one, walks `collectGroup` to find where its
// group starts, and inserts a `.table-wrap-resize-handle` right before the
// group's top element plus a `.table-wrap-placeholder` right before the
// grid itself (see app.css for both) — the placeholder is what reserves
// the grid's normal flow space the instant it goes `position: fixed`,
// exactly as in the previous design. Handle and placeholder both carry the
// same `data-grid-id` so a drag can look up its grid without relying on
// DOM adjacency (the two aren't necessarily neighbors once a group spans
// more than one element). Orphaned handles/placeholders (their grid
// removed by the page itself, e.g. a Loading flag flipping) get cleaned
// up the same pass. Re-running on every DOM mutation sounds expensive on
// a Blazor Server page with frequent patches, but it's rAF-debounced
// (collapses any burst within one animation frame to a single pass) and
// each pass is a couple of cheap querySelectorAll calls bounded by the
// handful of grids on screen, not by row count.
//
// The drag itself re-measures the group's standard (in-flow) box at the
// START of every gesture, rather than caching it once, so it stays correct
// even if page content above resizes between drags. Moving the mouse up
// computes a smaller `top` for the group, capped at `TOPBAR_HEIGHT` (the
// app's own fixed nav bar, 64px — see MainLayout.razor.css's `.topbar` —
// so an expanded grid can never cover the nav itself) and floored at the
// group's own standard `top` (moving down can only ever return to
// standard, never shrink smaller — matching "pull it back down... to the
// previous height, the standard one" exactly). The handle stays
// `position: fixed`, tracking the group's current top edge while
// expanded, so it's always reachable to shrink back down or expand
// further, never left behind under the grid or the shifted toolbar.
window.mafaliRowResize = {
    _inited: false,
    init: function () {
        if (window.mafaliRowResize._inited) return;
        window.mafaliRowResize._inited = true;

        const TOPBAR_HEIGHT = 64;
        let nextGridId = 0;

        // An expanded grid is `position: fixed` and floats over whatever
        // used to be above it, covering some of it — but nothing about
        // that changes any ancestor's own scroll room, so if the covered
        // content wasn't already scrollable (the normal case: a grid page
        // fits exactly, only the grid itself scrolls internally), there's
        // nothing to scroll INTO to reveal what's now hidden underneath.
        // Badr, 2026-09-28: "the area above it should remain scrollable or
        // become scrollable... so we can scroll down and see the things
        // that are hidden".
        //
        // Four things that look reasonable here and confirmed NOT to
        // work, so don't re-try them:
        // - padding-bottom on .app-body (MainLayout.razor.css's real
        //   scroll container, the 100vh/overflow:hidden .app-shell's
        //   inner overflow-y:auto child): tested directly, had zero
        //   effect on .app-body.scrollHeight even at 300px — some
        //   combination of its flex-column/min-height:0/overflow:auto
        //   setup just doesn't count a scroll container's own trailing
        //   padding toward its scrollHeight here.
        // - a spacer appended as .app-body's last child (sibling of
        //   .page): .page-flush carries a negative margin on all sides
        //   (app.css, bleeds content edge-to-edge against .app-body's own
        //   padding) — confirmed live that negative margin pulls a
        //   trailing sibling up into it, silently eating most of the
        //   spacer's contribution.
        // - a spacer appended INSIDE .page: .page itself is
        //   `height: 100%; overflow: hidden` (app.css) — a grid page is
        //   meant to fit exactly and clip, managing its own scroll
        //   entirely internally (.table-wrap's own overflow: auto). That
        //   overflow: hidden silently swallows anything that doesn't fit,
        //   including the spacer — confirmed live, .app-body.scrollHeight
        //   never budged no matter how tall the spacer got, because .page
        //   clipped it before .app-body ever saw it.
        // - a NORMAL FLOW spacer (plain block, participates in flex
        //   layout) appended as the page root's last child: works for
        //   pages using the plain .page class, but Parcours Client and
        //   Recherche Client use their own page-specific root classes
        //   (.pc-page / .rc-page, not .page — see PAGE_ROOT_SELECTOR
        //   below) which both also contain a *separate* flex: 1;
        //   min-height: 0 scrollable panel above the grid (.pc-body-wrap,
        //   the identity/contact form) as a sibling of .grid-group-start
        //   and the grid itself. A flex-flow spacer competes with THAT
        //   panel for space — every time the spacer grows, flexbox
        //   shrinks the panel to compensate, which shifts
        //   .grid-group-start's natural (pre-transform) position. Since
        //   the drag's translateY offset is computed once at drag-start
        //   and never re-measures that natural position, the two drift
        //   apart continuously for as long as the drag continues. Badr,
        //   2026-09-28, live repro: "the rectangle that holds the
        //   buttons... didn't stick to the grid, a distance keeps forming
        //   as i keep resizing" — exactly this.
        // What actually works: position: absolute, anchored to the page
        // container's own bottom edge (top: 100%) instead of sitting in
        // normal flow — it still extends the container's scrollable
        // bounds (position: absolute descendants count toward
        // scrollHeight same as anything else), but an absolutely
        // positioned element is completely outside flex distribution, so
        // it can never cause any sibling to resize, at any point during
        // the drag.
        const overages = new Map();
        let scrollSpacer = null;
        let scrollPage = null;

        // Every real page's root card plays the same role .page does
        // (clip-to-fit, manage its own scroll) under a page-specific name
        // — add to this list if a future page introduces another one;
        // falling through to .app-body silently targets the wrong
        // container instead of failing loudly, which is exactly how this
        // bug happened the first time.
        const PAGE_ROOT_SELECTOR = '.page, .pc-page, .rc-page';

        function updateScrollSpacer(pageEl) {
            if (!scrollPage) {
                scrollPage = pageEl;
                // Left set once applied, even after the spacer shrinks back
                // to 0 — position: relative with no offset is visually
                // inert, and reverting it would need tracking yet another
                // original value for no real benefit.
                if (getComputedStyle(scrollPage).position === 'static') {
                    scrollPage.style.position = 'relative';
                }
            }
            if (!scrollSpacer) {
                scrollSpacer = document.createElement('div');
                scrollSpacer.className = 'resize-scroll-spacer';
                scrollPage.appendChild(scrollSpacer);
            }
            let total = 0;
            overages.forEach((v) => { total += v; });
            scrollSpacer.style.height = total + 'px';
            scrollPage.style.overflowY = total > 0 ? 'auto' : '';
        }

        // Walks backward from a `.table-wrap` collecting every sibling
        // above it (skipping any handle/placeholder already inserted),
        // stopping at — and including — the first `.grid-group-start` it
        // meets, or at the top of the parent if there isn't one. Also
        // stops (without including) at any OTHER `.table-wrap`, so two
        // grids sitting back-to-back never merge into one group.
        function collectGroup(tableWrap) {
            const group = [tableWrap];
            let el = tableWrap.previousElementSibling;
            while (el) {
                if (el.classList.contains('table-wrap-resize-handle') || el.classList.contains('table-wrap-placeholder')) {
                    el = el.previousElementSibling;
                    continue;
                }
                if (el.classList.contains('table-wrap')) break;
                group.unshift(el);
                if (el.classList.contains('grid-group-start')) break;
                el = el.previousElementSibling;
            }
            return group;
        }

        const ensureHandles = () => {
            document.querySelectorAll('.table-wrap-resize-handle, .table-wrap-placeholder').forEach((el) => {
                const id = el.dataset.gridId;
                if (!id || !document.querySelector('.table-wrap[data-grid-id="' + id + '"]')) {
                    el.remove();
                }
            });
            // A grid can be removed (page navigation) while still mid-
            // expansion — its own overage would otherwise linger forever,
            // permanently reserving scroll room nothing still needs. Blazor
            // navigation also typically replaces .page wholesale, so
            // scrollSpacer/scrollPage themselves go stale the same way —
            // dropped here rather than reused, so the next grid to expand
            // (on whatever page is now showing) creates fresh ones instead
            // of touching detached nodes.
            overages.forEach((_, id) => {
                if (!document.querySelector('.table-wrap[data-grid-id="' + id + '"]')) {
                    overages.delete(id);
                }
            });
            if (scrollPage && !document.body.contains(scrollPage)) {
                overages.clear();
                scrollSpacer = null;
                scrollPage = null;
            }
            document.querySelectorAll('.table-wrap').forEach((tableWrap) => {
                if (tableWrap.dataset.gridId) return;
                const id = 'g' + (nextGridId++);
                tableWrap.dataset.gridId = id;

                const group = collectGroup(tableWrap);

                const handle = document.createElement('div');
                handle.className = 'table-wrap-resize-handle';
                handle.dataset.gridId = id;
                group[0].parentElement.insertBefore(handle, group[0]);

                const placeholder = document.createElement('div');
                placeholder.className = 'table-wrap-placeholder';
                placeholder.dataset.gridId = id;
                // Match the real grid's own flex-grow/shrink/basis exactly
                // (e.g. Filtres Prédéfinis' two stacked grids share space
                // 3:2, not evenly) — a mismatched placeholder changes how
                // much of the leftover space every OTHER flex sibling gets
                // the instant this one goes `position: fixed`, which
                // visibly resized the unrelated grid above it on that page
                // (confirmed live) even though nothing touched it directly.
                const computed = getComputedStyle(tableWrap);
                placeholder.style.flex = computed.flex;
                // A grid with flex-grow:0 (e.g. Parcours Client's
                // `.pc-histo { height: 160px; flex: none; }`) isn't sized
                // by flex distribution at all — its own `height` rule is
                // what actually gives it a size, and flex:none alone gives
                // the placeholder none, collapsing its reserved space to
                // ~0. Confirmed live: the scrollable form above grew into
                // that gap, which then threw off the resting position of
                // everything below it, including this grid's own action
                // row. Grids with real flex-grow (most of them — they fill
                // available space dynamically) deliberately skip this, so
                // the placeholder keeps flexing with the window instead of
                // freezing at whatever height happened to be current when
                // first measured.
                if (parseFloat(computed.flexGrow) === 0) {
                    // getBoundingClientRect, not computed.height — the
                    // latter is content-box only, short of the real
                    // rendered box by however much border/padding the
                    // grid has (.pc-histo's own 2px border-top among
                    // others), which under-reserved the placeholder's
                    // space by just enough to be visible as drift.
                    placeholder.style.height = tableWrap.getBoundingClientRect().height + 'px';
                }
                tableWrap.parentElement.insertBefore(placeholder, tableWrap);
            });
        };

        let scheduled = false;
        const scheduleEnsure = () => {
            if (scheduled) return;
            scheduled = true;
            requestAnimationFrame(() => {
                scheduled = false;
                ensureHandles();
            });
        };

        ensureHandles();
        new MutationObserver(scheduleEnsure).observe(document.body, { childList: true, subtree: true });

        let drag = null;

        document.addEventListener('mousedown', (e) => {
            const handle = e.target.closest('.table-wrap-resize-handle');
            if (!handle) return;
            const id = handle.dataset.gridId;
            const target = document.querySelector('.table-wrap[data-grid-id="' + id + '"]');
            const placeholder = document.querySelector('.table-wrap-placeholder[data-grid-id="' + id + '"]');
            if (!target || !placeholder) return;
            e.preventDefault();
            handle.classList.add('dragging');

            const group = collectGroup(target);
            // The group's topmost element reflects the standard (in-flow)
            // top even while expanded, since only `target` itself ever
            // leaves flow — everything else in the group just gets a
            // paint-only translateY, never removed from layout.
            const topEl = group[0];
            const topRect = topEl.getBoundingClientRect();
            const standardTargetRect = target.dataset.expanded === '1' ? placeholder.getBoundingClientRect() : target.getBoundingClientRect();

            drag = {
                handle,
                placeholder,
                target,
                group,
                startY: e.clientY,
                groupTop: topRect.top,
                standardTarget: { top: standardTargetRect.top, bottom: standardTargetRect.bottom, left: standardTargetRect.left, width: standardTargetRect.width },
            };
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });

        function positionHandle(handle, standardTarget, top) {
            const handleHeight = handle.getBoundingClientRect().height || 8;
            handle.style.position = 'fixed';
            handle.style.left = standardTarget.left + 'px';
            handle.style.width = standardTarget.width + 'px';
            handle.style.top = (top - handleHeight / 2) + 'px';
            handle.style.margin = '0';
            handle.style.zIndex = '501';
        }

        function resetHandle(handle) {
            handle.style.position = '';
            handle.style.left = '';
            handle.style.width = '';
            handle.style.top = '';
            handle.style.margin = '';
            handle.style.zIndex = '';
        }

        function onMouseMove(e) {
            if (!drag) return;
            const { target, placeholder, handle, group, groupTop, standardTarget } = drag;
            // Dragging up (smaller clientY) grows the grid — the handle
            // sits on the GROUP's top border, so moving that border up
            // means the whole cluster claims more of the space above it.
            const delta = drag.startY - e.clientY;
            let newGroupTop = groupTop - delta;
            newGroupTop = Math.max(TOPBAR_HEIGHT, Math.min(groupTop, newGroupTop));
            const offset = newGroupTop - groupTop; // <= 0

            if (offset < -0.5) {
                group.forEach((el) => {
                    if (el === target) return;
                    el.style.transform = 'translateY(' + offset + 'px)';
                });
                const newTop = standardTarget.top + offset;
                target.style.position = 'fixed';
                target.style.left = standardTarget.left + 'px';
                target.style.width = standardTarget.width + 'px';
                target.style.top = newTop + 'px';
                // Explicit height, not just top+bottom left to auto-stretch
                // between them — some pages give their grid its own fixed
                // `height` in a scoped .razor.css (e.g. Parcours Client's
                // `.pc-histo { height: 160px; }`, sized down from 260px per
                // earlier feedback so the form above it had more room).
                // That rule still wins over an implicit top/bottom stretch
                // once `position: fixed` takes over sizing, so height has to
                // be set here explicitly to actually override it — confirmed
                // live: without this, the grid's top correctly climbed but
                // its box stayed pinned at exactly 160px tall regardless.
                target.style.height = (standardTarget.bottom - newTop) + 'px';
                target.style.zIndex = '500';
                target.dataset.expanded = '1';
                // Read by the wheel-passthrough listener below: the grid's
                // own original top, so it can tell "still inside the grid's
                // always-been-here footprint" (scroll the grid, unchanged)
                // apart from "the newly-claimed strip on top, covering
                // whatever used to be there" (scroll THAT instead). Written
                // on every move, not just once, since standardTarget.top is
                // constant for the whole gesture anyway — simpler than a
                // separate first-frame check.
                target.dataset.standardTop = standardTarget.top;
                placeholder.style.display = 'block';
                positionHandle(handle, standardTarget, newGroupTop);
                overages.set(target.dataset.gridId, -offset);
                updateScrollSpacer(target.closest(PAGE_ROOT_SELECTOR) || document.querySelector('.app-body'));
            } else {
                group.forEach((el) => {
                    if (el === target) return;
                    el.style.transform = '';
                });
                target.style.position = '';
                target.style.left = '';
                target.style.width = '';
                target.style.top = '';
                target.style.height = '';
                target.style.zIndex = '';
                target.dataset.expanded = '0';
                placeholder.style.display = 'none';
                resetHandle(handle);
                overages.delete(target.dataset.gridId);
                updateScrollSpacer(target.closest(PAGE_ROOT_SELECTOR) || document.querySelector('.app-body'));
            }
        }

        function onMouseUp() {
            if (!drag) return;
            drag.handle.classList.remove('dragging');
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            drag = null;
        }

        // Walks up from `el` for the nearest ancestor that can actually
        // scroll in the direction being asked for — not just "has
        // overflow:auto", but has real overflow content in that axis, so a
        // wheel gesture doesn't get swallowed by a container that LOOKS
        // scrollable but has nothing to scroll (leaving the gesture dead
        // instead of falling through to whatever's above it, e.g. the
        // document itself).
        function findScrollable(el, wantX, wantY) {
            let node = el;
            while (node && node !== document.documentElement) {
                const style = getComputedStyle(node);
                const canY = wantY && (style.overflowY === 'auto' || style.overflowY === 'scroll') && node.scrollHeight > node.clientHeight;
                const canX = wantX && (style.overflowX === 'auto' || style.overflowX === 'scroll') && node.scrollWidth > node.clientWidth;
                if (canY || canX) return node;
                node = node.parentElement;
            }
            return null;
        }

        // Badr, 2026-09-21: once a grid is expanded (floating over whatever
        // used to be above it), that covered content becomes completely
        // unreachable — the mouse just hits the opaque grid on top, so wheel
        // scrolling over it scrolls the grid's own rows instead, and
        // whatever's hidden underneath can't be scrolled at all anymore,
        // in either direction. Chose "let scroll pass through" over capping
        // how far a grid can grow: keep the full overlap, but redirect wheel
        // input to whatever's actually underneath when the cursor is over
        // the newly-claimed strip specifically — NOT the grid's own
        // always-been-there footprint below `standardTop`, which must keep
        // scrolling the grid itself exactly as before.
        document.addEventListener('wheel', (e) => {
            const overGrid = e.target.closest('.table-wrap[data-expanded="1"]');
            if (!overGrid) return;
            const standardTop = parseFloat(overGrid.dataset.standardTop);
            if (e.clientY >= standardTop) return;

            // Hit-test with the grid briefly excluded from consideration —
            // the only way to find out what's really underneath a
            // `position: fixed` overlay at this exact point, without
            // disturbing anything else about how it's painted.
            overGrid.style.pointerEvents = 'none';
            const under = document.elementFromPoint(e.clientX, e.clientY);
            overGrid.style.pointerEvents = '';
            if (!under) return;

            const scrollable = findScrollable(under, e.deltaX !== 0, e.deltaY !== 0);
            if (!scrollable) return;

            e.preventDefault();
            scrollable.scrollTop += e.deltaY;
            scrollable.scrollLeft += e.deltaX;
        }, { passive: false });
    }
};
window.mafaliRowResize.init();

// Backs the Bon de Commande page's Destination section and (later) the
// "Générer fichier bon de commande" button on Parcours Client. The File
// System Access API's directory handle is a browser object with no
// server-side equivalent — it's inherently local to one browser profile,
// so it's persisted in this browser's own IndexedDB, not this app's
// database. Chrome/Edge only; isSupported() is how the Razor page decides
// whether to show the folder picker or fall back to a plain download.
window.mafaliBonCommandeDestination = {
    isSupported: function () {
        return 'showDirectoryPicker' in window;
    },

    // User-gesture-triggered (called from a Blazor @onclick handler) —
    // showDirectoryPicker() throws SecurityError outside a user activation
    // window, which the click → C# → JS round-trip stays inside of as long
    // as it resolves quickly (the standard, documented way Blazor Server
    // apps use this API). Returns null (not a thrown error) if the user
    // just cancels the native picker — that's not a failure.
    pickFolder: async function () {
        let handle;
        try {
            handle = await window.showDirectoryPicker({ mode: 'readwrite' });
        } catch (e) {
            if (e.name === 'AbortError') return null;
            throw e;
        }
        await this._storeHandle(handle);
        return handle.name;
    },

    // Display-only — does not re-verify the permission is still granted
    // (that happens at actual write time, in writeFile below). Returns
    // null if no folder has ever been granted.
    getCurrentFolderName: async function () {
        const handle = await this._getStoredHandle();
        return handle ? handle.name : null;
    },

    // Called from "Générer fichier bon de commande" once bytes are ready.
    // Returns a short status string instead of throwing, so the C# side
    // can decide what to show without needing exception-shaped plumbing
    // over JS interop for expected outcomes (no folder yet, permission
    // lost) vs genuine errors. Re-requests permission every time rather
    // than trusting the original grant — it can silently expire, and this
    // still happens inside the same click → C# → JS chain as the
    // template-choice button, so it stays inside the user-activation
    // window the same way pickFolder does.
    writeFile: async function (fileName, streamRef) {
        const handle = await this._getStoredHandle();
        if (!handle) return 'no-folder';

        let permission;
        try {
            permission = await handle.requestPermission({ mode: 'readwrite' });
        } catch (e) {
            return 'permission-error';
        }
        if (permission !== 'granted') return 'permission-denied';

        try {
            const arrayBuffer = await streamRef.arrayBuffer();
            const fileHandle = await handle.getFileHandle(fileName, { create: true });
            const writable = await fileHandle.createWritable();
            await writable.write(arrayBuffer);
            await writable.close();
            return 'ok';
        } catch (e) {
            return 'write-error';
        }
    },

    _dbPromise: null,
    _openDb: function () {
        if (this._dbPromise) return this._dbPromise;
        this._dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open('mafali-bon-commande', 1);
            req.onupgradeneeded = () => req.result.createObjectStore('handles');
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        return this._dbPromise;
    },
    _storeHandle: async function (handle) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('handles', 'readwrite');
            tx.objectStore('handles').put(handle, 'destinationFolder');
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    },
    _getStoredHandle: async function () {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction('handles', 'readonly');
            const req = tx.objectStore('handles').get('destinationFolder');
            req.onsuccess = () => resolve(req.result || null);
            req.onerror = () => reject(req.error);
        });
    }
};
