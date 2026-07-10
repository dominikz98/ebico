// Triggers a client-side download of a text file (issue #55, Test-CA/key tools).
// Called from Blazor via IJSRuntime.InvokeVoidAsync("ebicoDownload", fileName, text).
window.ebicoDownload = (filename, text) => {
    const blob = new Blob([text], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
};
