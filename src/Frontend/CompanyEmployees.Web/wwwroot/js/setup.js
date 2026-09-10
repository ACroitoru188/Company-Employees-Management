window.companySetup = {
    waitForRestartAndRedirect: function (targetUrl, title, subtitle) {
        targetUrl = targetUrl || '/';
        title = title || 'Restarting application... Please wait.';
        subtitle = subtitle || 'Reconnecting automatically once the server is ready...';

        var startTime = performance.now();

        // Show a full-screen loading overlay with contextual message and live timer
        var overlay = document.getElementById('company-restart-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'company-restart-overlay';
            overlay.style.position = 'fixed';
            overlay.style.top = '0';
            overlay.style.left = '0';
            overlay.style.width = '100vw';
            overlay.style.height = '100vh';
            overlay.style.backgroundColor = 'rgba(15, 23, 42, 0.94)';
            overlay.style.backdropFilter = 'blur(10px)';
            overlay.style.webkitBackdropFilter = 'blur(10px)';
            overlay.style.zIndex = '999999';
            overlay.style.display = 'flex';
            overlay.style.flexDirection = 'column';
            overlay.style.alignItems = 'center';
            overlay.style.justifyContent = 'center';
            overlay.style.color = '#ffffff';
            overlay.style.fontFamily = 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
            overlay.innerHTML = `
                <div style="width: 48px; height: 48px; border: 4px solid rgba(255,255,255,0.15); border-top-color: #38bdf8; border-radius: 50%; animation: companySpin 0.85s linear infinite; margin-bottom: 24px;"></div>
                <div id="company-restart-title" style="font-size: 1.35rem; font-weight: 600; margin-bottom: 8px; text-align: center; letter-spacing: -0.01em;">${title}</div>
                <div id="company-restart-phase" style="font-size: 0.95rem; color: #38bdf8; font-weight: 500; margin-bottom: 12px; text-align: center;">Stopping application...</div>
                <div id="company-restart-timer" style="display: inline-block; padding: 4px 14px; background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.15); border-radius: 9999px; font-size: 0.85rem; font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; color: #cbd5e1; margin-bottom: 14px;">Elapsed: 0.0s</div>
                <div id="company-restart-subtitle" style="font-size: 0.88rem; color: #94a3b8; text-align: center; max-width: 520px; line-height: 1.5;">${subtitle}</div>
                <div id="company-restart-metrics" style="margin-top: 14px; font-size: 0.82rem; color: #64748b; text-align: center; max-width: 600px; font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;"></div>
                <style>@keyframes companySpin { to { transform: rotate(360deg); } }</style>
            `;
            document.body.appendChild(overlay);
        }

        var timerElement = document.getElementById('company-restart-timer');
        var phaseElement = document.getElementById('company-restart-phase');
        var subtitleElement = document.getElementById('company-restart-subtitle');
        var metricsElement = document.getElementById('company-restart-metrics');

        // Live stopwatch updater
        var stopwatchInterval = setInterval(function () {
            var elapsedSec = ((performance.now() - startTime) / 1000).toFixed(1);
            if (timerElement) {
                timerElement.textContent = 'Elapsed: ' + elapsedSec + 's';
            }
        }, 100);

        var hasSeenServerDown = false;
        var isFinalizing = false;

        var pollInterval = setInterval(function () {
            if (isFinalizing) return;

            fetch('/api/health', { cache: 'no-store' })
                .then(function (res) {
                    if (res.ok) {
                        if (hasSeenServerDown && !isFinalizing) {
                            isFinalizing = true;
                            clearInterval(stopwatchInterval);
                            clearInterval(pollInterval);

                            var totalClientElapsed = ((performance.now() - startTime) / 1000).toFixed(1);

                            if (timerElement) {
                                timerElement.textContent = 'Total time: ' + totalClientElapsed + 's';
                                timerElement.style.borderColor = 'rgba(56, 189, 248, 0.4)';
                                timerElement.style.color = '#38bdf8';
                            }
                            if (phaseElement) {
                                phaseElement.textContent = 'Ready. Redirecting...';
                                phaseElement.style.color = '#34d399';
                            }

                            res.json().then(function (data) {
                                if (data && data.startupMetrics) {
                                    var metrics = data.startupMetrics;
                                    var bootSec = (metrics.totalStartupMs / 1000).toFixed(1);
                                    var migSec = (metrics.migrationsMs / 1000).toFixed(1);
                                    var seedSec = (metrics.seedingMs / 1000).toFixed(1);
                                    var activeName = data.activeProvider || 'Primary';
                                    var secondaryName = data.secondaryProvider ? (' | Standby: ' + data.secondaryProvider) : '';

                                    if (metricsElement) {
                                        metricsElement.textContent = 'Provider: ' + activeName + secondaryName +
                                            ' | Boot: ' + bootSec + 's (Migrations: ' + migSec + 's, Seeding: ' + seedSec + 's)';
                                    }

                                    console.log('[System Restart Diagnostics]');
                                    console.table({
                                        'Active Provider': activeName,
                                        'Secondary Provider': data.secondaryProvider || 'None',
                                        'Failover Active': data.isFailoverActive ? 'Yes' : 'No',
                                        'Client Total Time': totalClientElapsed + 's',
                                        'Server Total Startup': bootSec + 's',
                                        'Failover/Check Time': metrics.failoverSelectMs + 'ms',
                                        'Migrations Duration': metrics.migrationsMs + 'ms',
                                        'Data Seeding Duration': metrics.seedingMs + 'ms',
                                        'Standby Bootstrap Duration': metrics.standbyBootstrapMs + 'ms'
                                    });

                                    // Display side-by-side comparison if multiple providers have been benchmarked
                                    if (data.providerComparison && data.providerComparison.providers) {
                                        var pMap = data.providerComparison.providers;
                                        var pKeys = Object.keys(pMap);
                                        if (pKeys.length >= 2) {
                                            var compTable = {};
                                            pKeys.forEach(function (k) {
                                                var p = pMap[k];
                                                compTable[p.displayName || p.providerId] = {
                                                    'Migrations': (p.migrationsMs / 1000).toFixed(2) + 's (' + p.migrationsMs + 'ms)',
                                                    'Data Seeding': (p.seedingMs / 1000).toFixed(2) + 's (' + p.seedingMs + 'ms)',
                                                    'Total Server Startup': (p.totalStartupMs / 1000).toFixed(2) + 's (' + p.totalStartupMs + 'ms)',
                                                    'Last Recorded UTC': p.recordedUtc
                                                };
                                            });
                                            console.log('[Provider Comparison History]');
                                            console.table(compTable);

                                            if (data.providerComparison.analysis && data.providerComparison.analysis.summary) {
                                                console.log('[Comparison Summary] ' + data.providerComparison.analysis.summary);
                                                if (metricsElement) {
                                                    var summaryDiv = document.createElement('div');
                                                    summaryDiv.style.marginTop = '6px';
                                                    summaryDiv.style.color = '#38bdf8';
                                                    summaryDiv.textContent = data.providerComparison.analysis.summary;
                                                    metricsElement.appendChild(summaryDiv);
                                                }
                                            }
                                        }
                                    }
                                }
                            }).catch(function () {
                                // If body parsing fails, proceed with redirect
                            }).finally(function () {
                                setTimeout(function () {
                                    window.location.href = targetUrl;
                                }, 1200);
                            });
                        }
                    } else {
                        hasSeenServerDown = true;
                        if (phaseElement) {
                            phaseElement.textContent = 'Starting up services & initializing database...';
                        }
                    }
                })
                .catch(function () {
                    hasSeenServerDown = true;
                    if (phaseElement) {
                        phaseElement.textContent = 'Rebooting container & restarting services...';
                    }
                });
        }, 400);
    }
};

