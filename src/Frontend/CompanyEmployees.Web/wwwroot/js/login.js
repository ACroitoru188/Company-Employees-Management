window.companyEmployeesLogin = {
    prepareBackgroundVideo(video, playbackRate) {
        if (!video) {
            return;
        }

        video.muted = true;
        video.playbackRate = playbackRate;
        video.play().catch(() => {
            // Keep the poster frame if a browser or device policy blocks autoplay.
        });

        let fadingForLoop = false;
        video.addEventListener("timeupdate", () => {
            if (!Number.isFinite(video.duration)) {
                return;
            }

            if (!fadingForLoop && video.duration - video.currentTime <= 1.2) {
                fadingForLoop = true;
                video.classList.add("is-looping");
            } else if (fadingForLoop && video.currentTime < 0.5) {
                fadingForLoop = false;
                requestAnimationFrame(() => video.classList.remove("is-looping"));
            }
        });
    },

    submit(formId, email, password, regionId, returnUrl) {
        const form = document.getElementById(formId);
        if (!form) {
            throw new Error("The login form could not be found.");
        }

        form.elements.namedItem("email").value = email;
        form.elements.namedItem("password").value = password;
        form.elements.namedItem("regionId").value = regionId;
        form.elements.namedItem("returnUrl").value = returnUrl;
        form.submit();
    }
};
