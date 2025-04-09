// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your Javascript code.

var pageLoad = function () { };

$(document).ready(function ()
{
  pageLoad();
});

// Formatters
function formatterMoney(val)
{
  return formatMoney(val);
}

function formatterRowIndex(value, row, index)
{
  return index + 1;
}

function formatterNumber(val)
{
  return Number(val);
}

function formatterDate(val)
{
  return moment(val).format("L");
}

function formatterDateTime(val)
{
  return moment(val).format("L LTS");
}