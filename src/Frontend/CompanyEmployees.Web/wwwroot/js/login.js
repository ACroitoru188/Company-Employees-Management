window.companyEmployeesLogin = {
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
