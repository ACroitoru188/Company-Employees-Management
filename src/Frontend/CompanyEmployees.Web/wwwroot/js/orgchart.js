window.companyOrgChart = {
    // Moves the org tree's highlight to one node and brings its row into view. The tree item owns
    // its own selection and ignores being told to drop it, so a jump made from code has to clear
    // the others by hand or the tree and the detail panel end up showing two different people.
    focusNode: function (nodeId) {
        // A frame later, because the caller has just expanded the ancestors on the path: neither
        // the new rows nor their heights exist yet at the moment this is invoked. Looking the node
        // up before that frame finds nothing, and "Focus Me" silently does nothing at all.
        requestAnimationFrame(function () {
            const node = document.getElementById('orgnode-' + nodeId);
            // Nothing to move to. The previous highlight is left alone rather than cleared, so the
            // tree keeps agreeing with the detail panel instead of emptying itself.
            if (!node) {
                return;
            }

            document.querySelectorAll('fluent-tree-item').forEach(function (item) {
                item.selected = false;
            });
            node.setAttribute('selected', '');

            // The tree item's own box wraps its whole subtree — measured at 1716px for a manager
            // with 38 people under them — so centring *it* puts the middle of the subtree on
            // screen and the person's own row far above the top. The row is the 44px
            // positioning-region inside the item's shadow root; that is what to centre.
            const row = node.shadowRoot && node.shadowRoot.querySelector('.positioning-region');
            (row || node).scrollIntoView({ block: 'center', behavior: 'smooth' });
        });
    },
    clearHighlights: function() {
        document.querySelectorAll('fluent-tree-item.org-path-highlight').forEach(function(item) {
            item.classList.remove('org-path-highlight');
        });
        document.querySelectorAll('fluent-tree-item').forEach(function (item) {
            item.selected = false;
        });
    }
};
