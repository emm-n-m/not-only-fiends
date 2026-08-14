window.fileHelpers = {
    downloadJson: function (filename, json) {
        const blob = new Blob([json], { type: "application/json" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    // Cancelling the native file dialog fires no `change` event, so a promise that only
    // resolves there leaves the awaiting Blazor handler pending forever — the "Open..." button
    // then does nothing on every subsequent click. `cancel` covers modern browsers; the
    // one-shot window focus handler covers the rest, and both are disarmed by `settle`.
    // `encoding` is passed to FileReader; `project` turns the file and its text into the value
    // the C# caller receives.
    _pickFile: function (inputId, encoding, project) {
        return new Promise((resolve) => {
            const input = document.getElementById(inputId);
            if (!input) { resolve(null); return; }

            let settled = false;
            const settle = (value) => {
                if (settled) return;
                settled = true;
                input.onchange = null;
                input.oncancel = null;
                window.removeEventListener("focus", onFocus);
                input.value = "";
                resolve(value);
            };

            const onFocus = () => {
                // Focus returns before `change` fires, so give the change handler a chance
                // before concluding the dialog was dismissed.
                setTimeout(() => { if (!input.files.length) settle(null); }, 300);
            };

            input.onchange = () => {
                const file = input.files[0];
                if (!file) { settle(null); return; }
                const reader = new FileReader();
                reader.onload = () => settle(project(file, reader.result));
                reader.onerror = () => settle(null);
                reader.readAsText(file, encoding);
            };
            input.oncancel = () => settle(null);
            window.addEventListener("focus", onFocus, { once: true });

            input.click();
        });
    },

    openJsonFile: function (inputId) {
        return this._pickFile(inputId, "utf-8", (file, text) => text);
    },

    openPcgFile: function (inputId) {
        // Read as Latin1 (iso-8859-1) — PCGen's native encoding
        return this._pickFile(inputId, "iso-8859-1", (file, text) => ({ name: file.name, content: text }));
    }
};
