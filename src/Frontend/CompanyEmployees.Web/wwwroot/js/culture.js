window.companyCulture = {
    set: function (culture) {
        const value = encodeURIComponent(`c=${culture}|uic=${culture}`);
        document.cookie = `.AspNetCore.Culture=${value}; path=/; max-age=31536000; samesite=lax`;
        window.location.reload();
    }
};
