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

    openJsonFile: function (inputId) {
        return new Promise((resolve) => {
            const input = document.getElementById(inputId);
            if (!input) { resolve(null); return; }
            input.onchange = () => {
                const file = input.files[0];
                if (!file) { resolve(null); return; }
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.readAsText(file);
                input.value = "";
            };
            input.click();
        });
    },

    openPcgFile: function (inputId) {
        return new Promise((resolve) => {
            const input = document.getElementById(inputId);
            if (!input) { resolve(null); return; }
            input.onchange = () => {
                const file = input.files[0];
                if (!file) { resolve(null); return; }
                const reader = new FileReader();
                reader.onload = () => resolve({ name: file.name, content: reader.result });
                // Read as Latin1 (iso-8859-1) — PCGen's native encoding
                reader.readAsText(file, 'iso-8859-1');
                input.value = "";
            };
            input.click();
        });
    }
};
