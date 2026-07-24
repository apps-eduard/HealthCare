window.healthcareShell = {
  isNarrow: function (maxWidth) {
    return window.matchMedia('(max-width: ' + maxWidth + 'px)').matches;
  },
  downloadBase64File: function (fileName, contentType, base64) {
    var binary = atob(base64);
    var len = binary.length;
    var bytes = new Uint8Array(len);
    for (var i = 0; i < len; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    var blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
    var url = URL.createObjectURL(blob);
    var anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName || 'download';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }
};
