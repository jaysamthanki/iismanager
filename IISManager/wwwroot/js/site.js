var pageLoad = function () { };

$(document).ready(function ()
{
  // bootstrap stacking modal fix
  $(document).on('show.bs.modal', '.modal', function ()
  {
    var zIndex = 1040 + (10 * $('.modal:visible').length);
    $(this).css('z-index', zIndex);
    setTimeout(function () { $('.modal-backdrop').not('.modal-stack').css('z-index', zIndex - 1).addClass('modal-stack'); }, 0);
  });

  $(".hidden").hide();
  $(".hidden").removeClass("hidden");

  pageLoad();
});

// Formatters
function formatterRowIndex(value, row, index)
{
  return index + 1;
}
