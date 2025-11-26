let rfb = null;

window.initializeVnc = function () {
    console.log('noVNC module loaded and ready');

    const screen = document.getElementById('vnc-screen');
    const placeholder = document.getElementById('vnc-screen-element');
    const conncting_element = document.getElementById('vnc-screen-connecting');


    if (screen && placeholder) {
        screen.classList.add('d-none');
        placeholder.classList.add('d-block');
        conncting_element.classList.add('d-none')
    }
};


window.connectVnc = function (dotnetRef, url, password, scaleViewport, viewOnly, showDotCursor) {
    try {
        const screen = document.getElementById('vnc-screen');
        const placeholder = document.getElementById('vnc-screen-element');
        const conncting_element = document.getElementById('vnc-screen-connecting');

        placeholder.classList.remove('d-block');
        placeholder.classList.add('d-none');
        conncting_element.classList.remove('d-none');
        conncting_element.classList.add('d-block');

        var returnObj = {
            Value: "",
            Status: false,
            StatusCode: 0,
            Ex: "",
            Message: ""
        };

        if (!screen) {
            returnObj.Status = false;
            returnObj.StatusCode = 1;
            returnObj.Message = "VNC screen element not found";
            return returnObj;
        }

        // Disconnect any existing connection
        if (rfb) {
            rfb.disconnect();
            rfb = null;
            returnObj.Status = false;
            returnObj.StatusCode = 1;
            returnObj.Message = "Disconnecting existing VNC connection";
            return returnObj;
        }

        const rfbOptions = {
            shared: false,
            repeaterID: '',
            wsProtocols: ['binary']
        };

        if (password && password.trim() !== '') {
            rfbOptions.credentials = { password: password };
        }

        // Clear the screen
        screen.innerHTML = '';

        rfb = new RFB(screen, url, rfbOptions);

        // Configure RFB settings
        rfb.scaleViewport = scaleViewport;
        rfb.resizeSession = true;
        rfb.clipViewport = false;
        rfb.dragViewport = false;
        rfb.viewOnly = viewOnly;
        rfb.showDotCursor = showDotCursor;
        rfb.background = '';
        rfb.qualityLevel = 3;
        rfb.compressionLevel = 2;

        rfb.focus();

        // ==================== ALL NOVNC EVENTS ====================

        // CONNECT - Successfully connected to VNC server
        rfb.addEventListener("connect", (e) => {
            //console.log("✓ Connected to VNC server");

            screen.classList.remove('d-none');
            screen.classList.add('d-block');
            conncting_element.classList.remove('d-block');
            conncting_element.classList.add('d-none');

            let statusObj = {
                Status: true,
                StatusCode: 2,
                Ex: "",
                Message: "Successfully connected to VNC server"
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);

            setTimeout(() => {
                const canvas = screen.querySelector('canvas');
                if (canvas && rfb._fb_width && rfb._fb_height) {
                    console.log("Canvas size:", canvas.width, "x", canvas.height);
                    console.log("Remote desktop size:", rfb._fb_width, "x", rfb._fb_height);
                }
            }, 500);
        });

        // 2. DISCONNECT - Disconnected from VNC server
        rfb.addEventListener("disconnect", (e) => {
            //console.log("✗ Disconnected from VNC server");
            //console.log("Disconnect reason:", e.detail.clean ? "Clean disconnect" : "Unexpected disconnect");

            let statusObj = {
                Value: "Disconnected",
                Status: false,
                StatusCode: 0,
                Ex: e.detail.reason || "",
                Message: "Disconnected from server"
            };
            resetscreenEvent();
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);


            rfb = null;
        });

        // 3. CREDENTIALSREQUIRED - Server requires authentication
        rfb.addEventListener("credentialsrequired", (e) => {
            //console.log("⚠ VNC server requires credentials");
            //console.log("Credential types:", e.detail);

            if (password) {
                rfb.sendCredentials({ password: password });
            } else {
                let statusObj = {
                    Value: "CredentialsRequired",
                    Status: false,
                    StatusCode: 0,
                    Ex: "No password provided",
                    Message: "Password required but not provided"
                };
                resetscreenEvent();
                dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);

            }
        });

        // 4. SECURITYFAILURE - Authentication failed
        rfb.addEventListener("securityfailure", (e) => {
            //console.error("✗ Security failure:", e.detail);

            let statusObj = {
                Value: "SecurityFailure",
                Status: false,
                StatusCode: 0,
                Ex: e.detail.reason || e.detail.status || "",
                Message: "Authentication failed: " + (e.detail.reason || "Invalid credentials")
            };
            resetscreenEvent();
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);

        });

        // 5. CLIPBOARD - Clipboard data received from server
        rfb.addEventListener("clipboard", (e) => {
            //console.log("📋 Clipboard data received:", e.detail.text.substring(0, 50) + "...");

            let statusObj = {
                Value: e.detail.text,
                Status: true,
                StatusCode: 1,
                Message: "Clipboard data received"
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 6. BELL - Bell/beep signal from server
        rfb.addEventListener("bell", (e) => {
            //console.log("🔔 Bell signal received");

            let statusObj = {
                Status: true,
                StatusCode: 1,
                Message: "Bell signal received"
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 7. DESKTOPNAME - Desktop name received
        rfb.addEventListener("desktopname", (e) => {
            //console.log(" Desktop name:", e.detail.name);

            let statusObj = {
                Value: e.detail.name,
                Status: true,
                StatusCode: 7,
                Message: "Desktop name: " + e.detail.name
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 8. CAPABILITIES - Server capabilities received
        rfb.addEventListener("capabilities", (e) => {
            //console.log(" Server capabilities:", e.detail);

            let statusObj = {
                Status: true,
                StatusCode: 1,
                Message: "Server capabilities received"
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 9. RESIZE - Remote desktop resolution changed
        rfb.addEventListener("resize", (e) => {
            //console.log(" Desktop resized:", e.detail.width + "x" + e.detail.height);

            let statusObj = {
                Value: e.detail.width + "x" + e.detail.height,
                Status: true,
                StatusCode: 1,
                Message: `Desktop resized to ${e.detail.width}x${e.detail.height}`
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 10. UPDATESTATE - Framebuffer update state (requesting/receiving updates)
        rfb.addEventListener("updatestate", (e) => {
            //console.log(" Update state:", e.detail.state, "- ID:", e.detail.id);
            // States: "requested", "updating", "updated"
            // Use sparingly as this fires frequently
        });

        // 11. FBUUPDATE - Framebuffer update received (fires very frequently)
        rfb.addEventListener("fbuupdate", (e) => {
            // console.log(" Framebuffer update:", e.detail);
            // This fires on every screen update - use sparingly for performance
            // e.detail contains: x, y, width, height, encoding
        });

        // 12. FULLUPDATEREQUEST - Full framebuffer update requested
        rfb.addEventListener("fullupdaterequest", (e) => {
            //console.log(" Full update requested");
        });

        // 13. FBUCOMPLETE - Framebuffer update complete
        rfb.addEventListener("fbucomplete", (e) => {
            // console.log("✓ Framebuffer update complete");
            // Fires frequently - use sparingly
        });

        // 14. POWER - Power state changed (if server supports it)
        rfb.addEventListener("power", (e) => {
            //console.log(" Power state:", e.detail.powerState);

            let statusObj = {
                Value: e.detail.powerState,
                Status: true,
                StatusCode: 1,
                Message: "Power state: " + e.detail.powerState
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        // 15. SERVERVERIFICATION - Server identity verification (TLS/security)
        rfb.addEventListener("serververification", (e) => {
            //console.log(" Server verification required");
            //console.log("Server type:", e.detail.type);
            //console.log("Server subjectHash:", e.detail.subjectHash);

            // You must call approve() or reject()
            e.detail.approve();

            let statusObj = {
                Status: true,
                StatusCode: 1,
                Message: "Server verification approved"
            };
            dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);
        });

        return returnObj;

    } catch (error) {
        console.error('Failed to connect:', error);

        let statusObj = {
            Status: false,
            StatusCode: 0,
            Ex: error.message,
            Message: "Exception: " + error.message
        };
        resetscreen();
        dotnetRef.invokeMethodAsync("NotifyVncStatus", statusObj);

        throw error;
    }
};

window.disconnectVnc = function () {
    if (rfb) {
        rfb.disconnect();
        rfb = null;
    }

    resetscreen();
};


function resetscreen() {

    const screen = document.getElementById('vnc-screen');
    const placeholder = document.getElementById('vnc-screen-element');
    if (screen != null && placeholder != null) {
        screen.classList.remove('d-block');
        screen.classList.add('d-none');
        placeholder.classList.remove('d-none');
        placeholder.classList.add('d-block');
    }

}

function resetscreenEvent() {
    const screen = document.getElementById('vnc-screen');
    const placeholder = document.getElementById('vnc-screen-element');
    const connectingElement = document.getElementById('vnc-screen-connecting');

    if (screen != null && placeholder != null && connectingElement != null) {
        // Hide VNC canvas
        screen.classList.remove('d-block');
        screen.classList.add('d-none');

        // Show placeholder image or UI
        placeholder.classList.remove('d-none');
        placeholder.classList.add('d-block');

        // Hide connecting screen if visible
        if (connectingElement) {
            connectingElement.classList.remove('d-block');
            connectingElement.classList.add('d-none');
        }
    }

}


window.sendWindowsKey = function () {
    if (rfb) {
        rfb.sendKey(0xFFEB, null, true);  // Windows key down
        setTimeout(() => {
            rfb.sendKey(0xFFEB, null, false); // Windows key up
        }, 100);
        console.log('Sent Windows Key');
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};


window.sendCtrlAltDel = function () {
    if (rfb) {
        rfb.sendCtrlAltDel();
        console.log('Sent Ctrl+Alt+Del');
    }
};

window.toggleFullscreen = function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) return;


    if (!document.fullscreenElement) {
        element.requestFullscreen().catch(err => {
            console.error('Error attempting to enable fullscreen:', err);
        });
        return true
    } else {
        document.exitFullscreen();
        return false

    }
};

window.fullscreenHelper = {
    addFullscreenListener: function (dotnetRefFullscren, elementId) {
        const element = document.getElementById(elementId);
        if (!element) return;

        document.addEventListener("fullscreenchange", () => {
            const isFullscreen = document.fullscreenElement === element;
            console.log("Fullscreen Change: " + isFullscreen);


            // Notify .NET (Blazor)
            dotnetRefFullscren.invokeMethodAsync("OnFullscreenChanged", isFullscreen);
        });

        document.addEventListener("fullscreenerror", () => {
            console.warn("Fullscreen Error!");
            dotnetRefFullscren.invokeMethodAsync("OnFullscreenChanged", false);
        });
    },

    toggleFullscreen: function (elementId) {
        const element = document.getElementById(elementId);
        if (!element) return false;

        if (!document.fullscreenElement) {
            element.requestFullscreen().catch(err => console.error(err));
        } else {
            document.exitFullscreen();
        }
    }
};



// Force full screen refresh
window.refreshVncScreen = function () {
    if (rfb && rfb._display) {
        console.log("Forcing full screen refresh");
        const screen = document.getElementById('vnc-screen');
        const canvas = screen ? screen.querySelector('canvas') : null;
        if (canvas) {
            rfb.scaleViewport = rfb.scaleViewport;
        }
        return true;
    }
    return false;
};

// ========== REAL-TIME SETTINGS FUNCTIONS ==========

window.setVncQuality = function (quality) {
    if (rfb) {
        const qualityValue = parseInt(quality);
        if (qualityValue >= 0 && qualityValue <= 9) {
            rfb.qualityLevel = qualityValue;
            console.log(`Quality level changed to: ${qualityValue}`);
            return true;
        } else {
            console.error('Quality must be between 0-9');
            return false;
        }
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncCompression = function (compression) {
    if (rfb) {
        const compressionValue = parseInt(compression);
        if (compressionValue >= 0 && compressionValue <= 9) {
            rfb.compressionLevel = compressionValue;
            console.log(`Compression level changed to: ${compressionValue}`);
            window.refreshVncScreen();
            return true;
        } else {
            console.error('Compression must be between 0-9');
            return false;
        }
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncScaleViewport = function (scale) {
    if (rfb) {
        rfb.scaleViewport = scale;
        console.log(`Scale viewport changed to: ${scale}`);
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncViewOnly = function (viewOnly) {
    if (rfb) {
        rfb.viewOnly = viewOnly;
        console.log(`View only mode changed to: ${viewOnly}`);
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncShowDotCursor = function (showDot) {
    if (rfb) {
        rfb.showDotCursor = showDot;
        console.log(`Show dot cursor changed to: ${showDot}`);
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncClipViewport = function (clip) {
    if (rfb) {
        rfb.clipViewport = clip;
        console.log(`Clip viewport changed to: ${clip}`);
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.setVncDragViewport = function (drag) {
    if (rfb) {
        rfb.dragViewport = drag;
        console.log(`Drag viewport changed to: ${drag}`);
        return true;
    } else {
        console.error('VNC not connected');
        return false;
    }
};

window.getVncStatus = function () {
    if (rfb) {
        return {
            connected: true,
            qualityLevel: rfb.qualityLevel,
            compressionLevel: rfb.compressionLevel,
            scaleViewport: rfb.scaleViewport,
            resizeSession: rfb.resizeSession,
            clipViewport: rfb.clipViewport,
            dragViewport: rfb.dragViewport,
            viewOnly: rfb.viewOnly,
            showDotCursor: rfb.showDotCursor,
            remoteWidth: rfb._fb_width,
            remoteHeight: rfb._fb_height
        };
    } else {
        return { connected: false };
    }
};

window.setVncSettings = function (settings) {
    if (!rfb) {
        console.error('VNC not connected');
        return false;
    }

    if (settings.qualityLevel !== undefined) {
        rfb.qualityLevel = parseInt(settings.qualityLevel);
    }
    if (settings.compressionLevel !== undefined) {
        rfb.compressionLevel = parseInt(settings.compressionLevel);
    }
    if (settings.scaleViewport !== undefined) {
        rfb.scaleViewport = settings.scaleViewport;
    }
    if (settings.resizeSession !== undefined) {
        rfb.resizeSession = settings.resizeSession;
    }
    if (settings.clipViewport !== undefined) {
        rfb.clipViewport = settings.clipViewport;
    }
    if (settings.dragViewport !== undefined) {
        rfb.dragViewport = settings.dragViewport;
    }
    if (settings.viewOnly !== undefined) {
        rfb.viewOnly = settings.viewOnly;
    }
    if (settings.showDotCursor !== undefined) {
        rfb.showDotCursor = settings.showDotCursor;
    }

    console.log('VNC settings updated:', settings);
    window.refreshVncScreen();
    return true;
};


// Run optimization check every 10 seconds
let networkOptimizationInterval = null;

window.startNetworkOptimization = function () {
    if (networkOptimizationInterval) {
        clearInterval(networkOptimizationInterval);
    }

    networkOptimizationInterval = setInterval(() => {
        if (rfb) {
            window.optimizeForNetwork();
        }
    }, 10000);

    console.log("✓ Network optimization started (checking every 10 seconds)");
};

