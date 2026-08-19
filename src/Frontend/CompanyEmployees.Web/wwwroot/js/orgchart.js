window.companyOrgChart = {
    // Moves the org tree's highlight to one node and brings its row into view. The tree item owns
    // its own selection and ignores being told to drop it, so a jump made from code has to clear
    // the others by hand or the tree and the detail panel end up showing two different people.
    focusNode: function (nodeId) {
        const id = 'orgnode-' + nodeId;

        // Twenty frames is a third of a second at 60Hz — long enough for a large team to finish
        // rendering over the circuit, short enough that a genuinely absent node gives up quietly.
        const maxAttempts = 20;

        const apply = function (attempt) {
            const node = document.getElementById(id);

            // Two things have to be true, and neither is guaranteed on the first frame after the
            // caller renders: the row must exist, and its custom element must have been upgraded.
            // Before the upgrade there is no shadow root and `selected` is discarded by the
            // component's own initialisation — which is exactly how a jump from the search landed
            // with the detail panel right and the row unmarked, but only sometimes.
            if (!node || !node.shadowRoot) {
                if (attempt < maxAttempts) {
                    requestAnimationFrame(function () { apply(attempt + 1); });
                }
                return;
            }

            // Cleared by property rather than attribute: the property is what the component
            // watches, and it is how every other row is un-selected below.
            document.querySelectorAll('fluent-tree-item').forEach(function (item) {
                if (item !== node) {
                    item.selected = false;
                }
            });
            node.selected = true;

            // The tree item's own box wraps its whole subtree — measured at 1716px for a manager
            // with 38 people under them — so centring *it* puts the middle of the subtree on
            // screen and the person's own row far above the top. The row is the 44px
            // positioning-region inside the item's shadow root; that is what to centre.
            const row = node.shadowRoot.querySelector('.positioning-region');
            (row || node).scrollIntoView({ block: 'center', behavior: 'smooth' });
        };

        const start = function () {
            requestAnimationFrame(function () { apply(0); });
        };

        // Waiting for the definition first means the retry budget above is spent on rendering,
        // not on the component library still loading.
        if (window.customElements && customElements.whenDefined) {
            customElements.whenDefined('fluent-tree-item').then(start, start);
        } else {
            start();
        }
    }
};
