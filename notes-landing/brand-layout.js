(function () {
  'use strict';
  var brand = document.querySelector('.hero-lockup');
  var downloads = document.querySelector('.hero-grid');
  if (!brand || !downloads) return;
  function fitBrand() {
    var items = Array.prototype.slice.call(downloads.children).map(function (item) {
      return item.getBoundingClientRect();
    }).filter(function (rect) { return rect.width > 0; });
    if (!items.length) return;
    var left = Math.min.apply(null, items.map(function (rect) { return rect.left; }));
    var right = Math.max.apply(null, items.map(function (rect) { return rect.right; }));
    brand.style.width = Math.min(downloads.clientWidth, right - left) + 'px';
  }
  fitBrand();
  if (window.ResizeObserver) {
    var observer = new ResizeObserver(fitBrand);
    observer.observe(downloads);
    Array.prototype.forEach.call(downloads.children, function (item) { observer.observe(item); });
  }
  window.addEventListener('resize', fitBrand);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(fitBrand);
})();
