window.healthcareShell = {
  isNarrow: function (maxWidth) {
    return window.matchMedia('(max-width: ' + maxWidth + 'px)').matches;
  }
};
