window.companySetup = {
    waitForRestartAndRedirect: function (targetUrl, title, subtitle) {
        targetUrl = targetUrl || '/';
        title = title || 'Restarting application... Please wait.';
        subtitle = subtitle || 'Reconnecting automatically once the server is ready...';

        // Show a full-screen loading overlay with contextual message
        var overlay = document.getElementById('company-restart-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'company-restart-overlay';
            overlay.style.position = 'fixed';
            overlay.style.top = '0';
            overlay.style.left = '0';
            overlay.style.width = '100vw';
            overlay.style.height = '100vh';
            overlay.style.backgroundColor = 'rgba(15, 23, 42, 0.92)';
            overlay.style.backdropFilter = 'blur(8px)';
            overlay.style.webkitBackdropFilter = 'blur(8px)';
            overlay.style.zIndex = '999999';
            overlay.style.display = 'flex';
            overlay.style.flexDirection = 'column';
            overlay.style.alignItems = 'center';
            overlay.style.justifyContent = 'center';
            overlay.style.color = '#ffffff';
            overlay.style.fontFamily = 'system-ui, -apple-system, sans-serif';
            overlay.innerHTML = `
                <div style="width: 48px; height: 48px; border: 4px solid rgba(255,255,255,0.15); border-top-color: #0ea5e9; border-radius: 50%; animation: companySpin 0.9s linear infinite; margin-bottom: 20px;"></div>
                <div style="font-size: 1.25rem; font-weight: 600; margin-bottom: 8px; text-align: center;">${title}</div>
                <div style="font-size: 0.9rem; color: #94a3b8; text-align: center;">${subtitle}</div>
                <style>@keyframes companySpin { to { transform: rotate(360deg); } }</style>
            `;
            document.body.appendChild(overlay);
        }

        var hasSeenServerDown = false;
        var interval = setInterval(function () {
            fetch('/api/health', { cache: 'no-store' })
                .then(function (res) {
                    if (res.ok && hasSeenServerDown) {
                        clearInterval(interval);
                        setTimeout(function () {
                            window.location.href = targetUrl;
                        }, 1200);
                    }
                })
                .catch(function () {
                    hasSeenServerDown = true;
                });
        }, 500);
    }
};
