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
    attach: function (headerRow, dotNetRef) {
        if (!headerRow) return;

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
            dotNetRef.invokeMethodAsync('OnColumnResized', key, newWidth);
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
            const sampleBodyCell = document.querySelector('.row .cell') || headerCell;

            const data = await dotNetRef.invokeMethodAsync('GetColumnAutoFitData', key);
            const newWidth = measureAutoFitWidth(data.header, data.values, sampleBodyCell, headerCell);
            dotNetRef.invokeMethodAsync('OnColumnResized', key, newWidth);
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
