window.teamOrdering = (() => {
    const itemSelector = ".team-order-item";
    const handleSelector = "[data-team-order-handle]";

    function getItems(list) {
        return Array.from(list.querySelectorAll(itemSelector));
    }

    function moveTeam(order, teamId, targetId, after) {
        const sourceIndex = order.indexOf(teamId);
        if (sourceIndex < 0 || !order.includes(targetId) || teamId === targetId) {
            return order;
        }

        order.splice(sourceIndex, 1);
        const targetIndex = order.indexOf(targetId);
        order.splice(targetIndex + (after ? 1 : 0), 0, teamId);
        return order;
    }

    const autoScrollEdgeSize = 56;
    const autoScrollMaxSpeed = 16;

    function findScrollContainer(element) {
        let node = element.parentElement;
        while (node && node !== document.body) {
            const style = window.getComputedStyle(node);
            if (style.overflowY === "auto" || style.overflowY === "scroll") {
                return node;
            }
            node = node.parentElement;
        }
        return document.scrollingElement || document.documentElement;
    }

    function initialize(list, dotNetReference) {
        if (!list || list.teamOrderingController) {
            return;
        }

        let drag = null;
        let autoScrollRaf = null;

        function autoScrollStep() {
            if (!drag) {
                autoScrollRaf = null;
                return;
            }

            const container = findScrollContainer(list);
            const rect = container.getBoundingClientRect();
            const pointerY = drag.pointerY;
            let delta = 0;

            if (pointerY < rect.top + autoScrollEdgeSize) {
                const intensity = Math.min(1, (rect.top + autoScrollEdgeSize - pointerY) / autoScrollEdgeSize);
                delta = -Math.ceil(autoScrollMaxSpeed * intensity);
            } else if (pointerY > rect.bottom - autoScrollEdgeSize) {
                const intensity = Math.min(1, (pointerY - (rect.bottom - autoScrollEdgeSize)) / autoScrollEdgeSize);
                delta = Math.ceil(autoScrollMaxSpeed * intensity);
            }

            if (delta !== 0) {
                container.scrollTop += delta;
            }

            autoScrollRaf = requestAnimationFrame(autoScrollStep);
        }

        function startAutoScroll() {
            if (autoScrollRaf === null) {
                autoScrollRaf = requestAnimationFrame(autoScrollStep);
            }
        }

        function stopAutoScroll() {
            if (autoScrollRaf !== null) {
                cancelAnimationFrame(autoScrollRaf);
                autoScrollRaf = null;
            }
        }

        function clearIndicators() {
            list.classList.remove("team-order-list--dragging");
            for (const item of getItems(list)) {
                item.classList.remove(
                    "team-order-item--dragging",
                    "team-order-item--drop-before",
                    "team-order-item--drop-after");
            }
        }

        function finish(commit) {
            if (!drag) {
                return;
            }

            const { handle, pointerId, teamId, targetId, after } = drag;
            const order = commit && targetId
                ? moveTeam(getItems(list).map(item => item.dataset.teamId), teamId, targetId, after)
                : null;
            drag = null;
            stopAutoScroll();
            if (handle.hasPointerCapture(pointerId)) {
                handle.releasePointerCapture(pointerId);
            }
            clearIndicators();

            if (order) {
                void dotNetReference.invokeMethodAsync("ReorderTeamsFromMonitor", order)
                    .catch(error => console.error("Teamreihenfolge konnte nicht gespeichert werden.", error));
            }
        }

        function onPointerDown(event) {
            const handle = event.target instanceof Element ? event.target.closest(handleSelector) : null;
            if (!handle || !list.contains(handle) || (event.pointerType !== "touch" && event.button !== 0)) {
                return;
            }

            const item = handle.closest(itemSelector);
            if (!item) {
                return;
            }

            event.preventDefault();
            drag = {
                pointerId: event.pointerId,
                handle,
                teamId: item.dataset.teamId,
                targetId: null,
                after: false,
                pointerY: event.clientY
            };
            list.classList.add("team-order-list--dragging");
            item.classList.add("team-order-item--dragging");
            try {
                handle.setPointerCapture(event.pointerId);
            } catch {
                finish(false);
            }
            startAutoScroll();
        }

        function onPointerMove(event) {
            if (!drag || drag.pointerId !== event.pointerId) {
                return;
            }

            event.preventDefault();
            drag.pointerY = event.clientY;
            const target = document.elementFromPoint(event.clientX, event.clientY);
            const item = target instanceof Element ? target.closest(itemSelector) : null;
            const source = getItems(list).find(entry => entry.dataset.teamId === drag.teamId);
            for (const entry of getItems(list)) {
                entry.classList.remove("team-order-item--drop-before", "team-order-item--drop-after");
            }

            if (!item || !list.contains(item) || item === source) {
                drag.targetId = null;
                return;
            }

            const bounds = item.getBoundingClientRect();
            const after = event.clientY >= bounds.top + bounds.height / 2;
            item.classList.add(after ? "team-order-item--drop-after" : "team-order-item--drop-before");
            drag.targetId = item.dataset.teamId;
            drag.after = after;
        }

        function onKeyDown(event) {
            const handle = event.target instanceof Element ? event.target.closest(handleSelector) : null;
            if (!handle || !list.contains(handle) || !["ArrowUp", "ArrowDown"].includes(event.key)) {
                return;
            }

            const items = getItems(list);
            const item = handle.closest(itemSelector);
            const index = items.indexOf(item);
            const targetIndex = index + (event.key === "ArrowUp" ? -1 : 1);
            if (targetIndex < 0 || targetIndex >= items.length) {
                return;
            }

            event.preventDefault();
            const order = items.map(entry => entry.dataset.teamId);
            [order[index], order[targetIndex]] = [order[targetIndex], order[index]];
            void dotNetReference.invokeMethodAsync("ReorderTeamsFromMonitor", order)
                .catch(error => console.error("Teamreihenfolge konnte nicht gespeichert werden.", error));
        }

        function onPointerUp(event) {
            if (drag?.pointerId === event.pointerId) {
                finish(true);
            }
        }

        function onPointerCancel(event) {
            if (drag?.pointerId === event.pointerId) {
                finish(false);
            }
        }

        function onLostPointerCapture(event) {
            if (drag?.pointerId === event.pointerId) {
                finish(false);
            }
        }

        list.addEventListener("pointerdown", onPointerDown);
        list.addEventListener("pointermove", onPointerMove);
        list.addEventListener("pointerup", onPointerUp);
        list.addEventListener("pointercancel", onPointerCancel);
        list.addEventListener("lostpointercapture", onLostPointerCapture);
        list.addEventListener("keydown", onKeyDown);

        list.teamOrderingController = {
            dispose() {
                finish(false);
                list.removeEventListener("pointerdown", onPointerDown);
                list.removeEventListener("pointermove", onPointerMove);
                list.removeEventListener("pointerup", onPointerUp);
                list.removeEventListener("pointercancel", onPointerCancel);
                list.removeEventListener("lostpointercapture", onLostPointerCapture);
                list.removeEventListener("keydown", onKeyDown);
                delete list.teamOrderingController;
            }
        };
    }

    function dispose(list) {
        list?.teamOrderingController?.dispose();
    }

    return { initialize, dispose };
})();
