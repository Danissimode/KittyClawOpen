const _savedScrolls = {};

window.saveColumnScrollPositions = function () {
    document.querySelectorAll('.column-body').forEach((el, i) => {
        _savedScrolls[i] = el.scrollTop;
    });
};

window.restoreColumnScrollPositions = function () {
    document.querySelectorAll('.column-body').forEach((el, i) => {
        if (_savedScrolls[i] !== undefined) el.scrollTop = _savedScrolls[i];
    });
};

// Real-time board updates via SSE
let _boardEventSource = null;

window.startBoardUpdates = function (projectSlug, dotNetRef) {
    if (_boardEventSource) {
        _boardEventSource.close();
    }
    const url = `/api/projects/${encodeURIComponent(projectSlug)}/events`;
    _boardEventSource = new EventSource(url);
    _boardEventSource.onmessage = (event) => {
        try {
            const data = JSON.parse(event.data);
            if (data.type === 'board-update') {
                dotNetRef.invokeMethodAsync('RefreshBoardAsync');
            }
        } catch (e) { /* ignore parse errors */ }
    };
    _boardEventSource.onerror = () => {
        // Auto-reconnect: EventSource reconnects automatically by default
    };
};

window.stopBoardUpdates = function () {
    if (_boardEventSource) {
        _boardEventSource.close();
        _boardEventSource = null;
    }
};
